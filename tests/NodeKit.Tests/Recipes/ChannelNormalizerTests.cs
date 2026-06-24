using NodeKit.Authoring.Recipes;
using Xunit;

namespace NodeKit.Tests.Recipes
{
    public class ChannelNormalizerTests
    {
        [Fact]
        public void Normalize_KnownChannel_Unchanged()
        {
            var result = ChannelNormalizer.Normalize("bioconda");

            Assert.Equal("bioconda", result.Value);
            Assert.Equal(RecipeValueNormalizationAction.Unchanged, result.Action);
        }

        [Fact]
        public void Normalize_Typo_SuggestsKnownChannel()
        {
            var result = ChannelNormalizer.Normalize("defalts");

            Assert.Equal("defaults", result.Value);
            Assert.Equal(RecipeValueNormalizationAction.SuggestedPendingConfirmation, result.Action);
        }

        [Fact]
        public void Normalize_CustomChannelName_PassesThroughUnchanged()
        {
            var result = ChannelNormalizer.Normalize("research-mirror");

            Assert.Equal("research-mirror", result.Value);
            Assert.Equal(RecipeValueNormalizationAction.Unchanged, result.Action);
        }

        [Fact]
        public void Normalize_InternalChannelName_PassesThroughUnchanged()
        {
            var result = ChannelNormalizer.Normalize("internal-bioconda");

            Assert.Equal("internal-bioconda", result.Value);
            Assert.Equal(RecipeValueNormalizationAction.Unchanged, result.Action);
        }

        [Fact]
        public void KnownChannels_ContainsExpectedThreeChannels()
        {
            Assert.Equal(new[] { "bioconda", "conda-forge", "defaults" }, ChannelNormalizer.KnownChannels);
        }
    }
}
