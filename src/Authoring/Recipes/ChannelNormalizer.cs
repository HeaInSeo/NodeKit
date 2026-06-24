using System;
using System.Collections.Generic;
using System.Linq;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Normalizes a custom channel input — see design doc Section 14.4. A
    /// channel within edit distance 2 of a known channel is only ever
    /// suggested, never applied silently; anything else (e.g. an internal
    /// mirror channel name) passes through unchanged.
    /// </summary>
    internal static class ChannelNormalizer
    {
        private const int TypoSuggestionMaxDistance = 2;

        public static IReadOnlyList<string> KnownChannels { get; } =
            new[] { "bioconda", "conda-forge", "defaults" };

        public static RecipeValueNormalizationResult Normalize(string input)
        {
            var trimmed = input.Trim();

            var exactMatch = KnownChannels.FirstOrDefault(c => string.Equals(c, trimmed, StringComparison.OrdinalIgnoreCase));
            if (exactMatch != null)
            {
                return exactMatch == trimmed
                    ? new RecipeValueNormalizationResult(input, exactMatch, RecipeValueNormalizationAction.Unchanged, null)
                    : new RecipeValueNormalizationResult(
                        input,
                        exactMatch,
                        RecipeValueNormalizationAction.Applied,
                        $"입력한 `{input}`을 표준 channel 값 `{exactMatch}`로 처리합니다.");
            }

#pragma warning disable CA1308 // comparison key only — KnownChannels entries are stored lowercase
            var trimmedLower = trimmed.ToLowerInvariant();
#pragma warning restore CA1308

            var nearest = KnownChannels
                .Select(c => (Channel: c, Distance: LevenshteinDistance(trimmedLower, c)))
                .OrderBy(c => c.Distance)
                .First();

            if (nearest.Distance <= TypoSuggestionMaxDistance)
            {
                return new RecipeValueNormalizationResult(
                    input,
                    nearest.Channel,
                    RecipeValueNormalizationAction.SuggestedPendingConfirmation,
                    $"`{input}`는 `{nearest.Channel}`를 의미하나요? [Y/n]");
            }

            return new RecipeValueNormalizationResult(input, trimmed, RecipeValueNormalizationAction.Unchanged, null);
        }

        private static int LevenshteinDistance(string a, string b)
        {
            var distances = new int[a.Length + 1][];
            for (var i = 0; i <= a.Length; i++)
            {
                distances[i] = new int[b.Length + 1];
                distances[i][0] = i;
            }

            for (var j = 0; j <= b.Length; j++)
            {
                distances[0][j] = j;
            }

            for (var i = 1; i <= a.Length; i++)
            {
                for (var j = 1; j <= b.Length; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    distances[i][j] = Math.Min(
                        Math.Min(distances[i - 1][j] + 1, distances[i][j - 1] + 1),
                        distances[i - 1][j - 1] + cost);
                }
            }

            return distances[a.Length][b.Length];
        }
    }
}
