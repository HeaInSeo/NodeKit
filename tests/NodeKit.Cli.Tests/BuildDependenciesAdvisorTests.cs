using System.Collections.Generic;
using NodeKit.Authoring.Recipes;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    public class BuildDependenciesAdvisorTests
    {
        [Fact]
        public void Describe_SourceBuildWithDependencies_ReturnsWarning()
        {
            var message = BuildDependenciesAdvisor.Describe(RecipeBuildKind.SourceBuild, new[] { "zlib1g-dev" });

            Assert.NotNull(message);
            Assert.Contains("zlib1g-dev", message);
        }

        [Fact]
        public void Describe_SourceBuildWithoutDependencies_ReturnsNull()
        {
            var message = BuildDependenciesAdvisor.Describe(RecipeBuildKind.SourceBuild, System.Array.Empty<string>());

            Assert.Null(message);
        }

        [Fact]
        public void Describe_NonSourceBuildKind_ReturnsNull()
        {
            // Only SourceBuild exposes BuildDependencies today (see
            // RecipeFieldCatalog) — this guards against the advisor firing
            // if that ever changes without updating this class too.
            var message = BuildDependenciesAdvisor.Describe(RecipeBuildKind.Conda, new[] { "zlib1g-dev" });

            Assert.Null(message);
        }
    }
}
