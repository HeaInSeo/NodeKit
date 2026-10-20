using System;
using System.IO;
using System.Linq;
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

        // Interactive_FullSequence_CreatesDraftFile가 쓰는 프롬프트 순서 그대로의 응답. 인덱스 0 functionId, 1 revision,
        // 6 스크립트 경로, 7 executable, 34~37 CPU/Memory request/limit가 필수 입력이다.
        private static readonly string[] _fullTranscript =
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
            var args = new[]
            {
                "function-recipe", "create", outPath,
                "--tool-spec-digest", ToolSpecDigest,
                "--base-tool-image-digest", BaseToolImageDigest,
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(args, new StringReader(string.Join("\n", _fullTranscript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());
            Assert.True(File.Exists(outPath));

            using var document = JsonDocument.Parse(File.ReadAllText(outPath));
            Assert.Equal("samtools.sort", document.RootElement.GetProperty("FunctionId").GetString());
            Assert.Equal("Draft", document.RootElement.GetProperty("State").GetString());
        }

        // S4-04-C: 필수 입력 프롬프트에서 stdin이 끝나면 recipe create와 같이 exit 130으로 취소하고, 저장 파일과
        // lock 파일을 남기지 않는다. answered = EOF 직전까지 응답한 줄 수(그 다음 프롬프트가 필수 입력).
        [Theory]
        [InlineData(0)]  // functionId
        [InlineData(1)]  // revision
        [InlineData(6)]  // 스크립트 경로
        [InlineData(7)]  // executable
        [InlineData(34)] // CPU request
        [InlineData(35)] // CPU limit
        [InlineData(36)] // Memory request
        [InlineData(37)] // Memory limit
        public void Interactive_EofAtRequiredInput_Exit130_NoFile(int answered)
        {
            var outPath = Path.Join(_workDir, "samtools-sort.json");
            var args = new[]
            {
                "function-recipe", "create", outPath,
                "--tool-spec-digest", ToolSpecDigest,
                "--base-tool-image-digest", BaseToolImageDigest,
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var stdin = new StringReader(string.Join("\n", _fullTranscript.Take(answered)));
            var exitCode = CliApp.Run(args, stdin, stdout, stderr);

            Assert.Equal(130, exitCode);
            Assert.Empty(Directory.EnumerateFileSystemEntries(_workDir));
            Assert.Contains("파일은 저장되지 않았습니다.", stdout.ToString());
        }

        // EOF를 받은 프롬프트가 필수가 아니면 지금처럼 빈 값으로 두고 저장한다 — 모든 프롬프트 EOF를 취소로
        // 만들지 않는다. 필수 입력을 모두 받은 뒤 첫 선택 프롬프트(Storage request)에서 stdin이 끝난다.
        [Fact]
        public void Interactive_EofAfterAllRequiredInputs_SavesDraft()
        {
            var outPath = Path.Join(_workDir, "samtools-sort.json");
            var args = new[]
            {
                "function-recipe", "create", outPath,
                "--tool-spec-digest", ToolSpecDigest,
                "--base-tool-image-digest", BaseToolImageDigest,
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var stdin = new StringReader(string.Join("\n", _fullTranscript.Take(38)));
            var exitCode = CliApp.Run(args, stdin, stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());
            using var document = JsonDocument.Parse(File.ReadAllText(outPath));
            Assert.Equal("1Gi", document.RootElement.GetProperty("EnforcedResources").GetProperty("MemoryLimit").GetString());
            Assert.Equal("Draft", document.RootElement.GetProperty("State").GetString());
        }

        // 필수 입력의 빈 줄은 EOF가 아니다 — Draft 작성 중 비워 둘 수 있는 기존 동작을 유지한다.
        [Fact]
        public void Interactive_BlankRequiredInput_IsNotCancel()
        {
            var outPath = Path.Join(_workDir, "samtools-sort.json");
            var args = new[]
            {
                "function-recipe", "create", outPath,
                "--tool-spec-digest", ToolSpecDigest,
                "--base-tool-image-digest", BaseToolImageDigest,
            };
            var transcript = _fullTranscript.ToArray();
            transcript[0] = string.Empty;

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(args, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            using var document = JsonDocument.Parse(File.ReadAllText(outPath));
            Assert.Equal(string.Empty, document.RootElement.GetProperty("FunctionId").GetString());
        }
    }
}
