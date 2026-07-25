using System;
using System.IO;
using System.Text.Json;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// nodekit function-recipe create — 대화형/비대화형 양쪽, quickstart.md
    /// 시나리오 1의 정확한 --field 조합으로 Draft 파일 생성을 확인한다.
    /// </summary>
    public class ToolFunctionRecipeCreateCommandTests : IDisposable
    {
        private const string ToolSpecDigest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string BaseToolImageDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        private readonly string _workDir =
            Path.Join(Path.GetTempPath(), "nodekit-tfr-create-tests-" + Guid.NewGuid());

        public ToolFunctionRecipeCreateCommandTests() => Directory.CreateDirectory(_workDir);

        public void Dispose() => Directory.Delete(_workDir, recursive: true);

        [Fact]
        public void NonInteractive_QuickstartScenario1FieldCombination_CreatesDraftFile()
        {
            var outPath = Path.Join(_workDir, "samtools-sort.json");
            var args = new[]
            {
                "function-recipe", "create", outPath,
                "--tool-spec-digest", ToolSpecDigest,
                "--base-tool-image-digest", BaseToolImageDigest,
                "--non-interactive",
                "--field", "FunctionId=samtools.sort",
                "--field", "Revision=v1",
                "--field", "ScriptPath=./sort.sh",
                "--field", "Command.Executable=samtools",
                "--field", "Command.Arguments=sort",
                "--field", "Command.Arguments=-@4",
                "--field", "InputPorts[0].Name=bam",
                "--field", "InputPorts[0].Required=true",
                "--field", "OutputPorts[0].Name=sortedBam",
                "--field", "FixtureReferences[0].LocalPath=./fixtures/small.bam",
                "--field", "EnforcedResources.CpuRequest=500m",
                "--field", "EnforcedResources.CpuLimit=2000m",
                "--field", "EnforcedResources.MemoryRequest=256Mi",
                "--field", "EnforcedResources.MemoryLimit=1Gi",
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(args, new StringReader(string.Empty), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());
            Assert.True(File.Exists(outPath));

            using var document = JsonDocument.Parse(File.ReadAllText(outPath));
            var root = document.RootElement;
            Assert.Equal("Draft", root.GetProperty("State").GetString());
            Assert.Equal("samtools.sort", root.GetProperty("FunctionId").GetString());
            Assert.Equal("bam", root.GetProperty("InputPorts")[0].GetProperty("Name").GetString());
            Assert.Equal("sortedBam", root.GetProperty("OutputPorts")[0].GetProperty("Name").GetString());
        }

        [Fact]
        public void NonInteractive_WithoutOutputPath_ReturnsExit2()
        {
            var args = new[]
            {
                "function-recipe", "create",
                "--tool-spec-digest", ToolSpecDigest,
                "--base-tool-image-digest", BaseToolImageDigest,
                "--non-interactive",
                "--field", "FunctionId=samtools.sort",
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(args, new StringReader(string.Empty), stdout, stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("경로", stderr.ToString());
        }

        [Fact]
        public void NonInteractive_MissingToolSpecDigest_ReturnsExit2()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var args = new[]
            {
                "function-recipe", "create", outPath,
                "--base-tool-image-digest", BaseToolImageDigest,
                "--non-interactive",
                "--field", "FunctionId=samtools.sort",
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(args, new StringReader(string.Empty), stdout, stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("--tool-spec-digest", stderr.ToString());
        }

        [Fact]
        public void Interactive_FullSequence_CreatesDraftFile()
        {
            var outPath = Path.Join(_workDir, "samtools-sort.json");
            var transcript = new[]
            {
                "samtools.sort",    // functionId
                "v1",               // revision
                "",                 // displayLabel
                "",                 // displayDescription
                "",                 // displayCategory
                "",                 // displayTags
                "./sort.sh",        // scriptPath
                // -- Command --
                "samtools",         // executable
                "sort,-@4",         // arguments csv
                "",                 // workingDirectory
                "",                 // environment loop: empty -> stop
                "",                 // exit codes (default 0)
                "",                 // soft timeout
                "",                 // hard timeout
                // -- Input ports --
                "bam",              // port name
                "",                 // dataFormat
                "",                 // cardinality
                "y",                // required
                "",                 // pathPlacementRule
                "",                 // companionFiles
                "",                 // next port name -> stop
                // -- Output ports --
                "sortedBam",        // port name
                "",                 // dataFormat
                "",                 // cardinality
                "./out/*.bam",      // pathOrGlob
                "",                 // completionCheck
                "",                 // downstreamCompatibilityNote
                "",                 // next port name -> stop
                // -- Fixtures --
                "./fixtures/small.bam", // localPath
                "",                 // localPath again -> empty
                "",                 // contentDigest -> empty -> stop
                // -- Expected results (1 output port) --
                "",                 // sortedBam rule -> skip
                // -- Intermediate file policies --
                "",                 // pathOrPattern -> stop
                // -- Parameters --
                "",                 // name -> stop
                // -- Enforced resources --
                "500m",             // CpuRequest
                "2000m",            // CpuLimit
                "256Mi",            // MemoryRequest
                "1Gi",              // MemoryLimit
                "",                 // StorageRequest
                "",                 // StorageLimit
                "",                 // MaxExecutionTimeSeconds
                "",                 // Parallelism
                // -- Execution environment --
                "",                 // SupportedPlatforms
                "",                 // WritablePaths
                "",                 // NetworkPolicy
                "",                 // RequiresRoot
                "",                 // RequiredCapabilities
                // -- Validation requirements --
                "",                 // MinimumObservationLevel
                "",                 // resourceSamples
                "",                 // processEvents
                "",                 // fileEvents
                "",                 // networkEvents
            };

            var args = new[]
            {
                "function-recipe", "create", outPath,
                "--tool-spec-digest", ToolSpecDigest,
                "--base-tool-image-digest", BaseToolImageDigest,
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(args, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());
            Assert.True(File.Exists(outPath));

            using var document = JsonDocument.Parse(File.ReadAllText(outPath));
            Assert.Equal("samtools.sort", document.RootElement.GetProperty("FunctionId").GetString());
            Assert.Equal("Draft", document.RootElement.GetProperty("State").GetString());
        }
    }
}
