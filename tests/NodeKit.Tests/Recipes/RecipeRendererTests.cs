using System;
using System.Linq;
using NodeKit.Authoring;
using NodeKit.Authoring.Recipes;
using NodeKit.Validation;
using Xunit;

namespace NodeKit.Tests.Recipes
{
    public class RecipeRendererTests
    {
        private const string PinnedBaseImage = "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private const string PinnedBioContainerImage = "quay.io/biocontainers/bwa:0.7.17--h5bf99c6_8@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private static readonly ToolInput[] _sampleInputs =
        {
            new() { Name = "reads", Role = "sample-fastq", Format = "fastq", Shape = "pair" },
        };

        private static readonly ToolOutput[] _sampleOutputs =
        {
            new() { Name = "aligned", Role = "aligned-bam", Format = "bam", Shape = "single", Class = "primary" },
        };

        [Fact]
        public void Render_Conda_SetsImageUriAndDockerfileWithInstallLine()
        {
            var recipe = NewRecipe(RecipeKind.Conda);
            recipe.BaseImage = PinnedBaseImage;
            recipe.Channels.AddRange(new[] { "bioconda", "conda-forge" });
            recipe.Packages.Add("bwa=0.7.17=h5bf99c6_8");

            var definition = RecipeRenderer.Render(recipe);

            Assert.Equal(PinnedBaseImage, definition.ImageUri);
            Assert.Contains($"FROM {PinnedBaseImage}", definition.DockerfileContent);
            Assert.Contains("RUN conda install -y bwa=0.7.17=h5bf99c6_8", definition.DockerfileContent);
            Assert.Contains("RUN conda config --add channels bioconda", definition.DockerfileContent);
        }

        [Fact]
        public void Render_Conda_PassesFullL1ValidatorChain()
        {
            var recipe = NewRecipe(RecipeKind.Conda);
            recipe.BaseImage = PinnedBaseImage;
            recipe.Channels.AddRange(new[] { "bioconda", "conda-forge" });
            recipe.Packages.Add("bwa=0.7.17=h5bf99c6_8");

            var definition = RecipeRenderer.Render(recipe);
            var violations = RunFullL1Chain(definition);

            Assert.Empty(violations);
        }

        [Fact]
        public void Render_Micromamba_UsesMicromambaInstallCommandWithBaseEnv()
        {
            // Regression: mambaorg/micromamba images don't auto-activate an
            // environment for plain RUN steps like conda-forge/miniforge images
            // do, so "micromamba install" without "-n base" always fails with
            // "No target prefix specified" regardless of package validity —
            // found via a real local NodeVault + buildah build during Micromamba
            // engine test coverage.
            var recipe = NewRecipe(RecipeKind.Micromamba);
            recipe.BaseImage = PinnedBaseImage;
            recipe.Channels.Add("bioconda");
            recipe.Packages.Add("samtools=1.17=h00cdaf9_0");

            var definition = RecipeRenderer.Render(recipe);
            var violations = RunFullL1Chain(definition);

            Assert.Contains("RUN micromamba install -n base -y samtools=1.17=h00cdaf9_0", definition.DockerfileContent);
            Assert.Contains("RUN micromamba config append channels bioconda", definition.DockerfileContent);
            Assert.Empty(violations);
        }

        [Fact]
        public void Render_PackageMirror_UsesMirrorUriAsChannel()
        {
            var recipe = NewRecipe(RecipeKind.PackageMirror);
            recipe.BaseImage = PinnedBaseImage;
            recipe.PackageMirrorUri = "https://mirror.internal/conda-channel";
            recipe.Packages.Add("bwa=0.7.17=h5bf99c6_8");

            var definition = RecipeRenderer.Render(recipe);
            var violations = RunFullL1Chain(definition);

            Assert.Contains("RUN conda config --add channels https://mirror.internal/conda-channel", definition.DockerfileContent);
            Assert.Empty(violations);
        }

