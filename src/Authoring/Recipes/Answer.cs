namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Tri-state answer for recommender questions. Unknown is neither
    /// evidence for nor against a method — it must not be treated like No.
    /// See docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md Section 10.1.
    /// </summary>
    internal enum Answer
    {
        Yes,
        No,
        Unknown,
    }
}
