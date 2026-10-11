using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Grpc.Core;
using NodeKit.Authoring.ToolFunctionRecipes;
using NodeKit.Cli.Operations;
using NodeKit.Cli.Tests.Fakes;
using NodeKit.Grpc;
using Xunit;
using ProtoBuildEvent = Nodevault.V1.BuildEvent;
using ProtoBuildEventKind = Nodevault.V1.BuildEventKind;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// P07.build (v0.9.1 S3-05-A..E, S3-04-C, S3-09 submit caller): `function-recipe submit`의 두 번째 이미지
    /// BuildService client 경로. 각 case는 실제 GrpcToolSpecClient를 in-process fake BuildService에 붙여
    /// 실제 generated gRPC 요청·RPC 순서·exit·출력·durable receipt를 본다. fake는 고정 응답만 돌려주므로
    /// 실제 NodeVault function-image build·Harbor·등록의 증거가 아니다. typed 등록 RPC는 이 경로에 없다
    /// (BuildService에는 RegisterToolFunction이 없다) — 등록0은 "빌드 응답 뒤 다른 호출 없음"으로 본다.
    /// client 경로 case는 상류 게이트를 테스트에서만 열어서 돈다. 실제 CLI 기본값은 닫힘이며(현재 NodeVault는
    /// kind 2를 거부) 그 동작은 UpstreamGateClosed_* case가 본다.
    /// </summary>
    public class ToolFunctionSecondImageBuildP07Tests : IDisposable
    {
        private const string FixedRequestId = "77777777-7777-7777-7777-777777777777";
        private const string FixtureBuildId = "build-fixture-p07";

        // 역할이 서로 다른 값: A=기반 이미지, C1=기반 ToolSpec, C2=이번 요청 ToolSpec, B=두 번째 이미지.
        private const string DigestA = "sha256:a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1";
        private const string DigestC1 = "c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1";
        private const string DigestC2 = "c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2";
        private const string DigestB = "sha256:b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2";
        private const string LegacyDigest = "sha256:9999999999999999999999999999999999999999999999999999999999999999";
        private const string ImageRefB = "harbor.local/nodevault/fn/samtools.sort:r7";
        private const string Script = "#!/bin/sh\nsamtools sort \"$@\"\n";

        private readonly string _workDir = Path.Join(Path.GetTempPath(), "nodekit-p07-build-" + Guid.NewGuid());

        public ToolFunctionSecondImageBuildP07Tests() => Directory.CreateDirectory(_workDir);

        public void Dispose() => Directory.Delete(_workDir, recursive: true);

        public static IEnumerable<object[]> PortShapeVariants() => new[]
        {
            new object[] { "input-only", true, false },
            new object[] { "output-only", false, true },
            new object[] { "zero-port", false, false },
        };

        // S3-05-A + S3-04-C: Resolve→Submit→Watch 각 1회, exact 4키 raw_spec, C2/B 분리 보존.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_SucceededWithFullB_Exit0_OrderedOnce_ExactRawSpec_C2AndBKeptSeparately(bool jsonl)
        {
            using var server = NewServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
                SucceededEvent(DigestB),
            };
            var recipePath = Save(ReadyRecipe());
            var recipeBefore = File.ReadAllText(recipePath);

            var (exit, stdout, stderr) = jsonl
                ? Submit(server, recipePath, "--format", "jsonl")
                : Submit(server, recipePath);

            Assert.True(exit == 0, stderr);
            Assert.Equal(new[] { "Resolve", "Submit", "Watch" }, server.Fake.CallOrder);
            Assert.Empty(server.Fake.CancelledBuildIds);

            var resolve = Assert.Single(server.Fake.ResolveRequests);
            Assert.Equal("samtools.sort", resolve.ToolName);
            Assert.Equal("r7", resolve.Version);
            Assert.Equal(ExpectedRawSpec(Script), resolve.RawSpec);
            AssertExactFourKeyRawSpec(resolve.RawSpec, Script);
            Assert.Equal(DigestC2, Assert.Single(server.Fake.SubmitRequests).ToolSpecDigest);
            Assert.Equal(FixedRequestId, server.Fake.SubmitRequests[0].RequestId);

            if (jsonl)
            {
                var completed = Completed(stdout);
                Assert.Equal("Succeeded", completed.GetProperty("status").GetString());
                Assert.Equal(DigestB, completed.GetProperty("image_digest").GetString());
                Assert.Equal(DigestC2, completed.GetProperty("tool_spec_digest").GetString());
                Assert.Equal(FixtureBuildId, completed.GetProperty("build_id").GetString());
                Assert.False(completed.TryGetProperty("error_code", out _));
            }
            else
            {
                Assert.Contains($"두 번째 이미지(B) digest: {ImageRefB}@{DigestB}", stdout);
                Assert.Contains($"이번 요청 ToolSpec digest(C2): {DigestC2}", stdout);
                Assert.Contains("typed 등록은 하지 않았습니다", stdout);
                Assert.DoesNotContain("Approved로", stdout);
                Assert.Equal(string.Empty, stderr);
            }

            var receipt = ReadReceipt(DefaultReceiptPath());
            Assert.Equal(OperationReceipt.ToolFunctionImageBuildKind, receipt.OperationKind);
            Assert.Equal(OperationPhase.Terminal, receipt.Phase);
            Assert.Equal(FixtureBuildId, receipt.BuildId);
            Assert.Equal(OperationObservation.SucceededOutcome, receipt.LastObservation!.Outcome);
            Assert.Equal(DigestB, receipt.LastObservation.ImageDigest);
            Assert.Equal(ExpectedRawSpec(Script), receipt.Envelope.RawSpec);
            Assert.Equal("samtools.sort", receipt.Envelope.ToolName);
            Assert.Equal("r7", receipt.Envelope.Version);
            Assert.NotNull(receipt.ResolvedSnapshotSha256);
            Assert.Contains(DigestC2, StoreText());

            // 기반 C1/A는 B/C2 자리에 쓰이지 않고, Recipe 파일도 바뀌지 않는다.
            Assert.NotEqual(DigestA, receipt.LastObservation.ImageDigest);
            Assert.DoesNotContain(DigestC1, StoreText());
            Assert.Equal(recipeBefore, File.ReadAllText(recipePath));
        }

        // S3-05-B: ResolveToolSpec InvalidArgument → Submit/Watch 0, exit 1, 진단.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void B_ResolveInvalidArgument_Exit1_SubmitWatchZero_Diagnostic(bool jsonl)
        {
            using var server = NewServer();
            server.Fake.OnResolveToolSpec = _ => throw new RpcException(new Status(StatusCode.InvalidArgument, "raw_spec rejected: fixture"));

            var (exit, stdout, stderr) = jsonl
                ? Submit(server, Save(ReadyRecipe()), "--format", "jsonl")
                : Submit(server, Save(ReadyRecipe()));

            Assert.Equal(1, exit);
            Assert.Equal(new[] { "Resolve" }, server.Fake.CallOrder);
            Assert.Empty(server.Fake.SubmitRequests);
            Assert.Empty(server.Fake.WatchRequests);
            if (jsonl)
            {
                var completed = Completed(stdout);
                Assert.Equal("Failed", completed.GetProperty("status").GetString());
                Assert.Equal("PRE_WATCH_FAILED", completed.GetProperty("error_code").GetString());
                Assert.Contains("raw_spec rejected", completed.GetProperty("message").GetString());
                Assert.False(completed.TryGetProperty("image_digest", out _));
            }
            else
            {
                Assert.Contains("raw_spec rejected", stderr);
                Assert.DoesNotContain("두 번째 이미지(B)", stdout);
            }

            Assert.Null(ReadReceipt(DefaultReceiptPath()).BuildId);
        }

        // S3-05-C: Watch terminal Failed 또는 Interrupted → exit 1, 성공/승인 표시 없음, 등록 0.
        [Theory]
        [InlineData("Failed", false)]
        [InlineData("Interrupted", false)]
        [InlineData("Failed", true)]
        [InlineData("Interrupted", true)]
        public void C_WatchFailedOrInterrupted_Exit1_NoSuccessOrApproval(string status, bool jsonl)
        {
            using var server = NewServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
                new() { Kind = ProtoBuildEventKind.Log, Status = status, BuildId = FixtureBuildId, Message = "fixture " + status },
            };

            var (exit, stdout, stderr) = jsonl
                ? Submit(server, Save(ReadyRecipe()), "--format", "jsonl")
                : Submit(server, Save(ReadyRecipe()));

            Assert.Equal(1, exit);
            Assert.Equal(new[] { "Resolve", "Submit", "Watch" }, server.Fake.CallOrder);
            Assert.DoesNotContain("두 번째 이미지(B)", stdout);
            Assert.DoesNotContain("Approved", stdout + stderr);
            if (jsonl)
            {
                var completed = Completed(stdout);
                Assert.Equal("Failed", completed.GetProperty("status").GetString());
                Assert.Equal("BUILD_FAILED", completed.GetProperty("error_code").GetString());
                Assert.False(completed.TryGetProperty("image_digest", out _));
            }
            else
            {
                Assert.Contains("빌드 실패", stderr);
            }

            Assert.Equal(OperationObservation.FailedOutcome, ReadReceipt(DefaultReceiptPath()).LastObservation!.Outcome);
        }

        // S3-05-D: terminal 없이 Watch 종료 → exit 0 금지, unknown/uncertain.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void D_WatchEndsWithoutTerminal_NotSuccess_UnknownUncertain(bool jsonl)
        {
            using var server = NewServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
            };

            var (exit, stdout, stderr) = jsonl
                ? Submit(server, Save(ReadyRecipe()), "--format", "jsonl")
                : Submit(server, Save(ReadyRecipe()));

            Assert.Equal(1, exit);
            if (jsonl)
            {
                var completed = Completed(stdout);
                Assert.Equal("STREAM_ENDED_WITHOUT_RESULT", completed.GetProperty("error_code").GetString());
                Assert.Equal("uncertain", completed.GetProperty("recovery").GetString());
            }
            else
            {
                Assert.Contains("빌드 결과를 확인하지 못한 채", stderr);
                Assert.DoesNotContain("두 번째 이미지(B)", stdout);
            }

            var receipt = ReadReceipt(DefaultReceiptPath());
            Assert.Equal(OperationPhase.Acknowledged, receipt.Phase);
            Assert.Null(receipt.LastObservation?.Outcome);
        }

        // S3-05-E: Succeeded지만 full B 없음(없음/잘림/태그) → 등록 가능한 결과 아님. A·legacy 로그 digest로 대신하지 않음.
        [Theory]
        [InlineData("", false)]
        [InlineData("sha256:b2b2", false)]
        [InlineData("latest", false)]
        [InlineData("", true)]
        [InlineData("sha256:b2b2", true)]
        public void E_SucceededWithoutFullB_Exit1_NotRegistrable_NoSubstituteDigest(string watchDigest, bool jsonl)
        {
            using var server = NewServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.DigestAcquired, BuildId = FixtureBuildId, Digest = LegacyDigest },
                SucceededEvent(watchDigest),
            };

            var (exit, stdout, stderr) = jsonl
                ? Submit(server, Save(ReadyRecipe()), "--format", "jsonl")
                : Submit(server, Save(ReadyRecipe()));

            Assert.Equal(1, exit);
            if (jsonl)
            {
                var completed = Completed(stdout);
                Assert.Equal("Failed", completed.GetProperty("status").GetString());
                Assert.Equal(SubmitCommand.SecondImageDigestMissingCode, completed.GetProperty("error_code").GetString());
                Assert.Equal("uncertain", completed.GetProperty("recovery").GetString());
                Assert.Equal("succeeded", completed.GetProperty("remote_build_state").GetString());
                Assert.False(completed.TryGetProperty("image_digest", out _));
            }
            else
            {
                Assert.Contains(SubmitCommand.SecondImageDigestMissingCode, stderr);
                Assert.DoesNotContain("두 번째 이미지(B) digest", stdout);
                Assert.DoesNotContain("typed 등록은 하지 않았습니다", stdout);
            }

            // 진행 중 legacy 로그 digest는 보일 수 있지만 결과 자리에는 A·legacy 어느 것도 쓰이지 않는다.
            Assert.DoesNotContain($"(B) digest: {LegacyDigest}", stdout);
            Assert.DoesNotContain($"(B) digest: {DigestA}", stdout);
            Assert.DoesNotContain($"\"image_digest\":\"{LegacyDigest}\"", stdout);
            Assert.DoesNotContain($"\"image_digest\":\"{DigestA}\"", stdout);
        }

        // S3-09-A/B/D at the submit caller: stale Ready / invalid A / blank script / Draft / Approved → RPC 0.
        [Theory]
        [InlineData("raw-shell-executable")]
        [InlineData("invalid-base-digest")]
        [InlineData("blank-script")]
        [InlineData("draft")]
        [InlineData("approved")]
        public void S3_09_RejectedCurrentRecipe_Exit1_RpcZero_NoReceipt(string variant)
        {
            using var server = NewServer();
            var recipe = ReadyRecipe();
            var script = Script;
            switch (variant)
            {
                case "raw-shell-executable":
                    recipe.Command.Executable = "samtools ; bad";
                    break;
                case "invalid-base-digest":
                    recipe.BaseToolImageDigest = "sha256:1234";
                    break;
                case "blank-script":
                    script = " \n\t\n";
                    break;
                case "draft":
                    recipe.State = ToolFunctionRecipeState.Draft;
                    break;
                case "approved":
                    recipe.State = ToolFunctionRecipeState.Approved;
                    break;
            }

            var recipePath = Save(recipe, script);
            var before = File.ReadAllText(recipePath);

            var (exit, stdout, stderr) = Submit(server, recipePath);

            Assert.True(exit == 1, $"{variant}: exit {exit}, stderr {stderr}");
            Assert.Empty(server.Fake.CallOrder);
            Assert.False(Directory.Exists(Path.Join(_workDir, ".nodekit")), variant);
            Assert.DoesNotContain("두 번째 이미지(B)", stdout);
            Assert.NotEqual(string.Empty, stderr);
            Assert.Equal(before, File.ReadAllText(recipePath));
        }

        // 이 제출의 B는 typed 등록 입력이 되므로 typed 전제(persisted env null)도 빌드 요청 전에 거부한다.
        [Fact]
        public void TypedPrerequisiteFailure_NullEnvironmentName_Exit1_RpcZero()
        {
            using var server = NewServer();
            var recipe = ReadyRecipe();
            recipe.Command.Environment.Add(new EnvironmentEntry { Name = "__NULL__", Source = "runtime.tmp" });
            var recipePath = Save(recipe);
            File.WriteAllText(recipePath, File.ReadAllText(recipePath).Replace("\"__NULL__\"", "null", StringComparison.Ordinal));

            var (exit, _, stderr) = Submit(server, recipePath);

            Assert.Equal(1, exit);
            Assert.Empty(server.Fake.CallOrder);
            Assert.Contains(ToolFunctionRegistrationRequestFactory.NullEnvironmentFieldCode, stderr);
        }

        [Fact]
        public void S3_09_RejectedCurrentRecipe_Jsonl_SinglePreflightFailedRecord()
        {
            using var server = NewServer();
            var recipe = ReadyRecipe();
            recipe.State = ToolFunctionRecipeState.Draft;

            var (exit, stdout, stderr) = Submit(server, Save(recipe), "--format", "jsonl");

            Assert.Equal(1, exit);
            Assert.Empty(server.Fake.CallOrder);
            var completed = Completed(stdout);
            Assert.Equal(ToolFunctionRecipeSubmitCommand.PreflightFailedCode, completed.GetProperty("error_code").GetString());
            Assert.Equal("terminal", completed.GetProperty("recovery").GetString());
            Assert.Contains("RECIPE_NOT_READY", stderr);
        }

        // S3-09-C: #103 port-shape 변형도 현재 valid Ready 값으로 새 요청 basis를 만든다.
        [Theory]
        [MemberData(nameof(PortShapeVariants))]
        public void S3_09_C_ValidPortShapeVariants_SendFreshRawSpec(string variant, bool keepInput, bool keepOutput)
        {
            using var server = NewServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent> { SucceededEvent(DigestB) };
            var recipe = ReadyRecipe();
            if (!keepInput)
            {
                recipe.InputPorts.Clear();
            }

            if (!keepOutput)
            {
                recipe.OutputPorts.Clear();
                recipe.ExpectedResults.Clear();
            }

            recipe.Command.Arguments.Clear();
            recipe.Parameters.Clear();

            var (exit, _, stderr) = Submit(server, Save(recipe));

            Assert.True(exit == 0, $"{variant}: {stderr}");
            Assert.Equal(ExpectedRawSpec(Script), Assert.Single(server.Fake.ResolveRequests).RawSpec);
        }

        // S3-09-C: 이미 보낸 operation의 저장 envelope는 원본 script를 나중에 고쳐도 바뀌지 않는다.
        [Fact]
        public void S3_09_C_StoredEnvelopeAndSourceSnapshot_UnaffectedByLaterScriptEdit()
        {
            using var server = NewServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent> { SucceededEvent(DigestB) };
            var recipePath = Save(ReadyRecipe());

            var (exit, _, stderr) = Submit(server, recipePath);
            Assert.True(exit == 0, stderr);
            var before = ReadReceipt(DefaultReceiptPath());

            File.WriteAllText(Path.Join(_workDir, "sort.sh"), "#!/bin/sh\necho changed\n");

            var after = ReadReceipt(DefaultReceiptPath());
            Assert.Equal(before.Envelope, after.Envelope);
            Assert.Equal(before.EnvelopeSha256, after.EnvelopeSha256);
            Assert.Equal(before.SourceSnapshotSha256, after.SourceSnapshotSha256);
            Assert.Equal(ExpectedRawSpec(Script), after.Envelope.RawSpec);

            // source snapshot은 Recipe 파일과 전송한 script를 각자의 논리 경로로 고정한다.
            Assert.Contains("\"logical_path\": \"./sort.sh\"", StoreText().Replace("\":\"", "\": \"", StringComparison.Ordinal));
            Assert.Contains("\"logical_path\": \"recipe.json\"", StoreText().Replace("\":\"", "\": \"", StringComparison.Ordinal));
        }

        // receipt watch on a second-image receipt applies the same full-B rule (S3-05-E) and keeps B when present.
        [Theory]
        [InlineData("", 1)]
        [InlineData(DigestB, 0)]
        public void ReceiptWatch_SecondImageReceipt_RequiresFullB(string watchDigest, int expectedExit)
        {
            string receiptPath;
            using (var first = NewServer())
            {
                first.Fake.WatchEvents = new List<ProtoBuildEvent>
                {
                    new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
                };
                var (submitExit, _, _) = Submit(first, Save(ReadyRecipe()));
                Assert.Equal(1, submitExit);
                receiptPath = DefaultReceiptPath();
            }

            using var server = NewServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent> { SucceededEvent(watchDigest) };
            using var client = new GrpcToolSpecClient(server.Channel);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exit = ReceiptCommand.Run(
                new[] { "receipt", "watch", receiptPath, "--format", "jsonl" }, stdout, stderr, client, TestContext.Current.CancellationToken);

            Assert.Equal(expectedExit, exit);
            Assert.Equal(new[] { "Watch" }, server.Fake.CallOrder);
            var completed = Completed(stdout.ToString());
            if (expectedExit == 0)
            {
                Assert.Equal(DigestB, completed.GetProperty("image_digest").GetString());
            }
            else
            {
                Assert.Equal(SubmitCommand.SecondImageDigestMissingCode, completed.GetProperty("error_code").GetString());
                Assert.False(completed.TryGetProperty("image_digest", out _));
            }

            Assert.Equal(OperationReceipt.ToolFunctionImageBuildKind, ReadReceipt(receiptPath).OperationKind);
        }

        [Fact]
        public void ReadyRecipe_WithoutUrlOrInjectedClient_Exit2_AfterLocalChecks()
        {
            var recipePath = Save(ReadyRecipe());
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exit = ToolFunctionRecipeSubmitCommand.Run(
                new[] { "function-recipe", "submit", recipePath, "--url", " " }, stdout, stderr, toolFunctionBuildGateOpen: true);

            Assert.Equal(2, exit);
            Assert.Contains("NodeVault 주소가 필요합니다", stderr.ToString());
            Assert.False(Directory.Exists(Path.Join(_workDir, ".nodekit")));
        }

        // 상류 게이트(r4242704027): 현재 NodeVault는 kind 2를 거부하므로 기본 게이트에서는 valid Ready도
        // 주소 확인·receipt·RPC 없이 TOOLFUNCTION_BUILD_GATE_CLOSED / exit 1로 끝난다. 주입 client가 있어도 같다.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void UpstreamGateClosed_ValidReady_Exit1_RpcZero_NoReceipt(bool jsonl)
        {
            using var server = NewServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent> { SucceededEvent(DigestB) };
            var recipePath = Save(ReadyRecipe());
            var before = File.ReadAllText(recipePath);
            using var client = new GrpcToolSpecClient(server.Channel);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var args = jsonl
                ? new[] { "function-recipe", "submit", recipePath, "--format", "jsonl" }
                : new[] { "function-recipe", "submit", recipePath };

            var exit = ToolFunctionRecipeSubmitCommand.Run(args, stdout, stderr, client, () => FixedRequestId);

            Assert.Equal(1, exit);
            Assert.Empty(server.Fake.CallOrder);
            Assert.False(Directory.Exists(Path.Join(_workDir, ".nodekit")));
            Assert.DoesNotContain("두 번째 이미지(B)", stdout.ToString());
            Assert.Equal(before, File.ReadAllText(recipePath));
            if (jsonl)
            {
                var completed = Completed(stdout.ToString());
                Assert.Equal("Failed", completed.GetProperty("status").GetString());
                Assert.Equal(ToolFunctionRecipeSubmitCommand.UpstreamGateClosedCode, completed.GetProperty("error_code").GetString());
                Assert.Equal("terminal", completed.GetProperty("recovery").GetString());
                Assert.False(completed.TryGetProperty("image_digest", out _));
            }
            else
            {
                Assert.Contains(ToolFunctionRecipeSubmitCommand.UpstreamGateClosedCode, stderr.ToString());
                Assert.Contains("kind 2", stderr.ToString());
            }
        }

        // 실제 CLI 진입점: 주소가 있어도(연결 불가 주소) 게이트에서 끝나므로 연결 시도·receipt가 없다.
        [Fact]
        public void UpstreamGateClosed_CliEntry_WithUrl_Exit1_BeforeUrlAndReceipt()
        {
            var recipePath = Save(ReadyRecipe());
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exit = CliApp.Run(
                new[] { "function-recipe", "submit", recipePath, "--url", "http://127.0.0.1:9", "--connect-timeout", "1" },
                new StringReader(string.Empty),
                stdout,
                stderr);

            Assert.Equal(1, exit);
            Assert.Contains(ToolFunctionRecipeSubmitCommand.UpstreamGateClosedCode, stderr.ToString());
            Assert.False(Directory.Exists(Path.Join(_workDir, ".nodekit")));
        }

        // 로컬 검사 실패는 게이트보다 먼저 같은 진단으로 끝난다(Draft → RECIPE_NOT_READY, 게이트 문구 없음).
        [Fact]
        public void UpstreamGateClosed_DraftStillGetsPreflightDiagnosticFirst()
        {
            using var server = NewServer();
            var recipe = ReadyRecipe();
            recipe.State = ToolFunctionRecipeState.Draft;
            var recipePath = Save(recipe);
            using var client = new GrpcToolSpecClient(server.Channel);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exit = ToolFunctionRecipeSubmitCommand.Run(new[] { "function-recipe", "submit", recipePath }, stdout, stderr, client, () => FixedRequestId);

            Assert.Equal(1, exit);
            Assert.Empty(server.Fake.CallOrder);
            Assert.Contains("RECIPE_NOT_READY", stderr.ToString());
            Assert.DoesNotContain(ToolFunctionRecipeSubmitCommand.UpstreamGateClosedCode, stderr.ToString());
        }

        /// <summary>P06 fixture와 같은 값의 현재 valid Ready Recipe.</summary>
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

        private static ProtoBuildEvent SucceededEvent(string imageDigest) => new()
        {
            Kind = ProtoBuildEventKind.Log,
            Status = "Succeeded",
            BuildId = FixtureBuildId,
            ImageRef = ImageRefB,
            ImageDigest = imageDigest,
            IntegrityHealth = "Healthy",
        };

        private static GrpcTestServer NewServer()
        {
            var server = new GrpcTestServer();
            server.Fake.OnResolveToolSpec = request => new Nodevault.V1.ResolvedToolSpecResponse
            {
                ToolSpecDigest = DigestC2,
                ToolName = request.ToolName,
                Version = request.Version,
            };
            server.Fake.OnSubmitToolBuild = _ => new Nodevault.V1.SubmitToolBuildResponse { BuildId = FixtureBuildId, Status = "Requested" };
            return server;
        }

        private string Save(ToolFunctionRecipe recipe, string script = Script)
        {
            File.WriteAllText(Path.Join(_workDir, "sort.sh"), script);
            var path = Path.Join(_workDir, "recipe.json");
            File.WriteAllText(path, JsonSerializer.Serialize(recipe, ToolFunctionRecipeCliIo.JsonOptions));
            return path;
        }

        private static (int Exit, string Stdout, string Stderr) Submit(GrpcTestServer server, string recipePath, params string[] extraArgs)
        {
            using var client = new GrpcToolSpecClient(server.Channel);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var args = new[] { "function-recipe", "submit", recipePath }.Concat(extraArgs).ToArray();
            var exit = ToolFunctionRecipeSubmitCommand.Run(args, stdout, stderr, client, () => FixedRequestId, toolFunctionBuildGateOpen: true);
            return (exit, stdout.ToString(), stderr.ToString());
        }

        private static string ExpectedRawSpec(string script)
        {
            Assert.True(ToolFunctionRawSpecFactory.TryBuild(DigestA, script, indented: false, out var raw, out _));
            return raw!;
        }

        private static void AssertExactFourKeyRawSpec(string rawSpec, string script)
        {
            Assert.True(ToolFunctionRawSpecFactory.TryValidate(rawSpec, out var error), error?.Message);
            using var document = JsonDocument.Parse(rawSpec);
            var root = document.RootElement;
            Assert.Equal(
                new[] { "schema_version", "kind", "base_image_digest", "script" },
                root.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.Equal(ToolFunctionRawSpecFactory.SchemaVersion, root.GetProperty("schema_version").GetString());
            Assert.Equal(JsonValueKind.Number, root.GetProperty("kind").ValueKind);
            Assert.Equal(2, root.GetProperty("kind").GetInt32());
            Assert.Equal(DigestA, root.GetProperty("base_image_digest").GetString());
            Assert.Equal(script, root.GetProperty("script").GetString());
        }

        private static OperationReceipt ReadReceipt(string receiptPath)
        {
            var store = LocalOperationStore.ForReceipt(receiptPath);
            Assert.Null(store.TryOpen(receiptPath, out var handle));
            using (handle)
            {
                return handle!.Receipt;
            }
        }

        private string DefaultReceiptPath() => Path.Join(_workDir, ".nodekit", "receipts", FixedRequestId + ".json");

        // 로컬 기록 root 아래 모든 파일 내용(receipt·snapshot) — 특정 값이 어디에도 저장되지 않았는지 볼 때 쓴다.
        private string StoreText() =>
            string.Join(
                "\n",
                Directory.EnumerateFiles(Path.Join(_workDir, ".nodekit"), "*", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith(".lock", StringComparison.Ordinal))
                    .Select(File.ReadAllText));

        private static JsonElement Completed(string stdout) =>
            Assert.Single(
                stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line =>
                    {
                        using var document = JsonDocument.Parse(line);
                        return document.RootElement.Clone();
                    }),
                r => r.GetProperty("type").GetString() == "completed");
    }
}
