namespace NodeKit.Authoring.ToolFunctionRecipes
{
    /// <summary>Allowlisted environment variable entry — name plus where its value comes from.</summary>
    internal sealed class EnvironmentEntry
    {
        public string Name { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;
    }
}
