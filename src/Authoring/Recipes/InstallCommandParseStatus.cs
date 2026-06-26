namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Three-state result of InstallCommandParser. See
    /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_V0.9.2.md Section 9.2.
    /// </summary>
    internal enum InstallCommandParseStatus
    {
        Parsed,
        PartiallyParsed,
        Failed,
    }
}
