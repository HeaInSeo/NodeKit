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
        public void FieldsFor_OrdersCommonThenMethodThenInputsThenOutputs()
        {
            foreach (var method in _allMethods)
            {
                var fields = RecipeFieldCatalog.FieldsFor(method);

                var expectedNames = RecipeFieldCatalog.CommonScalarFields
                    .Select(f => f.Name)
                    .Concat(RecipeFieldCatalog.MethodFields[method].Select(f => f.Name))
                    .Append(RecipeFieldCatalog.InputsField.Name)
                    .Append(RecipeFieldCatalog.OutputsField.Name)
                    .ToList();

                Assert.Equal(expectedNames, fields.Select(f => f.Name).ToList());
            }
        }

        [Fact]
        public void FieldsFor_InputsAndOutputsAreAlwaysRequired()
        {
            foreach (var method in _allMethods)
            {
                var fields = RecipeFieldCatalog.FieldsFor(method);

                var inputs = fields.Single(f => f.Name == "Inputs");
                var outputs = fields.Single(f => f.Name == "Outputs");

                Assert.Equal(RecipeFieldRequirement.Required, inputs.Requirement);
                Assert.Equal(RecipeFieldRequirement.Required, outputs.Requirement);
            }
        }

        [Fact]
        public void BlockingRequiredFieldsFor_AlwaysIncludesInputsAndOutputs()
        {
            foreach (var method in _allMethods)
            {
                var required = RecipeFieldCatalog.BlockingRequiredFieldsFor(method);

                Assert.Contains(required, f => f.Name == "Inputs");
                Assert.Contains(required, f => f.Name == "Outputs");
                Assert.All(required, f => Assert.Equal(RecipeFieldRequirement.Required, f.Requirement));
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
        public void InputsField_Apply_AddsToRecipeInputs()
        {
            var recipe = new RecipeDocument();
            var input = new NodeKit.Authoring.ToolInput { Name = "reads" };

            RecipeFieldCatalog.InputsField.Apply(recipe, input);

            Assert.Same(input, Assert.Single(recipe.Inputs));
        }

        [Fact]
        public void OutputsField_Apply_AddsToRecipeOutputs()
        {
            var recipe = new RecipeDocument();
            var output = new NodeKit.Authoring.ToolOutput { Name = "aligned" };

            RecipeFieldCatalog.OutputsField.Apply(recipe, output);

            Assert.Same(output, Assert.Single(recipe.Outputs));
        }
    }
}
