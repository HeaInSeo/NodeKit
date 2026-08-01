using System;
using NodeKit.Authoring.Recipes;
using Xunit;

namespace NodeKit.Tests.Recipes
{
    public class RecipeKindResolverTests
    {
        [Fact]
        public void Resolve_PackageWithoutEngine_Throws()
        {
            var document = new RecipeDocument { PackageEngine = string.Empty };

            Assert.Throws<InvalidOperationException>(
                () => RecipeKindResolver.Resolve(RecipeMethodId.Package, document));
        }

        [Fact]
        public void Resolve_PackageWithWhitespaceEngine_Throws()
        {
            var document = new RecipeDocument { PackageEngine = "   " };

            Assert.Throws<InvalidOperationException>(
                () => RecipeKindResolver.Resolve(RecipeMethodId.Package, document));
        }

        [Fact]
        public void Resolve_PackageWithCondaEngine_ResolvesToConda()
        {
            var document = new RecipeDocument { PackageEngine = "conda" };

            var result = RecipeKindResolver.Resolve(RecipeMethodId.Package, document);

            Assert.Equal(RecipeKind.Conda, result);
        }

        [Fact]
        public void Resolve_PackageWithMicromambaEngine_ResolvesToMicromamba()
        {
            var document = new RecipeDocument { PackageEngine = "micromamba" };

            var result = RecipeKindResolver.Resolve(RecipeMethodId.Package, document);

            Assert.Equal(RecipeKind.Micromamba, result);
        }

        [Fact]
        public void Resolve_Container_ResolvesToBioContainer()
        {
            var result = RecipeKindResolver.Resolve(RecipeMethodId.Container, new RecipeDocument());

            Assert.Equal(RecipeKind.BioContainer, result);
        }

        [Fact]
        public void Resolve_Mirror_ResolvesToPackageMirror()
        {
            var result = RecipeKindResolver.Resolve(RecipeMethodId.Mirror, new RecipeDocument());

            Assert.Equal(RecipeKind.PackageMirror, result);
        }

        [Fact]
        public void Resolve_Source_ResolvesToSourceBuild()
        {
            var result = RecipeKindResolver.Resolve(RecipeMethodId.Source, new RecipeDocument());

            Assert.Equal(RecipeKind.SourceBuild, result);
        }

        [Fact]
        public void Resolve_SourceStructured_ResolvesToSourceBuildStructured()
        {
            var result = RecipeKindResolver.Resolve(RecipeMethodId.SourceStructured, new RecipeDocument());

            Assert.Equal(RecipeKind.SourceBuildStructured, result);
        }

        [Fact]
        public void Resolve_Dockerfile_ResolvesToDockerfileFallback()
        {
            var result = RecipeKindResolver.Resolve(RecipeMethodId.Dockerfile, new RecipeDocument());

            Assert.Equal(RecipeKind.DockerfileFallback, result);
        }

        [Fact]
        public void Resolve_NullDocument_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => RecipeKindResolver.Resolve(RecipeMethodId.Container, null!));
        }
    }
}
