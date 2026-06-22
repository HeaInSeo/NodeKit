namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// NodeKit recipe authoring discriminator. NodeKit-only concept — NodeVault's
    /// raw_spec contract has no equivalent schema; see
    /// docs/NODEKIT_CLI_RECIPE_SPEC_DRAFT.md.
    /// </summary>
    internal enum RecipeVariant
    {
        Conda,
        Micromamba,
        BioContainer,
        SourceBuild,
        PackageMirror,
        DockerfileFallback,
    }
}
