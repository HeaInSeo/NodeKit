using NodeKit.Authoring.Recipes;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    public class SourceBuildBaseImageAdvisorTests
    {
        [Fact]
        public void Describe_SourceBuildWithCurlImage_ReturnsWarning()
        {
            var message = SourceBuildBaseImageAdvisor.Describe(
                RecipeBuildKind.SourceBuild,
                "docker.io/curlimages/curl:8.8.0@sha256:cbe461f2f26e573c5f4296c5f6c904011e3f1296dabf53e73b3f126d689c3463");

            Assert.NotNull(message);
        }

        [Fact]
        public void Describe_SourceBuildWithOrdinaryImage_ReturnsNodeVaultRejectionWarning()
        {
            // Broadened after the 2026-07-13 adversarial review finding:
            // legacy SourceBuild's single-stage RUN (curl fetch + build
            // commands) is now unconditionally likely to be rejected by
            // NodeVault's Sprint 9 final-stage risky-tool policy, regardless
            // of which BaseImage is used — so this must warn even for an
            // ordinary image, not just the fetch-only-image heuristic.
            var message = SourceBuildBaseImageAdvisor.Describe(
                RecipeBuildKind.SourceBuild,
                "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");

            Assert.NotNull(message);
            Assert.Contains("source-structured", message);
        }

        [Fact]
        public void Describe_SourceBuildWithNullBaseImage_ReturnsNodeVaultRejectionWarning()
        {
            var message = SourceBuildBaseImageAdvisor.Describe(RecipeBuildKind.SourceBuild, null);

            Assert.NotNull(message);
            Assert.Contains("source-structured", message);
        }

        [Fact]
        public void Describe_NonSourceBuildKind_ReturnsNullEvenWithCurlImage()
        {
            // The advisory is specific to SourceBuild, since that's the only
            // build kind where BaseImage doubles as the final runtime image
            // AND is fetched from directly in the same RUN line.
            var message = SourceBuildBaseImageAdvisor.Describe(
                RecipeBuildKind.Conda,
                "docker.io/curlimages/curl:8.8.0@sha256:cbe461f2f26e573c5f4296c5f6c904011e3f1296dabf53e73b3f126d689c3463");

            Assert.Null(message);
        }
    }
}
