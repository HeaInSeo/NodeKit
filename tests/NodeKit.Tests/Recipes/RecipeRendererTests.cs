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
            var recipe = NewRecipe(RecipeVariant.Conda);
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
            var recipe = NewRecipe(RecipeVariant.Conda);
            recipe.BaseImage = PinnedBaseImage;
            recipe.Channels.AddRange(new[] { "bioconda", "conda-forge" });
            recipe.Packages.Add("bwa=0.7.17=h5bf99c6_8");

            var definition = RecipeRenderer.Render(recipe);
            var violations = RunFullL1Chain(definition);

            Assert.Empty(violations);
        }

        [Fact]
        public void Render_Micromamba_UsesMicromambaInstallCommand()
        {
            var recipe = NewRecipe(RecipeVariant.Micromamba);
            recipe.BaseImage = PinnedBaseImage;
            recipe.Channels.Add("bioconda");
            recipe.Packages.Add("samtools=1.17=h00cdaf9_0");

            var definition = RecipeRenderer.Render(recipe);
            var violations = RunFullL1Chain(definition);

            Assert.Contains("RUN micromamba install -y samtools=1.17=h00cdaf9_0", definition.DockerfileContent);
            Assert.Contains("RUN micromamba config append channels bioconda", definition.DockerfileContent);
            Assert.Empty(violations);
        }

        [Fact]
        public void Render_PackageMirror_UsesMirrorUriAsChannel()
        {
            var recipe = NewRecipe(RecipeVariant.PackageMirror);
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
            var recipe = NewRecipe(RecipeVariant.BioContainer);
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
            var recipe = NewRecipe(RecipeVariant.SourceBuild);
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
        public void Render_SourceBuild_PassesFullL1ValidatorChain()
        {
            var recipe = NewRecipe(RecipeVariant.SourceBuild);
            recipe.BaseImage = PinnedBaseImage;
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
            var recipe = NewRecipe(RecipeVariant.DockerfileFallback);
            recipe.BaseImage = PinnedBaseImage;
            recipe.DockerfileContent = $"FROM {PinnedBaseImage}\nRUN echo ok\n";

            var definition = RecipeRenderer.Render(recipe);
            var violations = RunFullL1Chain(definition);

            Assert.Equal(PinnedBaseImage, definition.ImageUri);
            Assert.Equal(recipe.DockerfileContent, definition.DockerfileContent);
            Assert.Empty(violations);
        }

        private static RecipeDocument NewRecipe(RecipeVariant variant) =>
            new()
            {
                Variant = variant,
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
