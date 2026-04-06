namespace NodeKit.Validation
{
    /// <summary>단일 검증 위반 항목.</summary>
    public class ValidationViolation
    {
        /// <summary>규칙 ID (예: "L1-001", "DFM002").</summary>
        public string RuleId { get; set; } = string.Empty;

        /// <summary>사람이 읽을 수 있는 위반 메시지.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>위반이 발생한 필드 이름 (선택).</summary>
        public string? Field { get; set; }

        public ValidationViolation(string ruleId, string message, string? field = null)
        {
            RuleId = ruleId;
            Message = message;
            Field = field;
        }
    }
}
