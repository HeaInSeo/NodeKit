using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NodeKit.Authoring;

namespace NodeKit.Validation
{
    /// <summary>
    /// L1 패키지 버전 고정 검증기.
    /// EnvironmentSpec (conda yml, requirements.txt 등)에서
    /// 버전+빌드 문자열이 없는 패키지 설치 구문을 차단한다.
    ///
    /// conda 형식:  name=version=build  (예: bwa=0.7.17=h5bf99c6_8)
    /// pip 형식:    name==version       (예: numpy==1.26.4)
    /// </summary>
    public class PackageVersionValidator : IValidator
    {
        // conda: 패키지 이름만 있거나 버전만 있는 행 (빌드 문자열 없음)
        private static readonly Regex CondaLine = new(
            @"^\s*-\s+(?<pkg>[a-zA-Z0-9_\-\.]+)(?:=(?<ver>[^=\n]+))?$",
            RegexOptions.Multiline);

        public ValidationResult Validate(ToolDefinition definition)
        {
            if (definition is null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (string.IsNullOrWhiteSpace(definition.EnvironmentSpec))
            {
                return ValidationResult.Pass;
            }

            var spec = definition.EnvironmentSpec;

            // conda yml 형식 감지
            if (spec.TrimStart().StartsWith("name:", StringComparison.Ordinal) ||
                spec.Contains("dependencies:", StringComparison.Ordinal))
            {
                return ValidateConda(spec);
            }

            // requirements.txt 형식 감지
            return ValidatePip(spec);
        }

        private static ValidationResult ValidateConda(string spec)
        {
            var violations = new List<ValidationViolation>();

            foreach (Match m in CondaLine.Matches(spec))
            {
                var pkg = m.Groups["pkg"].Value;
                var ver = m.Groups["ver"].Value;
                var line = m.Value.Trim();

                // = 개수로 고정 수준 판단
                var eqCount = 0;
                foreach (var c in line)
                {
                    if (c == '=')
                    {
                        eqCount++;
                    }
                }

                if (eqCount == 0)
                {
                    violations.Add(new ValidationViolation(
                        "L1-PKG-001",
                        $"패키지 '{pkg}'에 버전이 지정되지 않았습니다. conda 형식: name=version=build_string",
                        "EnvironmentSpec"));
                }
                else if (eqCount == 1 && !string.IsNullOrEmpty(ver))
                {
                    violations.Add(new ValidationViolation(
                        "L1-PKG-002",
                        $"패키지 '{pkg}={ver}'에 빌드 문자열이 없습니다. conda 형식: name=version=build_string",
                        "EnvironmentSpec"));
                }
            }

            return new ValidationResult(violations);
        }

        private static ValidationResult ValidatePip(string spec)
        {
            var violations = new List<ValidationViolation>();

            foreach (var rawLine in spec.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line) ||
                    line.StartsWith('#') ||
                    line.StartsWith('-'))
                {
                    continue;
                }

                if (!line.Contains("==", StringComparison.Ordinal))
                {
                    violations.Add(new ValidationViolation(
                        "L1-PKG-003",
                        $"패키지 '{line}'에 정확한 버전이 없습니다. pip 형식: name==version (예: numpy==1.26.4)",
                        "EnvironmentSpec"));
                }
            }

            return new ValidationResult(violations);
        }
    }
}
