using System.Collections.Generic;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Beginner-facing input shortcut that fills Role/Format/Shape without
    /// asking those fields directly — see design doc Section 13.2.
    /// </summary>
    internal sealed record ToolInputPreset(
        string Id,
        LocalizedText Label,
        LocalizedText Description,
        string Role,
        string Format,
        string Shape,
        IReadOnlyList<string> Examples);
}
