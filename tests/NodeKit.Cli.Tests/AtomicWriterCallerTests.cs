using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// P02.writer_caller — create/validate/render 저장 경로가 AtomicFileWriter를
    /// 거쳐 실패(2)/취소(130)를 그대로 전달하는지 검수한다(S4-03-G caller 적용,
    /// S1-06-C04/C07). 저장 경계 자체는 AtomicFileWriterTests가 검수한다.
    ///
    /// 공통 기대: 실패면 기존 파일 byte 보존, 새 대상이면 최종 파일 없음,
    /// 성공 안내 없음, 임시 파일 잔여 없음, stack trace 없음.
    /// </summary>
    public class AtomicWriterCallerTests : IDisposable
    {
        private const string Marker = "existing-output-marker";

        private const string ImageRefWithDigest =
            "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private const string ToolSpecDigest =
            "sha256:1111111111111111111111111111111111111111111111111111111111111111";

        private const string BaseToolImageDigest =
            "sha256:2222222222222222222222222222222222222222222222222222222222222222";

        private const string ValidRecipeJson = """
        {
            "SchemaVersion": "draft-1",
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

        private readonly string _workDir = Path.Join(Path.GetTempPath(), "nodekit-writer-caller-tests-" + Guid.NewGuid());
        private readonly IDisposable _resolveClientOverride =
            ResolveRecipeClientTestOverride.Use(NullResolveRecipeClient.Instance);

        public AtomicWriterCallerTests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
            _resolveClientOverride.Dispose();
        }

        /// <summary>출력 경로를 받는 저장 caller 4개. 각 항목: (이름, 실행).</summary>
        public static IEnumerable<object[]> OutputCallers() =>
            new[] { "render", "recipe-create", "function-recipe-create", "function-recipe-render" }
                .Select(name => new object[] { name });

        // ── 성공: 새 완전한 파일, 임시 파일 없음 ────────────────────────────────

        [Theory]
        [MemberData(nameof(OutputCallers))]
        public void Caller_Success_CommitsCompleteJson_NoTempLeft(string caller)
        {
            var outPath = Path.Join(_workDir, "out", "result.json");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllText(outPath, Marker);

            var (exit, _, stderr) = RunCaller(caller, outPath);

            Assert.Equal(0, exit);
            Assert.Empty(stderr);
            using var json = JsonDocument.Parse(File.ReadAllText(outPath));
            Assert.Equal(JsonValueKind.Object, json.RootElement.ValueKind);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(outPath)!, "*" + AtomicFileWriter.TempSuffix));
        }

        // ── 실패: 다른 writer가 잠금을 잡은 동안 → exit 2, 기존 bytes 보존 ──────────

        [Theory]
        [MemberData(nameof(OutputCallers))]
        public void Caller_WhileLocked_Exit2_KeepsExistingBytes_NoSuccessMessage(string caller)
        {
            var outPath = Path.Join(_workDir, "out", "result.json");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllText(outPath, Marker);

            (int Exit, string Stdout, string Stderr) result;
            using (HoldWriterLock(outPath))
            {
                result = RunCaller(caller, outPath);
            }

            Assert.Equal(2, result.Exit);
            Assert.Equal(Marker, File.ReadAllText(outPath));
            Assert.Contains($"[{AtomicFileWriter.LockedCode}]", result.Stderr);
            AssertNoSuccessMessage(caller, outPath, result.Stdout);
            AssertNoStackTrace(result.Stderr);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(outPath)!, "*" + AtomicFileWriter.TempSuffix));
        }

        [Theory]
        [MemberData(nameof(OutputCallers))]
        public void Caller_TargetIsDirectory_Exit2_Unsupported_DirectoryUntouched(string caller)
        {
            var outPath = Path.Join(_workDir, "out.json");
            Directory.CreateDirectory(outPath);
            File.WriteAllText(Path.Join(outPath, "inside.txt"), Marker);

            var (exit, stdout, stderr) = RunCaller(caller, outPath);

            Assert.Equal(2, exit);
            Assert.Contains($"[{AtomicFileWriter.TargetUnsupportedCode}]", stderr);
            Assert.Equal(Marker, File.ReadAllText(Path.Join(outPath, "inside.txt")));
            AssertNoSuccessMessage(caller, outPath, stdout);
            AssertNoStackTrace(stderr);
        }

        [Theory]
        [MemberData(nameof(OutputCallers))]
        public void Caller_ParentMissing_Exit2_NoFileNoDirectoryCreated(string caller)
        {
            var parent = Path.Join(_workDir, "no-such-dir");
            var outPath = Path.Join(parent, "result.json");

            var (exit, stdout, stderr) = RunCaller(caller, outPath);

            Assert.Equal(2, exit);
            Assert.Contains($"[{AtomicFileWriter.ParentMissingCode}]", stderr);
            Assert.False(Directory.Exists(parent));
            AssertNoSuccessMessage(caller, outPath, stdout);
            AssertNoStackTrace(stderr);
        }

        [Theory]
        [MemberData(nameof(OutputCallers))]
        public void Caller_DirectoryNotWritable_Exit2_KeepsExistingBytes(string caller)
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Skip("Unix 권한 검사 전용");
                return;
            }

            if (Environment.UserName == "root")
            {
                Assert.Skip("root는 디렉터리 쓰기 권한 제한을 무시함");
            }

            var outDir = Path.Join(_workDir, "readonly");
            Directory.CreateDirectory(outDir);
            var outPath = Path.Join(outDir, "result.json");
            File.WriteAllText(outPath, Marker);
            File.SetUnixFileMode(outDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            (int Exit, string Stdout, string Stderr) result;
            try
            {
                result = RunCaller(caller, outPath);
            }
            finally
            {
                File.SetUnixFileMode(outDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            Assert.Equal(2, result.Exit);
            Assert.Contains($"[{AtomicFileWriter.FailedCode}]", result.Stderr);
            Assert.Equal(Marker, File.ReadAllText(outPath));
            AssertNoSuccessMessage(caller, outPath, result.Stdout);
            AssertNoStackTrace(result.Stderr);
        }

        // ── function-recipe validate: 입력 파일에 되돌려 쓰는 caller ───────────────

        [Fact]
        public void FunctionRecipeValidate_Success_RewritesInputAtomically()
        {
            var recipePath = CreateFunctionRecipe(Path.Join(_workDir, "fn.json"));

            var (exit, stdout, stderr) = Run("function-recipe", "validate", recipePath);

            Assert.Equal(0, exit);
            Assert.Contains("검증 통과", stdout);
            Assert.Empty(stderr);
            using var json = JsonDocument.Parse(File.ReadAllText(recipePath));
            Assert.Equal("Ready", json.RootElement.GetProperty("State").GetString());
            Assert.Empty(Directory.GetFiles(_workDir, "*" + AtomicFileWriter.TempSuffix));
        }

        [Fact]
        public void FunctionRecipeValidate_WhileLocked_Exit2_KeepsInputBytes_NoSuccessMessage()
        {
            var recipePath = CreateFunctionRecipe(Path.Join(_workDir, "fn.json"));
            var before = File.ReadAllBytes(recipePath);

            (int Exit, string Stdout, string Stderr) result;
            using (HoldWriterLock(recipePath))
            {
                result = Run("function-recipe", "validate", recipePath);
            }

            Assert.Equal(2, result.Exit);
            Assert.Equal(before, File.ReadAllBytes(recipePath));
            Assert.DoesNotContain("검증 통과", result.Stdout);
            Assert.Contains($"[{AtomicFileWriter.LockedCode}]", result.Stderr);
            AssertNoStackTrace(result.Stderr);
        }

        // ── recipe create 대화형: 저장 실패 2 / 저장 직전 취소 130 (S1-06-C04/C07) ──

        [Fact]
        public void InteractiveRecipeCreate_SaveFails_Exit2_KeepsExistingBytes_NoSavedMessage()
        {
            var outPath = Path.Join(_workDir, "interactive.json");
            File.WriteAllText(outPath, Marker);

            (int Exit, string Stdout, string Stderr) result;
            using (HoldWriterLock(outPath))
            {
                result = RunInteractive(outPath, cancelled: false);
            }

            Assert.Equal(2, result.Exit);
            Assert.Equal(Marker, File.ReadAllText(outPath));
            Assert.DoesNotContain("저장되었습니다", result.Stdout);
            Assert.Contains($"[{AtomicFileWriter.LockedCode}]", result.Stderr);
            AssertNoStackTrace(result.Stderr);
        }

        [Fact]
        public void InteractiveRecipeCreate_CancelledBeforeSave_Exit130_KeepsExistingBytes()
        {
            var outPath = Path.Join(_workDir, "interactive.json");
            File.WriteAllText(outPath, Marker);

            var (exit, stdout, _) = RunInteractive(outPath, cancelled: true);

            Assert.Equal(130, exit);
            Assert.Equal(Marker, File.ReadAllText(outPath));
            Assert.DoesNotContain("저장되었습니다", stdout);
            Assert.Contains("파일은 저장되지 않았습니다.", stdout);
        }

        [Fact]
        public void InteractiveRecipeCreate_Success_PrintsSavedOnlyAfterCommit()
        {
            var outPath = Path.Join(_workDir, "interactive.json");
            File.WriteAllText(outPath, Marker);

            var (exit, stdout, stderr) = RunInteractive(outPath, cancelled: false);

            Assert.Equal(0, exit);
            Assert.Empty(stderr);
            Assert.Contains($"저장되었습니다: {outPath}", stdout);
            Assert.Contains("bwa=0.7.17=h5bf99c6_8", File.ReadAllText(outPath));
        }

        // ── helpers ────────────────────────────────────────────────────────────

        private (int Exit, string Stdout, string Stderr) RunCaller(string caller, string outPath) => caller switch
        {
            "render" => Run("render", WriteInput("recipe.json", ValidRecipeJson), "--out", outPath),
            "recipe-create" => Run(
                "recipe", "create", outPath,
                "--non-interactive", "--method", "package",
                "--field", "ToolName=bwa-mem",
                "--field", "ToolVersion=0.7.17",
                "--field", "Script=run.sh",
                "--field", $"BaseImage={ImageRefWithDigest}",
                "--field", "Packages=bwa=0.7.17=h5bf99c6_8",
                "--field", "Channels=bioconda"),
            "function-recipe-create" => Run(FunctionCreateArgs(outPath)),
            "function-recipe-render" => Run("function-recipe", "render", ReadyFunctionRecipe(), "--out", outPath),
            _ => throw new ArgumentOutOfRangeException(nameof(caller), caller, null),
        };

        private static void AssertNoSuccessMessage(string caller, string outPath, string stdout)
        {
            Assert.DoesNotContain("저장되었습니다", stdout);
            if (caller == "function-recipe-create")
            {
                Assert.DoesNotContain(outPath, stdout);
            }
        }

        private static void AssertNoStackTrace(string stderr)
        {
            Assert.DoesNotContain("   at ", stderr);
            Assert.DoesNotContain("Exception", stderr);
        }

        // 다른 NodeKit writer가 같은 대상을 저장하는 중인 상태를 만든다.
        private static FileStream HoldWriterLock(string target) =>
            new(
                AtomicFileWriter.LockPath(Path.GetDirectoryName(Path.GetFullPath(target))!, Path.GetFileName(target)),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);

        private string WriteInput(string name, string content)
        {
            var inputDir = Path.Join(_workDir, "input");
            Directory.CreateDirectory(inputDir);
            var path = Path.Join(inputDir, name);
            File.WriteAllText(path, content);
            return path;
        }

        private static string[] FunctionCreateArgs(string outPath) => new[]
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
            "--field", "InputPorts[0].Name=bam",
            "--field", "InputPorts[0].Required=true",
            "--field", "OutputPorts[0].Name=sortedBam",
            "--field", "FixtureReferences[0].LocalPath=./fixtures/small.bam",
            "--field", "EnforcedResources.CpuRequest=500m",
            "--field", "EnforcedResources.CpuLimit=2000m",
            "--field", "EnforcedResources.MemoryRequest=256Mi",
            "--field", "EnforcedResources.MemoryLimit=1Gi",
        };

        // function-recipe create(Draft)로 만든 입력. 다른 디렉터리에 두어 출력
        // 디렉터리의 FR-023 functionId/revision 충돌 검사와 겹치지 않게 한다.
        private string CreateFunctionRecipe(string path)
        {
            var (exit, _, stderr) = Run(FunctionCreateArgs(path));
            Assert.True(exit == 0, stderr);
            return path;
        }

        private string ReadyFunctionRecipe()
        {
            var inputDir = Path.Join(_workDir, "input");
            Directory.CreateDirectory(inputDir);
            var path = Path.Join(inputDir, "ready.json");
            if (!File.Exists(path))
            {
                CreateFunctionRecipe(path);
                var (exit, _, stderr) = Run("function-recipe", "validate", path);
                Assert.True(exit == 0, stderr);
            }

            return path;
        }

        private (int Exit, string Stdout, string Stderr) RunInteractive(string outPath, bool cancelled)
        {
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "n", "y", "n", "n", // Q&A -> recommend package
                "", // accept recommended method
                "bioconda", "", // 채널 확정 단계
                "0", // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh", ImageRefWithDigest,
                "bwa=0.7.17=h5bf99c6_8", "",
            };
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exit = RecipeCreateInteractiveRunner.Run(
                outPath,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                new FixedCancellationSource(cancelled),
                resolveClient: NullResolveRecipeClient.Instance,
                imageDigestResolver: NullImageDigestResolver.Instance);

            return (exit, stdout.ToString(), stderr.ToString());
        }

        private static (int Exit, string Stdout, string Stderr) Run(params string[] args)
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exit = CliApp.Run(args, new StringReader(string.Empty), stdout, stderr);
            return (exit, stdout.ToString(), stderr.ToString());
        }

        private sealed class FixedCancellationSource : IRecipeCreateCancellationSource
        {
            public FixedCancellationSource(bool cancelled) => IsCancellationRequested = cancelled;

            public bool IsCancellationRequested { get; }
        }
    }
}
