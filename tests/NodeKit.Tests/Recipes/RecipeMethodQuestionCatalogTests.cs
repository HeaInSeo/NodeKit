using System.Linq;
using NodeKit.Authoring.Recipes;
using Xunit;

namespace NodeKit.Tests.Recipes
{
    public class RecipeMethodQuestionCatalogTests
    {
        [Fact]
        public void Questions_FollowFixedOrder()
        {
            var keys = RecipeMethodQuestionCatalog.Questions.Select(q => q.Key).ToList();

            Assert.Equal(
                new[]
                {
                    nameof(RecipeMethodAnswers.IsRestrictedNetwork),
                    nameof(RecipeMethodAnswers.HasInternalPackageMirror),
                    nameof(RecipeMethodAnswers.HasExistingContainerImage),
                    nameof(RecipeMethodAnswers.HasPackageInPublicChannels),
                    nameof(RecipeMethodAnswers.HasSourceArchiveAndChecksum),
                    nameof(RecipeMethodAnswers.HasExistingDockerfile),
                },
                keys);
        }

        [Fact]
        public void Questions_AllHaveNonEmptyPrompts()
        {
            Assert.All(RecipeMethodQuestionCatalog.Questions, q =>
            {
                Assert.NotEmpty(q.Prompt.Get("ko"));
                Assert.NotEmpty(q.Prompt.Get("en"));
            });
        }
    }
}
