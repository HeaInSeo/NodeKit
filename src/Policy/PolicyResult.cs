using System.Collections.Generic;
using System.Linq;

namespace NodeKit.Policy
{
    /// <summary>DockGuard 정책 검사 결과.</summary>
    public class PolicyResult
    {
        public static readonly PolicyResult Pass = new(new List<PolicyViolation>());

        public IReadOnlyList<PolicyViolation> Violations { get; }

        public bool IsAllowed => Violations.Count == 0;

        public PolicyResult(IEnumerable<PolicyViolation> violations)
        {
            Violations = violations.ToList().AsReadOnly();
        }
    }
}
