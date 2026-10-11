using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Google.Protobuf;
using NodeKit.Authoring.ToolFunctionRecipes;
using NodeKit.Cli;
using NodeKit.Grpc;
using NodeKit.Validation.ToolFunctionRecipes;
using Xunit;
using Pb = Nodevault.V1;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// P06.raw_typed — raw_spec v1 exact 4-key serializer와 Recipe→typed
    /// RegisterToolFunctionRequest mapper (v0.9.1 계약 S3-02-A/C/D/E/F, S3-04-A/B/D/E,
    /// S3-06-A..F). 실제 ResolveToolSpec/RegisterToolFunction 전송(S3-04-C, S3-07)과
    /// render/submit current gate(P06.current)는 이 테스트 범위가 아니다 — 여기서는
    /// RPC를 하지 않는다.
    /// </summary>
    public class ToolFunctionRawTypedP06Tests : IDisposable
    {
        // A/B/C1/C2가 모두 다른 fixture (toolfunction-persisted.json identityRoles sample).
        private const string DigestA = "sha256:a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1";
        private const string DigestB = "sha256:b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2";
        private const string DigestC1 = "c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1";
        private const string BasisC2 = "c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2";
        private const string Script = "#!/bin/sh\nsamtools sort \"$@\"\n";
        private const string RequestId = "req-p06-raw-typed-1";

        private readonly string _workDir =
            Path.Join(Path.GetTempPath(), "nodekit-p06-raw-typed-" + Guid.NewGuid());

        public ToolFunctionRawTypedP06Tests() => Directory.CreateDirectory(_workDir);

        public void Dispose() => Directory.Delete(_workDir, recursive: true);

        private static ToolFunctionRegistrationInputs Inputs(
            string? c1 = DigestC1,
            string? b = DigestB,
            string? c2 = BasisC2) => new(RequestId, c1, b, c2);

        /// <summary>실행·표시·검증·환경 필드를 모두 채운 Ready Recipe (현재 L1 검증 통과).</summary>
        private static ToolFunctionRecipe FullRecipe() => new()
        {
            State = ToolFunctionRecipeState.Ready,
            ToolSpecDigest = DigestC1,
            BaseToolImageDigest = DigestA,
            FunctionId = "samtools.sort",
            Revision = "r7",
            DisplayLabel = "Sort BAM",
            DisplayDescription = "Coordinate-sort a BAM file",
            DisplayCategory = "alignment",
            DisplayTags = new List<string> { "bam", "sort" },
            ScriptPath = "./sort.sh",
            Command = new CommandContract
            {
                Executable = "samtools",
                Arguments = new List<string> { "sort", "-@", "{param.threads}", "-o", "out.bam", "--mode", "{param.mode}", "in.bam" },
                WorkingDirectory = "/work",
                Environment = new List<EnvironmentEntry>
                {
                    new() { Name = "TMPDIR", Source = "runtime.tmp" },
                    new() { Name = "LANG", Source = "platform.locale" },
                },
                SuccessExitCodes = new List<int> { 0, 3 },
                TimeoutPolicy = new TimeoutPolicy { SoftSeconds = 60, HardSeconds = 120 },
            },
            InputPorts = new List<PortContract>
            {
                new()
                {
                    Name = "reads",
                    Direction = PortDirection.Input,
                    DataFormat = "bam",
                    Cardinality = PortCardinality.Multiple,
                    Required = false,
                    PathPlacementRule = "/work/in/{name}",
                    CompanionFiles = new List<string> { ".bai", ".csi" },
                },
            },
            OutputPorts = new List<PortContract>
            {
                new()
                {
                    Name = "sorted",
                    Direction = PortDirection.Output,
                    DataFormat = "bam",
                    Cardinality = PortCardinality.Single,
                    PathOrGlob = "/work/out.bam",
                    CompletionCheck = "exists",
                    DownstreamCompatibilityNote = "coordinate-sorted",
                },
                new()
                {
                    Name = "log",
                    Direction = PortDirection.Output,
                    DataFormat = "txt",
                    Cardinality = PortCardinality.Single,
                    PathOrGlob = "/work/*.log",
                    CompletionCheck = "nonempty",
                },
            },
            FixtureReferences = new List<FixtureReference>
            {
                new() { LocalPath = "fixtures/small.bam", ContentDigest = "sha256:" + new string('d', 64) },
                new() { LocalPath = string.Empty, ContentDigest = "sha256:" + new string('e', 64) },
            },
            ExpectedResults = new List<ExpectedResult>
            {
                new() { OutputPortName = "sorted", ExpectedValueOrRule = "samtools quickcheck" },
            },
            IntermediateFilePolicies = new List<IntermediateFilePolicyEntry>
            {
                new() { PathOrPattern = "/work/tmp/*", Policy = IntermediateFilePolicyKind.SensitiveTemp },
                new() { PathOrPattern = "/work/cache/*", Policy = IntermediateFilePolicyKind.Cache },
            },
            Parameters = new List<ParameterContract>
            {
                new()
                {
                    Name = "threads",
                    Type = ParameterType.Integer,
                    DefaultValue = "4",
                    AllowedRange = "1..64",
                    Required = true,
                    CliArgumentMapping = "-@",
                    MutuallyExclusiveGroup = string.Empty,
                },
                new()
                {
                    Name = "mode",
                    Type = ParameterType.Enum,
                    DefaultValue = "coord",
                    AllowedRange = "coord|name",
                    Required = false,
                    CliArgumentMapping = "--mode",
                    MutuallyExclusiveGroup = "order",
                },
            },
            EnforcedResources = new ResourceContract
            {
                CpuRequest = "500m",
                CpuLimit = "2",
                MemoryRequest = "1Gi",
                MemoryLimit = "2Gi",
                StorageRequest = "5Gi",
                StorageLimit = "10Gi",
                MaxExecutionTimeSeconds = 3600,
                Parallelism = 2,
            },
            ExecutionEnvironment = new ExecutionEnvironmentContract
            {
                SupportedPlatforms = new List<string> { "linux/amd64", "linux/arm64" },
                WritablePaths = new List<string> { "/work", "/tmp" },
                NetworkPolicy = "none",
                RequiresRoot = false,
                RequiredCapabilities = new List<string> { "CAP_CHOWN" },
            },
            ValidationRequirements = new ValidationRequirements
            {
                MinimumObservationLevel = ObservationLevel.Enhanced,
                RequiredCoverage = new Dictionary<string, bool> { ["sorted"] = true, ["log"] = false },
            },
        };

        private static Pb.RegisterToolFunctionRequest Map(ToolFunctionRecipe recipe, ToolFunctionRegistrationInputs? inputs = null)
        {
            Assert.True(
                ToolFunctionRegistrationRequestFactory.TryBuild(recipe, inputs ?? Inputs(), out var plan, out var error),
                error?.Message);
            Assert.Null(error);
            return plan!.Request;
        }

        private static ToolFunctionWireError MapFails(ToolFunctionRecipe recipe, ToolFunctionRegistrationInputs? inputs = null)
        {
            Assert.False(ToolFunctionRegistrationRequestFactory.TryBuild(recipe, inputs ?? Inputs(), out var plan, out var error));
            Assert.Null(plan);
            Assert.NotNull(error);
            Assert.Equal(1, error!.ExitCode);
            return error;
        }

        private static string RawSpec(string? a, string? script, bool indented = false)
        {
            Assert.True(ToolFunctionRawSpecFactory.TryBuild(a, script, indented, out var json, out var error), error?.Message);
            return json!;
        }

        // ── S3-04: raw_spec v1 ───────────────────────────────────────────────

        [Fact]
        public void S3_04_A_RawSpecHasExactlyFourKeysWithV1Values()
        {
            var json = RawSpec(DigestA, Script);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.Equal(
                new[] { "schema_version", "kind", "base_image_digest", "script" },
                root.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.Equal("nodevault.build.raw_spec.v1", root.GetProperty("schema_version").GetString());
            Assert.Equal(JsonValueKind.Number, root.GetProperty("kind").ValueKind);
            Assert.Equal("2", root.GetProperty("kind").GetRawText());
            Assert.Equal(DigestA, root.GetProperty("base_image_digest").GetString());
            Assert.Equal(Script, root.GetProperty("script").GetString());
            Assert.True(ToolFunctionRawSpecFactory.TryValidate(json, out var error), error?.Message);
        }

        [Fact]
        public void S3_04_B_FullRecipeThroughMaterializerAddsNoTypedMetadataToRawSpec()
        {
            var projectDir = Path.Join(_workDir, "project");
            Directory.CreateDirectory(projectDir);
            File.WriteAllText(Path.Join(projectDir, "sort.sh"), Script);
            var recipe = FullRecipe();

            Assert.True(ToolFunctionSourceMaterializer.TryMaterialize(
                recipe, Path.Join(projectDir, "recipe.json"), out var snapshot, out var sourceError), sourceError?.Message);
            var json = RawSpec(snapshot!.BaseImageDigest, snapshot.Script);

            using var doc = JsonDocument.Parse(json);
            Assert.Equal(4, doc.RootElement.EnumerateObject().Count());
            Assert.Equal(Script, doc.RootElement.GetProperty("script").GetString());

            // typed metadata·C1·ScriptPath 문자열은 raw_spec 어디에도 없다.
            foreach (var absent in new[]
            {
                recipe.FunctionId, recipe.Revision, recipe.DisplayLabel, recipe.DisplayCategory,
                DigestC1, "./sort.sh", "reads", "sorted", "threads", "500m", "stage1", "stage2",
            })
            {
                Assert.DoesNotContain(absent, json, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void S3_04_D_CompactAndIndentedPreviewParseToTheSameObject()
        {
            var compact = RawSpec(DigestA, Script);
            var pretty = RawSpec(DigestA, Script, indented: true);

            Assert.NotEqual(compact, pretty);
            Assert.Contains('\n', pretty);
            Assert.True(ToolFunctionRawSpecFactory.TryValidate(pretty, out _));
            using var a = JsonDocument.Parse(compact);
            using var b = JsonDocument.Parse(pretty);
            Assert.True(JsonElement.DeepEquals(a.RootElement, b.RootElement));
        }

        public static IEnumerable<object[]> InvalidRawSpecs() => new[]
        {
            new object[] { "kind-string", "{\"schema_version\":\"nodevault.build.raw_spec.v1\",\"kind\":\"2\",\"base_image_digest\":\"" + DigestA + "\",\"script\":\"echo\"}" },
            new object[] { "kind-float", "{\"schema_version\":\"nodevault.build.raw_spec.v1\",\"kind\":2.0,\"base_image_digest\":\"" + DigestA + "\",\"script\":\"echo\"}" },
            new object[] { "kind-exponent", "{\"schema_version\":\"nodevault.build.raw_spec.v1\",\"kind\":2e0,\"base_image_digest\":\"" + DigestA + "\",\"script\":\"echo\"}" },
            new object[] { "kind-1","{\"schema_version\":\"nodevault.build.raw_spec.v1\",\"kind\":1,\"base_image_digest\":\"" + DigestA + "\",\"script\":\"echo\"}" },
            new object[] { "schema-typo", "{\"schema_version\":\"nodevault.build.raw_spec.v2\",\"kind\":2,\"base_image_digest\":\"" + DigestA + "\",\"script\":\"echo\"}" },
            new object[] { "schema-case", "{\"schema_version\":\"NodeVault.build.raw_spec.v1\",\"kind\":2,\"base_image_digest\":\"" + DigestA + "\",\"script\":\"echo\"}" },
            new object[] { "extra-field", "{\"schema_version\":\"nodevault.build.raw_spec.v1\",\"kind\":2,\"base_image_digest\":\"" + DigestA + "\",\"script\":\"echo\",\"tool_name\":\"x\"}" },
            new object[] { "missing-script", "{\"schema_version\":\"nodevault.build.raw_spec.v1\",\"kind\":2,\"base_image_digest\":\"" + DigestA + "\"}" },
            new object[] { "blank-script", "{\"schema_version\":\"nodevault.build.raw_spec.v1\",\"kind\":2,\"base_image_digest\":\"" + DigestA + "\",\"script\":\"\"}" },
            new object[] { "whitespace-script", "{\"schema_version\":\"nodevault.build.raw_spec.v1\",\"kind\":2,\"base_image_digest\":\"" + DigestA + "\",\"script\":\" \\n\\t\"}" },
            new object[] { "bad-digest", "{\"schema_version\":\"nodevault.build.raw_spec.v1\",\"kind\":2,\"base_image_digest\":\"sha256:bad\",\"script\":\"echo\"}" },
            new object[] { "duplicate-key", "{\"schema_version\":\"nodevault.build.raw_spec.v1\",\"kind\":2,\"kind\":2,\"base_image_digest\":\"" + DigestA + "\",\"script\":\"echo\"}" },
            new object[] { "trailing-content", "{\"schema_version\":\"nodevault.build.raw_spec.v1\",\"kind\":2,\"base_image_digest\":\"" + DigestA + "\",\"script\":\"echo\"} {}" },
            new object[] { "stage-wrapper", "{\"stage1\":{\"kind\":2,\"base_image_digest\":\"" + DigestA + "\",\"script\":\"echo\"}}" },
            new object[] { "array", "[]" },
            new object[] { "empty", "" },
        };

        [Theory]
        [MemberData(nameof(InvalidRawSpecs))]
        public void S3_04_E_ValidatorRejectsNonV1Payloads(string variant, string json)
        {
            Assert.False(ToolFunctionRawSpecFactory.TryValidate(json, out var error), variant);
            Assert.Equal(ToolFunctionRawSpecFactory.InvalidRawSpecCode, error!.Code);
            Assert.Equal(1, error.ExitCode);
        }

        [Theory]
        [InlineData("sha256:bad", Script)]
        [InlineData(DigestC1, Script)]
        [InlineData(null, Script)]
        [InlineData(DigestA, "")]
        [InlineData(DigestA, " \n\t")]
        [InlineData(DigestA, null)]
        public void S3_04_E_SerializerBuildsNoPayloadForInvalidFields(string? a, string? script)
        {
            Assert.False(ToolFunctionRawSpecFactory.TryBuild(a, script, false, out var json, out var error));
            Assert.Null(json);
            Assert.Equal(1, error!.ExitCode);
        }

        [Fact]
        public void S3_02_C_UpperCaseBaseDigestIsKeptVerbatim()
        {
            var upper = "sha256:" + new string('A', 64);

            var json = RawSpec(upper, Script);

            using var doc = JsonDocument.Parse(json);
            Assert.Equal(upper, doc.RootElement.GetProperty("base_image_digest").GetString());
        }

        // ── S3-02: A/B/C1/C2 역할 분리 ───────────────────────────────────────

        [Fact]
        public void S3_02_A_FourDistinctDigestsLandInTheirOwnRoles()
        {
            var recipe = FullRecipe();
            Assert.True(ToolFunctionRegistrationRequestFactory.TryBuild(recipe, Inputs(), out var plan, out _));
            var raw = RawSpec(recipe.BaseToolImageDigest, Script);

            using var doc = JsonDocument.Parse(raw);
            Assert.Equal(DigestA, doc.RootElement.GetProperty("base_image_digest").GetString());
            Assert.Equal(DigestC1, plan!.Request.BaseToolSpecDigest);
            Assert.Equal(DigestB, plan.Request.ImageDigest);
            Assert.Equal(BasisC2, plan.SecondRequestBasis);

            // A와 C2는 typed 요청 어디에도 없다; C1/B는 raw_spec에 없다.
            var wire = JsonFormatter.Default.Format(plan.Request);
            Assert.DoesNotContain(DigestA, wire, StringComparison.Ordinal);
            Assert.DoesNotContain(BasisC2, wire, StringComparison.Ordinal);
            Assert.DoesNotContain(DigestC1, raw, StringComparison.Ordinal);
            Assert.DoesNotContain(DigestB, raw, StringComparison.Ordinal);
        }

        [Fact]
        public void S3_02_A_CanonicalizationVersionIsW2SetV1AndRequestIdIsPassedThrough()
        {
            var request = Map(FullRecipe());

            Assert.Equal("w2-set-v1", request.CanonicalizationVersion);
            Assert.Equal(RequestId, request.RequestId);
        }

        [Fact]
        public void S3_02_A_CompleteSameBuildReceiptIsObservedSameBuild()
        {
            var observation = new ToolFunctionBaseBuildObservation("http://127.0.0.1:50051", "req-base-1", "build-1", DigestC1, DigestA);

            var state = ToolFunctionBaseLinkEvidence.Classify(DigestC1, DigestA, observation);

            Assert.Equal(ToolFunctionBaseLinkState.ObservedSameBuild, state);
            Assert.Equal("OBSERVED_SAME_BUILD", ToolFunctionBaseLinkEvidence.Label(state));
        }

        public static IEnumerable<object?[]> ManualOrMismatchedLinks() => new[]
        {
            // variant, recipe A, observation (endpoint, request ID, build ID, C1, A); hasObservation=false → receipt 없음
            new object?[] { "no-receipt", DigestA, false, null, null, null, null, null },
            new object?[] { "c1-from-other-build", DigestA, true, "e", "r", "b", new string('f', 64), DigestA },
            new object?[] { "a-from-other-build", DigestA, true, "e", "r", "b", DigestC1, DigestB },
            new object?[] { "a-case-differs", "sha256:" + DigestA[7..].ToUpperInvariant(), true, "e", "r", "b", DigestC1, DigestA },
            new object?[] { "missing-endpoint", DigestA, true, null, "r", "b", DigestC1, DigestA },
            new object?[] { "missing-request-id", DigestA, true, "e", " ", "b", DigestC1, DigestA },
            new object?[] { "missing-build-id", DigestA, true, "e", "r", null, DigestC1, DigestA },
            new object?[] { "missing-c1", DigestA, true, "e", "r", "b", null, DigestA },
            new object?[] { "missing-a", DigestA, true, "e", "r", "b", DigestC1, string.Empty },
        };

        [Theory]
        [MemberData(nameof(ManualOrMismatchedLinks))]
        public void S3_02_A_ManualMixedOrIncompleteLinkIsManualUnverified(
            string variant,
            string a,
            bool hasObservation,
            string? endpoint,
            string? requestId,
            string? buildId,
            string? observedC1,
            string? observedA)
        {
            var observation = hasObservation
                ? new ToolFunctionBaseBuildObservation(endpoint, requestId, buildId, observedC1, observedA)
                : null;

            var state = ToolFunctionBaseLinkEvidence.Classify(DigestC1, a, observation);

            Assert.True(state == ToolFunctionBaseLinkState.ManualUnverified, variant);
            Assert.Equal("MANUAL_UNVERIFIED", ToolFunctionBaseLinkEvidence.Label(state));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void S3_02_D_MissingSecondImageDigestIsRejectedWithoutFallback(string? b)
        {
            var error = MapFails(FullRecipe(), Inputs(b: b));

            Assert.Equal(ToolFunctionRegistrationRequestFactory.MissingFunctionImageDigestCode, error.Code);
        }

        [Theory]
        [InlineData("sha256:bad")]
        [InlineData(DigestC1)]
        public void S3_02_D_MalformedSecondImageDigestIsRejected(string b)
        {
            var error = MapFails(FullRecipe(), Inputs(b: b));

            Assert.Equal(ToolFunctionRegistrationRequestFactory.InvalidFunctionImageDigestCode, error.Code);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void S3_02_E_ABWithoutC1IsRejectedAndPointsToToolSpecDigestOption(string? c1)
        {
            var error = MapFails(FullRecipe(), Inputs(c1: c1));

            Assert.Equal(ToolFunctionRegistrationRequestFactory.MissingToolSpecDigestCode, error.Code);
            Assert.Contains("--tool-spec-digest", error.Message, StringComparison.Ordinal);
        }

        public static IEnumerable<object[]> MalformedC1() => new[]
        {
            new object[] { "image-shaped", DigestA },
            new object[] { "image-shaped-c1-hex", "sha256:" + DigestC1 },
            new object[] { "63-chars", DigestC1[..63] },
            new object[] { "65-chars", DigestC1 + "c" },
            new object[] { "non-hex", "g" + DigestC1[1..] },
            new object[] { "upper-hex", DigestC1.ToUpperInvariant() },
            new object[] { "leading-space", " " + DigestC1[1..] },
            new object[] { "trailing-space", DigestC1[..63] + " " },
            new object[] { "surrounding-space", " " + DigestC1 + " " },
            new object[] { "trailing-lf", DigestC1 + "\n" },
            new object[] { "inner-space", DigestC1[..32] + " " + DigestC1[33..] },
            new object[] { "whitespace-only", "   " },
        };

        [Theory]
        [MemberData(nameof(MalformedC1))]
        public void S3_02_F_MalformedC1IsRejectedBeforeRegistration(string variant, string c1)
        {
            var error = MapFails(FullRecipe(), Inputs(c1: c1));

            Assert.True(ToolFunctionRegistrationRequestFactory.InvalidToolSpecDigestCode == error.Code, variant);
        }

        [Fact]
        public void S3_02_F_ValidBareLowerHexC1IsPassedVerbatim()
        {
            var c1 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

            var request = Map(FullRecipe(), Inputs(c1: c1));

            Assert.Equal(c1, request.BaseToolSpecDigest);
        }

        // ── S3-06: typed mapper ──────────────────────────────────────────────

        [Fact]
        public void S3_06_A_PersistedFullRecipeSurvivesSaveReloadValidateAndMapsEveryGroup()
        {
            var path = Path.Join(_workDir, "recipe.json");
            File.WriteAllText(path, JsonSerializer.Serialize(FullRecipe(), ToolFunctionRecipeCliIo.JsonOptions));
            Assert.True(ToolFunctionRecipeCliIo.TryLoad(path, TextWriter.Null, out var loaded));
            Assert.True(ToolFunctionRecipeValidationPipeline.Validate(loaded!).IsValid);

            var request = Map(loaded!);

            var command = request.Spec.Command;
            Assert.Equal("samtools", command.Executable);
            Assert.Equal(
                new[] { "sort", "-@", "{param.threads}", "-o", "out.bam", "--mode", "{param.mode}", "in.bam" },
                command.Arguments.ToArray());
            Assert.Equal("/work", command.WorkingDirectory);
            Assert.Equal(new[] { ("TMPDIR", "runtime.tmp"), ("LANG", "platform.locale") }, command.Environment.Select(e => (e.Name, e.Source)).ToArray());
            Assert.Equal(new[] { 0, 3 }, command.SuccessExitCodes.ToArray());
            Assert.Equal(60, command.TimeoutPolicy.SoftSeconds);
            Assert.Equal(120, command.TimeoutPolicy.HardSeconds);

            var input = Assert.Single(request.Spec.Inputs);
            Assert.Equal("reads", input.Name);
            Assert.Equal("bam", input.DataFormat);
            Assert.Equal(Pb.Cardinality.Multiple, input.Cardinality);
            Assert.False(input.Required);
            Assert.Equal("/work/in/{name}", input.PathPlacementRule);
            Assert.Equal(new[] { ".bai", ".csi" }, input.CompanionFiles.ToArray());
            Assert.Equal(string.Empty, input.PathOrGlob);
            Assert.Equal(string.Empty, input.CompletionCheck);

            Assert.Equal(new[] { "sorted", "log" }, request.Spec.Outputs.Select(o => o.Name).ToArray());
            var sorted = request.Spec.Outputs[0];
            Assert.Equal("bam", sorted.DataFormat);
            Assert.Equal(Pb.Cardinality.Single, sorted.Cardinality);
            Assert.Equal("/work/out.bam", sorted.PathOrGlob);
            Assert.Equal("exists", sorted.CompletionCheck);
            Assert.Equal(string.Empty, sorted.PathPlacementRule);
            Assert.Empty(sorted.CompanionFiles);
            Assert.False(sorted.Required);

            Assert.Equal(
                new[]
                {
                    ("threads", Pb.ParameterType.Integer, "4", "1..64", true, "-@", string.Empty),
                    ("mode", Pb.ParameterType.Enum, "coord", "coord|name", false, "--mode", "order"),
                },
                request.Spec.Parameters.Select(p => (p.Name, p.Type, p.DefaultValue, p.AllowedRange, p.Required, p.CliArgumentMapping, p.MutuallyExclusiveGroup)).ToArray());
            Assert.Equal(
                new[] { ("/work/tmp/*", Pb.IntermediateFilePolicyKind.SensitiveTemp), ("/work/cache/*", Pb.IntermediateFilePolicyKind.Cache) },
                request.Spec.IntermediateFilePolicies.Select(p => (p.PathOrPattern, p.Policy)).ToArray());

            var environment = request.Spec.ExecutionEnvironment;
            Assert.Equal(new[] { "/work", "/tmp" }, environment.WritablePaths.ToArray());
            Assert.Equal("none", environment.NetworkPolicy);
            Assert.False(environment.RequiresRoot);
            Assert.Equal(new[] { "CAP_CHOWN" }, environment.RequiredCapabilities.ToArray());

            var presentation = request.Presentation;
            Assert.Equal("Sort BAM", presentation.Label);
            Assert.Equal("Coordinate-sort a BAM file", presentation.Description);
            Assert.Equal("alignment", presentation.Category);
            Assert.Equal(new[] { "bam", "sort" }, presentation.Tags.ToArray());
            Assert.Equal(string.Empty, presentation.ShortSummary);
            Assert.Equal(string.Empty, presentation.Locale);
            var note = Assert.Single(presentation.OutputPortPresentations);
            Assert.Equal(("sorted", "coordinate-sorted"), (note.PortName, note.DownstreamCompatibilityNote));

            var validation = request.ValidationPolicy;
            Assert.Equal(
                new[] { ("fixtures/small.bam", "sha256:" + new string('d', 64)), (string.Empty, "sha256:" + new string('e', 64)) },
                validation.FixtureReferences.Select(f => (f.LocalPath, f.ContentDigest)).ToArray());
            var expected = Assert.Single(validation.ExpectedResults);
            Assert.Equal(("sorted", "samtools quickcheck"), (expected.OutputPortName, expected.ExpectedValueOrRule));
            Assert.Equal(Pb.ObservationLevel.Enhanced, validation.ValidationRequirements.MinimumObservationLevel);
            Assert.Equal(2, validation.ValidationRequirements.RequiredCoverage.Count);
            Assert.True(validation.ValidationRequirements.RequiredCoverage["sorted"]);
            Assert.False(validation.ValidationRequirements.RequiredCoverage["log"]);

            var hints = request.EnvironmentHints;
            Assert.Equal(new[] { "linux/amd64", "linux/arm64" }, hints.SupportedPlatforms.ToArray());
            var resources = hints.EnforcedResources;
            Assert.Equal(
                ("500m", "2", "1Gi", "2Gi", "5Gi", "10Gi", 3600, 2),
                (resources.CpuRequest, resources.CpuLimit, resources.MemoryRequest, resources.MemoryLimit,
                 resources.StorageRequest, resources.StorageLimit, resources.MaxExecutionTimeSeconds, resources.Parallelism));

            // 실행 필드만 spec(digest 입력)에 있다: 표시·검증·환경 hint 값은 spec에 없다.
            var specJson = JsonFormatter.Default.Format(request.Spec);
            foreach (var digestOut in new[] { "Sort BAM", "coordinate-sorted", "fixtures/small.bam", "samtools quickcheck", "linux/amd64", "500m" })
            {
                Assert.DoesNotContain(digestOut, specJson, StringComparison.Ordinal);
            }
        }

        public static IEnumerable<object[]> PortShapeVariants() => new[]
        {
            new object[] { "input-only", true, false },
            new object[] { "output-only", false, true },
            new object[] { "zero-port", false, false },
        };

        [Theory]
        [MemberData(nameof(PortShapeVariants))]
        public void S3_06_A_InputOnlyOutputOnlyZeroPortAndEmptyArgumentsStayValidAndMap(string variant, bool keepInputs, bool keepOutputs)
        {
            var recipe = FullRecipe();
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
            var path = Path.Join(_workDir, variant + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(recipe, ToolFunctionRecipeCliIo.JsonOptions));
            Assert.True(ToolFunctionRecipeCliIo.TryLoad(path, TextWriter.Null, out var loaded));
            Assert.True(ToolFunctionRecipeValidationPipeline.Validate(loaded!).IsValid, variant);

            var request = Map(loaded!);

            Assert.Equal(keepInputs ? 1 : 0, request.Spec.Inputs.Count);
            Assert.Equal(keepOutputs ? 2 : 0, request.Spec.Outputs.Count);
            Assert.Empty(request.Spec.Command.Arguments);
            Assert.Empty(request.Spec.Parameters);
            Assert.Equal(keepOutputs ? 1 : 0, request.Presentation.OutputPortPresentations.Count);
        }

        [Fact]
        public void S3_06_A_ExistingValidatorFailuresAreKept()
        {
            var missingExecutable = FullRecipe();
            missingExecutable.Command.Executable = string.Empty;
            var orphanExpected = FullRecipe();
            orphanExpected.ExpectedResults.Add(new ExpectedResult { OutputPortName = "nope", ExpectedValueOrRule = "x" });
            var duplicatePort = FullRecipe();
            duplicatePort.OutputPorts.Add(new PortContract { Name = "sorted", Direction = PortDirection.Output });

            Assert.Contains(ToolFunctionRecipeValidationPipeline.Validate(missingExecutable).Violations, v => v.RuleId == "L1-TFR-006");
            Assert.Contains(ToolFunctionRecipeValidationPipeline.Validate(orphanExpected).Violations, v => v.RuleId == "L1-TFR-007");
            Assert.Contains(ToolFunctionRecipeValidationPipeline.Validate(duplicatePort).Violations, v => v.RuleId == "L1-TFR-004");
        }

        // proto 쪽 기대값은 이름과 실제 proto 번호(nodevault.proto) 둘 다 확인한다.
        // local 0-based 번호를 그대로 cast하면 한 칸씩 어긋난다.
        [Theory]
        [InlineData("Single", "Single", 1)]
        [InlineData("Multiple", "Multiple", 2)]
        public void S3_06_B_CardinalityMapsByNameNotByNumber(string local, string protoName, int protoNumber)
        {
            var value = Enum.Parse<PortCardinality>(local);

            var mapped = ToolFunctionRegistrationRequestFactory.MapCardinality(value);

            Assert.Equal(Enum.Parse<Pb.Cardinality>(protoName), mapped);
            Assert.Equal(protoNumber, (int)mapped!.Value);
            Assert.NotEqual((int)value, protoNumber);
        }

        [Theory]
        [InlineData("String", "String", 1)]
        [InlineData("Integer", "Integer", 2)]
        [InlineData("Number", "Number", 3)]
        [InlineData("Boolean", "Boolean", 4)]
        [InlineData("Enum", "Enum", 5)]
        public void S3_06_B_ParameterTypeMapsByName(string local, string protoName, int protoNumber)
        {
            var mapped = ToolFunctionRegistrationRequestFactory.MapParameterType(Enum.Parse<ParameterType>(local));

            Assert.Equal(Enum.Parse<Pb.ParameterType>(protoName), mapped);
            Assert.Equal(protoNumber, (int)mapped!.Value);
        }

        [Theory]
        [InlineData("Ephemeral", "Ephemeral", 1)]
        [InlineData("Cache", "Cache", 2)]
        [InlineData("Checkpoint", "Checkpoint", 3)]
        [InlineData("SidecarOutput", "SidecarOutput", 4)]
        [InlineData("SensitiveTemp", "SensitiveTemp", 5)]
        public void S3_06_B_IntermediateFilePolicyKindMapsByName(string local, string protoName, int protoNumber)
        {
            var mapped = ToolFunctionRegistrationRequestFactory.MapIntermediateFilePolicyKind(Enum.Parse<IntermediateFilePolicyKind>(local));

            Assert.Equal(Enum.Parse<Pb.IntermediateFilePolicyKind>(protoName), mapped);
            Assert.Equal(protoNumber, (int)mapped!.Value);
        }

        [Theory]
        [InlineData("Basic", "Basic", 1)]
        [InlineData("Enhanced", "Enhanced", 2)]
        [InlineData("Full", "Full", 3)]
        public void S3_06_B_ObservationLevelMapsByName(string local, string protoName, int protoNumber)
        {
            var mapped = ToolFunctionRegistrationRequestFactory.MapObservationLevel(Enum.Parse<ObservationLevel>(local));

            Assert.Equal(Enum.Parse<Pb.ObservationLevel>(protoName), mapped);
            Assert.Equal(protoNumber, (int)mapped!.Value);
        }

        [Fact]
        public void S3_06_B_LocalEnumNamesAllHaveAProtoMapping()
        {
            // local enum에 값이 추가되면 표도 같이 고쳐야 한다 — 빠지면 여기서 실패한다.
            foreach (var value in Enum.GetValues<PortCardinality>())
            {
                Assert.NotNull(ToolFunctionRegistrationRequestFactory.MapCardinality(value));
            }

            foreach (var value in Enum.GetValues<ParameterType>())
            {
                Assert.NotNull(ToolFunctionRegistrationRequestFactory.MapParameterType(value));
            }

            foreach (var value in Enum.GetValues<IntermediateFilePolicyKind>())
            {
                Assert.NotNull(ToolFunctionRegistrationRequestFactory.MapIntermediateFilePolicyKind(value));
            }

            foreach (var value in Enum.GetValues<ObservationLevel>())
            {
                Assert.NotNull(ToolFunctionRegistrationRequestFactory.MapObservationLevel(value));
            }
        }

        [Fact]
        public void S3_06_C_UnsetOptionalsStayUnspecifiedWithoutInventedDefaults()
        {
            var recipe = FullRecipe();
            recipe.ValidationRequirements = new ValidationRequirements();
            recipe.Command.TimeoutPolicy = null;
            recipe.EnforcedResources.MaxExecutionTimeSeconds = null;
            recipe.EnforcedResources.Parallelism = null;

            var request = Map(recipe);

            Assert.Equal(Pb.ObservationLevel.Unspecified, request.ValidationPolicy.ValidationRequirements.MinimumObservationLevel);
            Assert.Empty(request.ValidationPolicy.ValidationRequirements.RequiredCoverage);
            Assert.Null(request.Spec.Command.TimeoutPolicy);
            Assert.Equal(0, request.EnvironmentHints.EnforcedResources.MaxExecutionTimeSeconds);
            Assert.Equal(0, request.EnvironmentHints.EnforcedResources.Parallelism);

            // proto3 JSON은 기본값을 생략한다: 미지정 level·timeout이 출력에 없다.
            var json = JsonFormatter.Default.Format(request);
            Assert.DoesNotContain("minimumObservationLevel", json, StringComparison.Ordinal);
            Assert.DoesNotContain("timeoutPolicy", json, StringComparison.Ordinal);
            Assert.DoesNotContain("OBSERVATION_LEVEL_BASIC", json, StringComparison.Ordinal);
        }

        [Fact]
        public void S3_06_C_PartialTimeoutKeepsTheUnsetHalfAtZero()
        {
            var recipe = FullRecipe();
            recipe.Command.TimeoutPolicy = new TimeoutPolicy { HardSeconds = 30 };

            var request = Map(recipe);

            Assert.Equal(0, request.Spec.Command.TimeoutPolicy.SoftSeconds);
            Assert.Equal(30, request.Spec.Command.TimeoutPolicy.HardSeconds);
        }

        [Fact]
        public void S3_06_D_OutputWithoutNoteHasNoPresentationEntry()
        {
            var recipe = FullRecipe();
            recipe.OutputPorts[0].DownstreamCompatibilityNote = string.Empty;

            var request = Map(recipe);

            Assert.Empty(request.Presentation.OutputPortPresentations);
        }

        [Fact]
        public void S3_06_D_NoteOnAnInputPortIsAnOrphan()
        {
            var recipe = FullRecipe();
            recipe.InputPorts[0].DownstreamCompatibilityNote = "not an output";

            var error = MapFails(recipe);

            Assert.Equal(ToolFunctionRegistrationRequestFactory.OrphanOutputPresentationCode, error.Code);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void S3_06_D_NoteOnADuplicatedOutputNameIsRejected(bool secondHasNote)
        {
            var recipe = FullRecipe();
            recipe.OutputPorts.Add(new PortContract
            {
                Name = "sorted",
                Direction = PortDirection.Output,
                DownstreamCompatibilityNote = secondHasNote ? "again" : string.Empty,
            });

            var error = MapFails(recipe);

            Assert.Equal(ToolFunctionRegistrationRequestFactory.DuplicateOutputPresentationCode, error.Code);
        }

        [Theory]
        [InlineData("input-cardinality", 99)]
        [InlineData("output-cardinality", -1)]
        [InlineData("parameter-type", 99)]
        [InlineData("intermediate-kind", 99)]
        [InlineData("observation-level", 99)]
        public void S3_06_E_UnknownLocalEnumValueIsRejectedNotDefaulted(string field, int value)
        {
            var recipe = FullRecipe();
            switch (field)
            {
                case "input-cardinality":
                    recipe.InputPorts[0].Cardinality = (PortCardinality)value;
                    break;
                case "output-cardinality":
                    recipe.OutputPorts[0].Cardinality = (PortCardinality)value;
                    break;
                case "parameter-type":
                    recipe.Parameters[0].Type = (ParameterType)value;
                    break;
                case "intermediate-kind":
                    recipe.IntermediateFilePolicies[0].Policy = (IntermediateFilePolicyKind)value;
                    break;
                default:
                    recipe.ValidationRequirements.MinimumObservationLevel = (ObservationLevel)value;
                    break;
            }

            var error = MapFails(recipe);

            Assert.True(ToolFunctionRegistrationRequestFactory.UnknownEnumValueCode == error.Code, field);
            Assert.Contains(value.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void S3_06_F_FunctionIdRevisionAndDisplayChangesDoNotTouchSpec()
        {
            var first = FullRecipe();
            var second = FullRecipe();
            second.FunctionId = "samtools.sort.v2";
            second.Revision = "r99";
            second.DisplayLabel = "Sort BAM (renamed)";
            second.DisplayTags.Add("renamed");

            var a = Map(first);
            var b = Map(second);

            Assert.Equal(a.Spec, b.Spec);
            Assert.NotEqual(a.Presentation, b.Presentation);

            // FunctionId/Revision은 요청 어디에도 들어가지 않는다(발명한 identity 필드 없음).
            var wire = JsonFormatter.Default.Format(b);
            Assert.DoesNotContain("samtools.sort.v2", wire, StringComparison.Ordinal);
            Assert.DoesNotContain("r99", wire, StringComparison.Ordinal);
            Assert.Equal(
                new[]
                {
                    "request_id", "base_tool_spec_digest", "image_digest", "spec", "presentation",
                    "validation_policy", "environment_hints", "canonicalization_version",
                },
                Pb.RegisterToolFunctionRequest.Descriptor.Fields.InFieldNumberOrder().Select(f => f.Name).ToArray());
        }

        [Fact]
        public void TypedMapperDoesNotMutateTheRecipe()
        {
            var recipe = FullRecipe();
            var before = JsonSerializer.Serialize(recipe, ToolFunctionRecipeCliIo.JsonOptions);

            Map(recipe);

            Assert.Equal(before, JsonSerializer.Serialize(recipe, ToolFunctionRecipeCliIo.JsonOptions));
        }
    }
}
