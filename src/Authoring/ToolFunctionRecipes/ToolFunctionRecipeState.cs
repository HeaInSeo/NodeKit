namespace NodeKit.Authoring.ToolFunctionRecipes
{
    /// <summary>
    /// ToolFunctionRecipe lifecycle state (data-model.md State 섹션). This
    /// feature only implements the Draft&lt;-&gt;Ready transition (User Story 1);
    /// Submitted/Built/Validated/Approved are reserved for NodeVault/NodeSentinel
    /// results and are never produced locally (FR-020).
    /// </summary>
    internal enum ToolFunctionRecipeState
    {
        Draft,
        Ready,
        Submitted,
        Built,
        Validated,
        Approved,
    }
}
