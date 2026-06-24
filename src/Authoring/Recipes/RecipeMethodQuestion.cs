namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// One recommender question. Key matches a RecipeMethodAnswers property
    /// name. See
    /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md Section 10.3.
    /// </summary>
    internal sealed record RecipeMethodQuestion(string Key, LocalizedText Prompt);
}
