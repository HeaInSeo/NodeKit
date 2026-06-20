using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NodeKit.Authoring;
using NodeKit.Policy;

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
    internal class PackageVersionValidator : IValidator
    {
        private static readonly char[] _dockerfileTokenSeparators = { ' ', '\t' };
        private static readonly string[] _shellCommandSeparators = { "&&", ";" };
        private static readonly Regex _editableInstallPattern =
            new(@"^(-e|--editable)(\s|=)", RegexOptions.Compiled);

        public ValidationResult Validate(ToolDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);

            var violations = new List<ValidationViolation>();

            if (!string.IsNullOrWhiteSpace(definition.EnvironmentSpec))
            {
                violations.AddRange(ValidateEnvironmentSpec(definition.EnvironmentSpec).Violations);
            }

            if (!string.IsNullOrWhiteSpace(definition.DockerfileContent))
            {
                violations.AddRange(ValidateDockerfile(definition.DockerfileContent).Violations);
            }

            return new ValidationResult(violations);
        }

        private static ValidationResult ValidateEnvironmentSpec(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec))
            {
                return ValidationResult.Pass;
            }

            // conda yml 형식 감지
            if (IsCondaEnvironmentYaml(spec))
            {
                return ValidateConda(spec);
            }

            // requirements.txt 형식 감지
            return ValidatePip(spec);
        }

        private static bool IsCondaEnvironmentYaml(string spec)
        {
            foreach (var rawLine in spec.Split('\n', StringSplitOptions.None))
            {
                var trimmed = rawLine.TrimStart();
                if (trimmed.StartsWith("name:", StringComparison.Ordinal) ||
                    trimmed.StartsWith("dependencies:", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static ValidationResult ValidateConda(string spec)
        {
            var violations = new List<ValidationViolation>();
            var lines = spec.Split('\n', StringSplitOptions.None);
            var inPipSubsection = false;
            var pipSectionIndent = -1;

            foreach (var rawLine in lines)
            {
                var trimmed = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                {
                    continue;
                }

                var indent = GetIndent(rawLine);
                if (inPipSubsection)
                {
                    if (indent > pipSectionIndent && trimmed.StartsWith("- ", StringComparison.Ordinal))
                    {
                        ValidatePipPackage(trimmed[2..].Trim(), violations);
                        continue;
                    }

                    inPipSubsection = false;
                    pipSectionIndent = -1;
                }

                if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
                {
                    continue;
                }

                var entry = trimmed[2..].Trim();
                if (entry.Equals("pip", StringComparison.Ordinal))
                {
                    continue;
                }

                if (entry.Equals("pip:", StringComparison.Ordinal))
                {
                    inPipSubsection = true;
                    pipSectionIndent = indent;
                    continue;
                }

                ValidateCondaPackage(entry, violations);
            }

            return new ValidationResult(violations);
        }

        private static ValidationResult ValidatePip(string spec)
        {
            var violations = new List<ValidationViolation>();

            foreach (var rawLine in spec.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                {
                    continue;
                }

                // -e/--editable (git+... 포함)은 버전 고정이 불가능한 설치 방식이라
                // 다른 '-' 옵션(-r, --index-url 등)처럼 그냥 건너뛰면 검증을 통째로 우회한다.
                if (_editableInstallPattern.IsMatch(line))
                {
                    violations.Add(new ValidationViolation(
                        "L1-PKG-004",
                        $"editable/VCS 설치는 버전을 고정할 수 없어 차단됩니다: '{line}'",
                        "EnvironmentSpec"));
                    continue;
                }

                if (line.StartsWith('-'))
                {
                    continue;
                }

                ValidatePipPackage(line, violations);
            }

            return new ValidationResult(violations);
        }

        private static ValidationResult ValidateDockerfile(string dockerfile)
        {
            var violations = new List<ValidationViolation>();

            foreach (var instruction in DockerfileParser.Parse(dockerfile))
            {
                if (!string.Equals(instruction.Cmd, "RUN", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var package in ExtractInstalledPackages(instruction.Raw))
                {
                    ValidateCondaPackage(package, violations, "DockerfileContent");
                }
            }

            return new ValidationResult(violations);
        }

        private static IEnumerable<string> ExtractInstalledPackages(string rawInstruction)
        {
            var runBody = rawInstruction.Length > 3
                ? rawInstruction[3..].Trim()
                : string.Empty;

            foreach (var command in runBody.Split(_shellCommandSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                var tokens = command
                    .Split(_dockerfileTokenSeparators, StringSplitOptions.RemoveEmptyEntries)
                    .ToList();

                if (tokens.Count < 2)
                {
                    continue;
                }

                if (!IsCondaInstallCommand(tokens))
                {
                    continue;
                }

                for (var index = 2; index < tokens.Count; index++)
                {
                    var token = tokens[index];
                    if (token.StartsWith('-'))
                    {
                        continue;
                    }

                    yield return token.Trim().Trim('"', '\'');
                }
            }
        }

        private static bool IsCondaInstallCommand(List<string> tokens)
        {
            return tokens.Count >= 2 &&
                (string.Equals(tokens[0], "micromamba", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tokens[0], "conda", StringComparison.OrdinalIgnoreCase)) &&
                string.Equals(tokens[1], "install", StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateCondaPackage(
            string packageExpression,
            List<ValidationViolation> violations,
            string field = "EnvironmentSpec")
        {
            if (string.IsNullOrWhiteSpace(packageExpression))
            {
                return;
            }

            var expression = packageExpression.Trim();
            var segments = expression.Split('=', StringSplitOptions.None);

            if (segments.Length <= 1 || string.IsNullOrWhiteSpace(segments[1]))
            {
                violations.Add(new ValidationViolation(
                    "L1-PKG-001",
                    $"패키지 '{expression}'에 버전이 지정되지 않았습니다. conda 형식: name=version=build_string",
                    field));
                return;
            }

            if (segments.Length <= 2 || string.IsNullOrWhiteSpace(segments[2]))
            {
                violations.Add(new ValidationViolation(
                    "L1-PKG-002",
                    $"패키지 '{expression}'에 빌드 문자열이 없습니다. conda 형식: name=version=build_string",
                    field));
            }
        }

        private static void ValidatePipPackage(string packageExpression, List<ValidationViolation> violations)
        {
            if (string.IsNullOrWhiteSpace(packageExpression))
            {
                return;
            }

            if (!packageExpression.Contains("==", StringComparison.Ordinal))
            {
                violations.Add(new ValidationViolation(
                    "L1-PKG-003",
                    $"패키지 '{packageExpression}'에 정확한 버전이 없습니다. pip 형식: name==version (예: numpy==1.26.4)",
                    "EnvironmentSpec"));
            }
        }

        private static int GetIndent(string line)
        {
            var indent = 0;
            foreach (var c in line)
            {
                if (!char.IsWhiteSpace(c))
                {
                    break;
                }

                indent++;
            }

            return indent;
        }
    }
}
