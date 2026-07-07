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
    /// ClearList is only meaningful for list-typed fields — RecipeAuthoringSession.Build()
    /// uses it to re-render a list field from scratch after EditListItem/DeleteListItem,
    /// since Apply only knows how to Add(), not replace, an item.
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
        Func<object, ValidationViolation?>? QuickValidate = null,
        Action<RecipeDocument>? ClearList = null,

        // Scalar 필드 전용. true면 대화형 프롬프트가 한 줄이 아니라 빈 줄로
        // 종료되는 여러 줄 입력을 받는다 — DockerfileContent처럼 값 자체가
        // 여러 줄(각 Dockerfile instruction이 별도 줄)이어야 하는 필드용.
        bool SupportsMultilineInput = false);
}
