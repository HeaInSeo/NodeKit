namespace NodeKit.Authoring.Recipes
{
    /// <summary>Result of normalizing a single custom format/role/channel input — see design doc Section 14.</summary>
    internal sealed record RecipeValueNormalizationResult(
        string OriginalInput,
        string Value,
        RecipeValueNormalizationAction Action,
        string? Message);
}
