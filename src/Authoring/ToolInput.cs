namespace NodeKit.Authoring
{
    /// <summary>Tool의 named input 포트 계약.</summary>
    public class ToolInput
    {
        /// <summary>포트 이름 (예: "reads").</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>의미적 역할 (예: "sample-fastq"). 포트 호환성 검증 기준.</summary>
        public string Role { get; set; } = string.Empty;

        /// <summary>데이터 형식 (예: "fastq", "fasta", "bam").</summary>
        public string Format { get; set; } = string.Empty;

        /// <summary>"single" 또는 "pair".</summary>
        public string Shape { get; set; } = "single";

        /// <summary>필수 입력 여부.</summary>
        public bool Required { get; set; } = true;
    }
}
