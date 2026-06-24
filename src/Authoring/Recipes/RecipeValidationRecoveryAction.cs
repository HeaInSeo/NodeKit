using System.Collections.Generic;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>One suggested fix for one or more final-validation violations — see design doc Section 20.2.</summary>
    internal sealed record RecipeValidationRecoveryAction(
        string Label,
        RecoveryActionKind Kind,
        IReadOnlyList<string> RelatedFields,
        LocalizedText Description,
        LocalizedText BeginnerHint);
}
