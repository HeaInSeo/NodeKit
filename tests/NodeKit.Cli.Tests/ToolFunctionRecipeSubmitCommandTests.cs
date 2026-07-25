using System;
using System.IO;
using System.Text.Json;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// SC-005 — 제출 시도는 State/내용과 무관하게 100% 게이트 미개방 안내로
    /// 차단된다(quickstart 시나리오 3b). Draft/Ready 두 State 모두 확인한다.
    /// </summary>
    public class ToolFunctionRecipeSubmitCommandTests : IDisposable
    {
        private readonly string _workDir =
            Path.Join(Path.GetTempPath(), "nodekit-tfr-submit-tests-" + Guid.NewGuid());

        public ToolFunctionRecipeSubmitCommandTests() => Directory.CreateDirectory(_workDir);

        public void Dispose() => Directory.Delete(_workDir, recursive: true);

        private string WriteRecipeFile(string state)
        {
            var path = Path.Join(_workDir, "recipe.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { State = state, FunctionId = "samtools.sort" }));
            return path;
        }

        [Fact]
        public void DraftState_AlwaysBlockedWithGateMessage()
        {
            var path = WriteRecipeFile("Draft");

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "function-recipe", "submit", path }, new StringReader(string.Empty), stdout, stderr);

            Assert.Equal(1, exitCode);
            Assert.Contains("게이트가 아직 열려 있지 않습니다", stdout.ToString());
        }

        [Fact]
        public void ReadyState_AlwaysBlockedWithGateMessage()
        {
            var path = WriteRecipeFile("Ready");

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "function-recipe", "submit", path }, new StringReader(string.Empty), stdout, stderr);

            Assert.Equal(1, exitCode);
            Assert.Contains("게이트가 아직 열려 있지 않습니다", stdout.ToString());
        }

        [Fact]
        public void MissingFile_ReturnsExit2WithoutCrashing()
        {
            var missingPath = Path.Join(_workDir, "does-not-exist.json");

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "function-recipe", "submit", missingPath }, new StringReader(string.Empty), stdout, stderr);

            Assert.Equal(2, exitCode);
        }
    }
}
