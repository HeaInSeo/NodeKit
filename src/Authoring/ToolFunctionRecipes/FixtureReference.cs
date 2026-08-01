namespace NodeKit.Authoring.ToolFunctionRecipes
{
    /// <summary>Dry-run 샘플 데이터/fixture 참조 (FR-009). 실제 dry-run 실행은 NodeSentinel 소관.</summary>
    internal sealed class FixtureReference
    {
        public string LocalPath { get; set; } = string.Empty;

        public string ContentDigest { get; set; } = string.Empty;

        public void Normalize()
        {
            LocalPath ??= string.Empty;
            ContentDigest ??= string.Empty;
        }
    }
}
