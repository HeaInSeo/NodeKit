using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NodeKit.Authoring;
using NodeKit.Policy;

namespace NodeKit.Validation
{
    /// <summary>
    /// L1 Dockerfile 구조 검증기. NodeVault에 BuildRequest로 넘기기 전에
    /// 명백히 빌드 불가능하거나 build context를 벗어나는 입력을 차단한다.
    /// </summary>
    internal class DockerfileStructureValidator : IValidator
    {
        private static readonly Regex _variableReferencePattern = new(
            @"\$(\{[A-Za-z_][A-Za-z0-9_]*\}|[A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled);

        // \z, not $ — .NET's $ (without RegexOptions.Multiline) tolerates one
        // trailing '\n', which would let a digest value with an embedded
        // newline pass this check (see RecipeValidator's matching fix note).
        private static readonly Regex _sha256DigestPattern = new(@"\A[0-9a-fA-F]{64}\z", RegexOptions.Compiled);

        public ValidationResult Validate(ToolDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);

            var dockerfile = definition.DockerfileContent;
            if (string.IsNullOrWhiteSpace(dockerfile))
            {
                return ValidationResult.Pass;
            }

            var violations = new List<ValidationViolation>();
            if (HasDanglingLineContinuation(dockerfile))
            {
                violations.Add(new ValidationViolation(
                    "L1-DOCKER-004",
                    "Dockerfile의 마지막 줄이 줄 이음 문자(\\)로 끝납니다. 이어지는 명령이 필요합니다.",
                    nameof(definition.DockerfileContent)));
            }

            var instructions = DockerfileParser.Parse(dockerfile);
            if (instructions.Count == 0)
            {
                violations.Add(new ValidationViolation(
                    "L1-DOCKER-001",
                    "Dockerfile에 실행 가능한 명령이 없습니다.",
                    nameof(definition.DockerfileContent)));
                return new ValidationResult(violations);
            }

            ValidateFromInstruction(violations, instructions[0], nameof(definition.DockerfileContent));
            ValidateAllFromInstructionsPinning(violations, instructions, nameof(definition.DockerfileContent));
            ValidateCopyAndAddInstructions(violations, instructions, nameof(definition.DockerfileContent));
            return new ValidationResult(violations);
        }

        private static bool HasDanglingLineContinuation(string dockerfile)
        {
            var lastMeaningfulLine = dockerfile.Split('\n', StringSplitOptions.None)
                .Reverse()
                .Select(rawLine => rawLine.Trim())
                .FirstOrDefault(trimmed => !string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith('#'));

            return lastMeaningfulLine?.EndsWith('\\') ?? false;
        }

        private static void ValidateFromInstruction(
            List<ValidationViolation> violations,
            DockerfileInstruction firstInstruction,
            string field)
        {
            if (!string.Equals(firstInstruction.Cmd, "FROM", StringComparison.Ordinal))
            {
                violations.Add(new ValidationViolation(
                    "L1-DOCKER-002",
                    "Dockerfile의 첫 번째 명령은 FROM이어야 합니다.",
                    field));
                return;
            }

            if (firstInstruction.Value.Count == 0)
            {
                violations.Add(new ValidationViolation(
                    "L1-DOCKER-003",
                    "FROM 명령에는 base image가 필요합니다.",
                    field));
            }
        }

        private static void ValidateAllFromInstructionsPinning(
            List<ValidationViolation> violations,
            IReadOnlyList<DockerfileInstruction> instructions,
            string field)
        {
            foreach (var instruction in instructions)
            {
                if (!string.Equals(instruction.Cmd, "FROM", StringComparison.Ordinal) || instruction.Value.Count == 0)
                {
                    continue;
                }

                ValidateBaseImagePinning(violations, instruction.Value[0], field);
            }
        }

        private static void ValidateBaseImagePinning(
            List<ValidationViolation> violations,
            string baseImage,
            string field)
        {
            if (baseImage.Contains(":latest", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(new ValidationViolation(
                    "L1-DOCKER-008",
                    $"FROM base image에 'latest' 태그는 허용되지 않습니다. 정확한 버전 태그 + digest(@sha256:...)를 사용하세요. ({baseImage})",
                    field));
                return;
            }

            var digestIndex = baseImage.IndexOf("@sha256:", StringComparison.OrdinalIgnoreCase);
            if (digestIndex < 0)
            {
                violations.Add(new ValidationViolation(
                    "L1-DOCKER-009",
                    $"FROM base image에 digest(@sha256:...)가 없습니다. 재현성 보장을 위해 digest 고정이 필수입니다. ({baseImage})",
                    field));
                return;
            }

            // 첫 번째 FROM은 ImageUriValidator가 이 hex 포맷까지 검증하지만, 두 번째
            // 이후 FROM(멀티스테이지)은 여기서만 검사된다 — "@sha256:" 포함 여부만
            // 보면 "@sha256:not-real-hex"처럼 형식이 엉터리인 digest도 통과했다.
            var digest = baseImage[(digestIndex + "@sha256:".Length)..];
            if (!_sha256DigestPattern.IsMatch(digest))
            {
                violations.Add(new ValidationViolation(
                    "L1-DOCKER-011",
                    $"FROM base image의 digest 형식이 올바르지 않습니다. sha256 digest는 64자리 16진수여야 합니다. ({baseImage})",
                    field));
            }
        }

        private static void ValidateCopyAndAddInstructions(
            List<ValidationViolation> violations,
            IReadOnlyList<DockerfileInstruction> instructions,
            string field)
        {
            foreach (var instruction in instructions)
            {
                if (!IsCopyOrAdd(instruction.Cmd))
                {
                    continue;
                }

                var positionalArgs = instruction.Value
                    .Where(value => !value.StartsWith("--", StringComparison.Ordinal))
                    .ToList();

                if (positionalArgs.Count < 2)
                {
                    violations.Add(new ValidationViolation(
                        "L1-DOCKER-005",
                        $"{instruction.Cmd} 명령에는 source와 destination이 모두 필요합니다.",
                        field));
                    continue;
                }

                ValidateBuildContextSources(violations, instruction.Cmd, positionalArgs.Take(positionalArgs.Count - 1), field);
            }
        }

        private static void ValidateBuildContextSources(
            List<ValidationViolation> violations,
            string command,
            IEnumerable<string> sources,
            string field)
        {
            foreach (var source in sources)
            {
                if (source.Contains("..", StringComparison.Ordinal))
                {
                    violations.Add(new ValidationViolation(
                        "L1-DOCKER-006",
                        $"{command} source '{source}'는 build context 밖을 참조할 수 없습니다.",
                        field));
                }

                if (_variableReferencePattern.IsMatch(source))
                {
                    violations.Add(new ValidationViolation(
                        "L1-DOCKER-010",
                        $"{command} source '{source}'는 ARG/ENV 변수 참조를 포함하여 build context 범위를 정적으로 검증할 수 없습니다 — 변수 없이 고정된 경로를 사용하세요.",
                        field));
                }

                if (string.Equals(command, "ADD", StringComparison.Ordinal) && IsRemoteSource(source))
                {
                    violations.Add(new ValidationViolation(
                        "L1-DOCKER-007",
                        $"ADD remote source '{source}'는 재현 가능한 build context로 고정할 수 없습니다. 파일을 context에 포함하고 COPY를 사용하세요.",
                        field));
                }
            }
        }

        private static bool IsCopyOrAdd(string command) =>
            string.Equals(command, "COPY", StringComparison.Ordinal) ||
            string.Equals(command, "ADD", StringComparison.Ordinal);

        private static bool IsRemoteSource(string source) =>
            source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }
}
