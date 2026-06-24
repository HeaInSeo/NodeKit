using NodeKit.Authoring.Recipes;
using Xunit;

namespace NodeKit.Tests.Recipes
{
    public class FormatNormalizerTests
    {
        [Fact]
        public void Normalize_UppercaseKnownFormat_AppliesLowercase()
        {
            var result = FormatNormalizer.Normalize("FASTQ");

            Assert.Equal("fastq", result.Value);
            Assert.Equal(RecipeValueNormalizationAction.Applied, result.Action);
            Assert.NotNull(result.Message);
        }

        [Fact]
        public void Normalize_LeadingDot_AppliesDotRemoval()
        {
            var result = FormatNormalizer.Normalize(".fastq");

            Assert.Equal("fastq", result.Value);
            Assert.Equal(RecipeValueNormalizationAction.Applied, result.Action);
        }

        [Fact]
        public void Normalize_KnownAlias_SuggestsPendingConfirmation()
        {
            var result = FormatNormalizer.Normalize("fq");

            Assert.Equal("fastq", result.Value);
            Assert.Equal(RecipeValueNormalizationAction.SuggestedPendingConfirmation, result.Action);
        }

        [Fact]
        public void Normalize_CompressionSuffixOnKnownFormat_SuggestsPendingConfirmation()
        {
            var result = FormatNormalizer.Normalize("fastq.gz");

            Assert.Equal("fastq", result.Value);
            Assert.Equal(RecipeValueNormalizationAction.SuggestedPendingConfirmation, result.Action);
            Assert.Contains("compression", result.Message);
        }

        [Fact]
        public void Normalize_VcfGz_SuggestsVcf()
        {
            var result = FormatNormalizer.Normalize("vcf.gz");

            Assert.Equal("vcf", result.Value);
            Assert.Equal(RecipeValueNormalizationAction.SuggestedPendingConfirmation, result.Action);
        }

        [Fact]
        public void Normalize_AlreadyNormalizedFormat_Unchanged()
        {
            var result = FormatNormalizer.Normalize("fastq");

            Assert.Equal("fastq", result.Value);
            Assert.Equal(RecipeValueNormalizationAction.Unchanged, result.Action);
            Assert.Null(result.Message);
        }

        [Fact]
        public void KnownFormats_ContainsExpectedEightFormats()
        {
            Assert.Equal(
                new[] { "fastq", "fasta", "bam", "bai", "sam", "vcf", "bed", "txt" },
                FormatNormalizer.KnownFormats);
        }
    }
}
