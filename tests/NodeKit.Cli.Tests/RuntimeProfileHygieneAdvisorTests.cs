using NodeKit.Authoring.Recipes;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    public class RuntimeProfileHygieneAdvisorTests
    {
        [Fact]
        public void Describe_AdvancedRuntimeProfileWithCurlImage_ReturnsWarning()
        {
            var message = RuntimeProfileHygieneAdvisor.Describe(
                RecipeKind.SourceBuildStructured,
                "advanced",
                "docker.io/curlimages/curl:8.8.0@sha256:cbe461f2f26e573c5f4296c5f6c904011e3f1296dabf53e73b3f126d689c3463");

            Assert.NotNull(message);
            Assert.Contains("추정", message);
        }

        [Fact]
        public void Describe_AdvancedRuntimeProfileWithBuildProfileImageItself_ReturnsWarning()
        {
            // The most plausible real mistake: pasting the curated build image
            // (buildpack-deps, which deliberately includes curl/gcc/make) into
            // RuntimeProfileImage instead of a minimal runtime image.
            var message = RuntimeProfileHygieneAdvisor.Describe(
                RecipeKind.SourceBuildStructured,
                "advanced",
                "docker.io/library/buildpack-deps:bookworm@sha256:4efddd9a54ddc095e672b2fdf514f1ee4d3bb6e1f6ffc988b022c75e6ea99383");

            Assert.NotNull(message);
        }

        [Fact]
        public void Describe_AdvancedRuntimeProfileWithOrdinaryImage_ReturnsNull()
        {
            var message = RuntimeProfileHygieneAdvisor.Describe(
                RecipeKind.SourceBuildStructured,
                "advanced",
                "debian:bookworm-slim@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");

            Assert.Null(message);
        }

        [Fact]
        public void Describe_CuratedRuntimeProfile_ReturnsNullEvenIfImageLooksRisky()
        {
            // Curated profiles are already buildah-verified — the advisor only
            // applies to the "advanced" escape hatch, where NodeKit has never
            // inspected the image.
            var message = RuntimeProfileHygieneAdvisor.Describe(
                RecipeKind.SourceBuildStructured,
                "minimal",
                "docker.io/curlimages/curl:8.8.0@sha256:cbe461f2f26e573c5f4296c5f6c904011e3f1296dabf53e73b3f126d689c3463");

            Assert.Null(message);
        }

        [Fact]
        public void Describe_NonSourceBuildStructuredKind_ReturnsNullEvenWithCurlImage()
        {
            var message = RuntimeProfileHygieneAdvisor.Describe(
                RecipeKind.SourceBuild,
                "advanced",
                "docker.io/curlimages/curl:8.8.0@sha256:cbe461f2f26e573c5f4296c5f6c904011e3f1296dabf53e73b3f126d689c3463");

            Assert.Null(message);
        }

        [Fact]
        public void Describe_NullRuntimeProfileImage_ReturnsNull()
        {
            var message = RuntimeProfileHygieneAdvisor.Describe(RecipeKind.SourceBuildStructured, "advanced", null);

            Assert.Null(message);
        }
    }
}
