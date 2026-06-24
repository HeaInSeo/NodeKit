namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// User answers to the fixed recommender question set. See
    /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md Section 10.2.
    /// HasPackageInPublicChannels is existence, not reachability — reachability
    /// is judged separately by the internal-network gate.
    /// </summary>
    internal sealed record RecipeMethodAnswers(
        Answer IsRestrictedNetwork,
        Answer HasInternalPackageMirror,
        Answer HasExistingContainerImage,
        Answer HasPackageInPublicChannels,
        Answer HasSourceArchiveAndChecksum,
        Answer HasExistingDockerfile);
}
