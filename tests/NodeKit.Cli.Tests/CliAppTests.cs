using System;
using System.IO;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    public class CliAppTests : IDisposable
    {
        private readonly string _workDir = Path.Combine(Path.GetTempPath(), "nodekit-cli-tests-" + Guid.NewGuid());

        public CliAppTests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
        }

        private const string ValidRecipeJson = """
        {
            "BuildKind": "DockerfileFallback",
            "ToolName": "bwa",
            "Version": "0.7.17",
            "BaseImage": "registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "DockerfileContent": "FROM registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\nRUN echo ok\nUSER 1000\n",
            "Script": "bwa mem",
            "Inputs": [ { "Name": "reads", "Role": "sample-fastq", "Format": "fastq", "Shape": "pair" } ],
            "Outputs": [ { "Name": "aligned", "Role": "aligned-bam", "Format": "bam", "Shape": "single", "Class": "primary" } ]
        }
        """;

        private const string InvalidRecipeJson = """
        {
            "BuildKind": "SourceBuild",
            "ToolName": "bwa",
            "Version": "0.7.17",
            "BaseImage": "registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "SourceUri": "https://example.com/bwa-0.7.17.tar.gz",
            "SourceBuildCommands": [ "make", "make install" ],
            "Script": "bwa mem",
            "Inputs": [ { "Name": "reads", "Role": "sample-fastq", "Format": "fastq", "Shape": "pair" } ],
            "Outputs": [ { "Name": "aligned", "Role": "aligned-bam", "Format": "bam", "Shape": "single", "Class": "primary" } ]
        }
        """;

        [Fact]
        public void Validate_ValidRecipe_ReturnsZero()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "validate", recipePath }, stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("OK", stdout.ToString());
            Assert.Empty(stderr.ToString());
        }

        [Fact]
        public void Validate_InvalidRecipe_ReturnsOneAndPrintsViolations()
        {
            var recipePath = WriteFile("recipe.json", InvalidRecipeJson);
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "validate", recipePath }, stdout, stderr);

            Assert.Equal(1, exitCode);
            Assert.Contains("L1-SRC-001", stderr.ToString());
            Assert.Contains("(SourceChecksum)", stderr.ToString());
        }

        [Fact]
        public void Render_ValidRecipe_WritesLegacyBuildRequestJsonAndReturnsZero()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            var outPath = Path.Combine(_workDir, "build-request.json");
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "render", recipePath, "--out", outPath }, stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            var json = File.ReadAllText(outPath);
            Assert.Contains("\"ToolName\"", json);
            Assert.Contains("\"ImageUri\"", json);
            Assert.Contains("\"DockerfileContent\"", json);
            Assert.Contains("bwa", json);
        }

        [Fact]
        public void Render_InvalidRecipe_ReturnsOneAndDoesNotWriteOutputFile()
        {
            var recipePath = WriteFile("recipe.json", InvalidRecipeJson);
            var outPath = Path.Combine(_workDir, "build-request.json");
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "render", recipePath, "--out", outPath }, stdout, stderr);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("L1-SRC-001", stderr.ToString());
            Assert.Contains("(SourceChecksum)", stderr.ToString());
        }

        [Fact]
        public void Run_UnknownCommand_ReturnsTwo()
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "submit", "recipe.json" }, stdout, stderr);

            Assert.Equal(2, exitCode);
        }

        private string WriteFile(string name, string content)
        {
            var path = Path.Combine(_workDir, name);
            File.WriteAllText(path, content);
            return path;
        }
    }
}
