using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NodeKit.Authoring.ToolFunctionRecipes;
using NodeKit.Cli;
using NodeKit.Grpc;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// P06.current — render/submit 경계의 current preflight (v0.9.1 계약 S3-09-A..D,
    /// current_preflight_ready). 저장된 State=Ready 표지만 믿지 않고 현재 파일 내용,
    /// base image A, ScriptPath source, raw_spec과 typed 등록 전제를 다시 확인한다.
    /// render는 RPC가 없고, 실제 submit caller 연결과 RPC transcript는 P07
    /// (ToolFunctionSecondImageBuildP07Tests)에 있다.
    /// </summary>
    public class ToolFunctionCurrentPreflightP06Tests : IDisposable
    {
        private const string DigestA = "sha256:a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1";
        private const string DigestC1 = "c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1";
        private const string Script = "#!/bin/sh\nsamtools sort \"$@\"\n";
        private const string Marker = "existing-output-marker";

        private readonly string _workDir =
            Path.Join(Path.GetTempPath(), "nodekit-p06-current-" + Guid.NewGuid());

        public ToolFunctionCurrentPreflightP06Tests() => Directory.CreateDirectory(_workDir);

        public void Dispose() => Directory.Delete(_workDir, recursive: true);

        public static IEnumerable<object[]> PortShapeVariants() => new[]
        {
            new object[] { "input-only", true, false },
            new object[] { "output-only", false, true },
            new object[] { "zero-port", false, false },
        };

        /// <summary>현재 L1 검증을 통과하는 Ready Recipe (raw_typed fixture와 같은 값).</summary>
        private static ToolFunctionRecipe ReadyRecipe() => new()
        {
            State = ToolFunctionRecipeState.Ready,
            ToolSpecDigest = DigestC1,
            BaseToolImageDigest = DigestA,
            FunctionId = "samtools.sort",
            Revision = "r7",
            DisplayLabel = "Sort BAM",
            ScriptPath = "./sort.sh",
            Command = new CommandContract
            {
                Executable = "samtools",
                Arguments = new List<string> { "sort", "-@", "{param.threads}", "-o", "out.bam", "in.bam" },
                WorkingDirectory = "/work",
                SuccessExitCodes = new List<int> { 0 },
            },
            InputPorts = new List<PortContract>
            {
                new() { Name = "reads", Direction = PortDirection.Input, DataFormat = "bam", Cardinality = PortCardinality.Single, Required = true },
            },
            OutputPorts = new List<PortContract>
            {
                new() { Name = "sorted", Direction = PortDirection.Output, DataFormat = "bam", Cardinality = PortCardinality.Single, PathOrGlob = "/work/out.bam" },
            },
            FixtureReferences = new List<FixtureReference>
            {
                new() { LocalPath = "fixtures/small.bam", ContentDigest = "sha256:" + new string('d', 64) },
            },
            ExpectedResults = new List<ExpectedResult>
            {
                new() { OutputPortName = "sorted", ExpectedValueOrRule = "samtools quickcheck" },
            },
            Parameters = new List<ParameterContract>
            {
                new() { Name = "threads", Type = ParameterType.Integer, DefaultValue = "4", Required = true, CliArgumentMapping = "-@" },
            },
            EnforcedResources = new ResourceContract
            {
                CpuRequest = "500m",
                CpuLimit = "2",
                MemoryRequest = "1Gi",
                MemoryLimit = "2Gi",
            },
        };

        private string Save(ToolFunctionRecipe recipe, string? script = Script, string name = "recipe.json")
        {
            if (script is not null)
            {
                File.WriteAllText(Path.Join(_workDir, "sort.sh"), script);
            }

            var path = Path.Join(_workDir, name);
            File.WriteAllText(path, JsonSerializer.Serialize(recipe, ToolFunctionRecipeCliIo.JsonOptions));
            return path;
        }

        private static (int Exit, string Stdout, string Stderr) Render(string recipePath, string outPath)
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exit = CliApp.Run(
                new[] { "function-recipe", "render", recipePath, "--out", outPath },
                new StringReader(string.Empty),
                stdout,
                stderr);
            return (exit, stdout.ToString(), stderr.ToString());
        }

        private static (bool Ok, ToolFunctionCurrentPreflight? Preflight, int Exit, string Stderr) Preflight(
            string recipePath, bool requireTypedRegistration)
        {
            Assert.True(ToolFunctionRecipeCliIo.TryLoad(recipePath, TextWriter.Null, out var recipe));
            using var stderr = new StringWriter();
            var ok = ToolFunctionSubmissionPreflight.TryRun(
                recipe!, recipePath, requireTypedRegistration, stderr, out var preflight, out var exit);
            return (ok, preflight, exit, stderr.ToString());
        }

        private static string ExpectedRawSpec(string script)
        {
            Assert.True(ToolFunctionRawSpecFactory.TryBuild(DigestA, script, indented: false, out var raw, out _));
            return raw!;
        }

        [Fact]
        public void S3_09_C_ValidReady_RenderWritesOutputAndPreflightFixesFreshBasis()
        {
            var recipePath = Save(ReadyRecipe());
            var outPath = Path.Join(_workDir, "out.json");

            var (exit, _, stderr) = Render(recipePath, outPath);

            Assert.True(exit == 0, stderr);
            Assert.True(File.Exists(outPath));

            var (ok, preflight, preflightExit, preflightErr) = Preflight(recipePath, requireTypedRegistration: true);
            Assert.True(ok, preflightErr);
            Assert.Equal(0, preflightExit);
            Assert.Equal(DigestA, preflight!.Source.BaseImageDigest);
            Assert.Equal("./sort.sh", preflight.Source.ScriptPath);
            Assert.Equal(Script, preflight.Source.Script);
            Assert.Equal(ExpectedRawSpec(Script), preflight.RawSpecJson);
            Assert.True(ToolFunctionRawSpecFactory.TryValidate(preflight.RawSpecJson, out _));
        }

        [Fact]
        public void S3_09_C_FreshPreflightReadsTheCurrentScriptEveryTime()
        {
            var recipePath = Save(ReadyRecipe());
            var first = Preflight(recipePath, requireTypedRegistration: true);

            const string changed = "#!/bin/sh\nsamtools sort -n \"$@\"\n";
            File.WriteAllText(Path.Join(_workDir, "sort.sh"), changed);
            var second = Preflight(recipePath, requireTypedRegistration: true);

            Assert.True(first.Ok && second.Ok, first.Stderr + second.Stderr);
            Assert.Equal(Script, first.Preflight!.Source.Script);
            Assert.Equal(changed, second.Preflight!.Source.Script);
            Assert.NotEqual(first.Preflight.Source.ScriptSha256, second.Preflight.Source.ScriptSha256);
            Assert.Equal(ExpectedRawSpec(changed), second.Preflight.RawSpecJson);

            // 앞서 고정한 basis는 파일이 바뀌어도 그대로다(snapshot).
            Assert.Equal(ExpectedRawSpec(Script), first.Preflight.RawSpecJson);
        }

        // #103: input-only/output-only/zero-port·빈 arguments는 유효한 Ready 변형이다.
        [Theory]
        [MemberData(nameof(PortShapeVariants))]
        public void S3_09_C_PortShapeAndEmptyArgumentVariantsRender(string variant, bool keepInputs, bool keepOutputs)
        {
            var recipe = ReadyRecipe();
            if (!keepInputs)
            {
                recipe.InputPorts.Clear();
            }

            if (!keepOutputs)
            {
                recipe.OutputPorts.Clear();
                recipe.ExpectedResults.Clear();
            }

            recipe.Parameters.Clear();
            recipe.Command.Arguments.Clear();
            var recipePath = Save(recipe, name: variant + ".json");
            var outPath = Path.Join(_workDir, variant + "-out.json");

            var (exit, _, stderr) = Render(recipePath, outPath);

            Assert.True(exit == 0, variant + ": " + stderr);
            Assert.True(File.Exists(outPath), variant);
            Assert.True(Preflight(recipePath, requireTypedRegistration: true).Ok, variant);
        }

        [Fact]
        public void S3_09_A_ReadyButEditedToInvalidCommand_RenderExit1WithViolationAndNoOutput()
        {
            var recipe = ReadyRecipe();
            recipe.Command.Executable = "samtools ; bad";
            var recipePath = Save(recipe);
            var existingOut = Path.Join(_workDir, "existing.json");
            File.WriteAllText(existingOut, Marker);
            var newOut = Path.Join(_workDir, "new.json");

            var existing = Render(recipePath, existingOut);
            var fresh = Render(recipePath, newOut);

            Assert.Equal(1, existing.Exit);
            Assert.Equal(1, fresh.Exit);
            Assert.Contains("L1-TFR-003", existing.Stderr);
            Assert.Equal(Marker, File.ReadAllText(existingOut));
            Assert.False(File.Exists(newOut));
            Assert.Contains("\"Ready\"", File.ReadAllText(recipePath));
        }

        [Theory]
        [InlineData("invalid-base-digest", "sha256:bad", Script, 1, ToolFunctionSourceMaterializer.InvalidBaseImageCode)]
        [InlineData("blank-script", DigestA, "  \n\t\n", 1, ToolFunctionSourceMaterializer.BlankScriptCode)]
        [InlineData("missing-script", DigestA, null, 2, ToolFunctionSourceMaterializer.ScriptReadFailedCode)]
        public void S3_09_B_ReadyWithInvalidBaseDigestOrScriptSource_RenderFailsWithoutOutput(
            string variant, string baseDigest, string? script, int expectedExit, string expectedCode)
        {
            var recipe = ReadyRecipe();
            recipe.BaseToolImageDigest = baseDigest;
            var recipePath = Save(recipe, script);
            var outPath = Path.Join(_workDir, "out.json");

            var (exit, stdout, stderr) = Render(recipePath, outPath);

            Assert.True(expectedExit == exit, variant + ": " + stderr);
            Assert.Contains(expectedCode, stderr);
            Assert.False(File.Exists(outPath), variant);
            Assert.DoesNotContain("저장되었습니다", stdout);
            Assert.False(Preflight(recipePath, requireTypedRegistration: true).Ok, variant);
        }

        [Theory]
        [InlineData("Draft")]
        [InlineData("Approved")]
        [InlineData("Validated")]
        [InlineData("Submitted")]
        [InlineData("Built")]
        public void S3_09_D_NonReadyStateIsRejectedAndNotRewritten(string state)
        {
            // ToolFunctionRecipeState는 internal이라 theory 인자는 이름으로 받는다.
            var recipe = ReadyRecipe();
            recipe.State = Enum.Parse<ToolFunctionRecipeState>(state);
            var recipePath = Save(recipe);
            var before = File.ReadAllText(recipePath);
            var outPath = Path.Join(_workDir, "out.json");

            var (exit, _, stderr) = Render(recipePath, outPath);
            var preflight = Preflight(recipePath, requireTypedRegistration: true);

            Assert.Equal(1, exit);
            Assert.Contains(ToolFunctionSubmissionPreflight.NotReadyCode, stderr);
            Assert.False(File.Exists(outPath));
            Assert.False(preflight.Ok);
            Assert.Equal(1, preflight.Exit);
            Assert.Equal(before, File.ReadAllText(recipePath));
        }

        // C1 형식 guard는 typed 제출 계약에 한정한다(S3-02-F) — render 미리보기는 막지 않는다.
        [Fact]
        public void TypedPrerequisite_ImageShapedC1BlocksOnlyTheTypedSubmissionPreflight()
        {
            var recipe = ReadyRecipe();
            recipe.ToolSpecDigest = "sha256:" + DigestC1;
            var recipePath = Save(recipe);

            var typed = Preflight(recipePath, requireTypedRegistration: true);
            var renderOnly = Preflight(recipePath, requireTypedRegistration: false);
            var (exit, _, stderr) = Render(recipePath, Path.Join(_workDir, "out.json"));

            Assert.False(typed.Ok);
            Assert.Equal(1, typed.Exit);
            Assert.Contains(ToolFunctionRegistrationRequestFactory.InvalidToolSpecDigestCode, typed.Stderr);
            Assert.Null(typed.Preflight);
            Assert.True(renderOnly.Ok, renderOnly.Stderr);
            Assert.True(exit == 0, stderr);
        }

        [Fact]
        public void TypedPrerequisite_MappingFailureBlocksTheTypedSubmissionPreflight()
        {
            var recipe = ReadyRecipe();
            recipe.OutputPorts[0].Direction = PortDirection.Input;
            var recipePath = Save(recipe);

            var typed = Preflight(recipePath, requireTypedRegistration: true);

            Assert.False(typed.Ok);
            Assert.Equal(1, typed.Exit);
            Assert.Contains(ToolFunctionRegistrationRequestFactory.PortDirectionMismatchCode, typed.Stderr);
        }

        private const string NullSentinel = "__NULL_IN_PERSISTED_JSON__";

        /// <summary>
        /// 저장된 JSON에 Command.Environment 항목 하나를 넣는다. sentinel 값은 파일 텍스트에서
        /// JSON null로 바꾼다 — 메모리 객체가 아니라 실제 load 경로를 거친다.
        /// </summary>
        private string SaveWithEnvironmentEntry(string name, string source)
        {
            var recipe = ReadyRecipe();
            recipe.Command.Environment.Add(new EnvironmentEntry { Name = name, Source = source });
            var recipePath = Save(recipe);
            var json = File.ReadAllText(recipePath).Replace("\"" + NullSentinel + "\"", "null", StringComparison.Ordinal);
            File.WriteAllText(recipePath, json);
            return recipePath;
        }

        // Codex P2 r4241971212: persisted Ready JSON의 environment name/source null은
        // protobuf setter의 ArgumentNullException이 아니라 exit 1 진단이어야 한다.
        [Theory]
        [InlineData("name-null", NullSentinel, "runtime.tmp")]
        [InlineData("source-null", "TMPDIR", NullSentinel)]
        [InlineData("both-null", NullSentinel, NullSentinel)]
        public void TypedPrerequisite_NullEnvironmentFieldInPersistedJson_FailsWithExit1Diagnostic(
            string variant, string name, string source)
        {
            var recipePath = SaveWithEnvironmentEntry(name, source);
            Assert.Contains("null", File.ReadAllText(recipePath));
            Assert.True(ToolFunctionRecipeCliIo.TryLoad(recipePath, TextWriter.Null, out var loaded), variant);
            Assert.True(loaded!.Command.Environment[0].Name is null || loaded.Command.Environment[0].Source is null, variant);

            var mapped = ToolFunctionRegistrationRequestFactory.TryCheckSpecMapping(loaded, out var mappingError);
            var typed = Preflight(recipePath, requireTypedRegistration: true);
            var built = ToolFunctionRegistrationRequestFactory.TryBuild(
                loaded, new ToolFunctionRegistrationInputs("req-1", DigestC1, "sha256:" + new string('b', 64), null), out var plan, out var buildError);

            Assert.False(built, variant);
            Assert.Null(plan);
            Assert.Equal(ToolFunctionRegistrationRequestFactory.NullEnvironmentFieldCode, buildError!.Code);
            Assert.False(mapped, variant);
            Assert.Equal(ToolFunctionRegistrationRequestFactory.NullEnvironmentFieldCode, mappingError!.Code);
            Assert.Equal(1, mappingError.ExitCode);
            Assert.Contains("Command.Environment[0]", mappingError.Message);
            Assert.False(typed.Ok, variant);
            Assert.Equal(1, typed.Exit);
            Assert.Null(typed.Preflight);
            Assert.Contains(ToolFunctionRegistrationRequestFactory.NullEnvironmentFieldCode, typed.Stderr);
        }

        [Fact]
        public void TypedPrerequisite_ValidEnvironmentEntryInPersistedJson_PassesAndKeepsValues()
        {
            var recipePath = SaveWithEnvironmentEntry("TMPDIR", "runtime.tmp");
            Assert.True(ToolFunctionRecipeCliIo.TryLoad(recipePath, TextWriter.Null, out var loaded));

            Assert.True(ToolFunctionRegistrationRequestFactory.TryCheckSpecMapping(loaded!, out var mappingError), mappingError?.Message);
            var typed = Preflight(recipePath, requireTypedRegistration: true);
            Assert.True(typed.Ok, typed.Stderr);

            var inputs = new ToolFunctionRegistrationInputs(
                "req-1", DigestC1, "sha256:" + new string('b', 64), null);
            Assert.True(ToolFunctionRegistrationRequestFactory.TryBuild(loaded!, inputs, out var plan, out var buildError), buildError?.Message);
            var entry = Assert.Single(plan!.Request.Spec.Command.Environment);
            Assert.Equal("TMPDIR", entry.Name);
            Assert.Equal("runtime.tmp", entry.Source);
        }

        // P06 review P3-10: 빈 문자열은 null이 아니다 — 거부하지 않고 그대로 매핑한다
        // (mapper가 IsNullOrEmpty로 넓어지면 이 시험이 실패한다).
        [Theory]
        [InlineData("both-empty", "", "")]
        [InlineData("source-empty", "TMPDIR", "")]
        [InlineData("name-empty", "", "runtime.tmp")]
        public void TypedPrerequisite_EmptyStringEnvironmentField_IsNotNull_PassesAndKeepsValues(
            string variant, string name, string source)
        {
            var recipePath = SaveWithEnvironmentEntry(name, source);
            Assert.True(ToolFunctionRecipeCliIo.TryLoad(recipePath, TextWriter.Null, out var loaded), variant);

            Assert.True(ToolFunctionRegistrationRequestFactory.TryCheckSpecMapping(loaded!, out var mappingError), $"{variant}: {mappingError?.Message}");
            var typed = Preflight(recipePath, requireTypedRegistration: true);
            Assert.True(typed.Ok, $"{variant}: {typed.Stderr}");

            var inputs = new ToolFunctionRegistrationInputs("req-1", DigestC1, "sha256:" + new string('b', 64), null);
            Assert.True(ToolFunctionRegistrationRequestFactory.TryBuild(loaded!, inputs, out var plan, out var buildError), $"{variant}: {buildError?.Message}");
            var entry = Assert.Single(plan!.Request.Spec.Command.Environment);
            Assert.Equal(name, entry.Name);
            Assert.Equal(source, entry.Source);
        }

        // P06 review P3-11: null이 뒤쪽 항목에 있으면 진단은 그 실제 index를 가리킨다(고정 [0] 아님).
        [Fact]
        public void TypedPrerequisite_NullEnvironmentFieldInLaterEntry_DiagnosticNamesThatIndex()
        {
            var recipe = ReadyRecipe();
            recipe.Command.Environment.Add(new EnvironmentEntry { Name = "TMPDIR", Source = "runtime.tmp" });
            recipe.Command.Environment.Add(new EnvironmentEntry { Name = "HOME", Source = "runtime.home" });
            recipe.Command.Environment.Add(new EnvironmentEntry { Name = NullSentinel, Source = "runtime.cache" });
            var recipePath = Save(recipe);
            File.WriteAllText(
                recipePath,
                File.ReadAllText(recipePath).Replace("\"" + NullSentinel + "\"", "null", StringComparison.Ordinal));
            Assert.True(ToolFunctionRecipeCliIo.TryLoad(recipePath, TextWriter.Null, out var loaded));
            Assert.Null(loaded!.Command.Environment[2].Name);

            Assert.False(ToolFunctionRegistrationRequestFactory.TryCheckSpecMapping(loaded, out var mappingError));
            Assert.Equal(ToolFunctionRegistrationRequestFactory.NullEnvironmentFieldCode, mappingError!.Code);
            Assert.Contains("Command.Environment[2]", mappingError.Message);
            Assert.DoesNotContain("Command.Environment[0]", mappingError.Message);

            var typed = Preflight(recipePath, requireTypedRegistration: true);
            Assert.False(typed.Ok);
            Assert.Contains("Command.Environment[2]", typed.Stderr);
        }
    }
}
