using System;
using System.Collections.Generic;
using System.Linq;
using NodeKit.Authoring;

namespace NodeKit.Validation
{
    internal class RequiredFieldsValidator : IValidator
    {
        public ValidationResult Validate(ToolDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);

            var violations = new List<ValidationViolation>();

            if (string.IsNullOrWhiteSpace(definition.Name))
            {
                violations.Add(new ValidationViolation(
                    "L1-REQ-001",
                    "Tool 이름은 필수입니다.",
                    nameof(definition.Name)));
            }

            if (string.IsNullOrWhiteSpace(definition.Version))
            {
                violations.Add(new ValidationViolation(
                    "L1-REQ-010",
                    "Tool 버전은 필수입니다.",
                    nameof(definition.Version)));
            }

            if (string.IsNullOrWhiteSpace(definition.DockerfileContent))
            {
                violations.Add(new ValidationViolation(
                    "L1-REQ-002",
                    "Dockerfile 내용은 필수입니다.",
                    nameof(definition.DockerfileContent)));
            }

            if (string.IsNullOrWhiteSpace(definition.Script))
            {
                violations.Add(new ValidationViolation(
                    "L1-REQ-003",
                    "기본 실행 명령은 필수입니다.",
                    nameof(definition.Script)));
            }

            AddCommandViolations(violations, definition.Command);

            return new ValidationResult(violations);
        }

        private static void AddCommandViolations(List<ValidationViolation> violations, IReadOnlyList<string> command)
        {
            if (command.Any(string.IsNullOrWhiteSpace))
            {
                violations.Add(new ValidationViolation(
                    "L1-REQ-018",
                    "런타임 커맨드 오버라이드는 비어 있는 항목을 포함할 수 없습니다.",
                    nameof(ToolDefinition.Command)));
            }
        }
    }
}