        [Fact]
        public void Render_BioContainer_SetsImageUriAndMinimalWrapperDockerfile()
        {
            var recipe = NewRecipe(RecipeKind.BioContainer);
            recipe.BioContainerImageUri = PinnedBioContainerImage;

            var definition = RecipeRenderer.Render(recipe);
            var violations = RunFullL1Chain(definition);

            Assert.Equal(PinnedBioContainerImage, definition.ImageUri);
            Assert.Equal($"FROM {PinnedBioContainerImage}\n", definition.DockerfileContent);
            Assert.Empty(violations);
        }

        [Fact]
        public void Render_SourceBuild_EmbedsBareHexChecksumNotPrefixed()
        {
            var recipe = NewRecipe(RecipeKind.SourceBuild);
            recipe.BaseImage = PinnedBaseImage;
            recipe.SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz";
            recipe.SourceChecksum = "sha256:abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd";
            recipe.SourceBuildCommands.AddRange(new[] { "make", "make install" });

            var definition = RecipeRenderer.Render(recipe);

            Assert.Contains("echo \"abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd  source.tar.gz\" | sha256sum -c -", definition.DockerfileContent);
            Assert.DoesNotContain("sha256:abcdef", definition.DockerfileContent);
            Assert.Contains("make && make install", definition.DockerfileContent);
        }

        [Fact]
        public void Render_SourceBuild_IncludesNonRootUser()
        {
            // SourceBuildCommands runs arbitrary shell, unlike Conda/Micromamba/
            // PackageMirror (pinned package installs only) — closest in risk to
            // dockerfile fallback, so its generated Dockerfile should not leave
            // the image running as root by default.
            var recipe = NewRecipe(RecipeKind.SourceBuild);
            recipe.BaseImage = PinnedBaseImage;
            recipe.SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz";
            recipe.SourceChecksum = "sha256:abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd";
            recipe.SourceBuildCommands.AddRange(new[] { "make", "make install" });

            var definition = RecipeRenderer.Render(recipe);

            Assert.Contains("USER 1000", definition.DockerfileContent);
        }

        [Fact]
        public void Render_SourceBuild_PassesFullL1ValidatorChain()
        {
            var recipe = NewRecipe(RecipeKind.SourceBuild);
            recipe.BaseImage = PinnedBaseImage;
            recipe.SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz";
            recipe.SourceChecksum = "sha256:abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd";
            recipe.SourceBuildCommands.AddRange(new[] { "make", "make install" });

            var definition = RecipeRenderer.Render(recipe);
            var violations = RunFullL1Chain(definition);

            Assert.Empty(violations);
        }

        // §13 R22-C (docs/NODEKIT_SOURCEBUILD_STRUCTURED_INTENT_DESIGN.md §5,
        // §11 Phase C). Real 2-stage split: builder (fetch/verify/extract/
        // build, curated to include curl/tar/sha256sum) -> runtime (only the
        // fixed export root copied in, USER applied here only, no
        // ENTRYPOINT). This replaced R22-B's single-stage placeholder — the
        // security fix these tests assert didn't exist before this sprint.

