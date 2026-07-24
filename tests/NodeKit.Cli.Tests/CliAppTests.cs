using System;
using System.IO;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    public class CliAppTests : IDisposable
    {
        private readonly string _workDir = Path.Join(Path.GetTempPath(), "nodekit-cli-tests-" + Guid.NewGuid());
        private readonly IDisposable _resolveClientOverride =
            ResolveRecipeClientTestOverride.Use(NullResolveRecipeClient.Instance);

        public CliAppTests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
            _resolveClientOverride.Dispose();
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

        // Review finding: a hand-written recipe.json omitting "BuildKind"
        // deserializes to BuildKind == null, and RecipeValidationPipeline
        // .ValidateRecipe() throws InvalidOperationException in that case (an
        // internal contract for interactive authoring, which always resolves
        // BuildKind first). validate/render never caught it, so the CLI
        // crashed with a raw stack trace instead of a clean error + exit code.
        private const string MissingBuildKindRecipeJson = """
        {
            "ToolName": "bwa",
            "Version": "0.7.17",
            "BaseImage": "registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "DockerfileContent": "FROM registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\nRUN echo ok\nUSER 1000\n",
            "Script": "bwa mem",
            "Inputs": [ { "Name": "reads", "Role": "sample-fastq", "Format": "fastq", "Shape": "pair" } ],
            "Outputs": [ { "Name": "aligned", "Role": "aligned-bam", "Format": "bam", "Shape": "single", "Class": "primary" } ]
        }
        """;

        [Fact]
        public void Validate_RecipeMissingBuildKind_ReturnsTwoInsteadOfThrowing()
        {
            var recipePath = WriteFile("recipe.json", MissingBuildKindRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "validate", recipePath }, stdout, stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("buildKind", stderr.ToString());
        }

        [Fact]
        public void Render_RecipeMissingBuildKind_ReturnsTwoInsteadOfThrowing()
        {
            var recipePath = WriteFile("recipe.json", MissingBuildKindRecipeJson);
            var outPath = Path.Join(_workDir, "build-request.json");
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "render", recipePath, "--out", outPath }, stdout, stderr);

            Assert.Equal(2, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("buildKind", stderr.ToString());
        }

        // Review finding: System.Text.Json does not enforce C#'s non-nullable
        // property declarations at runtime, so external JSON with an explicit
        // "null" for a List<T>/string property crashes RecipeRenderer.Render
        // (ArgumentNullException/NullReferenceException) instead of surfacing
        // as a clean L1 violation — RecipeDocument.Normalize() fixes this.

        private const string NullCommandRecipeJson = """
        {
            "BuildKind": "DockerfileFallback",
            "ToolName": "bwa",
            "Version": "0.7.17",
            "BaseImage": "registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "DockerfileContent": "FROM registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\nRUN echo ok\nUSER 1000\n",
            "Script": "bwa mem",
            "Command": null,
            "Inputs": [ { "Name": "reads", "Role": "sample-fastq", "Format": "fastq", "Shape": "pair" } ],
            "Outputs": [ { "Name": "aligned", "Role": "aligned-bam", "Format": "bam", "Shape": "single", "Class": "primary" } ]
        }
        """;

        [Fact]
        public void Validate_RecipeWithExplicitNullCommand_DoesNotCrash()
        {
            var recipePath = WriteFile("recipe.json", NullCommandRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "validate", recipePath }, stdout, stderr);

            Assert.Equal(0, exitCode);
        }

        private const string NullSourceChecksumRecipeJson = """
        {
            "BuildKind": "SourceBuild",
            "ToolName": "bwa",
            "Version": "0.7.17",
            "BaseImage": "registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "SourceUri": "https://example.com/bwa-0.7.17.tar.gz",
            "SourceChecksum": null,
            "SourceBuildCommands": [ "make", "make install" ],
            "Script": "bwa mem"
        }
        """;

        [Fact]
        public void Validate_RecipeWithExplicitNullSourceChecksum_ReturnsViolationInsteadOfCrashing()
        {
            var recipePath = WriteFile("recipe.json", NullSourceChecksumRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "validate", recipePath }, stdout, stderr);

            Assert.Equal(1, exitCode);
            Assert.Contains("L1-SRC-001", stderr.ToString());
        }

        [Fact]
        public void Validate_ValidRecipe_ReturnsZero()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "validate", recipePath }, stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("OK", stdout.ToString());
            Assert.Empty(stderr.ToString());
        }

        [Fact]
        public void Validate_InvalidRecipe_ReturnsOneAndPrintsViolations()
        {
            var recipePath = WriteFile("recipe.json", InvalidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "validate", recipePath }, stdout, stderr);

            Assert.Equal(1, exitCode);
            Assert.Contains("L1-SRC-001", stderr.ToString());
            Assert.Contains("(SourceChecksum)", stderr.ToString());
        }

        [Fact]
        public void Render_ValidRecipe_WritesLegacyBuildRequestJsonAndReturnsZero()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            var outPath = Path.Join(_workDir, "build-request.json");
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "render", recipePath, "--out", outPath }, stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            var json = File.ReadAllText(outPath);
            Assert.Contains("\"ToolName\"", json);
            Assert.Contains("\"ImageUri\"", json);
            Assert.Contains("\"DockerfileContent\"", json);
            Assert.Contains("bwa", json);
        }

        // 리뷰 지적: File.WriteAllText가 try/catch 밖에 있어서 --out에 쓸 수
        // 없는 경로(디렉터리 자체, 존재하지 않는 상위 디렉터리, 권한 없음)를
        // 주면 스택트레이스와 함께 크래시했다 — TryLoadRecipe의 읽기 쪽엔
        // 이미 있던 IOException 보호가 쓰기 쪽엔 없었다.
        [Fact]
        public void Render_OutPathIsDirectory_ReturnsTwoInsteadOfCrashing()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "render", recipePath, "--out", _workDir }, stdout, stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("출력 파일을 쓸 수 없습니다", stderr.ToString());
        }

        [Fact]
        public void Render_OutPathHasNonexistentParentDirectory_ReturnsTwoInsteadOfCrashing()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            var outPath = Path.Join(_workDir, "no-such-subdir", "out.json");
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "render", recipePath, "--out", outPath }, stdout, stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("출력 파일을 쓸 수 없습니다", stderr.ToString());
        }

        [Fact]
        public void Render_RawSpecFormat_WritesActualSubmitWirePayloadAndReturnsZero()
        {
            // NodeKit#72: render's default output (--format build-request, the
            // legacy BuildRequest shape) can be mistaken for what submit actually
            // sends. --format raw-spec calls the same ToolSpecRawSpecFactory
            // submit uses, so this preview is byte-for-byte what a real submit
            // would put on the wire (no network call).
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            var outPath = Path.Join(_workDir, "raw-spec.json");
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(
                new[] { "render", recipePath, "--out", outPath, "--format", "raw-spec" }, stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            var json = File.ReadAllText(outPath);
            Assert.Contains("\"tool_name\":\"bwa\"", json);
            Assert.Contains("\"dockerfile_content\"", json);
            Assert.Contains("\"kind\":1", json);
            Assert.DoesNotContain("\"ToolName\"", json);
        }

        [Fact]
        public void Render_UnknownFormatOption_ReturnsTwoWithExplicitError()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            var outPath = Path.Join(_workDir, "out.json");
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(
                new[] { "render", recipePath, "--out", outPath, "--format", "bogus" }, stdout, stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("--format", stderr.ToString());
            Assert.False(File.Exists(outPath));
        }

        [Theory]
        [InlineData("RAW-SPEC")]
        [InlineData("raw_spec")]
        [InlineData("Raw-Spec")]
        [InlineData(" raw-spec ")]
        public void Render_FormatOptionCaseOrUnderscoreVariant_IsAccepted(string formatValue)
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            var outPath = Path.Join(_workDir, "raw-spec.json");
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(
                new[] { "render", recipePath, "--out", outPath, "--format", formatValue }, stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("\"tool_name\":\"bwa\"", File.ReadAllText(outPath));
        }

        [Fact]
        public void Render_RawSpecFormatWithPretty_WritesIndentedJson()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            var outPath = Path.Join(_workDir, "raw-spec.json");
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(
                new[] { "render", recipePath, "--out", outPath, "--format", "raw-spec", "--pretty" }, stdout, stderr);

            Assert.Equal(0, exitCode);
            var json = File.ReadAllText(outPath);
            Assert.Contains("\n", json);
            Assert.Contains("\"tool_name\": \"bwa\"", json);
        }

        [Fact]
        public void Render_BuildRequestFormatWithPretty_StaysIndentedAndUnchanged()
        {
            // --pretty only affects --format raw-spec (build-request is already
            // indented by default) — this pins that combining the two doesn't
            // change build-request's existing output shape.
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            var outPath = Path.Join(_workDir, "build-request.json");
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(
                new[] { "render", recipePath, "--out", outPath, "--pretty" }, stdout, stderr);

            Assert.Equal(0, exitCode);
            var json = File.ReadAllText(outPath);
            Assert.Contains("\"ToolName\"", json);
            Assert.Contains("\n", json);
        }

        [Fact]
        public void Render_InvalidRecipe_ReturnsOneAndDoesNotWriteOutputFile()
        {
            var recipePath = WriteFile("recipe.json", InvalidRecipeJson);
            var outPath = Path.Join(_workDir, "build-request.json");
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "render", recipePath, "--out", outPath }, stdout, stderr);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("L1-SRC-001", stderr.ToString());
            Assert.Contains("(SourceChecksum)", stderr.ToString());
        }

        [Fact]
        public void Run_UnknownCommand_ReturnsTwo()
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "submit", "recipe.json" }, stdout, stderr);

            Assert.Equal(2, exitCode);
        }

        // P2 리뷰: --help/-h가 사실상 없었다(최상위는 "알 수 없는 명령"으로,
        // render/submit --help는 옵션 누락 에러로 처리됨) — 상업용 CLI 기준
        // 사용자가 문서 없이 옵션을 발견할 방법이 없었다.
        [Theory]
        [InlineData("--help")]
        [InlineData("-h")]
        public void Run_TopLevelHelpFlag_ReturnsZeroWithUsage(string helpFlag)
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { helpFlag }, stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("nodekit validate", stdout.ToString());
            Assert.Contains("nodekit render", stdout.ToString());
            Assert.Contains("nodekit submit", stdout.ToString());
            Assert.Contains("nodekit recipe create", stdout.ToString());
            Assert.Empty(stderr.ToString());
        }

        [Fact]
        public void Validate_HelpFlag_ReturnsZeroWithUsageInsteadOfRequiringRecipePath()
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "validate", "--help" }, stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("사용법: nodekit validate", stdout.ToString());
        }

        [Fact]
        public void Render_HelpFlag_ReturnsZeroWithUsageInsteadOfMissingOutError()
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "render", "--help" }, stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("사용법: nodekit render", stdout.ToString());
            Assert.Contains("--format", stdout.ToString());
            Assert.Contains("--pretty", stdout.ToString());
        }

        [Fact]
        public void RecipeCreate_HelpFlag_ReturnsZeroWithUsage()
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "recipe", "create", "--help" }, stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("사용법: nodekit recipe create", stdout.ToString());
        }

        // §13 R19: --strict-reproducible blocks version-only conda pins that
        // NodeKit's L1 otherwise allows during authoring but NodeVault's final
        // gate rejects (confirmed live, n03).

        private const string VersionOnlyPinRecipeJson = """
        {
            "BuildKind": "Conda",
            "ToolName": "bwa",
            "Version": "0.7.17",
            "BaseImage": "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "Packages": [ "bwa=0.7.17" ],
            "Channels": [ "bioconda" ],
            "Script": "bwa mem"
        }
        """;

        [Fact]
        public void Validate_VersionOnlyPin_WithoutStrictFlag_ReturnsZero()
        {
            var recipePath = WriteFile("recipe.json", VersionOnlyPinRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "validate", recipePath }, stdout, stderr);

            Assert.Equal(0, exitCode);
        }

        [Fact]
        public void Validate_VersionOnlyPin_WithStrictFlag_ReturnsOne()
        {
            var recipePath = WriteFile("recipe.json", VersionOnlyPinRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "validate", recipePath, "--strict-reproducible" }, stdout, stderr);

            Assert.Equal(1, exitCode);
            Assert.Contains("L1-RCP-016", stderr.ToString());
        }

        // Design follow-up (NodeKit#66): validate/render used to silently ignore
        // unrecognized options and only checked for --strict-reproducible via a bare
        // Array.IndexOf, unlike submit's explicit CliOptionParser-based validation.
        // Both commands now share the same parser (CliOptionParser) as submit.

        [Fact]
        public void Validate_UnknownOption_ReturnsTwoWithExplicitError()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "validate", recipePath, "--typo-flag" }, stdout, stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("알 수 없는 옵션입니다: --typo-flag", stderr.ToString());
        }

        [Fact]
        public void Render_UnknownOption_ReturnsTwoWithExplicitError()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            var outPath = Path.Join(_workDir, "build-request.json");
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "render", recipePath, "--out", outPath, "--typo-flag" }, stdout, stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("알 수 없는 옵션입니다: --typo-flag", stderr.ToString());
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void Render_OutOptionMissingValue_ReturnsTwoWithExplicitError()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "render", recipePath, "--out" }, stdout, stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("--out 옵션에는 값이 필요합니다", stderr.ToString());
        }

        [Fact]
        public void Render_OutOptionValueLooksLikeAnotherOption_ReturnsTwoWithExplicitError()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(new[] { "render", recipePath, "--out", "--strict-reproducible" }, stdout, stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("--out 옵션에는 값이 필요합니다", stderr.ToString());
        }

        [Fact]
        public void Render_OutOptionDuplicated_ReturnsTwoWithExplicitError()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = CliApp.Run(
                new[] { "render", recipePath, "--out", "a.json", "--out", "b.json" }, stdout, stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("--out 옵션이 여러 번 지정되었습니다", stderr.ToString());
        }

        private string WriteFile(string name, string content)
        {
            var path = Path.Join(_workDir, name);
            File.WriteAllText(path, content);
            return path;
        }
    }
}
