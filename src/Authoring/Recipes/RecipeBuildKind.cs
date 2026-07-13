namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// NodeKit recipe authoring discriminator. NodeKit-only concept — NodeVault's
    /// raw_spec contract has no equivalent schema; see
    /// docs/NODEKIT_CLI_RECIPE_SPEC_DRAFT.md.
    /// </summary>
    internal enum RecipeBuildKind
    {
        Conda,
        Micromamba,
        BioContainer,
        SourceBuild,
        PackageMirror,
        DockerfileFallback,

        /// <summary>
        /// §13 R22-B. Structured SourceBuild — separates the build environment
        /// from the runtime environment (BuildProfile/RuntimeProfile) instead
        /// of using one BaseImage for both roles. See
        /// docs/NODEKIT_SOURCEBUILD_STRUCTURED_INTENT_DESIGN.md. SourceBuild
        /// (legacy, single BaseImage) is unaffected and stays supported.
        /// </summary>
        SourceBuildStructured,
    }
}
