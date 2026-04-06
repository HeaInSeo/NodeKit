namespace NodeKit.Policy
{
    /// <summary>DockGuard 정책 위반 항목.</summary>
    public class PolicyViolation
    {
        /// <summary>DockGuard 규칙 ID (예: "DFM001").</summary>
        public string RuleId { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public PolicyViolation(string ruleId, string message)
        {
            RuleId = ruleId;
            Message = message;
        }
    }
}
