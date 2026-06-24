namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// User-facing recipe authoring method. Not 1:1 with RecipeBuildKind —
    /// Package resolves to Conda or Micromamba depending on PackageEngine.
    /// See RecipeBuildKindResolver and
    /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md Section 4.
    /// </summary>
    internal enum RecipeMethodId
    {
        Container,
        Package,
        Mirror,
        Source,
        Dockerfile,
    }
}
