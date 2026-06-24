namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Authoring-level field requirement tier. Governs whether
    /// RecipeAuthoringSession can progress/complete — separate from
    /// CLAUDE.md Section 3's final L1 reproducibility policy, which always
    /// applies regardless of this tier. Any field an L1 hard rule blocks
    /// (unpinned tag, missing digest, unpinned package version) must be
    /// Required here, never Recommended/Optional. See
    /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md Section 6
    /// and Section 19.3.
    /// </summary>
    internal enum RecipeFieldRequirement
    {
        /// <summary>Missing value is a blocking violation.</summary>
        Required,

        /// <summary>Missing value is auto-filled by Build().</summary>
        Defaulted,

        /// <summary>Missing value is fine.</summary>
        Optional,

        /// <summary>Missing value shows a non-blocking warning.</summary>
        Recommended,
    }
}
