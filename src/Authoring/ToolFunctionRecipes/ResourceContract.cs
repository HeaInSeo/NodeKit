namespace NodeKit.Authoring.ToolFunctionRecipes
{
    /// <summary>
    /// Enforced tier 자원 계약만 담는다 (FR-013). Observed/Recommended tier
    /// 필드는 FR-014에 따라 이 타입에 의도적으로 정의하지 않는다 — 그래서
    /// 사용자가 그 값을 입력할 방법 자체가 스키마 수준에서 없다
    /// (data-model.md ResourceContract "명시적으로 포함하지 않는 필드" 참고).
    /// </summary>
    internal sealed class ResourceContract
    {
        public string CpuRequest { get; set; } = string.Empty;

        public string CpuLimit { get; set; } = string.Empty;

        public string MemoryRequest { get; set; } = string.Empty;

        public string MemoryLimit { get; set; } = string.Empty;

        public string StorageRequest { get; set; } = string.Empty;

        public string StorageLimit { get; set; } = string.Empty;

        public int? MaxExecutionTimeSeconds { get; set; }

        public int? Parallelism { get; set; }

        public void Normalize()
        {
            CpuRequest ??= string.Empty;
            CpuLimit ??= string.Empty;
            MemoryRequest ??= string.Empty;
            MemoryLimit ??= string.Empty;
            StorageRequest ??= string.Empty;
            StorageLimit ??= string.Empty;
        }
    }
}
