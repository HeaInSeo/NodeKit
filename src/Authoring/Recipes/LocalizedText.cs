using System.Collections.Generic;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Locale-keyed display text for recipe field metadata. See
    /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md Section 7.1.
    /// </summary>
    internal sealed record LocalizedText(IReadOnlyDictionary<string, string> Values)
    {
        public string Get(string locale, string fallbackLocale = "en")
        {
            if (Values.TryGetValue(locale, out var value))
            {
                return value;
            }

            return Values.TryGetValue(fallbackLocale, out var fallback) ? fallback : string.Empty;
        }
    }
}
