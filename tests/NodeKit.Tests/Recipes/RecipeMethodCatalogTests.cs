using System.Linq;
using NodeKit.Authoring.Recipes;
using Xunit;

namespace NodeKit.Tests.Recipes
{
    public class RecipeMethodCatalogTests
    {
        [Fact]
        public void Methods_ContainsAllSixMethodsExactlyOnce()
        {
            // §13 R22-B added RecipeMethodId.SourceStructured (5 -> 6) — see
            // docs/NODEKIT_SOURCEBUILD_STRUCTURED_INTENT_DESIGN.md. It's
            // intentionally listed right after Source, since it's an
            // advanced/opt-in variant of the same method.
            var methods = RecipeMethodCatalog.Methods.Select(m => m.Method).ToList();

            Assert.Equal(
                new[]
                {
                    RecipeMethodId.Container,
                    RecipeMethodId.Package,
                    RecipeMethodId.Mirror,
                    RecipeMethodId.Source,
                    RecipeMethodId.SourceStructured,
                    RecipeMethodId.Dockerfile,
                },
                methods);
        }

        [Fact]
        public void For_ReturnsMatchingMethodInfo()
        {
            var info = RecipeMethodCatalog.For(RecipeMethodId.Container);

            Assert.Equal(RecipeMethodId.Container, info.Method);
            Assert.NotEmpty(info.Label.Get("ko"));
            Assert.NotEmpty(info.PreparationHint.Get("ko"));
            Assert.NotNull(info.Warning);
        }

        [Fact]
        public void For_Package_HasNoWarning()
        {
            var info = RecipeMethodCatalog.For(RecipeMethodId.Package);

            Assert.Null(info.Warning);
        }
    }
}
