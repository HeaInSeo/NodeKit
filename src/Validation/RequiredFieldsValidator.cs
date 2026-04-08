using System;
using System.Collections.Generic;
using System.Linq;
using NodeKit.Authoring;

namespace NodeKit.Validation
{
    /// <summary>
    /// L1 필수 필드 및 기본 I/O 구조 검증기.
    /// </summary>
    public class RequiredFieldsValidator : IValidator
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
    }
}
