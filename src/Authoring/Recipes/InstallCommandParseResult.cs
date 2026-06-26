using System.Collections.Generic;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Result of InstallCommandParser.Parse. See
    /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_V0.9.2.md Section 9.2/9.3.
    /// </summary>
    internal sealed record InstallCommandParseResult(
        InstallCommandParseStatus Status,
        string? PackageEngine,
        IReadOnlyList<string> Channels,
        IReadOnlyList<string> Packages,
        IReadOnlyList<string> Missing,
        IReadOnlyList<string> Warnings,
        string? OriginalCommand);
}
