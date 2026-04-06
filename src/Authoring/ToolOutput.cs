namespace NodeKit.Authoring
{
    /// <summary>Tool의 named output 선언.</summary>
    public class ToolOutput
    {
        /// <summary>출력 이름 (예: "output.bam").</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>설명 (선택).</summary>
        public string Description { get; set; } = string.Empty;
    }
}
