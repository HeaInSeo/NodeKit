namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// One selectable option for a RecipeFieldType.Choice field. See
    /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md Section 7.3.
    /// </summary>
    internal sealed record RecipeChoice(string Value, LocalizedText Label, LocalizedText Description);
}
