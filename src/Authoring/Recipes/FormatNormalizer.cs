using System.Collections.Generic;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Normalizes a custom format input — see design doc Section 14.1-14.2.
    /// Trim/lowercase/leading-dot removal is applied (with a one-line
    /// notice); a known alias or a detected compression suffix (".gz") is
    /// only ever suggested, never applied silently.
    /// </summary>
    internal static class FormatNormalizer
    {
        private static readonly Dictionary<string, string> _aliasMap = new()
        {
            ["fq"] = "fastq",
        };

        public static IReadOnlyList<string> KnownFormats { get; } =
            new[] { "fastq", "fasta", "bam", "bai", "sam", "vcf", "bed", "txt" };

        public static RecipeValueNormalizationResult Normalize(string input)
        {
            var trimmed = input.Trim();
#pragma warning disable CA1308 // lowercase is the actual normalized output value, not a comparison key
            var withoutLeadingDot = trimmed.ToLowerInvariant().TrimStart('.');
#pragma warning restore CA1308

            var core = withoutLeadingDot;
            string? compressionSuffix = null;
            if (core.EndsWith(".gz", System.StringComparison.Ordinal))
            {
                compressionSuffix = ".gz";
                core = core[..^compressionSuffix.Length];
            }

            if (compressionSuffix != null)
            {
                var suggested = ResolveAlias(core);
                var compressionMessage = $"`{input}`에서 compression suffix `{compressionSuffix}`를 감지했습니다. v1 recipe에서는 compression을 별도 필드로 저장하지 않습니다. format을 `{suggested}`로 사용할까요? [Y/n]";
                return new RecipeValueNormalizationResult(
                    input,
                    suggested,
                    RecipeValueNormalizationAction.SuggestedPendingConfirmation,
                    compressionMessage);
            }

            if (_aliasMap.TryGetValue(core, out var aliasTarget))
            {
                return new RecipeValueNormalizationResult(
                    input,
                    aliasTarget,
                    RecipeValueNormalizationAction.SuggestedPendingConfirmation,
                    $"입력한 `{input}`을 표준 format 값 `{aliasTarget}`로 처리할까요? [Y/n]");
            }

            if (core == input)
            {
                return new RecipeValueNormalizationResult(input, core, RecipeValueNormalizationAction.Unchanged, null);
            }

            return new RecipeValueNormalizationResult(
                input,
                core,
                RecipeValueNormalizationAction.Applied,
                $"입력한 `{input}`을 표준 format 값 `{core}`로 처리합니다.");
        }

        private static string ResolveAlias(string core) =>
            _aliasMap.TryGetValue(core, out var target) ? target : core;
    }
}
