using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Authoring;
using NodeKit.Authoring.Recipes;
using NodeKit.Cli;
using NodeKit.Grpc;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// P03.render (NodeKit v0.9.1 contract S1-07): render runs the full local
    /// validation first, keeps the legacy build-request preview apart from the
    /// real ToolSpec raw_spec (exact 7 keys, same bytes submit sends), and never
    /// carries the legacy Command/Inputs/Outputs/display fields into raw_spec.
    /// </summary>
    public class RecipeRenderP03Tests : IDisposable
    {
        private const string Digest =
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private const string ImageRefWithDigest = "condaforge/miniforge3:24.3.0-0@" + Digest;

        private static readonly string[] _rawSpecKeys =
        {
            "tool_name", "version", "kind", "image_uri", "dockerfile_content", "script", "environment_spec",
        };

        private readonly string _workDir = Path.Join(Path.GetTempPath(), "nodekit-p03-render-tests-" + Guid.NewGuid());
        private readonly IDisposable _resolveClientOverride =
            ResolveRecipeClientTestOverride.Use(NullResolveRecipeClient.Instance);

        public RecipeRenderP03Tests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
            _resolveClientOverride.Dispose();
        }

        // ── S1-07-C01 · build-request is a local preview, not a submit input ──

        [Fact]
        public void S1_07_C01_BuildRequestFormat_WritesLegacyPreviewShape()
        {
            var recipePath = WriteRecipe(PackageRecipe());
            var outPath = Path.Join(_workDir, "build-request.json");

            var exitCode = RunCli(out var stdout, out var stderr, "render", recipePath, "--out", outPath, "--format", "build-request");

            Assert.Equal(0, exitCode);
            Assert.Empty(stdout);
            Assert.Empty(stderr);
            using var document = JsonDocument.Parse(File.ReadAllText(outPath));
            var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToList();
            foreach (var key in new[] { "ToolName", "Version", "ImageUri", "DockerfileContent", "Script", "Command", "Inputs", "Outputs", "DisplayLabel" })
            {
                Assert.Contains(key, names);
            }

            Assert.DoesNotContain("tool_name", names);
            Assert.DoesNotContain("kind", names);
            Assert.Equal("bwa-mem", document.RootElement.GetProperty("ToolName").GetString());
            Assert.Contains("bwa=0.7.17=h5bf99c6_8", document.RootElement.GetProperty("DockerfileContent").GetString());
        }

        [Fact]
        public void S1_07_C01_DefaultFormatIsBuildRequest()
        {
            var recipePath = WriteRecipe(PackageRecipe());
            var outPath = Path.Join(_workDir, "build-request.json");

            Assert.Equal(0, RunCli(out _, out _, "render", recipePath, "--out", outPath));

            using var document = JsonDocument.Parse(File.ReadAllText(outPath));
            Assert.True(document.RootElement.TryGetProperty("ToolName", out _));
            Assert.False(document.RootElement.TryGetProperty("tool_name", out _));
        }

        [Fact]
        public void S1_07_C01_HelpSaysBuildRequestIsNotTheSubmitInput()
        {
            var exitCode = RunCli(out var stdout, out _, "render", "--help");

            Assert.Equal(0, exitCode);
            Assert.Contains("build-request는 submit의 입력이 아님", stdout);
            Assert.Contains("실제 제출은 nodekit submit <recipe.json>", stdout);
        }

        // ── S1-07-C02 · raw-spec is the exact submit raw_spec ─────────────────

        [Fact]
        public void S1_07_C02_RawSpecCompact_HasExactSevenKeysInWireOrder()
        {
            var recipe = PackageRecipe();
            var rawSpec = RenderRawSpec(WriteRecipe(recipe));

            using var document = JsonDocument.Parse(rawSpec);
            Assert.Equal(_rawSpecKeys, document.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.Equal("bwa-mem", document.RootElement.GetProperty("tool_name").GetString());
            Assert.Equal("0.7.17", document.RootElement.GetProperty("version").GetString());
            Assert.Equal(1, document.RootElement.GetProperty("kind").GetInt32());
            Assert.Equal(ImageRefWithDigest, document.RootElement.GetProperty("image_uri").GetString());
            Assert.Equal("run.sh", document.RootElement.GetProperty("script").GetString());
            Assert.Contains("bwa=0.7.17=h5bf99c6_8", document.RootElement.GetProperty("dockerfile_content").GetString());
            Assert.DoesNotContain("requested_at", rawSpec);
            Assert.DoesNotContain("raw_spec", rawSpec);
            Assert.DoesNotContain("\n", rawSpec);
        }

        [Fact]
        public void S1_07_C02_RawSpecFile_IsByteIdenticalToFactoryOutput()
        {
            var recipe = PackageRecipe();
            var rawSpec = RenderRawSpec(WriteRecipe(recipe));

            var expected = ToolSpecRawSpecFactory.Build(RecipeRenderer.Render(recipe));
            Assert.Equal(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(rawSpec));
        }

        [Fact]
        public void S1_07_C02_RawSpecFile_IsByteIdenticalToWhatSubmitSends()
        {
            var recipePath = WriteRecipe(PackageRecipe());
            var rawSpec = RenderRawSpec(recipePath);

            var client = new CapturingToolSpecClient();
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = SubmitCommand.Run(new[] { "submit", recipePath }, stdout, stderr, client);

            Assert.Equal(0, exitCode);
            Assert.Equal("bwa-mem", client.ToolName);
            Assert.Equal("0.7.17", client.Version);
            Assert.Equal(rawSpec, client.RawSpec);
        }

        [Fact]
        public void S1_07_C02_StdoutOut_PrintsTheSameRawSpecPlusNewline()
        {
            var recipePath = WriteRecipe(PackageRecipe());
            var rawSpec = RenderRawSpec(recipePath);

            var exitCode = RunCli(out var stdout, out var stderr, "render", recipePath, "--out", "-", "--format", "raw-spec");

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);
            Assert.Equal(rawSpec + Environment.NewLine, stdout);
        }

        [Theory]
        [InlineData("dockerfile")]
        [InlineData("source-structured")]
        public void S1_07_C02_OtherBuildKinds_UseSevenKeysAndKindOne(string kind)
        {
            var recipe = kind == "dockerfile" ? DockerfileRecipe() : StructuredRecipe();
            var rawSpec = RenderRawSpec(WriteRecipe(recipe));

            using var document = JsonDocument.Parse(rawSpec);
            Assert.Equal(_rawSpecKeys, document.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.Equal(1, document.RootElement.GetProperty("kind").GetInt32());
            Assert.Equal(ToolSpecRawSpecFactory.Build(RecipeRenderer.Render(recipe)), rawSpec);
        }

        // ── S1-07-C03 · render runs the full pipeline, including RecipeValidator ──

        [Theory]
        [InlineData("build-request")]
        [InlineData("raw-spec")]
        public void S1_07_C03_ChannelWithLineFeed_ExitsOneWithNoOutput(string format)
        {
            var recipe = PackageRecipe();
            recipe.Channels = new List<string> { "bioconda\nRUN echo bad" };
            var recipePath = WriteRecipe(recipe);
            Assert.Contains("bioconda\\nRUN echo bad", File.ReadAllText(recipePath));
            var outPath = Path.Join(_workDir, "out.json");

            var exitCode = RunCli(out var stdout, out var stderr, "render", recipePath, "--out", outPath, "--format", format);

            Assert.Equal(1, exitCode);
            Assert.Empty(stdout);
            Assert.Contains("L1-RCP-012 (Channels)", stderr);
            Assert.False(File.Exists(outPath));
            Assert.Empty(Directory.GetFiles(_workDir, "*nodekit-tmp*"));
        }

        [Fact]
        public void S1_07_C03_ChannelWithLineFeed_KeepsAnExistingOutputFile()
        {
            var recipe = PackageRecipe();
            recipe.Channels = new List<string> { "bioconda\nRUN echo bad" };
            var recipePath = WriteRecipe(recipe);
            var outPath = Path.Join(_workDir, "out.json");
            var marker = Encoding.UTF8.GetBytes("marker\n");
            File.WriteAllBytes(outPath, marker);

            var exitCode = RunCli(out _, out _, "render", recipePath, "--out", outPath, "--format", "raw-spec");

            Assert.Equal(1, exitCode);
            Assert.Equal(marker, File.ReadAllBytes(outPath));
        }

        [Fact]
        public void S1_07_C03_ChannelWithLineFeed_StdoutOutPrintsNothing()
        {
            var recipe = PackageRecipe();
            recipe.Channels = new List<string> { "bioconda\nRUN echo bad" };

            var exitCode = RunCli(out var stdout, out var stderr, "render", WriteRecipe(recipe), "--out", "-", "--format", "raw-spec");

            Assert.Equal(1, exitCode);
            Assert.Empty(stdout);
            Assert.Contains("L1-RCP-012", stderr);
        }

        // ── S1-07-C04 · output path errors ────────────────────────────────────

        [Theory]
        [InlineData("build-request")]
        [InlineData("raw-spec")]
        public void S1_07_C04_OutPathIsDirectory_ExitsTwoWithoutSuccessText(string format)
        {
            var outDir = Path.Join(_workDir, "out-dir");
            Directory.CreateDirectory(outDir);

            var exitCode = RunCli(out var stdout, out var stderr, "render", WriteRecipe(PackageRecipe()), "--out", outDir, "--format", format);

            Assert.Equal(2, exitCode);
            Assert.Empty(stdout);
            Assert.Contains("[WRITE_TARGET_UNSUPPORTED]", stderr);
            Assert.DoesNotContain("   at ", stderr);
            Assert.Empty(Directory.GetFileSystemEntries(outDir));
        }

        [Theory]
        [InlineData("build-request")]
        [InlineData("raw-spec")]
        public void S1_07_C04_OutPathParentMissing_ExitsTwoWithoutCreatingIt(string format)
        {
            var outPath = Path.Join(_workDir, "missing", "out.json");

            var exitCode = RunCli(out var stdout, out var stderr, "render", WriteRecipe(PackageRecipe()), "--out", outPath, "--format", format);

            Assert.Equal(2, exitCode);
            Assert.Empty(stdout);
            Assert.Contains("[WRITE_PARENT_MISSING]", stderr);
            Assert.DoesNotContain("   at ", stderr);
            Assert.False(Directory.Exists(Path.Join(_workDir, "missing")));
        }

        // ── S1-07-C05 · legacy fields stay out of raw_spec ────────────────────

        [Fact]
        public void S1_07_C05_LegacyFields_AreInBuildRequestButNotInRawSpec()
        {
            var recipePath = WriteRecipe(LegacyFieldsRecipe());

            var rawSpec = RenderRawSpec(recipePath);
            var buildRequestPath = Path.Join(_workDir, "build-request.json");
            Assert.Equal(0, RunCli(out _, out _, "render", recipePath, "--out", buildRequestPath));
            var buildRequest = File.ReadAllText(buildRequestPath);

            foreach (var value in new[] { "legacy-cmd", "legacy-in", "legacy-out", "Legacy label", "Legacy description", "legacy-category", "legacy-tag" })
            {
                Assert.Contains(value, buildRequest);
                Assert.DoesNotContain(value, rawSpec);
            }

            using var document = JsonDocument.Parse(rawSpec);
            Assert.Equal(_rawSpecKeys, document.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        }

        [Fact]
        public void S1_07_C05_FactoryIgnoresLegacyToolDefinitionFields()
        {
            var plain = new ToolDefinition { Name = "bwa-mem", Version = "0.7.17", ImageUri = ImageRefWithDigest, Script = "run.sh" };
            var legacy = new ToolDefinition
            {
                Name = "bwa-mem",
                Version = "0.7.17",
                ImageUri = ImageRefWithDigest,
                Script = "run.sh",
                Command = new List<string> { "legacy-cmd" },
                Inputs = new List<ToolInput> { new() { Name = "legacy-in", Role = "r", Format = "fastq", Shape = "single" } },
                Outputs = new List<ToolOutput> { new() { Name = "legacy-out", Role = "r", Format = "bam", Shape = "single", Class = "primary" } },
                DisplayLabel = "Legacy label",
                DisplayDescription = "Legacy description",
                DisplayCategory = "legacy-category",
                DisplayTags = new List<string> { "legacy-tag" },
            };

            Assert.Equal(ToolSpecRawSpecFactory.Build(plain), ToolSpecRawSpecFactory.Build(legacy));
        }

        [Fact]
        public void S1_07_C05_SubmitDoesNotSendLegacyFields()
        {
            var client = new CapturingToolSpecClient();
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(new[] { "submit", WriteRecipe(LegacyFieldsRecipe()) }, stdout, stderr, client);

            Assert.Equal(0, exitCode);
            Assert.DoesNotContain("legacy-", client.RawSpec);
            Assert.DoesNotContain("Legacy", client.RawSpec);
        }

        [Fact]
        public void S1_07_C05_InteractiveCreate_PointsCommandAndPortsToFunctionRecipe()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // quick mode
                "n", "n", "y", "n", "n", "n", // Q&A -> recommend container
                "", // accept recommended method
                "bwa-mem", "0.7.17", "run.sh",
                "condaforge/miniforge3:24.3.0-0", // ImageRef
                Digest, // ImageDigest; Command is next and is not asked
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            var text = stdout.ToString();
            Assert.Contains(RecipeCreateFlow.CommandPortReplacedMessage, text);
            Assert.Contains("--tool-spec-digest", text);
            Assert.Contains("nodekit function-recipe create", text);
            Assert.Contains("ToolSpec raw_spec에 포함되지 않습니다", text);

            using var saved = JsonDocument.Parse(File.ReadAllText(outPath));
            Assert.Equal(0, saved.RootElement.GetProperty("Command").GetArrayLength());
            Assert.Equal(0, saved.RootElement.GetProperty("Inputs").GetArrayLength());
            Assert.Equal(0, saved.RootElement.GetProperty("Outputs").GetArrayLength());
        }

        [Fact]
        public void S1_07_C05_NonInteractiveCommandField_WarnsThatItIsNotSent()
        {
            var outPath = Path.Join(_workDir, "recipe.json");

            var exitCode = RunCli(
                out _,
                out var stderr,
                "recipe", "create", outPath, "--non-interactive", "--method", "container",
                "--field", "ToolName=bwa-mem",
                "--field", "ToolVersion=0.7.17",
                "--field", "Script=run.sh",
                "--field", "ImageRef=condaforge/miniforge3:24.3.0-0",
                "--field", $"ImageDigest={Digest}",
                "--field", "Command=bwa");

            Assert.Equal(0, exitCode);
            Assert.Contains("경고: " + RecipeCreateFlow.CommandPortGuidance, stderr);
            Assert.True(File.Exists(outPath));
        }

        [Fact]
        public void S1_07_C05_NonInteractiveWithoutCommand_HasNoCommandWarning()
        {
            var outPath = Path.Join(_workDir, "recipe.json");

            var exitCode = RunCli(
                out _,
                out var stderr,
                "recipe", "create", outPath, "--non-interactive", "--method", "container",
                "--field", "ToolName=bwa-mem",
                "--field", "ToolVersion=0.7.17",
                "--field", "Script=run.sh",
                "--field", "ImageRef=condaforge/miniforge3:24.3.0-0",
                "--field", $"ImageDigest={Digest}");

            Assert.Equal(0, exitCode);
            Assert.DoesNotContain(RecipeCreateFlow.CommandPortGuidance, stderr);
        }

        [Fact]
        public void S1_07_C05_RenderHelp_SaysLegacyFieldsAreNotInRawSpec()
        {
            Assert.Equal(0, RunCli(out var stdout, out _, "render", "--help"));

            Assert.Contains("Command/Inputs/Outputs/Display* 필드는 build-request 미리보기에만 있고 raw_spec에는 포함되지 않음", stdout);
            Assert.Contains("nodekit function-recipe create", stdout);
        }

        // ── fixtures ──────────────────────────────────────────────────────────

        // F-PACKAGE: complete bwa pin from bioconda with conda.
        private static RecipeDocument PackageRecipe() => new()
        {
            BuildKind = RecipeKind.Conda,
            ToolName = "bwa-mem",
            Version = "0.7.17",
            Script = "run.sh",
            BaseImage = ImageRefWithDigest,
            PackageEngine = "conda",
            Channels = new List<string> { "bioconda" },
            Packages = new List<string> { "bwa=0.7.17=h5bf99c6_8" },
        };

        private static RecipeDocument LegacyFieldsRecipe()
        {
            var recipe = PackageRecipe();
            recipe.Command = new List<string> { "legacy-cmd" };
            recipe.Inputs = new List<ToolInput> { new() { Name = "legacy-in", Role = "sample-fastq", Format = "fastq", Shape = "single" } };
            recipe.Outputs = new List<ToolOutput> { new() { Name = "legacy-out", Role = "aligned-bam", Format = "bam", Shape = "single", Class = "primary" } };
            recipe.DisplayLabel = "Legacy label";
            recipe.DisplayDescription = "Legacy description";
            recipe.DisplayCategory = "legacy-category";
            recipe.DisplayTags = new List<string> { "legacy-tag" };
            return recipe;
        }

        private static RecipeDocument DockerfileRecipe() => new()
        {
            BuildKind = RecipeKind.DockerfileFallback,
            ToolName = "bwa-mem",
            Version = "0.7.17",
            Script = "run.sh",
            BaseImage = ImageRefWithDigest,
            DockerfileContent = "FROM " + ImageRefWithDigest + "\nRUN echo ok\nUSER 1000\n",
        };

        private static RecipeDocument StructuredRecipe() => new()
        {
            BuildKind = RecipeKind.SourceBuildStructured,
            ToolName = "bwa-mem",
            Version = "0.7.17",
            Script = "run.sh",
            BuildProfile = "generic",
            RuntimeProfile = "minimal",
            SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
            SourceChecksum = Digest,
            SourceBuildCommands = new List<string> { "make install DESTDIR=/nodekit/output" },
        };

        private string RenderRawSpec(string recipePath)
        {
            var rawSpecPath = Path.Join(_workDir, "raw-spec-" + Guid.NewGuid() + ".json");
            Assert.Equal(0, RunCli(out _, out var stderr, "render", recipePath, "--out", rawSpecPath, "--format", "raw-spec"));
            Assert.Empty(stderr);
            return File.ReadAllText(rawSpecPath);
        }

        private string WriteRecipe(RecipeDocument recipe)
        {
            var path = Path.Join(_workDir, "input-" + Guid.NewGuid() + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(recipe, RecipeCreateCommand.JsonOptions));
            return path;
        }

        private static int RunCli(out string stdout, out string stderr, params string[] args)
        {
            using var stdoutWriter = new StringWriter();
            using var stderrWriter = new StringWriter();
            var exitCode = CliApp.Run(args, TextReader.Null, stdoutWriter, stderrWriter);
            stdout = stdoutWriter.ToString();
            stderr = stderrWriter.ToString();
            return exitCode;
        }

        private sealed class CapturingToolSpecClient : IToolSpecBuildClient
        {
            public string? ToolName { get; private set; }

            public string? Version { get; private set; }

            public string RawSpec { get; private set; } = string.Empty;

#pragma warning disable CS1998
            public async IAsyncEnumerable<BuildEvent> ResolveAndBuildAsync(
                string toolName,
                string version,
                string rawSpec,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                ToolName = toolName;
                Version = version;
                RawSpec = rawSpec;
                yield return new BuildEvent { Kind = BuildEventKind.Succeeded };
            }
#pragma warning restore CS1998

            public Task CancelBuildAsync(string buildId, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
        }
    }
}
