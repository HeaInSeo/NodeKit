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
        /// §13 R22-B. Advanced/opt-in method resolving to
        /// RecipeKind.SourceBuildStructured. Intentionally not wired into
        /// RecipeMethodRecommender/MethodRecommendationPresenter/BeginnerGuideFlow
        /// yet — reachable only via `nodekit recipe create --non-interactive
        /// --method source-structured`, not the interactive wizard. See
        /// docs/NODEKIT_SOURCEBUILD_STRUCTURED_INTENT_DESIGN.md.
        /// </summary>
        SourceStructured,
    }
}
