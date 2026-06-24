using NodeKit.Authoring.Recipes;
using NodeKit.Validation.Recipes;
using Xunit;

namespace NodeKit.Tests.Recipes
{
    public class RecipeValidatorTests
    {
        private const string PinnedBaseImage = "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private const string ValidChecksum = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        [Fact]
        public void Validate_Conda_WithBaseImageAndPackages_Passes()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeBuildKind.Conda,
                BaseImage = PinnedBaseImage,
                Packages = { "bwa=0.7.17=h5bf99c6_8" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_Conda_WithoutPackages_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeBuildKind.Conda,
                BaseImage = PinnedBaseImage,
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-002");
        }

        [Fact]
        public void Validate_Conda_WithoutBaseImage_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeBuildKind.Conda,
                Packages = { "bwa=0.7.17=h5bf99c6_8" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-001");
        }

        [Fact]
        public void Validate_PackageMirror_WithoutMirrorUri_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeBuildKind.PackageMirror,
                BaseImage = PinnedBaseImage,
                Packages = { "bwa=0.7.17=h5bf99c6_8" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-003");
        }

        [Fact]
        public void Validate_PackageMirror_WithMirrorUriAndPackages_Passes()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeBuildKind.PackageMirror,
                BaseImage = PinnedBaseImage,
                PackageMirrorUri = "https://mirror.internal/conda-channel",
                Packages = { "bwa=0.7.17=h5bf99c6_8" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_BioContainer_WithoutImageUri_Fails()
        {
            var recipe = new RecipeDocument { BuildKind = RecipeBuildKind.BioContainer };

            var result = RecipeValidator.Validate(recipe);

            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-005");
        }

        [Fact]
        public void Validate_BioContainer_WithImageUri_Passes()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeBuildKind.BioContainer,
                BioContainerImageUri = "quay.io/biocontainers/bwa:0.7.17--h5bf99c6_8@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_SourceBuild_WithoutChecksum_FailsWithMissingRule()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeBuildKind.SourceBuild,
                BaseImage = PinnedBaseImage,
                SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                SourceBuildCommands = { "make", "make install" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.Contains(result.Violations, v => v.RuleId == "L1-SRC-001");
        }

        [Fact]
        public void Validate_SourceBuild_WithMalformedChecksum_FailsWithFormatRule()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeBuildKind.SourceBuild,
                BaseImage = PinnedBaseImage,
                SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                SourceChecksum = "not-a-real-checksum",
                SourceBuildCommands = { "make", "make install" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.Contains(result.Violations, v => v.RuleId == "L1-SRC-002");
        }

        [Fact]
        public void Validate_SourceBuild_WithoutBuildCommands_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeBuildKind.SourceBuild,
                BaseImage = PinnedBaseImage,
                SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                SourceChecksum = ValidChecksum,
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-007");
        }

        [Fact]
        public void Validate_SourceBuild_FullyPopulated_Passes()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeBuildKind.SourceBuild,
                BaseImage = PinnedBaseImage,
                SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                SourceChecksum = ValidChecksum,
                SourceBuildCommands = { "make", "make install" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_DockerfileFallback_WithoutDockerfileContent_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeBuildKind.DockerfileFallback,
                BaseImage = PinnedBaseImage,
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-008");
        }

        [Fact]
        public void Validate_DockerfileFallback_WithDockerfileContent_Passes()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeBuildKind.DockerfileFallback,
                BaseImage = PinnedBaseImage,
                DockerfileContent = "FROM ubuntu:22.04@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\nRUN echo ok\n",
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.True(result.IsValid);
        }
    }
}
