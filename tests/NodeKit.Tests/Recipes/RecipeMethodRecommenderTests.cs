using System.Linq;
using NodeKit.Authoring.Recipes;
using Xunit;

namespace NodeKit.Tests.Recipes
{
    public class RecipeMethodRecommenderTests
    {
        [Fact]
        public void RestrictedNetworkYes_MirrorYes_RecommendsMirror()
        {
            var answers = new RecipeMethodAnswers(
                IsRestrictedNetwork: Answer.Yes,
                HasInternalPackageMirror: Answer.Yes,
                HasExistingContainerImage: Answer.Unknown,
                HasPackageInPublicChannels: Answer.Unknown,
                HasSourceArchiveAndChecksum: Answer.Unknown,
                HasExistingDockerfile: Answer.Unknown);

            var result = RecipeMethodRecommender.Recommend(answers);

            Assert.Equal(RecipeMethodId.Mirror, result.RecommendedMethod);
            Assert.DoesNotContain(result.Alternatives, c => c.Method == RecipeMethodId.Package);
        }

        [Fact]
        public void RestrictedNetworkYes_MirrorUnknown_PublicPackageYes_NoRecommendation_MirrorSourceContainerDockerfileCandidates()
        {
            var answers = new RecipeMethodAnswers(
                IsRestrictedNetwork: Answer.Yes,
                HasInternalPackageMirror: Answer.Unknown,
                HasExistingContainerImage: Answer.Unknown,
                HasPackageInPublicChannels: Answer.Yes,
                HasSourceArchiveAndChecksum: Answer.Unknown,
                HasExistingDockerfile: Answer.Unknown);

            var result = RecipeMethodRecommender.Recommend(answers);

            Assert.Null(result.RecommendedMethod);
            Assert.Equal(
                new[] { RecipeMethodId.Mirror, RecipeMethodId.Source, RecipeMethodId.Container, RecipeMethodId.Dockerfile },
                result.Alternatives.Select(c => c.Method).ToArray());
            Assert.DoesNotContain(result.Alternatives, c => c.Method == RecipeMethodId.Package);
        }

        [Fact]
        public void RestrictedNetworkYes_SourceAndContainerAlternatives_IncludeExternalDependencyWarning()
        {
            var answers = new RecipeMethodAnswers(
                IsRestrictedNetwork: Answer.Yes,
                HasInternalPackageMirror: Answer.No,
                HasExistingContainerImage: Answer.Unknown,
                HasPackageInPublicChannels: Answer.Unknown,
                HasSourceArchiveAndChecksum: Answer.Unknown,
                HasExistingDockerfile: Answer.Unknown);

            var result = RecipeMethodRecommender.Recommend(answers);

            Assert.Contains(result.Alternatives, c => c.Method == RecipeMethodId.Source);
            Assert.Contains(result.Alternatives, c => c.Method == RecipeMethodId.Container);
            Assert.NotEmpty(result.Warnings);
        }

        [Fact]
        public void RestrictedNetworkNo_ContainerYesAndPackageYes_RecommendsContainer_PackageIsAlternative()
        {
            var answers = new RecipeMethodAnswers(
                IsRestrictedNetwork: Answer.No,
                HasInternalPackageMirror: Answer.Unknown,
                HasExistingContainerImage: Answer.Yes,
                HasPackageInPublicChannels: Answer.Yes,
                HasSourceArchiveAndChecksum: Answer.No,
                HasExistingDockerfile: Answer.No);

            var result = RecipeMethodRecommender.Recommend(answers);

            Assert.Equal(RecipeMethodId.Container, result.RecommendedMethod);
            Assert.Contains(result.Alternatives, c => c.Method == RecipeMethodId.Package);
        }

        [Fact]
        public void RestrictedNetworkNo_PackageYesOnly_RecommendsPackage()
        {
            var answers = new RecipeMethodAnswers(
                IsRestrictedNetwork: Answer.No,
                HasInternalPackageMirror: Answer.Unknown,
                HasExistingContainerImage: Answer.No,
                HasPackageInPublicChannels: Answer.Yes,
                HasSourceArchiveAndChecksum: Answer.No,
                HasExistingDockerfile: Answer.No);

            var result = RecipeMethodRecommender.Recommend(answers);

            Assert.Equal(RecipeMethodId.Package, result.RecommendedMethod);
            Assert.Empty(result.Alternatives);
            Assert.Empty(result.Warnings);
        }

        [Fact]
        public void RestrictedNetworkUnknown_PackageYes_RecommendsPackage_WithNetworkWarning()
        {
            var answers = new RecipeMethodAnswers(
                IsRestrictedNetwork: Answer.Unknown,
                HasInternalPackageMirror: Answer.Unknown,
                HasExistingContainerImage: Answer.No,
                HasPackageInPublicChannels: Answer.Yes,
                HasSourceArchiveAndChecksum: Answer.No,
                HasExistingDockerfile: Answer.No);

            var result = RecipeMethodRecommender.Recommend(answers);

            Assert.Equal(RecipeMethodId.Package, result.RecommendedMethod);
            Assert.NotEmpty(result.Warnings);
        }

        [Fact]
        public void UnknownHeavy_NoRecommendation()
        {
            var answers = new RecipeMethodAnswers(
                IsRestrictedNetwork: Answer.No,
                HasInternalPackageMirror: Answer.Unknown,
                HasExistingContainerImage: Answer.Unknown,
                HasPackageInPublicChannels: Answer.Unknown,
                HasSourceArchiveAndChecksum: Answer.Unknown,
                HasExistingDockerfile: Answer.No);

            var result = RecipeMethodRecommender.Recommend(answers);

            Assert.Null(result.RecommendedMethod);
            Assert.Equal(
                new[] { RecipeMethodId.Container, RecipeMethodId.Package, RecipeMethodId.Source },
                result.Alternatives.Select(c => c.Method).ToArray());
            Assert.DoesNotContain(result.Alternatives, c => c.Method == RecipeMethodId.Dockerfile);
            Assert.Equal(3, result.MissingInformation.Count);
        }

        [Fact]
        public void UnknownIsNotExcludedLikeNo()
        {
            var answers = new RecipeMethodAnswers(
                IsRestrictedNetwork: Answer.No,
                HasInternalPackageMirror: Answer.No,
                HasExistingContainerImage: Answer.Unknown,
                HasPackageInPublicChannels: Answer.No,
                HasSourceArchiveAndChecksum: Answer.No,
                HasExistingDockerfile: Answer.No);

            var result = RecipeMethodRecommender.Recommend(answers);

            Assert.Null(result.RecommendedMethod);
            Assert.Contains(result.Alternatives, c => c.Method == RecipeMethodId.Container);
        }
    }
}
