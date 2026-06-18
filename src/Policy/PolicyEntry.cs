namespace NodeKit.Policy
{
    /// <summary>단일 정책 규칙 메타데이터.</summary>
    internal sealed class PolicyEntry
    {
        public string RuleId { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;
    }
}
