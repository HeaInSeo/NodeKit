using System;
using System.Collections.Generic;
using System.Linq;
using NodeKit.Authoring;

namespace NodeKit.Validation
{
    /// <summary>
    /// L1 필수 필드 및 기본 I/O 구조 검증기.
    /// </summary>
    internal class RequiredFieldsValidator : IValidator
    {
        private static readonly HashSet<string> _validIoShapes = new(StringComparer.Ordinal)
        {
            "single",
            "pair",
        };

        private static readonly HashSet<string> _validOutputClasses = new(StringComparer.Ordinal)
        {
            "primary",
            "secondary",
        };

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
                    "실행 스크립트는 필수입니다.",
                    nameof(definition.Script)));
            }

            if (definition.Inputs.Count == 0)
            {
                violations.Add(new ValidationViolation(
                    "L1-REQ-004",
                    "최소 1개의 input 이름이 필요합니다.",
                    nameof(definition.Inputs)));
            }

            if (definition.Outputs.Count == 0)
            {
                violations.Add(new ValidationViolation(
                    "L1-REQ-005",
                    "최소 1개의 output 이름이 필요합니다.",
                    nameof(definition.Outputs)));
            }

            AddInvalidIoViolations(violations, definition.Inputs.Select(input => input.Name), "Input", nameof(definition.Inputs), "L1-REQ-006", "L1-REQ-007");
            AddInvalidIoViolations(violations, definition.Outputs.Select(output => output.Name), "Output", nameof(definition.Outputs), "L1-REQ-008", "L1-REQ-009");
            AddInputContractViolations(violations, definition.Inputs);
            AddOutputContractViolations(violations, definition.Outputs);
            AddCommandViolations(violations, definition.Command);

            return new ValidationResult(violations);
        }

        private static void AddInvalidIoViolations(
            List<ValidationViolation> violations,
            IEnumerable<string?> names,
            string label,
            string field,
            string emptyRuleId,
            string duplicateRuleId)
        {
            var normalizedNames = names.Select(name => name?.Trim() ?? string.Empty).ToList();

            if (normalizedNames.Any(string.IsNullOrWhiteSpace))
            {
                violations.Add(new ValidationViolation(
                    emptyRuleId,
                    $"{label} 이름은 비어 있을 수 없습니다.",
                    field));
            }

            var duplicate = normalizedNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .GroupBy(name => name, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);

            if (duplicate != null)
            {
                violations.Add(new ValidationViolation(
                    duplicateRuleId,
                    $"{label} 이름 '{duplicate.Key}'이(가) 중복되었습니다.",
                    field));
            }
        }

        private static void AddInputContractViolations(List<ValidationViolation> violations, List<ToolInput> inputs)
        {
            for (var index = 0; index < inputs.Count; index++)
            {
                var input = inputs[index];
                if (string.IsNullOrWhiteSpace(input.Role))
                {
                    violations.Add(new ValidationViolation(
                        "L1-REQ-011",
                        $"Input '{DisplayIoName(input.Name, index)}' role은 필수입니다.",
                        nameof(ToolDefinition.Inputs)));
                }

                if (string.IsNullOrWhiteSpace(input.Format))
                {
                    violations.Add(new ValidationViolation(
                        "L1-REQ-012",
                        $"Input '{DisplayIoName(input.Name, index)}' format은 필수입니다.",
                        nameof(ToolDefinition.Inputs)));
                }

                if (!_validIoShapes.Contains(input.Shape))
                {
                    violations.Add(new ValidationViolation(
                        "L1-REQ-013",
                        $"Input '{DisplayIoName(input.Name, index)}' shape은 single 또는 pair여야 합니다.",
                        nameof(ToolDefinition.Inputs)));
                }
            }
        }

        private static void AddOutputContractViolations(List<ValidationViolation> violations, List<ToolOutput> outputs)
        {
            for (var index = 0; index < outputs.Count; index++)
            {
                var output = outputs[index];
                if (string.IsNullOrWhiteSpace(output.Role))
                {
                    violations.Add(new ValidationViolation(
                        "L1-REQ-014",
                        $"Output '{DisplayIoName(output.Name, index)}' role은 필수입니다.",
                        nameof(ToolDefinition.Outputs)));
                }

                if (string.IsNullOrWhiteSpace(output.Format))
                {
                    violations.Add(new ValidationViolation(
                        "L1-REQ-015",
                        $"Output '{DisplayIoName(output.Name, index)}' format은 필수입니다.",
                        nameof(ToolDefinition.Outputs)));
                }

                if (!_validIoShapes.Contains(output.Shape))
                {
                    violations.Add(new ValidationViolation(
                        "L1-REQ-016",
                        $"Output '{DisplayIoName(output.Name, index)}' shape은 single 또는 pair여야 합니다.",
                        nameof(ToolDefinition.Outputs)));
                }

                if (!_validOutputClasses.Contains(output.Class))
                {
                    violations.Add(new ValidationViolation(
                        "L1-REQ-017",
                        $"Output '{DisplayIoName(output.Name, index)}' class는 primary 또는 secondary여야 합니다.",
                        nameof(ToolDefinition.Outputs)));
                }
            }
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

        private static string DisplayIoName(string? name, int index) =>
            string.IsNullOrWhiteSpace(name) ? $"#{index + 1}" : name.Trim();
    }
}
