using System.Collections.Generic;
using NodeKit.Validation;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Built from final-validation failures while the session stays alive —
    /// see design doc Section 20.3 and Section 19.3 step 7.
    /// </summary>
    internal sealed record RecipeValidationRecoveryPlan(
        IReadOnlyList<RecipeValidationRecoveryAction> Actions,
        IReadOnlyList<ValidationViolation> UnmappedViolations);
}
