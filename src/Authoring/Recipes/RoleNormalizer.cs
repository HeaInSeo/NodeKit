using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Normalizes a custom role input to snake_case — see design doc
    /// Section 14.3. Always informed, never requires confirmation.
    /// </summary>
    internal static class RoleNormalizer
    {
        public static IReadOnlyList<string> KnownRoles { get; } =
            new[] { "reads", "reference", "alignment", "variants", "index", "log", "metrics" };

        public static RecipeValueNormalizationResult Normalize(string input)
        {
            var trimmed = input.Trim();
#pragma warning disable CA1308 // lowercase is the actual normalized output value, not a comparison key
            var snakeCase = Regex.Replace(trimmed.ToLowerInvariant(), @"\s+", "_");
#pragma warning restore CA1308

            if (snakeCase == input)
            {
                return new RecipeValueNormalizationResult(input, snakeCase, RecipeValueNormalizationAction.Unchanged, null);
            }

            return new RecipeValueNormalizationResult(
                input,
                snakeCase,
                RecipeValueNormalizationAction.Applied,
                $"입력한 `{input}`을 표준 role 값 `{snakeCase}`로 처리합니다.");
        }
    }
}
