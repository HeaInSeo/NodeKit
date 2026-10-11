using System;
using System.IO;
using System.Text;
using System.Text.Json;
using NodeKit.Authoring.ToolFunctionRecipes;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// P06.base_script — Recipe 디렉터리 기준 ScriptPath source materialize와
    /// exact base image A 검증 (v0.9.1 계약 S3-03-A..E, S3-02-B/C/E base image 부분).
    /// raw_spec 직렬화/typed mapper(P06.raw_typed)와 render/submit current gate
    /// (P06.current)는 이 테스트 범위가 아니다.
    /// </summary>
    public class ToolFunctionSourceMaterializerP06Tests : IDisposable
    {
        private const string DigestA = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string ScriptB = "#!/bin/sh\necho 1\n";

        private readonly string _workDir =
            Path.Join(Path.GetTempPath(), "nodekit-p06-base-script-" + Guid.NewGuid());

        public ToolFunctionSourceMaterializerP06Tests() => Directory.CreateDirectory(_workDir);

        public void Dispose()
        {
            if (!OperatingSystem.IsWindows())
            {
                foreach (var file in Directory.EnumerateFiles(_workDir, "*", SearchOption.AllDirectories))
                {
                    File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }

            Directory.Delete(_workDir, recursive: true);
        }

        private string ProjectDir => Path.Join(_workDir, "project");

        private string RecipePath => Path.Join(ProjectDir, "recipe.json");

        private static ToolFunctionRecipe Recipe(string scriptPath, string baseImageDigest = DigestA) =>
            new() { BaseToolImageDigest = baseImageDigest, ScriptPath = scriptPath };

        private string WriteScript(string relativePath, string content)
        {
            var path = Path.Join(ProjectDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));
            return path;
        }

        private ToolFunctionSourceError Fail(ToolFunctionRecipe recipe)
        {
            Assert.False(ToolFunctionSourceMaterializer.TryMaterialize(recipe, RecipePath, out var snapshot, out var error));
            Assert.Null(snapshot);
            return error!;
        }

        // S3-03-A: Recipe 위치 기준으로 읽는다. 다른 CWD에서 실행해도 같은 파일을 읽는지는
        // CwdIndependenceP06Tests가 실제 CWD를 바꿔 확인한다.
        [Fact]
        public void S3_03_A_RelativeScriptPath_ResolvesAgainstRecipeDirectory()
        {
            WriteScript("sort.sh", ScriptB);

            Assert.True(ToolFunctionSourceMaterializer.TryMaterialize(Recipe("./sort.sh"), RecipePath, out var snapshot, out var error));

            Assert.Null(error);
            Assert.Equal(ScriptB, snapshot!.Script);
            Assert.Equal(DigestA, snapshot.BaseImageDigest);
        }

        [Fact]
        public void S3_03_A_NestedAndParentRelativePaths_ResolveAgainstRecipeDirectory()
        {
            WriteScript("scripts/run.sh", "echo nested\n");
            File.WriteAllText(Path.Join(_workDir, "shared.sh"), "echo parent\n");

            Assert.True(ToolFunctionSourceMaterializer.TryMaterialize(Recipe("scripts/run.sh"), RecipePath, out var nested, out _));
            Assert.True(ToolFunctionSourceMaterializer.TryMaterialize(Recipe("../shared.sh"), RecipePath, out var parent, out _));

            Assert.Equal("echo nested\n", nested!.Script);
            Assert.Equal("echo parent\n", parent!.Script);
        }

        // S3-03-B: source bytes 그대로(LF 2개 포함). 경로 문자열은 script 값이 아니다.
        // snapshot의 ScriptPath는 선언된 논리 상대 경로이며 해석된 절대 경로가 아니다.
        [Fact]
        public void S3_03_B_ScriptContentIsExactSource_NotThePath()
        {
            WriteScript("sort.sh", ScriptB);

            Assert.True(ToolFunctionSourceMaterializer.TryMaterialize(Recipe("./sort.sh"), RecipePath, out var snapshot, out _));

            Assert.Equal(new byte[] { 0x23, 0x21, 0x2F, 0x62, 0x69, 0x6E, 0x2F, 0x73, 0x68, 0x0A, 0x65, 0x63, 0x68, 0x6F, 0x20, 0x31, 0x0A }, Encoding.UTF8.GetBytes(snapshot!.Script));
            Assert.NotEqual("./sort.sh", snapshot.Script);
            Assert.Equal("./sort.sh", snapshot.ScriptPath);
            Assert.DoesNotContain(_workDir, snapshot.ToString(), StringComparison.Ordinal);
            Assert.Equal(
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(ScriptB))),
                snapshot.ScriptSha256);
        }

        [Fact]
        public void S3_03_B_NonAsciiAndBomBytes_ArePreservedVerbatim()
        {
            var path = Path.Join(ProjectDir, "u.sh");
            Directory.CreateDirectory(ProjectDir);
            byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("echo 한글\r\n")];
            File.WriteAllBytes(path, bytes);

            Assert.True(ToolFunctionSourceMaterializer.TryMaterialize(Recipe("u.sh"), RecipePath, out var snapshot, out _));

            Assert.Equal(bytes, new UTF8Encoding(false).GetBytes(snapshot!.Script));
        }

        // S3-03-C: 없는 파일 / 디렉터리 / 읽기 거부 → 진단 + exit 2, 예외 없음.
        [Fact]
        public void S3_03_C_MissingFile_ReturnsExit2Diagnostic()
        {
            Directory.CreateDirectory(ProjectDir);

            var error = Fail(Recipe("./missing.sh"));

            Assert.Equal(2, error.ExitCode);
            Assert.Equal(ToolFunctionSourceMaterializer.ScriptReadFailedCode, error.Code);
            Assert.Contains("./missing.sh", error.Message, StringComparison.Ordinal);
            Assert.Contains(Path.Join(ProjectDir, "missing.sh"), error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void S3_03_C_MissingParentDirectory_ReturnsExit2Diagnostic()
        {
            Directory.CreateDirectory(ProjectDir);

            var error = Fail(Recipe("nope/run.sh"));

            Assert.Equal(2, error.ExitCode);
            Assert.Equal(ToolFunctionSourceMaterializer.ScriptReadFailedCode, error.Code);
        }

        [Fact]
        public void S3_03_C_DirectoryPath_ReturnsExit2Diagnostic()
        {
            Directory.CreateDirectory(Path.Join(ProjectDir, "scripts"));

            var error = Fail(Recipe("scripts"));

            Assert.Equal(2, error.ExitCode);
            Assert.Equal(ToolFunctionSourceMaterializer.ScriptReadFailedCode, error.Code);
        }

        [Fact]
        public void S3_03_C_UnreadableFile_ReturnsExit2Diagnostic()
        {
            var path = WriteScript("locked.sh", ScriptB);
            if (OperatingSystem.IsWindows())
            {
                Assert.Skip("Unix 파일 권한으로 읽기 거부를 만드는 시험이다");
            }
            else
            {
                File.SetUnixFileMode(path, UnixFileMode.None);
            }

            Assert.SkipWhen(CanRead(path), "mode 000 파일을 읽을 수 있는 환경(root 등)이라 읽기 거부를 재현할 수 없음");

            var error = Fail(Recipe("locked.sh"));

            Assert.Equal(2, error.ExitCode);
            Assert.Equal(ToolFunctionSourceMaterializer.ScriptReadFailedCode, error.Code);
        }

        [Fact]
        public void S3_03_C_InvalidUtf8_ReturnsExit2Diagnostic()
        {
            Directory.CreateDirectory(ProjectDir);
            File.WriteAllBytes(Path.Join(ProjectDir, "bin.sh"), new byte[] { 0x65, 0x63, 0xC3, 0x28, 0x0A });

            var error = Fail(Recipe("bin.sh"));

            Assert.Equal(2, error.ExitCode);
            Assert.Equal(ToolFunctionSourceMaterializer.ScriptNotUtf8Code, error.Code);
        }

        // 이동 profile: 원본 A의 절대 host 경로는 받지 않고 rewrite/fallback도 하지 않는다.
        [Fact]
        public void AbsoluteScriptPath_ReturnsExit2_EvenWhenTheFileExists()
        {
            var absolute = WriteScript("sort.sh", ScriptB);

            var error = Fail(Recipe(absolute));

            Assert.Equal(2, error.ExitCode);
            Assert.Equal(ToolFunctionSourceMaterializer.AbsoluteScriptPathCode, error.Code);
        }

        // S3-03-D: 빈 파일/공백/개행뿐 → local validation 실패 exit 1.
        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\n")]
        [InlineData(" \t\r\n\n")]
        public void S3_03_D_BlankScript_ReturnsExit1(string content)
        {
            WriteScript("blank.sh", content);

            var error = Fail(Recipe("blank.sh"));

            Assert.Equal(1, error.ExitCode);
            Assert.Equal(ToolFunctionSourceMaterializer.BlankScriptCode, error.Code);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void BlankScriptPath_ReturnsExit1(string scriptPath)
        {
            Directory.CreateDirectory(ProjectDir);

            var error = Fail(Recipe(scriptPath));

            Assert.Equal(1, error.ExitCode);
            Assert.Equal(ToolFunctionSourceMaterializer.MissingScriptPathCode, error.Code);
        }

        // S3-03-E (fresh 쪽): fresh 요청은 그 시점의 현재 source를 읽어 고정한다. 이후 파일
        // 수정/삭제는 이미 만든 snapshot을 바꾸지 않고, 다음 fresh 요청은 새 내용을 읽는다.
        // 저장된 envelope replay의 재읽기0 증명은 P04.integrate 소관이다.
        [Fact]
        public void S3_03_E_FreshSnapshotIsFixed_AndNextFreshReadsCurrentSource()
        {
            var path = WriteScript("sort.sh", "echo v1\n");
            Assert.True(ToolFunctionSourceMaterializer.TryMaterialize(Recipe("sort.sh"), RecipePath, out var first, out _));

            File.WriteAllText(path, "echo v2\n");
            Assert.True(ToolFunctionSourceMaterializer.TryMaterialize(Recipe("sort.sh"), RecipePath, out var second, out _));
            File.Delete(path);

            Assert.Equal("echo v1\n", first!.Script);
            Assert.Equal("echo v2\n", second!.Script);
            Assert.NotEqual(first.ScriptSha256, second.ScriptSha256);
            Assert.Equal(2, Fail(Recipe("sort.sh")).ExitCode);
            Assert.Equal("echo v1\n", first.Script);
        }

        // 저장했다 다시 읽은 Recipe 파일 경로로 materialize해도 Recipe 위치 기준이다.
        [Fact]
        public void PersistedRecipe_LoadedFromFile_MaterializesAgainstItsDirectory()
        {
            WriteScript("sort.sh", ScriptB);
            File.WriteAllText(RecipePath, JsonSerializer.Serialize(new { SchemaVersion = "draft-1", BaseToolImageDigest = DigestA, ScriptPath = "./sort.sh" }));
            using var stderr = new StringWriter();
            Assert.True(ToolFunctionRecipeCliIo.TryLoad(RecipePath, stderr, out var loaded));

            Assert.True(ToolFunctionSourceMaterializer.TryMaterialize(loaded!, RecipePath, out var snapshot, out _));

            Assert.Equal(ScriptB, snapshot!.Script);
        }

        // S3-02-B: 잘못된 exact base digest → local rejection exit 1.
        [Theory]
        [InlineData("sha256:bad")]
        [InlineData("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        [InlineData("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        [InlineData("sha256:gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
        [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        [InlineData("SHA256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        [InlineData(" sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        [InlineData("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n")]
        [InlineData("repo/tool@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        public void S3_02_B_InvalidBaseImageDigest_ReturnsExit1_BeforeReadingScript(string digest)
        {
            Directory.CreateDirectory(ProjectDir);

            var error = Fail(Recipe("./missing.sh", digest));

            Assert.Equal(1, error.ExitCode);
            Assert.Equal(ToolFunctionSourceMaterializer.InvalidBaseImageCode, error.Code);
        }

        // S3-02-C: 대문자 hex도 schema 허용 형태로 받고, client는 값을 바꾸지 않는다.
        [Fact]
        public void S3_02_C_UpperCaseHexDigest_IsAcceptedVerbatim()
        {
            const string upper = "sha256:ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";
            WriteScript("sort.sh", ScriptB);

            Assert.True(ToolFunctionSourceMaterializer.TryMaterialize(Recipe("sort.sh", upper), RecipePath, out var snapshot, out _));

            Assert.Equal(upper, snapshot!.BaseImageDigest);
        }

        // S3-02-E (base image 쪽): C1(toolSpecDigest)만 있고 A가 없으면 missing exact base
        // image로 거부하며 C1을 A로 쓰지 않는다.
        [Theory]
        [InlineData("")]
        [InlineData("  ")]
        public void S3_02_E_MissingBaseImage_ReturnsExit1_WithoutFallbackToToolSpecDigest(string digest)
        {
            WriteScript("sort.sh", ScriptB);
            var recipe = Recipe("sort.sh", digest);
            recipe.ToolSpecDigest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

            var error = Fail(recipe);

            Assert.Equal(1, error.ExitCode);
            Assert.Equal(ToolFunctionSourceMaterializer.MissingBaseImageCode, error.Code);
        }

        private static bool CanRead(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    [CollectionDefinition(nameof(CwdIndependenceP06Collection), DisableParallelization = true)]
    public sealed class CwdIndependenceP06Collection
    {
    }

    /// <summary>
    /// S3-03-A: 실제 프로세스 CWD를 Recipe와 다른 디렉터리(같은 이름의 다른 script가
    /// 있는 곳)로 바꿔도 Recipe 위치의 script를 읽는다. CWD는 프로세스 전역이므로
    /// 병렬 실행이 꺼진 collection에서만 바꾸고 원래대로 돌려놓는다.
    /// </summary>
    [Collection(nameof(CwdIndependenceP06Collection))]
    public sealed class CwdIndependenceP06Tests : IDisposable
    {
        private readonly string _workDir =
            Path.Join(Path.GetTempPath(), "nodekit-p06-cwd-" + Guid.NewGuid());

        public CwdIndependenceP06Tests() => Directory.CreateDirectory(_workDir);

        public void Dispose() => Directory.Delete(_workDir, recursive: true);

        [Fact]
        public void S3_03_A_DifferentCwd_ReadsScriptNextToRecipe()
        {
            var project = Directory.CreateDirectory(Path.Join(_workDir, "project")).FullName;
            var elsewhere = Directory.CreateDirectory(Path.Join(_workDir, "else")).FullName;
            File.WriteAllText(Path.Join(project, "sort.sh"), "#!/bin/sh\necho project\n");
            File.WriteAllText(Path.Join(elsewhere, "sort.sh"), "#!/bin/sh\necho cwd\n");
            var recipe = new ToolFunctionRecipe
            {
                BaseToolImageDigest = "sha256:" + new string('b', 64),
                ScriptPath = "./sort.sh",
            };

            var original = Directory.GetCurrentDirectory();
            ToolFunctionBaseScriptSnapshot? fromAbsoluteRecipePath;
            ToolFunctionBaseScriptSnapshot? fromRelativeRecipePath;
            try
            {
                Directory.SetCurrentDirectory(elsewhere);
                Assert.True(ToolFunctionSourceMaterializer.TryMaterialize(recipe, Path.Join(project, "recipe.json"), out fromAbsoluteRecipePath, out _));
                Assert.True(ToolFunctionSourceMaterializer.TryMaterialize(recipe, Path.Join("..", "project", "recipe.json"), out fromRelativeRecipePath, out _));
            }
            finally
            {
                Directory.SetCurrentDirectory(original);
            }

            Assert.Equal("#!/bin/sh\necho project\n", fromAbsoluteRecipePath!.Script);
            Assert.Equal("#!/bin/sh\necho project\n", fromRelativeRecipePath!.Script);
        }
    }
}
