using System;
using NodeKit.Authoring.Recipes;
using Xunit;

namespace NodeKit.Tests.Recipes
{
    public class RecipeBuildKindResolverTests
    {
        [Fact]
        public void Resolve_PackageWithoutEngine_Throws()
        {
            var document = new RecipeDocument { PackageEngine = string.Empty };

            Assert.Throws<InvalidOperationException>(
                () => RecipeBuildKindResolver.Resolve(RecipeMethodId.Package, document));
        }

        [Fact]
        public void Resolve_PackageWithWhitespaceEngine_Throws()
        {
            var document = new RecipeDocument { PackageEngine = "   " };

            Assert.Throws<InvalidOperationException>(
                () => RecipeBuildKindResolver.Resolve(RecipeMethodId.Package, document));
        }

        [Fact]
        public void Resolve_PackageWithCondaEngine_ResolvesToConda()
        {
            var document = new RecipeDocument { PackageEngine = "conda" };

            var result = RecipeBuildKindResolver.Resolve(RecipeMethodId.Package, document);

            Assert.Equal(RecipeBuildKind.Conda, result);
        }

        [Fact]
        public void Resolve_PackageWithMicromambaEngine_ResolvesToMicromamba()
        {
            var document = new RecipeDocument { PackageEngine = "micromamba" };

            var result = RecipeBuildKindResolver.Resolve(RecipeMethodId.Package, document);

            Assert.Equal(RecipeBuildKind.Micromamba, result);
        }

        [Fact]
        public void Resolve_Container_ResolvesToBioContainer()
        {
            var result = RecipeBuildKindResolver.Resolve(RecipeMethodId.Container, new RecipeDocument());

            Assert.Equal(RecipeBuildKind.BioContainer, result);
        }

        [Fact]
        public void Resolve_Mirror_ResolvesToPackageMirror()
        {
            var result = RecipeBuildKindResolver.Resolve(RecipeMethodId.Mirror, new RecipeDocument());

            Assert.Equal(RecipeBuildKind.PackageMirror, result);
        }

        [Fact]
        public void Resolve_Source_ResolvesToSourceBuild()
        {
            var result = RecipeBuildKindResolver.Resolve(RecipeMethodId.Source, new RecipeDocument());

            Assert.Equal(RecipeBuildKind.SourceBuild, result);
        }

        [Fact]
        public void Resolve_Dockerfile_ResolvesToDockerfileFallback()
        {
            var result = RecipeBuildKindResolver.Resolve(RecipeMethodId.Dockerfile, new RecipeDocument());

            Assert.Equal(RecipeBuildKind.DockerfileFallback, result);
        }

        [Fact]
        public void Resolve_NullDocument_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => RecipeBuildKindResolver.Resolve(RecipeMethodId.Container, null!));
        }
    }
}
