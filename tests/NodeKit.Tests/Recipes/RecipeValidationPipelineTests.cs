using System;
using NodeKit.Authoring.Recipes;
using NodeKit.Validation.Recipes;
using Xunit;

namespace NodeKit.Tests.Recipes
{
    public class RecipeValidationPipelineTests
    {
        [Fact]
        public void ValidateRecipe_Throws_WhenBuildKindIsNull()
        {
            var doc = new RecipeDocument();

            var ex = Assert.Throws<InvalidOperationException>(
                () => RecipeValidationPipeline.ValidateRecipe(doc));

            Assert.Contains("RecipeKindResolver.Resolve()", ex.Message);
        }
    }
}
