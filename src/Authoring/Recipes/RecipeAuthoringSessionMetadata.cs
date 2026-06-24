namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Method-specific warning/acknowledgement state for a session. Never
    /// written to RecipeDocument or recipe.json — see design doc Section 17.3.
    /// </summary>
    internal sealed record RecipeAuthoringSessionMetadata
    {
        public bool DockerfileWarningAccepted { get; init; }

        public bool ImageTagWarningShown { get; init; }

        public bool ImageTagWarningAccepted { get; init; }
    }
}
