namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Result of ImageReferenceNormalizer.Normalize.
    /// See docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_V0.9.2.md Sections 10.3–10.5.
    /// </summary>
    internal sealed record ImageReferenceNormalizeResult(
        ImageReferenceNormalizeStatus Status,
        string RepositoryAndTag,
        string? Digest,
        string? CanonicalUri,
        string? EmbeddedDigest,
        string? SeparateDigest);
}
