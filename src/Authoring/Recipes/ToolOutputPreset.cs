using System.Collections.Generic;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Beginner-facing output shortcut that fills Role/Format/Class without
    /// asking those fields directly — see design doc Section 13.3.
    /// </summary>
    internal sealed record ToolOutputPreset(
        string Id,
        LocalizedText Label,
        LocalizedText Description,
        string Role,
        string Format,
        string Class,
        IReadOnlyList<string> Examples);
}
