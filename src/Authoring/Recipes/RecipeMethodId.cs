namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// User-facing recipe authoring method. Not 1:1 with RecipeKind —
    /// Package resolves to Conda or Micromamba depending on PackageEngine.
    /// See RecipeKindResolver and
    /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md Section 4.
    /// </summary>
    internal enum RecipeMethodId
    {
        Container,
        Package,
        Mirror,
        Source,
        Dockerfile,

        /// <summary>
        /// §13 R22-B. Resolves to RecipeKind.SourceBuildStructured. This is the
        /// default source path: the Guided source clue and the Quick-setup
        /// source-archive recommendation both land here (generic/minimal
        /// profiles), and `--method source-structured` reaches it
        /// non-interactively. Only the custom ("advanced") build/runtime
        /// profile is an explicit opt-in. See
        /// docs/NODEKIT_SOURCEBUILD_STRUCTURED_INTENT_DESIGN.md.
        /// </summary>
        SourceStructured,
    }
}
