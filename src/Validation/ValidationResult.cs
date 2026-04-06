using System.Collections.Generic;
using System.Linq;

namespace NodeKit.Validation
{
    /// <summary>검증 결과. 위반 목록이 비어있으면 통과.</summary>
    public class ValidationResult
    {
        public static readonly ValidationResult Pass = new(new List<ValidationViolation>());

        public IReadOnlyList<ValidationViolation> Violations { get; }

        public bool IsValid => Violations.Count == 0;

        public ValidationResult(IEnumerable<ValidationViolation> violations)
        {
            Violations = violations.ToList().AsReadOnly();
        }

        public static ValidationResult Fail(string ruleId, string message, string? field = null)
            => new(new[] { new ValidationViolation(ruleId, message, field) });

        public static ValidationResult Combine(IEnumerable<ValidationResult> results)
        {
            var all = results.SelectMany(r => r.Violations).ToList();
            return new ValidationResult(all);
        }
    }
}
