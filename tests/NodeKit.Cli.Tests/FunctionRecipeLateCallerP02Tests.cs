using System;
using System.IO;
using System.Linq;
using NodeKit.Cli;
using NodeKit.Cli.Tests.Fakes;
using NodeKit.Grpc;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// P02.late_caller — function-recipe caller의 S4-03-D/E/F 파일·옵션 전달.
    /// D: validate --help는 파일을 열지 않고 usage/exit 0. E: validate/submit의
    /// 알 수 없는 옵션은 조용히 무시하지 않고 stderr + exit 2(RPC 0). F: render는
    /// invalid Ready 입력에서 P06 current preflight 실패를 exit 1로 전달하고 새 출력이나
    /// 기존 파일 overwrite를 만들지 않는다(검증 규칙 자체는 P06 시험이 검수).
    /// </summary>
    public class FunctionRecipeLateCallerP02Tests : IDisposable
    {
        private const string Marker = "existing-output-marker";

        // L1을 통과하지 못하는 Ready Recipe — 필수 필드가 비어 있다.
        private const string InvalidReadyJson = """
            { "SchemaVersion": "draft-1", "State": "Ready", "FunctionId": "samtools.sort" }
            """;

        private readonly string _workDir =
            Path.Join(Path.GetTempPath(), "nodekit-p02-late-" + Guid.NewGuid());

        public FunctionRecipeLateCallerP02Tests() => Directory.CreateDirectory(_workDir);

        public void Dispose() => Directory.Delete(_workDir, recursive: true);

        private string WriteFile(string name, string content)
        {
            var path = Path.Join(_workDir, name);
            File.WriteAllText(path, content);
            return path;
        }

        private static (int Exit, string Stdout, string Stderr) Run(params string[] args)
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exit = CliApp.Run(args, new StringReader(string.Empty), stdout, stderr);
            return (exit, stdout.ToString(), stderr.ToString());
        }

        private string[] WorkDirEntries() =>
            Directory.EnumerateFileSystemEntries(_workDir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray()!;

        [Theory]
        [InlineData("--help")]
        [InlineData("-h")]
        public void S4_03_D_ValidateHelp_PrintsUsageExit0WithoutOpeningAFile(string helpFlag)
        {
            var (exit, stdout, stderr) = Run("function-recipe", "validate", helpFlag);

            Assert.Equal(0, exit);
            Assert.Contains("nodekit function-recipe validate <path>", stdout);
            Assert.Empty(stderr);
            Assert.Empty(Directory.EnumerateFileSystemEntries(_workDir));
        }

        [Fact]
        public void S4_03_D_ValidateHelpAfterPath_DoesNotReadOrRewriteTheFile()
        {
            var recipePath = WriteFile("recipe.json", InvalidReadyJson);

            // 읽을 수 없는 파일이어도 --help는 성공한다 — 파일을 열지 않는다는 증거.
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(recipePath, UnixFileMode.None);
            }

            try
            {
                var (exit, stdout, stderr) = Run("function-recipe", "validate", recipePath, "--help");

                Assert.Equal(0, exit);
                Assert.Contains("nodekit function-recipe validate <path>", stdout);
                Assert.DoesNotContain("검증 통과", stdout);
                Assert.Empty(stderr);
            }
            finally
            {
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(recipePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }

            Assert.Equal(InvalidReadyJson, File.ReadAllText(recipePath));
            Assert.Equal(new[] { "recipe.json" }, WorkDirEntries());
        }

        [Theory]
        [InlineData("--typo")]
        [InlineData("--strict-reproducible")]
        [InlineData("extra")]
        public void S4_03_E_ValidateUnknownOption_Exit2AndFileUntouched(string option)
        {
            var recipePath = WriteFile("recipe.json", InvalidReadyJson);

            var (exit, stdout, stderr) = Run("function-recipe", "validate", recipePath, option);

            Assert.Equal(2, exit);
            Assert.Contains($"알 수 없는 옵션입니다: {option}", stderr);
            Assert.Contains("이 명령은 옵션을 지원하지 않습니다", stderr);
            Assert.DoesNotContain("검증 통과", stdout);
            // 옵션 오류는 파일 검증보다 먼저 끝난다 — L1 위반 출력이 없다.
            Assert.DoesNotContain("L1-", stderr);
            Assert.Equal(InvalidReadyJson, File.ReadAllText(recipePath));
            Assert.Equal(new[] { "recipe.json" }, WorkDirEntries());
        }

        [Fact]
        public void S4_03_E_SubmitUnknownOption_Exit2RpcZeroAndFileUntouched()
        {
            var recipePath = WriteFile("recipe.json", InvalidReadyJson);
            using var server = new GrpcTestServer();
            using var client = new GrpcToolSpecClient(server.Channel);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exit = ToolFunctionRecipeSubmitCommand.Run(
                new[] { "function-recipe", "submit", recipePath, "--typo" },
                stdout,
                stderr,
                client,
                toolFunctionBuildGateOpen: true);

            Assert.Equal(2, exit);
            Assert.Contains("알 수 없는 옵션입니다: --typo", stderr.ToString());
            Assert.Empty(server.Fake.CallOrder);
            Assert.Equal(InvalidReadyJson, File.ReadAllText(recipePath));
            Assert.Equal(new[] { "recipe.json" }, WorkDirEntries());
        }

        [Fact]
        public void S4_03_F_RenderInvalidReady_Exit1NoNewOutputAndNoOverwrite()
        {
            var recipePath = WriteFile("recipe.json", InvalidReadyJson);
            var existingOut = WriteFile("existing.json", Marker);
            var newOut = Path.Join(_workDir, "new.json");

            var existing = Run("function-recipe", "render", recipePath, "--out", existingOut);
            var fresh = Run("function-recipe", "render", recipePath, "--out", newOut);

            Assert.Equal(1, existing.Exit);
            Assert.Equal(1, fresh.Exit);
            Assert.Contains("L1-", existing.Stderr);
            Assert.DoesNotContain("저장되었습니다", existing.Stdout + fresh.Stdout);
            Assert.Equal(Marker, File.ReadAllText(existingOut));
            Assert.False(File.Exists(newOut));
            Assert.Equal(InvalidReadyJson, File.ReadAllText(recipePath));
            // writer에 도달하지 않으므로 temp/lock 파일도 생기지 않는다.
            Assert.Equal(new[] { "existing.json", "recipe.json" }, WorkDirEntries());
        }
    }
}
