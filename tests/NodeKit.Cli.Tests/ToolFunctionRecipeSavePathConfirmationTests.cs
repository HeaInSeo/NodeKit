using System;
using System.IO;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// FR-023 — 같은 functionId/revision을 가진 기존 ToolFunctionRecipe 파일이
    /// 있으면 충돌로 감지하고 묵시적으로 덮어쓰지 않는다(quickstart 시나리오 4
    /// 대응, 기존 SavePathConfirmationTests.cs 패턴 미러).
    /// </summary>
    public class ToolFunctionRecipeSavePathConfirmationTests : IDisposable
    {
        private const string ToolSpecDigest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string BaseToolImageDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        private readonly string _workDir =
            Path.Join(Path.GetTempPath(), "nodekit-tfr-savepath-tests-" + Guid.NewGuid());

        public ToolFunctionRecipeSavePathConfirmationTests() => Directory.CreateDirectory(_workDir);

        public void Dispose() => Directory.Delete(_workDir, recursive: true);

        private static string[] BaseArgs(string outPath) => new[]
        {
            "function-recipe", "create", outPath,
            "--tool-spec-digest", ToolSpecDigest,
            "--base-tool-image-digest", BaseToolImageDigest,
            "--non-interactive",
            "--field", "FunctionId=samtools.sort",
            "--field", "Revision=v1",
            "--field", "ScriptPath=./sort.sh",
            "--field", "Command.Executable=samtools",
            "--field", "InputPorts[0].Name=bam",
            "--field", "OutputPorts[0].Name=sortedBam",
            "--field", "FixtureReferences[0].LocalPath=./fixtures/small.bam",
            "--field", "EnforcedResources.CpuRequest=500m",
            "--field", "EnforcedResources.CpuLimit=2000m",
            "--field", "EnforcedResources.MemoryRequest=256Mi",
            "--field", "EnforcedResources.MemoryLimit=1Gi",
        };

        [Fact]
        public void SameFunctionIdAndRevision_DifferentFileName_DetectsConflictAndDoesNotOverwrite()
        {
            var firstPath = Path.Join(_workDir, "samtools-sort.json");
            using (var stdout1 = new StringWriter())
            using (var stderr1 = new StringWriter())
            {
                var firstExit = CliApp.Run(BaseArgs(firstPath), new StringReader(string.Empty), stdout1, stderr1);
                Assert.Equal(0, firstExit);
            }

            var secondPath = Path.Join(_workDir, "samtools-sort-2.json");
            using var stdout2 = new StringWriter();
            using var stderr2 = new StringWriter();
            var secondExit = CliApp.Run(BaseArgs(secondPath), new StringReader(string.Empty), stdout2, stderr2);

            Assert.Equal(1, secondExit);
            Assert.False(File.Exists(secondPath));
            Assert.Contains("samtools.sort", stderr2.ToString());
        }

        [Fact]
        public void DifferentRevision_DifferentFileName_NoConflict()
        {
            var firstPath = Path.Join(_workDir, "samtools-sort-v1.json");
            using (var stdout1 = new StringWriter())
            using (var stderr1 = new StringWriter())
            {
                Assert.Equal(0, CliApp.Run(BaseArgs(firstPath), new StringReader(string.Empty), stdout1, stderr1));
            }

            var secondArgs = BaseArgs(Path.Join(_workDir, "samtools-sort-v2.json"));
            secondArgs[Array.IndexOf(secondArgs, "Revision=v1")] = "Revision=v2";

            using var stdout2 = new StringWriter();
            using var stderr2 = new StringWriter();
            var secondExit = CliApp.Run(secondArgs, new StringReader(string.Empty), stdout2, stderr2);

            Assert.Equal(0, secondExit);
            Assert.True(File.Exists(Path.Join(_workDir, "samtools-sort-v2.json")));
        }

        [Fact]
        public void SamePath_Resave_IsNotTreatedAsConflict()
        {
            var path = Path.Join(_workDir, "samtools-sort.json");
            using (var stdout1 = new StringWriter())
            using (var stderr1 = new StringWriter())
            {
                Assert.Equal(0, CliApp.Run(BaseArgs(path), new StringReader(string.Empty), stdout1, stderr1));
            }

            using var stdout2 = new StringWriter();
            using var stderr2 = new StringWriter();
            var exitCode = CliApp.Run(BaseArgs(path), new StringReader(string.Empty), stdout2, stderr2);

            Assert.Equal(0, exitCode);
        }
    }
}
