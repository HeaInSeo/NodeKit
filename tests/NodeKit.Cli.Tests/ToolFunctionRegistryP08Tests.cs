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
using Pb = Nodevault.V1;
using ProtoBuildEvent = Nodevault.V1.BuildEvent;
using ProtoBuildEventKind = Nodevault.V1.BuildEventKind;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// P08.registry (v0.9.1 S3-07-A..G): `function-recipe submit`이 full B 뒤 보내는 typed RegisterToolFunction과
    /// `function-recipe register-replay`. 실제 GrpcToolSpecClient/GrpcToolFunctionRegistryClient를 in-process fake
    /// BuildService/ToolRegistryService에 붙여 실제 generated 요청 bytes·RPC 순서·exit·출력·durable receipt를 본다.
    /// fake는 고정 응답만 돌려주므로 실제 NodeVault 등록·서버 멱등성의 증거가 아니다. 등록 timeout/Ctrl-C(S3-08)는
    /// P08.timeout 범위라 여기 없다.
    /// </summary>
    public class ToolFunctionRegistryP08Tests : IDisposable
    {
        private const string BuildRequestId = "77777777-7777-7777-7777-777777777777";
        private const string RegistrationId = "88888888-8888-8888-8888-888888888888";
        private const string RegistrationId2 = "99999999-9999-9999-9999-999999999999";
        private const string FixtureBuildId = "build-fixture-p08";

        private const string DigestA = "sha256:a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1";
        private const string DigestC1 = "c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1";
        private const string DigestC2 = "c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2";
        private const string DigestB = "sha256:b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2";
        private const string DigestB2 = "sha256:b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3";

        // 서버가 정한 값 그대로 써야 한다 — 일부러 digest처럼 보이지 않는 값을 쓴다(client 계산·재형식 0).
        private const string ServerF = "fdigest-from-server-0001";
        private const string ServerH = "cas-from-server-0001";
        private const string ServerP = "prev-0001";
        private const string Version = "w2-set-v1";
        private const string Script = "#!/bin/sh\nsamtools sort \"$@\"\n";

        private readonly string _workDir = Path.Join(Path.GetTempPath(), "nodekit-p08-registry-" + Guid.NewGuid());

        public ToolFunctionRegistryP08Tests() => Directory.CreateDirectory(_workDir);

        public void Dispose()
        {
            foreach (var dir in Directory.EnumerateDirectories(_workDir, "*", SearchOption.AllDirectories))
            {
                SetMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            Directory.Delete(_workDir, recursive: true);
        }

        private static void SetMode(string path, UnixFileMode mode)
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, mode);
            }
        }

        // S3-07-A/B: 빌드 3 RPC 뒤 등록 1회. 요청은 w2-set-v1·C1·B·typed groups이고, RPC 시점에 같은 bytes와
        // prepared receipt가 이미 디스크에 있다. 응답 F/H/P/version은 그대로 출력·저장된다.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_B_RegisteredAfterBuild_DurableBeforeRpc_ServerValuesVerbatim(bool jsonl)
        {
            using var server = NewServer();
            server.Registry.OnRegisterToolFunction = _ => Response(ServerF, ServerH);
            RegistrationReceipt? atRpc = null;
            byte[]? snapshotAtRpc = null;
            string[]? buildCallsAtRpc = null;
            server.Registry.OnReceived = request =>
            {
                buildCallsAtRpc = server.Fake.CallOrder.ToArray();
                atRpc = ReadRawReceipt(RegistrationPath());
                snapshotAtRpc = File.ReadAllBytes(SnapshotPath(atRpc!.RequestSha256));
            };

            var (exit, stdout, stderr) = jsonl ? Submit(server, Save(ReadyRecipe()), "--format", "jsonl") : Submit(server, Save(ReadyRecipe()));

            Assert.True(exit == 0, stderr);
            Assert.Equal(new[] { "Resolve", "Submit", "Watch" }, buildCallsAtRpc);
            var request = Assert.Single(server.Registry.RegisterRequests);
            Assert.Equal(RegistrationId, request.RequestId);
            Assert.Equal(DigestC1, request.BaseToolSpecDigest);
            Assert.Equal(DigestB, request.ImageDigest);
            Assert.Equal(Version, request.CanonicalizationVersion);
            Assert.NotNull(request.Spec);
            Assert.NotNull(request.Presentation);
            Assert.NotNull(request.ValidationPolicy);
            Assert.NotNull(request.EnvironmentHints);

            // durable-before-RPC: 서버가 받은 bytes가 그 순간 이미 저장된 snapshot과 같고 receipt는 prepared/attempts 1.
            Assert.Equal(RegistrationState.Prepared, atRpc!.State);
            Assert.Equal(1, atRpc.Attempts);
            Assert.Equal(server.Registry.RegisterRequestBytes[0], snapshotAtRpc);

            var receipt = ReadRawReceipt(RegistrationPath());
            Assert.Equal(RegistrationState.Registered, receipt.State);
            Assert.Equal(ServerF, receipt.Result!.ToolFunctionDigest);
            Assert.Equal(ServerH, receipt.Result.CasHash);
            Assert.Equal(ServerP, receipt.Result.PresentationRevisionId);
            Assert.Equal(Version, receipt.Result.CanonicalizationVersion);
            Assert.Equal(DigestA, receipt.Provenance.BaseImageDigest);
            Assert.Equal(DigestC1, receipt.Provenance.BaseToolSpecDigest);
            Assert.Equal(DigestB, receipt.Provenance.FunctionImageDigest);
            Assert.Equal(DigestC2, receipt.Provenance.SecondRequestBasis);
            Assert.Equal(BuildRequestId, receipt.Provenance.BuildRequestId);
            Assert.Equal(FixtureBuildId, receipt.Provenance.BuildId);

            if (jsonl)
            {
                var records = Records(stdout);
                var completed = Assert.Single(records, r => r.GetProperty("type").GetString() == "completed");
                Assert.Equal("Succeeded", completed.GetProperty("status").GetString());
                Assert.Equal("registration", completed.GetProperty("phase").GetString());
                Assert.Equal(ServerF, completed.GetProperty("tool_function_digest").GetString());
                Assert.Equal(ServerH, completed.GetProperty("cas_hash").GetString());
                Assert.Equal(ServerP, completed.GetProperty("presentation_revision_id").GetString());
                Assert.Equal(Version, completed.GetProperty("canonicalization_version").GetString());
                Assert.Equal(RegistrationId, completed.GetProperty("registration_request_id").GetString());
                Assert.Equal(DigestB, completed.GetProperty("image_digest").GetString());
                Assert.Contains(records, r => r.GetProperty("type").GetString() == "state" && r.GetProperty("state").GetString() == "Succeeded");
            }
            else
            {
                Assert.Contains($"cas_hash (H, 최종 pin): {ServerH}", stdout);
                Assert.Contains($"tool_function_digest (F): {ServerF}", stdout);
                Assert.Contains($"presentation_revision_id (P): {ServerP}", stdout);
                Assert.Contains("등록 성공은 승인(Approved)이 아닙니다", stdout);
                Assert.DoesNotContain("typed 등록은 하지 않았습니다", stdout);
                Assert.Equal(string.Empty, stderr);
            }
        }

        // S3-07-A: 등록 basis를 저장하지 못하면 RegisterToolFunction을 보내지 않는다(exit 2, RPC 0).
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_StoreFailure_Exit2_RegisterRpcZero(bool jsonl)
        {
            using var server = NewServer();
            var recipePath = Save(ReadyRecipe());
            Directory.CreateDirectory(Path.Join(_workDir, ".nodekit"));
            File.WriteAllText(Path.Join(_workDir, ".nodekit", "registrations"), "not a directory");

            var (exit, stdout, stderr) = jsonl ? Submit(server, recipePath, "--format", "jsonl") : Submit(server, recipePath);

            Assert.Equal(2, exit);
            Assert.Empty(server.Registry.RegisterRequests);
            if (jsonl)
            {
                var completed = Completed(stdout);
                Assert.Equal("Failed", completed.GetProperty("status").GetString());
                Assert.Equal("not_submitted", completed.GetProperty("remote_registration_state").GetString());
                Assert.Equal(LocalOperationStore.WriteFailedCode, completed.GetProperty("error_code").GetString());
            }
            else
            {
                Assert.Contains(LocalOperationStore.WriteFailedCode, stderr);
            }
        }

        // S3-07-C: 비었거나 지원하지 않는 version은 결과 불명(exit 1). 성공으로 보지 않고 다른 버전·새 ID로 다시 보내지 않는다.
        [Theory]
        [InlineData("")]
        [InlineData("legacy-order-v0")]
        public void C_VersionMissingOrUnsupported_Exit1_Unknown_NoResubmit(string version)
        {
            using var server = NewServer();
            server.Registry.OnRegisterToolFunction = _ => Response(ServerF, ServerH, version);

            var (exit, stdout, _) = Submit(server, Save(ReadyRecipe()), "--format", "jsonl");

            Assert.Equal(1, exit);
            Assert.Single(server.Registry.RegisterRequests);
            var completed = Completed(stdout);
            Assert.Equal("Failed", completed.GetProperty("status").GetString());
            Assert.Equal(ToolFunctionRegistrar.VersionMismatchCode, completed.GetProperty("error_code").GetString());
            Assert.Equal("unknown", completed.GetProperty("remote_registration_state").GetString());
            Assert.False(completed.TryGetProperty("cas_hash", out _));
            var receipt = ReadRawReceipt(RegistrationPath());
            Assert.Equal(RegistrationState.Unknown, receipt.State);
            Assert.Null(receipt.Result);
            Assert.Contains(version.Length == 0 ? "canonicalization_version=''" : version, receipt.LastError!.Observed);
        }

        // S3-07-D: ACK 유실 뒤 Recipe를 바꾸고 script를 지워도 replay는 저장된 같은 bytes·request ID·endpoint만 보낸다.
        // 빌드 RPC는 다시 가지 않는다(새 이미지 빌드 0).
        [Fact]
        public void D_AckLostThenReplay_SameBytesAndId_SourceEditsIgnored_NoNewBuild()
        {
            using var server = NewServer();
            server.Registry.OnRegisterToolFunction = _ => throw new RpcException(new Status(StatusCode.Unavailable, "ack lost: fixture"));
            var recipePath = Save(ReadyRecipe());

            var (exit, _, stderr) = Submit(server, recipePath);

            Assert.Equal(1, exit);
            Assert.Contains(ToolFunctionRegistrar.UnknownCode, stderr);
            Assert.Contains("register-replay", stderr);
            Assert.Equal(RegistrationState.Unknown, ReadRawReceipt(RegistrationPath()).State);

            var edited = ReadyRecipe();
            edited.DisplayLabel = "edited after submit";
            edited.Revision = "r8";
            File.WriteAllText(recipePath, JsonSerializer.Serialize(edited, ToolFunctionRecipeCliIo.JsonOptions));
            File.Delete(Path.Join(_workDir, "sort.sh"));
            var buildCalls = server.Fake.CallOrder.Count;
            server.Registry.OnRegisterToolFunction = _ => Response(ServerF, ServerH);

            var (replayExit, replayOut, replayErr) = Replay(server, RegistrationPath(), "--format", "jsonl");

            Assert.True(replayExit == 0, replayErr);
            Assert.Equal(2, server.Registry.RegisterRequests.Count);
            Assert.Equal(server.Registry.RegisterRequestBytes[0], server.Registry.RegisterRequestBytes[1]);
            Assert.Equal(RegistrationId, server.Registry.RegisterRequests[1].RequestId);
            Assert.Equal(buildCalls, server.Fake.CallOrder.Count);
            Assert.Equal(ServerH, Completed(replayOut).GetProperty("cas_hash").GetString());
            var receipt = ReadRawReceipt(RegistrationPath());
            Assert.Equal(RegistrationState.Registered, receipt.State);
            Assert.Equal(2, receipt.Attempts);
            Assert.Equal(SubmitCommand.InjectedClientEndpoint, receipt.Endpoint);

            // terminal 뒤 replay는 RPC 없이 저장된 결과를 보여준다.
            var (againExit, againOut, _) = Replay(server, RegistrationPath(), "--format", "jsonl");
            Assert.Equal(0, againExit);
            Assert.Equal(2, server.Registry.RegisterRequests.Count);
            Assert.Equal(ServerH, Completed(againOut).GetProperty("cas_hash").GetString());
        }

        // S3-07-E: 다른 B1/B2에 서버가 같은 F와 다른 H1/H2를 돌려주면 각 receipt가 자기 H를 보존한다(덮어쓰기 0).
        [Fact]
        public void E_SameFDifferentH_EachReceiptKeepsItsOwnPin()
        {
            using var server = NewServer();
            server.Registry.OnRegisterToolFunction = request => Response(ServerF, "cas-for-" + request.ImageDigest);
            var recipePath = Save(ReadyRecipe());

            Assert.Equal(0, Submit(server, recipePath).Exit);
            server.Fake.WatchEvents = new List<ProtoBuildEvent> { SucceededEvent(DigestB2) };
            Assert.Equal(0, SubmitWithIds(server, recipePath, RegistrationId2, "66666666-6666-6666-6666-666666666666").Exit);

            var first = ReadRawReceipt(RegistrationPath());
            var second = ReadRawReceipt(RegistrationPath(RegistrationId2));
            Assert.Equal(ServerF, first.Result!.ToolFunctionDigest);
            Assert.Equal(ServerF, second.Result!.ToolFunctionDigest);
            Assert.Equal("cas-for-" + DigestB, first.Result.CasHash);
            Assert.Equal("cas-for-" + DigestB2, second.Result.CasHash);
            Assert.Equal(DigestB, first.Provenance.FunctionImageDigest);
            Assert.Equal(DigestB2, second.Provenance.FunctionImageDigest);
        }

        // S3-07-F: InvalidArgument = 확정 거부(terminal, replay도 RPC 0). Unavailable = 결과 불명 + replay 안내.
        [Fact]
        public void F_InvalidArgumentRejected_UnavailableUnknown()
        {
            using var server = NewServer();
            server.Registry.OnRegisterToolFunction = _ => throw new RpcException(new Status(StatusCode.InvalidArgument, "spec rejected: fixture"));

            var (exit, stdout, _) = Submit(server, Save(ReadyRecipe()), "--format", "jsonl");

            Assert.Equal(1, exit);
            var completed = Completed(stdout);
            Assert.Equal(ToolFunctionRegistrar.RejectedCode, completed.GetProperty("error_code").GetString());
            Assert.Equal("rejected", completed.GetProperty("remote_registration_state").GetString());
            Assert.Equal("terminal", completed.GetProperty("recovery").GetString());
            Assert.Equal(RegistrationState.Rejected, ReadRawReceipt(RegistrationPath()).State);

            var (replayExit, _, replayErr) = Replay(server, RegistrationPath());
            Assert.Equal(1, replayExit);
            Assert.Single(server.Registry.RegisterRequests);
            Assert.Contains(ToolFunctionRegistrar.RejectedCode, replayErr);

            using var server2 = NewServer();
            server2.Registry.OnRegisterToolFunction = _ => throw new RpcException(new Status(StatusCode.Unavailable, "down: fixture"));
            var (exit2, stdout2, _) = SubmitWithIds(server2, Save(ReadyRecipe()), RegistrationId2, "55555555-5555-5555-5555-555555555555", "--format", "jsonl");
            Assert.Equal(1, exit2);
            var unknown = Completed(stdout2);
            Assert.Equal("unknown", unknown.GetProperty("remote_registration_state").GetString());
            Assert.Equal("uncertain", unknown.GetProperty("recovery").GetString());
            Assert.Contains("register-replay", unknown.GetProperty("message").GetString());
        }

        // S3-07-G: supported version이어도 F/H가 없거나 손상되면 완료 출력 0, exit 1, unknown. client가 채우지 않는다.
        [Theory]
        [InlineData("", ServerH)]
        [InlineData(ServerF, "")]
        [InlineData(ServerF, "cas with space")]
        public void G_IdentityMissingOrCorrupt_Exit1_Unknown(string f, string h)
        {
            using var server = NewServer();
            server.Registry.OnRegisterToolFunction = _ => Response(f, h);

            var (exit, stdout, _) = Submit(server, Save(ReadyRecipe()), "--format", "jsonl");

            Assert.Equal(1, exit);
            var completed = Completed(stdout);
            Assert.Equal("Failed", completed.GetProperty("status").GetString());
            Assert.Equal(ToolFunctionRegistrar.IdentityMissingCode, completed.GetProperty("error_code").GetString());
            Assert.False(completed.TryGetProperty("cas_hash", out _));
            var receipt = ReadRawReceipt(RegistrationPath());
            Assert.Equal(RegistrationState.Unknown, receipt.State);
            Assert.Null(receipt.Result);
        }

        // S3-07-G: 정상 F/H/P/version을 받았어도 receipt를 저장하지 못하면 exit 2, 받은 값은 진단에만 남고 registered가 아니다.
        [Fact]
        public void G_ReceiptWriteFailureAfterNormalResponse_Exit2_NotDurable()
        {
            // 디렉터리 쓰기 권한을 빼서 receipt 쓰기만 실패시킨다 — Unix 권한 모델이 필요하다(CI는 ubuntu).
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using var server = NewServer();
            server.Registry.OnRegisterToolFunction = _ => Response(ServerF, ServerH);
            server.Registry.OnReceived = _ =>
                SetMode(Path.Join(_workDir, ".nodekit", "registrations"), UnixFileMode.UserRead | UnixFileMode.UserExecute);

            var (exit, stdout, _) = Submit(server, Save(ReadyRecipe()), "--format", "jsonl");

            SetMode(Path.Join(_workDir, ".nodekit", "registrations"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Assert.Equal(2, exit);
            var completed = Completed(stdout);
            Assert.Equal("Failed", completed.GetProperty("status").GetString());
            Assert.Contains(ServerH, completed.GetProperty("message").GetString());
            Assert.False(completed.TryGetProperty("cas_hash", out _));
            var receipt = ReadRawReceipt(RegistrationPath());
            Assert.Equal(RegistrationState.Prepared, receipt.State);
            Assert.Equal(1, receipt.Attempts);
            Assert.Null(receipt.Result);
        }

        // replay는 저장된 endpoint로만 보낸다. 손상된 기록은 RPC 없이 exit 2다.
        [Fact]
        public void Replay_EndpointMismatchOrCorruptRecord_Exit2_RpcZero()
        {
            using var server = NewServer();
            server.Registry.OnRegisterToolFunction = _ => throw new RpcException(new Status(StatusCode.Unavailable, "down: fixture"));
            Assert.Equal(1, Submit(server, Save(ReadyRecipe())).Exit);

            var (exit, _, stderr) = Replay(server, RegistrationPath(), "--url", "http://other.example:50051");
            Assert.Equal(2, exit);
            Assert.Contains(ToolFunctionRegistrar.EndpointMismatchCode, stderr);
            Assert.Single(server.Registry.RegisterRequests);

            var receipt = ReadRawReceipt(RegistrationPath());
            File.WriteAllBytes(SnapshotPath(receipt.RequestSha256), new byte[] { 1, 2, 3 });
            var (corruptExit, _, corruptErr) = Replay(server, RegistrationPath());
            Assert.Equal(2, corruptExit);
            Assert.Contains(LocalOperationStore.InvalidCode, corruptErr);
            Assert.Single(server.Registry.RegisterRequests);
        }

        // OS가 거부하는 receipt 경로(NUL 문자·공백뿐)는 예외로 죽지 않고 exit 2다. jsonl이면 구조화된 실패 한 줄(RPC 0).
        [Theory]
        [InlineData("bad\0path.json", false)]
        [InlineData("bad\0path.json", true)]
        [InlineData("   ", false)]
        [InlineData("   ", true)]
        public void Replay_InvalidReceiptPath_Exit2_NoThrow_RpcZero(string receiptPath, bool jsonl)
        {
            using var server = NewServer();

            var (exit, stdout, stderr) = jsonl
                ? Replay(server, receiptPath, "--format", "jsonl")
                : Replay(server, receiptPath);

            Assert.Equal(2, exit);
            Assert.Empty(server.Registry.RegisterRequests);
            if (jsonl)
            {
                var completed = Completed(stdout);
                Assert.Equal(LocalOperationStore.InvalidCode, completed.GetProperty("error_code").GetString());
            }
            else
            {
                Assert.Contains(LocalOperationStore.InvalidCode, stderr);
            }
        }

        // S3-07-G: 저장된 registered 결과도 처음 응답과 같은 identity·version 검사를 받는다. 손상되면 RPC 없는
        // 성공(exit 0)이 아니라 신뢰할 수 없는 기록(exit 2)이다.
        [Theory]
        [InlineData("", ServerH, Version)]
        [InlineData("   ", ServerH, Version)]
        [InlineData(ServerF, "", Version)]
        [InlineData(ServerF, "sha256:ab cd", Version)]
        [InlineData(ServerF, ServerH, "w2-set-v0")]
        [InlineData(ServerF, ServerH, "")]
        public void Replay_CorruptRegisteredResult_Exit2_NotSucceeded_RpcZero(string f, string h, string version)
        {
            using var server = NewServer();
            server.Registry.OnRegisterToolFunction = _ => Response(ServerF, ServerH);
            Assert.Equal(0, Submit(server, Save(ReadyRecipe())).Exit);
            var receipt = ReadRawReceipt(RegistrationPath());
            Assert.Equal(RegistrationState.Registered, receipt.State);
            var corrupted = receipt with
            {
                Result = receipt.Result! with { ToolFunctionDigest = f, CasHash = h, CanonicalizationVersion = version },
            };
            File.WriteAllBytes(RegistrationPath(), JsonSerializer.SerializeToUtf8Bytes(corrupted));

            var (exit, stdout, stderr) = Replay(server, RegistrationPath(), "--format", "jsonl");

            Assert.Equal(2, exit);
            Assert.Single(server.Registry.RegisterRequests);
            var completed = Completed(stdout);
            Assert.Equal(LocalOperationStore.InvalidCode, completed.GetProperty("error_code").GetString());
            Assert.DoesNotContain(ServerH, stdout + stderr);
        }

        // receipt 경로 뒤에 붙인 --help/-h도 다른 하위 명령처럼 stdout 도움말·exit 0이다(RPC 0).
        [Theory]
        [InlineData("--help")]
        [InlineData("-h")]
        public void Replay_TrailingHelp_PrintsUsage_Exit0_RpcZero(string helpFlag)
        {
            using var server = NewServer();

            var (exit, stdout, stderr) = Replay(server, RegistrationPath(), "--format", "jsonl", helpFlag);

            Assert.Equal(0, exit);
            Assert.Contains("register-replay", stdout);
            Assert.Equal(string.Empty, stderr);
            Assert.Empty(server.Registry.RegisterRequests);
        }

        // full B가 없는 빌드 성공은 등록으로 넘어가지 않는다(등록 RPC 0, receipt 0).
        [Fact]
        public void NoFullB_NoRegistration()
        {
            using var server = NewServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent> { SucceededEvent("sha256:short") };

            var (exit, _, _) = Submit(server, Save(ReadyRecipe()));

            Assert.Equal(1, exit);
            Assert.Empty(server.Registry.RegisterRequests);
            Assert.False(Directory.Exists(Path.Join(_workDir, ".nodekit", "registrations")));
        }

        private static Pb.RegisterToolFunctionResponse Response(string f, string h, string version = Version) => new()
        {
            ToolFunctionDigest = f,
            CasHash = h,
            PresentationRevisionId = ServerP,
            CanonicalizationVersion = version,
        };

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
            ImageRef = "harbor.local/nodevault/fn/samtools.sort:r7",
            ImageDigest = imageDigest,
            IntegrityHealth = "Healthy",
        };

        private static GrpcTestServer NewServer()
        {
            var server = new GrpcTestServer();
            server.Fake.OnResolveToolSpec = request => new Pb.ResolvedToolSpecResponse
            {
                ToolSpecDigest = DigestC2,
                ToolName = request.ToolName,
                Version = request.Version,
            };
            server.Fake.OnSubmitToolBuild = _ => new Pb.SubmitToolBuildResponse { BuildId = FixtureBuildId, Status = "Requested" };
            server.Fake.WatchEvents = new List<ProtoBuildEvent> { SucceededEvent(DigestB) };
            return server;
        }

        private string Save(ToolFunctionRecipe recipe)
        {
            File.WriteAllText(Path.Join(_workDir, "sort.sh"), Script);
            var path = Path.Join(_workDir, "recipe.json");
            File.WriteAllText(path, JsonSerializer.Serialize(recipe, ToolFunctionRecipeCliIo.JsonOptions));
            return path;
        }

        private static (int Exit, string Stdout, string Stderr) Submit(GrpcTestServer server, string recipePath, params string[] extraArgs) =>
            SubmitWithIds(server, recipePath, RegistrationId, BuildRequestId, extraArgs);

        private static (int Exit, string Stdout, string Stderr) SubmitWithIds(
            GrpcTestServer server, string recipePath, string registrationId, string buildRequestId, params string[] extraArgs)
        {
            using var client = new GrpcToolSpecClient(server.Channel);
            using var registry = new GrpcToolFunctionRegistryClient(server.Channel);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var args = new[] { "function-recipe", "submit", recipePath }.Concat(extraArgs).ToArray();
            var exit = ToolFunctionRecipeSubmitCommand.Run(
                args, stdout, stderr, client, () => buildRequestId, toolFunctionBuildGateOpen: true, registryClient: registry, registrationRequestIdProvider: () => registrationId);
            return (exit, stdout.ToString(), stderr.ToString());
        }

        private static (int Exit, string Stdout, string Stderr) Replay(GrpcTestServer server, string receiptPath, params string[] extraArgs)
        {
            using var registry = new GrpcToolFunctionRegistryClient(server.Channel);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var args = new[] { "function-recipe", "register-replay", receiptPath }.Concat(extraArgs).ToArray();
            var exit = ToolFunctionRegistrar.RunReplay(args, stdout, stderr, registry);
            return (exit, stdout.ToString(), stderr.ToString());
        }

        private string RegistrationPath(string requestId = RegistrationId) =>
            Path.Join(_workDir, ".nodekit", "registrations", requestId + ".json");

        private string SnapshotPath(string sha256) =>
            Path.Join(_workDir, ".nodekit", "snapshots", "registration", sha256 + ".bin");

        private static RegistrationReceipt ReadRawReceipt(string path) =>
            JsonSerializer.Deserialize<RegistrationReceipt>(File.ReadAllBytes(path))!;

        private static List<JsonElement> Records(string stdout) =>
            stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
                    using var document = JsonDocument.Parse(line);
                    return document.RootElement.Clone();
                })
                .ToList();

        private static JsonElement Completed(string stdout) =>
            Assert.Single(Records(stdout), r => r.GetProperty("type").GetString() == "completed");
    }
}