        [Fact]
        public void Render_SourceBuildStructured_CuratedProfiles_ProducesTwoStageDockerfile()
        {
            var recipe = NewRecipe(RecipeKind.SourceBuildStructured);
            recipe.BuildProfile = "generic";
            recipe.RuntimeProfile = "minimal";
            recipe.SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz";
            recipe.SourceChecksum = "sha256:abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd";
            recipe.SourceBuildCommands.Add("make install DESTDIR=/nodekit/output");

            var definition = RecipeRenderer.Render(recipe);
            var dockerfile = definition.DockerfileContent;

            var buildImage = SourceBuildProfileCatalog.FindBuildProfile("generic")!.ImageReference;
            var runtimeImage = SourceBuildProfileCatalog.FindRuntimeProfile("minimal")!.ImageReference;

            Assert.Equal(buildImage, definition.ImageUri);
            Assert.Contains($"FROM {buildImage} AS builder", dockerfile);
            Assert.Contains($"FROM {runtimeImage}", dockerfile);
            Assert.Contains("COPY --from=builder /nodekit/output/ /", dockerfile);
            Assert.Contains("mkdir -p /nodekit/output", dockerfile);
            Assert.DoesNotContain("ENTRYPOINT", dockerfile);

            // USER must apply to the runtime stage only — it should appear
            // strictly after the second FROM, not before it (i.e. not on the
            // builder stage).
            var secondFromIndex = dockerfile.IndexOf($"FROM {runtimeImage}", StringComparison.Ordinal);
            var userIndex = dockerfile.IndexOf("USER 1000", StringComparison.Ordinal);
            Assert.True(userIndex > secondFromIndex, "USER 1000 must come after the runtime stage's FROM");
        }

        [Fact]
        public void Render_SourceBuildStructured_AdvancedBuildAndRuntimeProfiles_UseCustomImages()
        {
            const string customRuntimeImage = "debian:bookworm@sha256:1111111111111111111111111111111111111111111111111111111111111a";
            var recipe = NewRecipe(RecipeKind.SourceBuildStructured);
            recipe.BuildProfile = "advanced";
            recipe.BuildProfileImage = PinnedBaseImage;
            recipe.RuntimeProfile = "advanced";
            recipe.RuntimeProfileImage = customRuntimeImage;
            recipe.SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz";
            recipe.SourceChecksum = "sha256:abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd";
            recipe.SourceBuildCommands.Add("make");

            var definition = RecipeRenderer.Render(recipe);

            Assert.Equal(PinnedBaseImage, definition.ImageUri);
            Assert.Contains($"FROM {PinnedBaseImage} AS builder", definition.DockerfileContent);
            Assert.Contains($"FROM {customRuntimeImage}", definition.DockerfileContent);
        }

        [Fact]
        public void Render_SourceBuildStructured_PassesFullL1ValidatorChain()
        {
            var recipe = NewRecipe(RecipeKind.SourceBuildStructured);
            recipe.BuildProfile = "generic";
            recipe.RuntimeProfile = "minimal";
            recipe.SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz";
            recipe.SourceChecksum = "sha256:abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd";
            recipe.SourceBuildCommands.AddRange(new[] { "make", "make install" });

            var definition = RecipeRenderer.Render(recipe);
            var violations = RunFullL1Chain(definition);

            Assert.Empty(violations);
        }

        [Fact]
        public void Render_DockerfileFallback_PassesThroughVerbatim()
        {
            var recipe = NewRecipe(RecipeKind.DockerfileFallback);
            recipe.BaseImage = PinnedBaseImage;
            recipe.DockerfileContent = $"FROM {PinnedBaseImage}\nRUN echo ok\n";

            var definition = RecipeRenderer.Render(recipe);
            var violations = RunFullL1Chain(definition);

            Assert.Equal(PinnedBaseImage, definition.ImageUri);
            Assert.Equal(recipe.DockerfileContent, definition.DockerfileContent);
            Assert.Empty(violations);
        }

        private static RecipeDocument NewRecipe(RecipeKind buildKind) =>
            new()
            {
                BuildKind = buildKind,
                ToolName = "bwa-mem",
                Version = "0.7.17",
                Script = "bwa mem -t 4 ref.fa reads_1.fq reads_2.fq",
                Inputs = { _sampleInputs[0] },
                Outputs = { _sampleOutputs[0] },
            };

        private static System.Collections.Generic.List<ValidationViolation> RunFullL1Chain(ToolDefinition definition)
        {
            IValidator[] validators =
            {
                new RequiredFieldsValidator(),
                new ImageUriValidator(),
                new DockerfileStructureValidator(),
                new PackageVersionValidator(),
            };

            return validators.SelectMany(v => v.Validate(definition).Violations).ToList();
        }
    }
}
