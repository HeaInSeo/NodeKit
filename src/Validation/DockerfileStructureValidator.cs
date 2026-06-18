using System;
using System.Collections.Generic;
using System.Linq;
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
            ValidateCopyAndAddInstructions(violations, instructions, nameof(definition.DockerfileContent));
            return new ValidationResult(violations);
        }

        private static bool HasDanglingLineContinuation(string dockerfile)
        {
            foreach (var rawLine in dockerfile.Split('\n', StringSplitOptions.None).Reverse())
            {
                var trimmed = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                {
                    continue;
                }

                return trimmed.EndsWith('\\');
            }

            return false;
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
