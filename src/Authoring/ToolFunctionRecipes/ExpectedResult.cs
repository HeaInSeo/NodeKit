namespace NodeKit.Authoring.ToolFunctionRecipes
{
    /// <summary>출력 포트별 예상 결과 선언 (FR-010). 실제 비교/판정은 NodeSentinel 소관.</summary>
    internal sealed class ExpectedResult
    {
        public string OutputPortName { get; set; } = string.Empty;

        public string ExpectedValueOrRule { get; set; } = string.Empty;

        public void Normalize()
        {
            OutputPortName ??= string.Empty;
            ExpectedValueOrRule ??= string.Empty;
        }
    }
}
