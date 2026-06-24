namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Shape of a recovery action a RecipeValidationRecoveryPlan can suggest —
    /// see design doc Section 20.1.
    /// </summary>
    internal enum RecoveryActionKind
    {
        EditSingleField,
        EditRelatedFields,
        ReviewSection,
        ShowExplanationOnly,
    }
}
