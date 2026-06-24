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
    }
}
