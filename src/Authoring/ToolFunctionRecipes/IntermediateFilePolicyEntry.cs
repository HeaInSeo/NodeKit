namespace NodeKit.Authoring.ToolFunctionRecipes
{
    internal enum IntermediateFilePolicyKind
    {
        Ephemeral,
        Cache,
        Checkpoint,
        SidecarOutput,
        SensitiveTemp,
    }

    /// <summary>중간/숨은 파일 보존 정책 선언 (FR-011).</summary>
    internal sealed class IntermediateFilePolicyEntry
    {
        public string PathOrPattern { get; set; } = string.Empty;

        public IntermediateFilePolicyKind Policy { get; set; }

        public void Normalize() => PathOrPattern ??= string.Empty;
    }
}
