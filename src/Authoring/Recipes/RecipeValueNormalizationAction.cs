namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// How a custom format/role/channel input was treated by normalization —
    /// see design doc Section 14. Normalization is never silent: Applied
    /// still carries a one-line message, and a typo/compression-suffix
    /// match always requires user confirmation rather than being applied.
    /// </summary>
    internal enum RecipeValueNormalizationAction
    {
        Unchanged,
        Applied,
        SuggestedPendingConfirmation,
    }
}
