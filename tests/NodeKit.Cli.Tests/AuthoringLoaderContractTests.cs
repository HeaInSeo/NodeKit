using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Cli;
using NodeKit.Grpc;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// P02.loader: Fixtures/Contract/cli-acceptance-contract.json의 authoringSchema
    /// 변형과 inputErrorContract를 실제 CLI caller 6개(validate/render/submit,
    /// function-recipe validate/render/submit)에 적용한다. S1-02-C01..C07과
    /// S4-03-B/C의 loader 부분을 함께 고정한다.
    /// </summary>
    public class AuthoringLoaderContractTests : IDisposable
    {
        private const string Marker = "existing-output-marker";

        // CliAppTests.ValidRecipeJson과 같은 유효 DockerfileFallback recipe의 속성들(중괄호 제외).
        private const string RecipeBody = """
            "BuildKind": "DockerfileFallback",
            "ToolName": "bwa",
            "Version": "0.7.17",
            "BaseImage": "registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "DockerfileContent": "FROM registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\nRUN echo ok\nUSER 1000\n",
            "Script": "bwa mem",
            "Inputs": [ { "Name": "reads", "Role": "sample-fastq", "Format": "fastq", "Shape": "pair" } ],
            "Outputs": [ { "Name": "aligned", "Role": "aligned-bam", "Format": "bam", "Shape": "single", "Class": "primary" } ]
            """;

        private const string FunctionRecipeBody = """
            "State": "Ready",
            "FunctionId": "samtools.sort"
            """;

        private readonly string _workDir = Path.Join(Path.GetTempPath(), "nodekit-loader-tests-" + Guid.NewGuid());
        private readonly IDisposable _resolveClientOverride =
            ResolveRecipeClientTestOverride.Use(NullResolveRecipeClient.Instance);

        public AuthoringLoaderContractTests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(_workDir, "*", SearchOption.AllDirectories))
            {
                if (!OperatingSystem.IsWindows() && File.Exists(path))
                {
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }

            Directory.Delete(_workDir, recursive: true);
            _resolveClientOverride.Dispose();
        }

        public static IEnumerable<object?[]> RejectedSchemaVariants() =>
            SchemaVariants().Where(v => v.Outcome == "REJECTED").Select(v => new object?[] { v.Id, v.Json });

        public static IEnumerable<object?[]> AcceptedSchemaVariants() =>
            SchemaVariants().Where(v => v.Outcome == "ACCEPTED_TO_L1").Select(v => new object?[] { v.Id, v.Json });

        [Fact]
        public void ContractVariants_AreTheFrozenEightWithExpectedOutcomes()
        {
            var variants = SchemaVariants().ToList();

            Assert.Equal(8, variants.Count);
            Assert.Equal(2, variants.Count(v => v.Outcome == "ACCEPTED_TO_L1"));
            Assert.All(
                variants.Where(v => v.Outcome == "REJECTED"),
                v => Assert.Equal((2, 0, true), (v.Exit, v.BusinessRpc, v.ExistingFilePreserved)));
        }

        [Theory]
        [MemberData(nameof(RejectedSchemaVariants))]
        public void RejectedSchema_RecipeValidate_ReturnsTwo(string id, string? schemaJson)
        {
            var recipePath = WriteFile($"{id}.json", Recipe(schemaJson));

            var (exit, _, stderr) = Run("validate", recipePath);

            Assert.Equal(2, exit);
            Assert.Contains("SchemaVersion", stderr);
            AssertNoStackTrace(stderr);
        }

        [Theory]
        [MemberData(nameof(RejectedSchemaVariants))]
        public void RejectedSchema_RecipeRender_ReturnsTwoAndKeepsExistingOutput(string id, string? schemaJson)
        {
            var recipePath = WriteFile($"{id}.json", Recipe(schemaJson));
            var outPath = WriteFile("out.json", Marker);

            var (exit, _, stderr) = Run("render", recipePath, "--out", outPath);

            Assert.Equal(2, exit);
            Assert.Equal(Marker, File.ReadAllText(outPath));
            Assert.Contains("SchemaVersion", stderr);
        }

        [Theory]
        [MemberData(nameof(RejectedSchemaVariants))]
        public void RejectedSchema_Submit_ReturnsTwoWithoutAnyRpc(string id, string? schemaJson)
        {
            var recipePath = WriteFile($"{id}.json", Recipe(schemaJson));
            var client = new CountingToolSpecClient();
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exit = SubmitCommand.Run(new[] { "submit", recipePath, "--format", "jsonl" }, stdout, stderr, client);

            Assert.Equal(2, exit);
            Assert.Equal(0, client.Calls);
            using var record = JsonDocument.Parse(stdout.ToString().Trim());
            Assert.Equal("UNSUPPORTED_SCHEMA_VERSION", record.RootElement.GetProperty("error_code").GetString());
        }

        [Theory]
        [MemberData(nameof(RejectedSchemaVariants))]
        public void RejectedSchema_FunctionRecipeValidate_ReturnsTwoAndKeepsInputBytes(string id, string? schemaJson)
        {
            var content = FunctionRecipe(schemaJson);
            var recipePath = WriteFile($"{id}.json", content);

            var (exit, stdout, stderr) = Run("function-recipe", "validate", recipePath);

            Assert.Equal(2, exit);
            Assert.Equal(content, File.ReadAllText(recipePath));
            Assert.DoesNotContain("검증 통과", stdout);
            Assert.Contains("SchemaVersion", stderr);
        }

        [Theory]
        [MemberData(nameof(RejectedSchemaVariants))]
        public void RejectedSchema_FunctionRecipeRender_ReturnsTwoAndKeepsExistingOutput(string id, string? schemaJson)
        {
            var recipePath = WriteFile($"{id}.json", FunctionRecipe(schemaJson));
            var outPath = WriteFile("out.json", Marker);

            var (exit, _, stderr) = Run("function-recipe", "render", recipePath, "--out", outPath);

            Assert.Equal(2, exit);
            Assert.Equal(Marker, File.ReadAllText(outPath));
            Assert.Contains("SchemaVersion", stderr);
        }

        [Theory]
        [MemberData(nameof(RejectedSchemaVariants))]
        public void RejectedSchema_FunctionRecipeSubmit_ReturnsTwoBeforeGateMessage(string id, string? schemaJson)
        {
            var recipePath = WriteFile($"{id}.json", FunctionRecipe(schemaJson));

            var (exit, stdout, stderr) = Run("function-recipe", "submit", recipePath);

            Assert.Equal(2, exit);
            Assert.Equal(string.Empty, stdout);
            Assert.Contains("SchemaVersion", stderr);
        }

        [Theory]
        [MemberData(nameof(AcceptedSchemaVariants))]
        public void AcceptedSchema_RecipeValidate_ReachesFullL1(string id, string schemaJson)
        {
            var recipePath = WriteFile($"{id}.json", Recipe(schemaJson));

            var (exit, stdout, stderr) = Run("validate", recipePath);

            Assert.Equal(0, exit);
            Assert.Equal("OK", stdout.Trim());
            Assert.Equal(string.Empty, stderr);
        }

        [Theory]
        [MemberData(nameof(AcceptedSchemaVariants))]
        public void AcceptedSchema_FunctionRecipeValidate_ReachesL1(string id, string schemaJson)
        {
            var recipePath = WriteFile($"{id}.json", FunctionRecipe(schemaJson));

            var (exit, _, stderr) = Run("function-recipe", "validate", recipePath);

            // 최소 recipe라 L1에서 막힌다 — 핵심은 schema gate(exit 2)를 지나 L1(exit 1)에 도달한 것.
            Assert.Equal(1, exit);
            Assert.Contains("L1-TFR-", stderr);
            Assert.DoesNotContain("SchemaVersion", stderr);
        }

        [Fact]
        public void DuplicateSchemaVersionKeysDifferingOnlyInCase_ReturnsTwo()
        {
            var recipePath = WriteFile("dup.json", Recipe("\"SchemaVersion\": \"draft-1\", \"schemaversion\": \"draft-2\""));

            var (exit, _, stderr) = Run("validate", recipePath);

            Assert.Equal(2, exit);
            Assert.Contains("여러 번", stderr);
        }

        // ── S1-02 ───────────────────────────────────────────────────────

        [Fact]
        public void S1_02_C01_ValidPackageRecipe_ValidatesThroughFullPipeline()
        {
            var recipePath = WriteFile("package.json", """
                {
                    "SchemaVersion": "draft-1",
                    "BuildKind": "Conda",
                    "ToolName": "bwa",
                    "Version": "0.7.17",
                    "BaseImage": "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                    "Channels": [ "bioconda" ],
                    "Packages": [ "bwa=0.7.17" ],
                    "Script": "bwa mem",
                    "Inputs": [ { "Name": "reads", "Role": "sample-fastq", "Format": "fastq", "Shape": "pair" } ],
                    "Outputs": [ { "Name": "aligned", "Role": "aligned-bam", "Format": "bam", "Shape": "single", "Class": "primary" } ]
                }
                """);

            var (exit, stdout, stderr) = Run("validate", recipePath);

            Assert.Equal(0, exit);
            Assert.Equal("OK", stdout.Trim());
            Assert.Equal(string.Empty, stderr);
        }

        [Fact]
        public void S1_02_C02_BrokenJson_ReturnsTwoAndWritesNoRenderOutput()
        {
            var recipePath = WriteFile("broken.json", "{broken json");
            var outPath = Path.Join(_workDir, "out.json");

            var (exit, _, stderr) = Run("render", recipePath, "--out", outPath);

            Assert.Equal(2, exit);
            Assert.False(File.Exists(outPath));
            Assert.Contains("JSON 파싱", stderr);
            AssertNoStackTrace(stderr);
        }

        [Fact]
        public void S1_02_C03_MissingBuildKind_ReturnsTwoAndHintIncludesSourceBuildStructured()
        {
            var recipePath = WriteFile("no-kind.json", Recipe("\"SchemaVersion\": \"draft-1\"").Replace("\"BuildKind\": \"DockerfileFallback\",", string.Empty, StringComparison.Ordinal));

            var (exit, _, stderr) = Run("validate", recipePath);

            Assert.Equal(2, exit);
            Assert.Contains("buildKind", stderr);
            Assert.Contains("SourceBuildStructured", stderr);
        }

        [Fact]
        public void S1_02_C04_UndefinedNumericBuildKind_ReturnsTwoWithoutRendererThrowOrRpc()
        {
            var content = Recipe("\"SchemaVersion\": \"draft-1\"").Replace("\"BuildKind\": \"DockerfileFallback\"", "\"BuildKind\": 999", StringComparison.Ordinal);
            var recipePath = WriteFile("kind-999.json", content);
            var outPath = Path.Join(_workDir, "out.json");

            var (renderExit, _, renderStderr) = Run("render", recipePath, "--out", outPath);

            Assert.Equal(2, renderExit);
            Assert.False(File.Exists(outPath));
            Assert.Contains("지원하지 않는 buildKind", renderStderr);
            Assert.Contains("SourceBuildStructured", renderStderr);
            AssertNoStackTrace(renderStderr);

            var client = new CountingToolSpecClient();
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var submitExit = SubmitCommand.Run(new[] { "submit", recipePath, "--format", "jsonl" }, stdout, stderr, client);

            Assert.Equal(2, submitExit);
            Assert.Equal(0, client.Calls);
            Assert.Contains("UNSUPPORTED_BUILD_KIND", stdout.ToString());
        }

        [Fact]
        public void S1_02_C05_SourceBuildCommandsNullElement_ReturnsL1ViolationNotCrash()
        {
            var recipePath = WriteFile("source-null.json", """
                {
                    "SchemaVersion": "draft-1",
                    "BuildKind": "SourceBuild",
                    "ToolName": "bwa",
                    "Version": "0.7.17",
                    "BaseImage": "registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                    "SourceUri": "https://example.com/bwa-0.7.17.tar.gz",
                    "SourceChecksum": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                    "SourceBuildCommands": [ null ],
                    "Script": "bwa mem"
                }
                """);

            var (exit, _, stderr) = Run("validate", recipePath);

            Assert.Equal(1, exit);
            Assert.Contains("L1-RCP-019 (SourceBuildCommands[0])", stderr);
            AssertNoStackTrace(stderr);
        }

        [Fact]
        public void S1_02_C06_MissingRecipePath_ValidateAndRenderReturnTwoWithoutOutput()
        {
            var missing = Path.Join(_workDir, "does-not-exist.json");
            var outPath = Path.Join(_workDir, "out.json");

            var (validateExit, _, validateStderr) = Run("validate", missing);
            var (renderExit, _, renderStderr) = Run("render", missing, "--out", outPath);

            Assert.Equal(2, validateExit);
            Assert.Equal(2, renderExit);
            Assert.Contains("읽을 수 없습니다", validateStderr);
            Assert.Contains("읽을 수 없습니다", renderStderr);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void S1_02_C07_RecipePathIsDirectory_ReturnsTwoWithPermissionDiagnostic()
        {
            // 지원 profile(Linux)에서 디렉터리 읽기는 UnauthorizedAccessException이다 —
            // root로 실행해도 결정적인 IO 조건이다.
            var dirPath = Path.Join(_workDir, "recipe-dir.json");
            Directory.CreateDirectory(dirPath);
            var outPath = Path.Join(_workDir, "out.json");

            var (exit, _, stderr) = Run("render", dirPath, "--out", outPath);

            Assert.Equal(2, exit);
            Assert.Contains("권한", stderr);
            Assert.False(File.Exists(outPath));
            AssertNoStackTrace(stderr);
        }

        [Fact]
        public void S1_02_C07_UnreadableRecipeFile_ReturnsTwoWithPermissionDiagnostic()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            var recipePath = WriteFile("unreadable.json", Recipe("\"SchemaVersion\": \"draft-1\""));
            File.SetUnixFileMode(recipePath, UnixFileMode.None);
            if (CanRead(recipePath))
            {
                // root 등 권한 검사를 우회하는 계정 — 디렉터리 변형이 같은 경로를 덮는다.
                return;
            }

            var (exit, _, stderr) = Run("validate", recipePath);

            Assert.Equal(2, exit);
            Assert.Contains("권한", stderr);
            AssertNoStackTrace(stderr);
        }

        // ── S4-03-B/C loader 부분 (ToolFunctionRecipe) ──────────────────

        [Theory]
        [InlineData("{ \"SchemaVersion\": \"draft-1\", ")]
        [InlineData("null")]
        [InlineData("[ { \"SchemaVersion\": \"draft-1\" } ]")]
        public void S4_03_B_FunctionRecipeMalformedOrLiteralNull_ReturnsTwo(string content)
        {
            var recipePath = WriteFile("tfr.json", content);

            var (exit, stdout, stderr) = Run("function-recipe", "validate", recipePath);

            Assert.Equal(2, exit);
            Assert.Equal(string.Empty, stdout);
            Assert.Equal(content, File.ReadAllText(recipePath));
            AssertNoStackTrace(stderr);
        }

        [Fact]
        public void S4_03_B_FunctionRecipeReadDenied_ReturnsTwo()
        {
            var dirPath = Path.Join(_workDir, "tfr-dir.json");
            Directory.CreateDirectory(dirPath);

            var (exit, _, stderr) = Run("function-recipe", "validate", dirPath);

            Assert.Equal(2, exit);
            Assert.Contains("권한", stderr);
        }

        [Theory]
        [InlineData("InputPorts", "InputPorts[0]")]
        [InlineData("OutputPorts", "OutputPorts[0]")]
        [InlineData("Parameters", "Parameters[0]")]
        [InlineData("FixtureReferences", "FixtureReferences[0]")]
        public void S4_03_C_FunctionRecipeArrayNullElement_ValidateReturnsOneWithFieldViolation(string property, string field)
        {
            var content = "{ \"SchemaVersion\": \"draft-1\", \"State\": \"Draft\", \"" + property + "\": [ null ] }";
            var recipePath = WriteFile("tfr-null.json", content);

            var (exit, stdout, stderr) = Run("function-recipe", "validate", recipePath);

            Assert.Equal(1, exit);
            Assert.Contains($"L1-TFR-013 ({field})", stderr);
            Assert.Equal(string.Empty, stdout);
            Assert.Equal(content, File.ReadAllText(recipePath));
            AssertNoStackTrace(stderr);
        }

        [Fact]
        public void S4_03_C_NestedNullElement_ReportsDottedPath()
        {
            var content = "{ \"SchemaVersion\": \"draft-1\", \"Command\": { \"Environment\": [ null ] } }";
            var recipePath = WriteFile("tfr-nested.json", content);

            var (exit, _, stderr) = Run("function-recipe", "validate", recipePath);

            Assert.Equal(1, exit);
            Assert.Contains("L1-TFR-013 (Command.Environment[0])", stderr);
        }

        [Fact]
        public void S4_03_C_ReadyFunctionRecipeWithNullPort_RenderReturnsOneAndKeepsOutput()
        {
            var recipePath = WriteFile("tfr-ready-null.json", "{ \"SchemaVersion\": \"draft-1\", \"State\": \"Ready\", \"InputPorts\": [ null ] }");
            var outPath = WriteFile("out.json", Marker);

            var (exit, _, stderr) = Run("function-recipe", "render", recipePath, "--out", outPath);

            Assert.Equal(1, exit);
            Assert.Equal(Marker, File.ReadAllText(outPath));
            Assert.Contains("L1-TFR-013 (InputPorts[0])", stderr);
            AssertNoStackTrace(stderr);
        }

        // ── helpers ─────────────────────────────────────────────────────

        private static string Recipe(string? schemaJson) =>
            "{\n" + (schemaJson is null ? string.Empty : schemaJson + ",\n") + RecipeBody + "\n}\n";

        private static string FunctionRecipe(string? schemaJson) =>
            "{\n" + (schemaJson is null ? string.Empty : schemaJson + ",\n") + FunctionRecipeBody + "\n}\n";

        private (int Exit, string Stdout, string Stderr) Run(params string[] args)
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exit = CliApp.Run(args, new StringReader(string.Empty), stdout, stderr);
            return (exit, stdout.ToString(), stderr.ToString());
        }

        private string WriteFile(string name, string content)
        {
            var path = Path.Join(_workDir, name);
            File.WriteAllText(path, content);
            return path;
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

        private static void AssertNoStackTrace(string stderr) =>
            Assert.DoesNotContain("   at ", stderr, StringComparison.Ordinal);

        private sealed record SchemaVariant(string Id, string? Json, string Outcome, int? Exit, int? BusinessRpc, bool? ExistingFilePreserved);

        private static IEnumerable<SchemaVariant> SchemaVariants()
        {
            var path = Path.Join(RepoRoot(), "tests", "NodeKit.Cli.Tests", "Fixtures", "Contract", "cli-acceptance-contract.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var v in doc.RootElement.GetProperty("authoringSchema").GetProperty("variants").EnumerateArray())
            {
                var expected = v.GetProperty("expected");
                yield return new SchemaVariant(
                    v.GetProperty("id").GetString()!,
                    v.GetProperty("json").ValueKind == JsonValueKind.Null ? null : v.GetProperty("json").GetString(),
                    expected.GetProperty("outcome").GetString()!,
                    expected.GetProperty("exit").ValueKind == JsonValueKind.Number ? expected.GetProperty("exit").GetInt32() : null,
                    expected.GetProperty("businessRpc").ValueKind == JsonValueKind.Number ? expected.GetProperty("businessRpc").GetInt32() : null,
                    expected.GetProperty("existingFilePreserved").ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? expected.GetProperty("existingFilePreserved").GetBoolean()
                        : null);
            }
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Join(dir.FullName, "NodeKit.sln")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new FileNotFoundException("repo root(NodeKit.sln)를 찾지 못했습니다.");
        }

        private sealed class CountingToolSpecClient : IToolSpecBuildClient
        {
            public int Calls { get; private set; }

            public IAsyncEnumerable<BuildEvent> ResolveAndBuildAsync(string toolName, string version, string rawSpec, CancellationToken cancellationToken = default)
            {
                Calls++;
                return Empty(cancellationToken);
            }

            public Task CancelBuildAsync(string buildId, CancellationToken cancellationToken = default)
            {
                Calls++;
                return Task.CompletedTask;
            }

            private static async IAsyncEnumerable<BuildEvent> Empty([EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await Task.CompletedTask;
                cancellationToken.ThrowIfCancellationRequested();
                yield break;
            }
        }
    }
}
