using System.Collections.Generic;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Preview of what a ChangeMethod call would do, shown before the user
    /// confirms — see design doc Section 15.2 and Section 16.
    /// </summary>
    internal sealed record ChangeMethodPreview(
        RecipeMethodId CurrentMethod,
        RecipeMethodId NextMethod,
        IReadOnlyList<string> PreservedFields,
        IReadOnlyList<string> FieldsRequiringRevalidation,
        IReadOnlyList<string> DiscardedFields,
        IReadOnlyList<string> ResetMetadata);
}
