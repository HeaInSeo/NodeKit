using System;
using System.IO;
using System.Text.Json;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// SC-005 (P07, 2026-10-11 정정) — `function-recipe submit`은 모든 시도에 명확한 결과를 준다.
    /// 여기서는 실제 CLI 진입점(CliApp)으로 로컬 입력 단계만 본다: 파일/SchemaVersion 오류는 exit 2,
    /// Draft와 현재 검증 실패 Ready는 exit 1이며 둘 다 NodeVault 주소 확인 전에 끝난다(요청 0회).
    /// 유효한 Ready는 현재 NodeVault가 kind 2를 받지 않으므로 상류 게이트에서 RPC 0회로 끝난다. 그 동작과
    /// 게이트를 연 BuildService client 경로·RPC transcript는 ToolFunctionSecondImageBuildP07Tests.
    /// 이전의 issue #19 "빌드 게이트 미개방" 고정 안내(State 무관 차단)는 더 이상 나오지 않는다.
    /// </summary>
    public class ToolFunctionRecipeSubmitCommandTests : IDisposable
    {
        private const string RetiredGateMessage = "게이트가 아직 열려 있지 않습니다";

        private readonly string _workDir =
            Path.Join(Path.GetTempPath(), "nodekit-tfr-submit-tests-" + Guid.NewGuid());

        public ToolFunctionRecipeSubmitCommandTests() => Directory.CreateDirectory(_workDir);

        public void Dispose() => Directory.Delete(_workDir, recursive: true);

        // 차단 fixture에는 SchemaVersion draft-1을 명시한다(cli-acceptance-contract.json
        // submitBlockDisposition). 누락/미지원 schema는 아래 별도 exit 2 변형이다.
        private string WriteRecipeFile(string state)
        {
            var path = Path.Join(_workDir, "recipe.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { SchemaVersion = "draft-1", State = state, FunctionId = "samtools.sort" }));
            return path;
        }

        private static (int Exit, string Stdout, string Stderr) Submit(string path)
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "function-recipe", "submit", path }, new StringReader(string.Empty), stdout, stderr);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }

        [Theory]
        [InlineData("""{ "State": "Ready", "FunctionId": "samtools.sort" }""")]
        [InlineData("""{ "SchemaVersion": null, "State": "Ready", "FunctionId": "samtools.sort" }""")]
        [InlineData("""{ "SchemaVersion": "draft-2", "State": "Ready", "FunctionId": "samtools.sort" }""")]
        public void SchemaVersionMissingOrUnsupported_ReturnsExit2BeforeAnyRequest(string json)
        {
            var path = Path.Join(_workDir, "recipe.json");
            File.WriteAllText(path, json);

            var (exitCode, stdout, stderr) = Submit(path);

            Assert.Equal(2, exitCode);
            Assert.DoesNotContain(RetiredGateMessage, stdout);
            Assert.Contains("SchemaVersion", stderr);
            Assert.Equal(json, File.ReadAllText(path));
        }

        [Fact]
        public void DraftState_RejectedAsNotReady_Exit1_FileUnchanged()
        {
            var path = WriteRecipeFile("Draft");
            var before = File.ReadAllText(path);

            var (exitCode, stdout, stderr) = Submit(path);

            Assert.Equal(1, exitCode);
            Assert.Contains("RECIPE_NOT_READY", stderr);
            Assert.DoesNotContain(RetiredGateMessage, stdout + stderr);
            Assert.DoesNotContain("NodeVault 주소가 필요합니다", stderr);
            Assert.Equal(before, File.ReadAllText(path));
        }

        [Fact]
        public void ReadyStateFailingCurrentValidation_Exit1WithViolations_FileUnchanged()
        {
            var path = WriteRecipeFile("Ready");
            var before = File.ReadAllText(path);

            var (exitCode, stdout, stderr) = Submit(path);

            Assert.Equal(1, exitCode);
            Assert.Contains("L1-TFR", stderr);
            Assert.DoesNotContain(RetiredGateMessage, stdout + stderr);
            Assert.DoesNotContain("NodeVault 주소가 필요합니다", stderr);
            Assert.Equal(before, File.ReadAllText(path));
        }

        [Fact]
        public void MissingFile_ReturnsExit2WithoutCrashing()
        {
            var missingPath = Path.Join(_workDir, "does-not-exist.json");

            var (exitCode, _, _) = Submit(missingPath);

            Assert.Equal(2, exitCode);
        }

        [Fact]
        public void StrictReproducibleOption_IsNotAccepted_Exit2()
        {
            var path = WriteRecipeFile("Ready");

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(
                new[] { "function-recipe", "submit", path, "--strict-reproducible" }, new StringReader(string.Empty), stdout, stderr);

            Assert.Equal(2, exitCode);
        }
    }
}
