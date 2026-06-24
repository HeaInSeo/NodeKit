using System;
using System.Collections.Generic;
using NodeKit.Validation;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Single recipe field's authoring metadata: requirement tier, default,
    /// display text, and how a value applies to a RecipeDocument. See
    /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md Section 7.4.
    /// DefaultValue is only meaningful when Requirement == Defaulted.
    /// </summary>
    internal sealed record RecipeFieldDescriptor(
        string Name,
        RecipeFieldType Type,
        RecipeFieldRequirement Requirement,
        object? DefaultValue,
        LocalizedText Label,
        LocalizedText Help,
        IReadOnlyList<string> Examples,
        IReadOnlyList<RecipeChoice> Choices,
        Action<RecipeDocument, object> Apply,
        Func<object, ValidationViolation?>? QuickValidate = null);
}
