using System;
using System.IO;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// Golden transcript tests for the interactive `nodekit recipe create`
    /// wizard — design doc Section 31.13. Drives CliApp.Run with a scripted
    /// stdin transcript and checks the resulting saved RecipeDocument JSON.
    /// </summary>
    public class RecipeCreateInteractiveTests : IDisposable
    {
        private const string ImageRefWithDigest =
            "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private const string DigestOnly =
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private readonly string _workDir = Path.Combine(Path.GetTempPath(), "nodekit-recipe-interactive-tests-" + Guid.NewGuid());

        public RecipeCreateInteractiveTests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
        }

        [Fact]
        public void BwaPackageHappyPath_SavesValidRecipe()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "n", // IsRestrictedNetwork
                "n", // HasInternalPackageMirror
                "n", // HasExistingContainerImage
                "y", // HasPackageInPublicChannels
                "n", // HasSourceArchiveAndChecksum
                "n", // HasExistingDockerfile
                "", // accept recommended method (package)
                "bwa-mem", // ToolName
                "0.7.17", // ToolVersion
                "run.sh", // Script
                ImageRefWithDigest, // ImageRef
                "bwa=0.7.17=h5bf99c6_8", // Packages item
                "", // complete Packages
                "bioconda", // Channels item
                "", // complete Channels
                "", // PackageEngine defaulted — skip, defaults to conda
                "reads", // Inputs item name
                "1", // fastq-paired preset
                "", // complete Inputs
                "bam", // Outputs item name
                "1", // bam-primary preset
                "", // complete Outputs
            };

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"PackageEngine\": \"conda\"", json);
            Assert.Contains("\"BuildKind\": \"Conda\"", json);
            Assert.Contains("bwa-mem", json);
        }

        [Fact]
        public void DockerfileWarningPath_RequiresAcceptanceAndSavesValidRecipe()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "n", // IsRestrictedNetwork
                "n", // HasInternalPackageMirror
                "n", // HasExistingContainerImage
                "n", // HasPackageInPublicChannels
                "n", // HasSourceArchiveAndChecksum
                "y", // HasExistingDockerfile
                "", // accept recommended method (dockerfile)
                "y", // confirm dockerfile warning
                "bwa-mem", // ToolName
                "0.7.17", // ToolVersion
                "run.sh", // Script
                ImageRefWithDigest, // ImageRef
                "./Dockerfile", // DockerfilePath
                $"FROM {ImageRefWithDigest}", // DockerfileContent
                "", // BuildContext defaulted — skip, defaults to "."
                "reads", // Inputs item name
                "1", // fastq-paired preset
                "", // complete Inputs
                "bam", // Outputs item name
                "1", // bam-primary preset
                "", // complete Outputs
            };

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("강한 주의", stdout.ToString());
            Assert.Empty(stderr.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildContext\": \".\"", json);
            Assert.Contains("\"BuildKind\": \"DockerfileFallback\"", json);
        }

        [Fact]
        public void InteractiveAndNonInteractive_ProduceIdenticalRecipeDocument_ForSameLogicalAnswers()
        {
            var interactiveOutPath = Path.Combine(_workDir, "interactive.json");
            var transcript = new[]
            {
                "n", "n", "n", "y", "n", "n", // Q&A -> recommend package
                "", // accept recommended method
                "bwa-mem", "0.7.17", "run.sh", ImageRefWithDigest,
                "bwa=0.7.17=h5bf99c6_8", "",
                "bioconda", "",
                "", // PackageEngine defaulted
                "reads", "1", "",
                "bam", "1", "",
            };
            var interactiveExitCode = CliApp.Run(
                new[] { "recipe", "create", interactiveOutPath },
                new StringReader(string.Join("\n", transcript)),
                new StringWriter(),
                new StringWriter());

            var nonInteractiveOutPath = Path.Combine(_workDir, "non-interactive.json");
            var nonInteractiveExitCode = CliApp.Run(
                new[]
                {
                    "recipe", "create", nonInteractiveOutPath,
                    "--non-interactive", "--method", "package",
                    "--field", "ToolName=bwa-mem",
                    "--field", "ToolVersion=0.7.17",
                    "--field", "Script=run.sh",
                    "--field", $"ImageRef={ImageRefWithDigest}",
                    "--field", "Packages=bwa=0.7.17=h5bf99c6_8",
                    "--field", "Channels=bioconda",
                    "--input", "reads=fastq-paired",
                    "--output", "bam=bam-primary",
                },
                new StringWriter(),
                new StringWriter());

            Assert.Equal(0, interactiveExitCode);
            Assert.Equal(0, nonInteractiveExitCode);
            Assert.Equal(WithoutVolatileFields(File.ReadAllText(nonInteractiveOutPath)), WithoutVolatileFields(File.ReadAllText(interactiveOutPath)));
        }

        private static string WithoutVolatileFields(string recipeJson) =>
            System.Text.RegularExpressions.Regex.Replace(recipeJson, "\"(Id|CreatedAt)\": \"[^\"]+\",?\n", string.Empty);

        [Fact]
        public void DockerfileWarningPath_DeclinedCancelsWithoutSaving()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "n", "n", "n", "n", "n", "y", // Q&A -> recommend dockerfile
                "", // accept recommended method
                "n", // decline dockerfile warning
            };

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void RestrictedNetworkGatePath_RecommendsMirror_SavesValidRecipe()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "y", // IsRestrictedNetwork
                "y", // HasInternalPackageMirror
                "n", // HasExistingContainerImage
                "n", // HasPackageInPublicChannels
                "n", // HasSourceArchiveAndChecksum
                "n", // HasExistingDockerfile
                "", // accept recommended method (mirror)
                "bwa-mem", "0.7.17", "run.sh",
                ImageRefWithDigest, // ImageRef
                "https://mirror.internal/conda-channel", // MirrorUri
                "bwa=0.7.17=h5bf99c6_8", "", // Packages item + complete
                "", // MirrorKind optional — skip
                "reads", "1", "", // Inputs
                "bam", "1", "", // Outputs
            };

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"PackageMirror\"", json);
        }

        [Fact]
        public void RestrictedNetworkUnknownPath_WarnsOnPackageRecommendation_SavesValidRecipe()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "u", // IsRestrictedNetwork
                "n", // HasInternalPackageMirror
                "n", // HasExistingContainerImage
                "y", // HasPackageInPublicChannels
                "n", // HasSourceArchiveAndChecksum
                "n", // HasExistingDockerfile
                "", // accept recommended method (package)
                "bwa-mem", "0.7.17", "run.sh", ImageRefWithDigest,
                "bwa=0.7.17=h5bf99c6_8", "",
                "bioconda", "",
                "", // PackageEngine defaulted — skip
                "reads", "1", "",
                "bam", "1", "",
            };

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("내부망인지 확실하지 않다고 답했습니다", stdout.ToString());
            Assert.Empty(stderr.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"Conda\"", json);
        }

        [Fact]
        public void UnknownHeavyAnswers_WithholdsRecommendation_RequiresExplicitMethodSelection()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "n", // IsRestrictedNetwork
                "n", // HasInternalPackageMirror
                "u", // HasExistingContainerImage
                "u", // HasPackageInPublicChannels
                "u", // HasSourceArchiveAndChecksum
                "u", // HasExistingDockerfile
                "2", // no recommendation — explicitly pick alternative priority 2 (package)
                "bwa-mem", "0.7.17", "run.sh", ImageRefWithDigest,
                "bwa=0.7.17=h5bf99c6_8", "",
                "bioconda", "",
                "",
                "reads", "1", "",
                "bam", "1", "",
            };

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("추천 보류", stdout.ToString());
            Assert.Empty(stderr.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"Conda\"", json);
        }

        [Fact]
        public void ChangeMethodMidFieldEntry_PackageToSource_PreservesToolNameAndDiscardsPackageFields()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "n", "n", "n", "y", "n", "n", // Q&A -> recommend package
                "", // accept recommended method
                "bwa-mem", "0.7.17", "run.sh",
                "/change-method", // at the ImageRef prompt, switch away from package
                "4", // source
                "y", // confirm change
                ImageRefWithDigest, // ImageRef, now under source
                "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz", // SourceUri
                DigestOnly, // SourceChecksum
                "make", "make install", "", // SourceBuildCommands + complete
                "", // BuildDependencies — leave empty, complete
                "reads", "1", "", // Inputs
                "bam", "1", "", // Outputs
            };

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("유지되는 필드: ToolName, ToolVersion", stdout.ToString());
            Assert.Contains("버려지는 필드: Packages, Channels, PackageEngine", stdout.ToString());
            Assert.Empty(stderr.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"SourceBuild\"", json);
        }

        [Fact]
        public void ChangeMethodAfterInputsCompleted_PackageToMirror_InvalidatedInputsDoNotBlockBuild()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "n", "n", "n", "y", "n", "n", // Q&A -> recommend package
                "", // accept recommended method
                "bwa-mem", "0.7.17", "run.sh", ImageRefWithDigest,
                "bwa=0.7.17=h5bf99c6_8", "",
                "bioconda", "",
                "",
                "reads", "1", "", // Inputs completed under package
                "/change-method", // at the first Outputs "이름:" prompt
                "3", // mirror
                "y", // confirm change
                "https://mirror.internal/conda-channel", // MirrorUri, now under mirror
                "", // MirrorKind optional — skip
                "bam", "1", "", // Outputs, fresh under mirror
            };

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"PackageMirror\"", json);
            Assert.Contains("reads", json);
            Assert.Contains("bam", json);
        }

        [Fact]
        public void CrossFieldImageDigestViolation_TriggersEditRelatedFieldsRecovery_FixesAndSaves()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "n", "n", "y", "n", "n", "n", // Q&A -> recommend container
                "", // accept recommended method
                "bwa-mem", "0.7.17", "run.sh",
                "condaforge/miniforge3:24.3.0-0", // ImageRef
                "sha256:bad", // ImageDigest — malformed, passes authoring but fails final validation
                "", // Command optional list — skip
                "reads", "1", "", // Inputs
                "bam", "1", "", // Outputs
                "1", // recovery: the only action, editing ImageRef+ImageDigest together
                "condaforge/miniforge3:24.3.0-0", // re-enter ImageRef unchanged
                DigestOnly, // re-enter ImageDigest, corrected
            };

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("ImageRef, ImageDigest 항목 함께 수정", stdout.ToString());
            Assert.Empty(stderr.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"BioContainer\"", json);
        }

        [Fact]
        public void OutputClassViolation_EditInPlaceFixesRecovery_SavesValidRecipe()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "n", "n", "y", "n", "n", "n", // Q&A -> recommend container
                "", // accept recommended method
                "bwa-mem", "0.7.17", "run.sh",
                "condaforge/miniforge3:24.3.0-0", // ImageRef
                DigestOnly, // ImageDigest
                "", // Command optional list — skip
                "reads", "1", "", // Inputs
                "bam", "custom", "alignment", "bam", "bogus", "", // Outputs: custom with invalid Class
                "1", // recovery: the only action, reviewing Inputs/Outputs
                "", // ReviewListSection(Inputs): no edit/delete, continue
                "", // PromptInputListField(Inputs): blank name completes the list again
                "e0", // ReviewListSection(Outputs): edit item 0
                "", // keep existing name "bam"
                "1", // bam-primary preset, fixes Class
                "", // ReviewListSection(Outputs): no further edit/delete, continue
                "", // PromptOutputListField(Outputs): blank name completes the list again
            };

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"BioContainer\"", json);
            Assert.Contains("\"Class\": \"primary\"", json);
            Assert.DoesNotContain("bogus", json);
        }

        [Fact]
        public void HelpCommand_AtFieldPrompt_PrintsFieldHelpThenRetriesSameField()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "n", "n", "y", "n", "n", "n", // Q&A -> recommend container
                "", // accept recommended method
                "/help", // ToolName prompt: request help instead of answering
                "bwa-mem", // ToolName, asked again after help text
                "0.7.17", "run.sh",
                "condaforge/miniforge3:24.3.0-0", // ImageRef
                DigestOnly, // ImageDigest
                "", // Command optional list — skip
                "reads", "1", "", // Inputs
                "bam", "1", "", // Outputs
            };

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());

            var stdoutText = stdout.ToString();
            Assert.Contains("도구 이름 — recipe에서 식별할 도구 이름입니다.", stdoutText);
            Assert.Contains("예시: bwa-mem", stdoutText);
            Assert.Contains("필수 항목입니다. 값이 없으면 최종 검증을 통과하지 못합니다.", stdoutText);

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"ToolName\": \"bwa-mem\"", json);
        }
    }
}
