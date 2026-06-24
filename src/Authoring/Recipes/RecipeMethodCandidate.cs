namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// One alternative method shown alongside a recommendation, in priority
    /// order. See
    /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md Section 11.1.
    /// </summary>
    internal sealed record RecipeMethodCandidate(
        RecipeMethodId Method,
        string Label,
        string Reason,
        int Priority);
}
