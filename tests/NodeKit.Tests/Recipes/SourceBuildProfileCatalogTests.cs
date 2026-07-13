using NodeKit.Authoring.Recipes;
using Xunit;

namespace NodeKit.Tests.Recipes
{
    public class SourceBuildProfileCatalogTests
    {
        [Fact]
        public void BuildProfiles_ContainsGeneric_WithPinnedDigest()
        {
            var entry = SourceBuildProfileCatalog.FindBuildProfile("generic");

            Assert.NotNull(entry);
            Assert.Contains("@sha256:", entry.ImageReference);
        }

        [Fact]
        public void RuntimeProfiles_ContainsMinimal_WithPinnedDigest()
        {
            var entry = SourceBuildProfileCatalog.FindRuntimeProfile("minimal");

            Assert.NotNull(entry);
            Assert.Contains("@sha256:", entry.ImageReference);
        }

        [Fact]
        public void FindBuildProfile_UnknownKey_ReturnsNull()
        {
            Assert.Null(SourceBuildProfileCatalog.FindBuildProfile("does-not-exist"));
        }

        [Fact]
        public void FindRuntimeProfile_UnknownKey_ReturnsNull()
        {
            Assert.Null(SourceBuildProfileCatalog.FindRuntimeProfile("does-not-exist"));
        }

        [Fact]
        public void FindBuildProfile_AdvancedKey_ReturnsNull()
        {
            // "advanced" is a sentinel, not a catalog entry — callers must
            // branch on it before calling Find*Profile.
            Assert.Null(SourceBuildProfileCatalog.FindBuildProfile(SourceBuildProfileCatalog.AdvancedKey));
        }
    }
}
