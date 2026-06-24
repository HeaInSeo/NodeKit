using System.Collections.Generic;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Review/debug/UI-display snapshot of an authoring session. Works on
    /// incomplete sessions, never applies Defaulted values, never runs
    /// validation. Not a final recipe — see design doc Section 18.1.
    /// InvalidatedFields lists fields ChangeMethod preserved but marked for
    /// re-confirmation — see design doc Section 16.4.
    /// </summary>
    internal sealed record RecipeAuthoringSnapshot(
        RecipeMethodId? SelectedMethod,
        IReadOnlyList<RecipeFieldValueSummary> Values,
        IReadOnlyList<string> MissingRequiredFields,
        IReadOnlyList<string> DefaultedFields,
        IReadOnlyList<string> RecommendedWarnings,
        IReadOnlyList<string> InvalidatedFields);
}
