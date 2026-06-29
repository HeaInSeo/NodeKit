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
