using System.Collections.Generic;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Method-centric recommendation result. Does not resolve the internal
    /// RecipeBuildKind — see
    /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md Section 11.1.
    /// </summary>
    internal sealed record RecipeMethodRecommendation(
        RecipeMethodId? RecommendedMethod,
        string Reason,
        IReadOnlyList<string> Evidence,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<RecipeMethodCandidate> Alternatives,
        IReadOnlyList<string> MissingInformation);
}
