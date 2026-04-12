namespace NodeKit.Authoring
{
    /// <summary>Tool의 named output 포트 계약.</summary>
    public class ToolOutput
    {
        /// <summary>포트 이름 (예: "aligned_bam").</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>의미적 역할 (예: "aligned-bam"). 포트 호환성 검증 기준.</summary>
        public string Role { get; set; } = string.Empty;

        /// <summary>데이터 형식 (예: "bam", "text").</summary>
        public string Format { get; set; } = string.Empty;

        /// <summary>"single" 또는 "pair".</summary>
        public string Shape { get; set; } = "single";

        /// <summary>"primary" (주 산출물) 또는 "secondary" (로그 등 보조).</summary>
        public string Class { get; set; } = "primary";
    }
}
