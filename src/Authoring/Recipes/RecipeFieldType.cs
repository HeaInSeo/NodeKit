namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Recipe field input shape. See
    /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md Section 7.2.
    /// </summary>
    internal enum RecipeFieldType
    {
        Scalar,
        Choice,
        StringList,
        InputList,
        OutputList,
    }
}
