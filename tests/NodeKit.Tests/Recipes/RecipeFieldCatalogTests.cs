using System.Linq;
using NodeKit.Authoring.Recipes;
using Xunit;

namespace NodeKit.Tests.Recipes
{
    public class RecipeFieldCatalogTests
    {
        private static readonly RecipeMethodId[] _allMethods =
        {
            RecipeMethodId.Container,
            RecipeMethodId.Package,
            RecipeMethodId.Mirror,
            RecipeMethodId.Source,
            RecipeMethodId.SourceStructured,
            RecipeMethodId.Dockerfile,
        };

        [Fact]
        public void FieldsFor_OrdersCommonThenMethod()
        {
            foreach (var method in _allMethods)
            {
                var fields = RecipeFieldCatalog.FieldsFor(method);

                var expectedNames = RecipeFieldCatalog.CommonScalarFields
                    .Select(f => f.Name)
                    .Concat(RecipeFieldCatalog.MethodFields[method].Select(f => f.Name))
                    .ToList();

                Assert.Equal(expectedNames, fields.Select(f => f.Name).ToList());
            }
        }

        [Fact]
        public void Container_ImageDigest_IsRequiredNotRecommended()
        {
            var imageDigest = RecipeFieldCatalog.MethodFields[RecipeMethodId.Container]
                .Single(f => f.Name == "ImageDigest");

            Assert.Equal(RecipeFieldRequirement.Required, imageDigest.Requirement);
        }

        [Fact]
        public void DefaultedFieldsFor_Package_ContainsOnlyPackageEngine()
        {
            var defaulted = RecipeFieldCatalog.DefaultedFieldsFor(RecipeMethodId.Package);

            Assert.Equal(new[] { "PackageEngine" }, defaulted.Select(f => f.Name).ToList());
            Assert.Equal("conda", defaulted.Single().DefaultValue);
        }

        [Fact]
        public void RecommendedFieldsFor_Source_ContainsOnlyBuildDependencies()
        {
            var recommended = RecipeFieldCatalog.RecommendedFieldsFor(RecipeMethodId.Source);

            Assert.Equal(new[] { "BuildDependencies" }, recommended.Select(f => f.Name).ToList());
        }

        // §13 R22-B (docs/NODEKIT_SOURCEBUILD_STRUCTURED_INTENT_DESIGN.md §5).

        [Fact]
        public void SourceStructured_HasBuildAndRuntimeProfileFields()
        {
            var names = RecipeFieldCatalog.MethodFields[RecipeMethodId.SourceStructured]
                .Select(f => f.Name)
                .ToList();

            Assert.Equal(
                new[]
                {
                    "BuildProfile", "BuildProfileImage", "SourceUri", "SourceChecksum",
                    "SourceBuildCommands", "BuildDependencies", "RuntimeProfile",
                    "RuntimeProfileImage", "RuntimeDependencies",
                },
                names);
        }

        [Fact]
        public void SourceStructured_BuildProfileAndRuntimeProfile_OfferAdvancedChoice()
        {
            var buildProfile = RecipeFieldCatalog.MethodFields[RecipeMethodId.SourceStructured]
                .Single(f => f.Name == "BuildProfile");
            var runtimeProfile = RecipeFieldCatalog.MethodFields[RecipeMethodId.SourceStructured]
                .Single(f => f.Name == "RuntimeProfile");

            Assert.Contains(buildProfile.Choices, c => c.Value == "advanced");
            Assert.Contains(buildProfile.Choices, c => c.Value == "generic");
            Assert.Contains(runtimeProfile.Choices, c => c.Value == "advanced");
            Assert.Contains(runtimeProfile.Choices, c => c.Value == "minimal");
        }

        [Fact]
        public void SourceStructured_ProfileImageFields_AreOptionalNotBlocking()
        {
            // Required-ness of *ProfileImage is conditional on the profile
            // choice being "advanced" — that condition is enforced by
            // RecipeValidator, not the field catalog's static Requirement tier
            // (RecipeFieldRequirement has no "conditionally required" concept).
            var buildImage = RecipeFieldCatalog.MethodFields[RecipeMethodId.SourceStructured]
                .Single(f => f.Name == "BuildProfileImage");
            var runtimeImage = RecipeFieldCatalog.MethodFields[RecipeMethodId.SourceStructured]
                .Single(f => f.Name == "RuntimeProfileImage");

            Assert.Equal(RecipeFieldRequirement.Optional, buildImage.Requirement);
            Assert.Equal(RecipeFieldRequirement.Optional, runtimeImage.Requirement);
        }

        [Fact]
        public void BlockingRequiredFieldsFor_AllRequiredFields()
        {
            foreach (var method in _allMethods)
            {
                var required = RecipeFieldCatalog.BlockingRequiredFieldsFor(method);

                Assert.All(required, f => Assert.Equal(RecipeFieldRequirement.Required, f.Requirement));
                Assert.True(required.Count > 0, $"{method} should have at least one required field");
            }
        }
    }
}
