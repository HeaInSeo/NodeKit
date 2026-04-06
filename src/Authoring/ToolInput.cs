namespace NodeKit.Authoring
{
    /// <summary>Tool의 named input 선언.</summary>
    public class ToolInput
    {
        /// <summary>입력 이름 (예: "input.fastq.R1").</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>설명 (선택).</summary>
        public string Description { get; set; } = string.Empty;
    }
}
