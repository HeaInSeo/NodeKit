namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Beginner-facing description of one RecipeMethodId — what it is, what
    /// the user needs to prepare, and any standing caveat. See
    /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md Section 4.2
    /// and the per-method preparation text in Sections 5.6 and 11.3.
    /// </summary>
    internal sealed record RecipeMethodInfo(
        RecipeMethodId Method,
        LocalizedText Label,
        LocalizedText Description,
        LocalizedText PreparationHint,
        LocalizedText? Warning);
}
