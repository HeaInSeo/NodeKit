using NodeKit.Authoring.Recipes;
using Xunit;

namespace NodeKit.Tests.Recipes
{
    public class RoleNormalizerTests
    {
        [Fact]
        public void Normalize_SpaceSeparatedWords_ConvertsToSnakeCase()
        {
            var result = RoleNormalizer.Normalize("Read Pair");

            Assert.Equal("read_pair", result.Value);
            Assert.Equal(RecipeValueNormalizationAction.Applied, result.Action);
        }

        [Fact]
        public void Normalize_TwoWordPhrase_ConvertsToSnakeCase()
        {
            var result = RoleNormalizer.Normalize("Reference Genome");

            Assert.Equal("reference_genome", result.Value);
        }

        [Fact]
        public void Normalize_UppercaseSingleWord_Lowercases()
        {
            var result = RoleNormalizer.Normalize("LOG");

            Assert.Equal("log", result.Value);
            Assert.Equal(RecipeValueNormalizationAction.Applied, result.Action);
        }

        [Fact]
        public void Normalize_AlreadySnakeCase_Unchanged()
        {
            var result = RoleNormalizer.Normalize("reads");

            Assert.Equal("reads", result.Value);
            Assert.Equal(RecipeValueNormalizationAction.Unchanged, result.Action);
            Assert.Null(result.Message);
        }

        [Fact]
        public void KnownRoles_ContainsExpectedSevenRoles()
        {
            Assert.Equal(
                new[] { "reads", "reference", "alignment", "variants", "index", "log", "metrics" },
                RoleNormalizer.KnownRoles);
        }
    }
}
