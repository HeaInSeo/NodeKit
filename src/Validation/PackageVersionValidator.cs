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

        // -r/--requirement은 다른 requirements 파일을 가리키기만 할 뿐 그 안의
        // 내용(패키지 버전 고정 여부)은 recipe/BuildRequest 어디에도 없어서
        // NodeKit이 볼 방법이 없다 — -e/--editable과 동일한 이유로 차단한다.
        private static readonly Regex _requirementsFileReferencePattern =
            new(@"^(-r|--requirement)(\s|=)", RegexOptions.Compiled);

        // pip install 옵션 중 다음 토큰을 인자로 소비하는 것들 — 이걸 패키지명으로
        // 오인해 버전 검사를 하면 안 된다. -r/--requirement는 여기 두지 않는다 —
        // 값을 건너뛰기만 하면 안 되고 아래에서 차단해야 하므로 별도 처리한다.
        private static readonly HashSet<string> _pipValueOptions = new(StringComparer.Ordinal)
        {
            "-c", "--constraint", "-t", "--target",
            "-i", "--index-url", "--extra-index-url", "--trusted-host",
            "--cache-dir", "--proxy", "--retries", "--timeout", "-f", "--find-links",
        };

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
            return spec.Split('\n', StringSplitOptions.None)
                .Select(rawLine => rawLine.TrimStart())
                .Any(trimmed => trimmed.StartsWith("name:", StringComparison.Ordinal) ||
                    trimmed.StartsWith("dependencies:", StringComparison.Ordinal));
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

            foreach (var line in spec.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(rawLine => rawLine.Trim()))
            {
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

                if (_requirementsFileReferencePattern.IsMatch(line))
                {
                    violations.Add(new ValidationViolation(
                        "L1-PKG-005",
                        $"'-r'/'--requirement'로 다른 requirements 파일을 참조하는 방식은 그 안의 패키지 버전 고정 여부를 확인할 수 없어 차단됩니다: '{line}'",
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

                ValidateDockerfilePipPackages(instruction.Raw, violations, "DockerfileContent");
            }

            return new ValidationResult(violations);
        }

        // DGF002(DockGuard genomics policy)와 동일한 요구사항 — RUN 내 pip install도
        // conda/micromamba install과 마찬가지로 버전 고정을 요구한다. conda 계열만
        // 검사하던 기존 코드는 이 경로를 완전히 놓치고 있었다(Dockerfile 방식에서
        // "RUN pip install numpy"가 아무 경고 없이 통과됨).
        private static void ValidateDockerfilePipPackages(string rawInstruction, List<ValidationViolation> violations, string field)
        {
            var runBody = rawInstruction.Length > 3
                ? rawInstruction[3..].Trim()
                : string.Empty;

            foreach (var tokens in runBody.Split(_shellCommandSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Select(command => command.Split(_dockerfileTokenSeparators, StringSplitOptions.RemoveEmptyEntries).ToList()))
            {
                var argStartIndex = GetPipInstallArgStartIndex(tokens);
                if (argStartIndex < 0)
                {
                    continue;
                }

                var skipNext = false;
                for (var index = argStartIndex; index < tokens.Count; index++)
                {
                    var token = tokens[index];
                    if (skipNext)
                    {
                        skipNext = false;
                        continue;
                    }

                    // Dockerfile 토큰은 공백으로 분리되므로 "-e"와 "git+..."가 별도
                    // 토큰이다 — 한 줄짜리 requirements.txt 항목("-e git+...")을
                    // 가정한 _editableInstallPattern은 "-e" 토큰 자체와 매치되지
                    // 않는다. "-e"/"--editable"을 값-소비 옵션처럼 먼저 명시적으로
                    // 처리해 다음 토큰(VCS URL/경로)을 건너뛴다.
                    if (token is "-e" or "--editable")
                    {
                        violations.Add(new ValidationViolation(
                            "L1-PKG-004",
                            "editable/VCS 설치는 버전을 고정할 수 없어 차단됩니다.",
                            field));
                        skipNext = true;
                        continue;
                    }

                    if (_editableInstallPattern.IsMatch(token))
                    {
                        violations.Add(new ValidationViolation(
                            "L1-PKG-004",
                            $"editable/VCS 설치는 버전을 고정할 수 없어 차단됩니다: '{token}'",
                            field));
                        continue;
                    }

                    // -r/--requirement도 -e/--editable과 같은 이유(그 파일 안의
                    // 버전 고정 여부를 확인할 방법이 없음)로 값-소비 옵션처럼 건너뛰지
                    // 않고 명시적으로 차단한다.
                    if (token is "-r" or "--requirement")
                    {
                        violations.Add(new ValidationViolation(
                            "L1-PKG-005",
                            "'-r'/'--requirement'로 다른 requirements 파일을 참조하는 방식은 그 안의 패키지 버전 고정 여부를 확인할 수 없어 차단됩니다.",
                            field));
                        skipNext = true;
                        continue;
                    }

                    if (_requirementsFileReferencePattern.IsMatch(token))
                    {
                        violations.Add(new ValidationViolation(
                            "L1-PKG-005",
                            $"'-r'/'--requirement'로 다른 requirements 파일을 참조하는 방식은 그 안의 패키지 버전 고정 여부를 확인할 수 없어 차단됩니다: '{token}'",
                            field));
                        continue;
                    }

                    if (token.StartsWith('-'))
                    {
                        skipNext = _pipValueOptions.Contains(token);
                        continue;
                    }

                    ValidatePipPackage(token.Trim().Trim('"', '\''), violations, field);
                }
            }
        }

        // "pip"/"pip3" exact-match만 보면 "/usr/bin/pip install"(절대 경로)나
        // "python -m pip install"(모듈 실행)처럼 흔한 형태가 전부 우회한다 —
        // 실행 파일 이름은 마지막 경로 구성요소(basename)로 비교하고, "python -m
        // pip install" 4토큰 패턴도 별도로 인식한다. 매치되면 실제 패키지 인자가
        // 시작하는 인덱스를, 매치되지 않으면 -1을 반환한다.
        private static int GetPipInstallArgStartIndex(List<string> tokens)
        {
            if (tokens.Count >= 2 &&
                IsPipExecutable(tokens[0]) &&
                string.Equals(tokens[1], "install", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            if (tokens.Count >= 4 &&
                IsPythonExecutable(tokens[0]) &&
                string.Equals(tokens[1], "-m", StringComparison.OrdinalIgnoreCase) &&
                IsPipExecutable(tokens[2]) &&
                string.Equals(tokens[3], "install", StringComparison.OrdinalIgnoreCase))
            {
                return 4;
            }

            return -1;
        }

        private static bool IsPipExecutable(string token)
        {
            var name = GetExecutableBasename(token);
            return string.Equals(name, "pip", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "pip3", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPythonExecutable(string token)
        {
            var name = GetExecutableBasename(token);
            return string.Equals(name, "python", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "python3", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetExecutableBasename(string token)
        {
            var lastSlash = token.LastIndexOf('/');
            return lastSlash >= 0 ? token[(lastSlash + 1)..] : token;
        }

        private static IEnumerable<string> ExtractInstalledPackages(string rawInstruction)
        {
            var runBody = rawInstruction.Length > 3
                ? rawInstruction[3..].Trim()
                : string.Empty;

            foreach (var tokens in runBody.Split(_shellCommandSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Select(command => command.Split(_dockerfileTokenSeparators, StringSplitOptions.RemoveEmptyEntries).ToList()))
            {
                if (tokens.Count < 2)
                {
                    continue;
                }

                if (!IsCondaInstallCommand(tokens))
                {
                    continue;
                }

                var skipNext = false;
                for (var index = 2; index < tokens.Count; index++)
                {
                    var token = tokens[index];
                    if (skipNext)
                    {
                        skipNext = false;
                        continue;
                    }

                    if (token.StartsWith('-'))
                    {
                        // Options that consume the next token as their argument:
                        // -c/--channel (channel name), -n/--name (env name), -p/--prefix (path)
                        skipNext = token is "-c" or "--channel" or "-n" or "--name" or "-p" or "--prefix";
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

            // build string (=version=build) 결정은 NodeVault ResolveToolSpec 담당.
            // NodeKit L1은 =version 형식(버전 고정)까지만 요구한다.
            // 참조: PLATFORM_MASTER_DESIGN.md §4.9
        }

        private static void ValidatePipPackage(
            string packageExpression,
            List<ValidationViolation> violations,
            string field = "EnvironmentSpec")
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
                    field));
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
