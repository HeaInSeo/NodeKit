using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using NodeKit.Cli.Operations;
using NodeKit.Cli.Tests.Fakes;
using NodeKit.Grpc;
using Xunit;
using Pb = Nodevault.V1;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// P08.timeout (v0.9.1 S3-08-G 등록 변형): typed 등록 단계의 opt-in `--registration-timeout`, 보낸 뒤/보내기 전
    /// Ctrl-C, 늦은 ACK. 실제 GrpcToolSpecClient/GrpcToolFunctionRegistryClient를 in-process fake에 붙여 exit·출력·
    /// RPC 횟수·durable receipt를 본다(늦은 ACK만 토큰을 무시하는 주입 client). fake 지연은 실제 NodeVault의 응답
    /// 시간이나 서버 멱등성의 증거가 아니다.
    /// </summary>
    public partial class ToolFunctionRegistryP08Tests
    {
        // 실행 환경 차이를 감안한 상한 — 1초 예산/즉시 취소가 이 안에 끝나야 한다(S3-08-E의 10초 경계).
        private static readonly TimeSpan ShutdownBound = TimeSpan.FromSeconds(10);

        // registration_deadline: 보낸 뒤 응답 없음 → 예산 만료 124·unknown·completed1. 저장 요청 보존, CancelToolBuild0·새 빌드0,
        // 이어서 같은 envelope replay가 같은 bytes/ID로 등록한다.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Timeout_AfterSend_Exit124_Unknown_ThenSameEnvelopeReplay(bool jsonl)
        {
            using var server = NewServer();
            server.Registry.OnRegisterToolFunctionAsync = HangUntilCancelled;
            var extra = jsonl ? new[] { "--registration-timeout", "1", "--format", "jsonl" } : new[] { "--registration-timeout", "1" };

            var watch = Stopwatch.StartNew();
            var (exit, stdout, stderr) = Submit(server, Save(ReadyRecipe()), extra);
            watch.Stop();

            Assert.True(exit == 124, stderr);
            Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(0.9), ShutdownBound);
            Assert.Single(server.Registry.RegisterRequests);
            Assert.Empty(server.Fake.CancelledBuildIds);
            Assert.Equal(new[] { "Resolve", "Submit", "Watch" }, server.Fake.CallOrder);

            var receipt = ReadRawReceipt(RegistrationPath());
            Assert.Equal(RegistrationState.Unknown, receipt.State);
            Assert.Equal(1, receipt.Attempts);
            Assert.Null(receipt.Result);
            Assert.Equal(ToolFunctionRegistrar.TimeoutCode, receipt.LastError!.Code);
            Assert.Equal(DigestB, receipt.Provenance.FunctionImageDigest);

            if (jsonl)
            {
                var completed = Completed(stdout);
                Assert.Equal("Failed", completed.GetProperty("status").GetString());
                Assert.Equal("registration", completed.GetProperty("phase").GetString());
                Assert.Equal(ToolFunctionRegistrar.TimeoutCode, completed.GetProperty("error_code").GetString());
                Assert.Equal(RegistrationState.Unknown, completed.GetProperty("remote_registration_state").GetString());
                Assert.Equal("uncertain", completed.GetProperty("recovery").GetString());
                Assert.Equal(RegistrationId, completed.GetProperty("registration_request_id").GetString());
                Assert.Equal(DigestB, completed.GetProperty("image_digest").GetString());
                Assert.Contains("register-replay", completed.GetProperty("message").GetString(), StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains(ToolFunctionRegistrar.TimeoutCode, stderr, StringComparison.Ordinal);
                Assert.Contains("1초(--registration-timeout)", stderr, StringComparison.Ordinal);
                Assert.Contains(ToolFunctionRegistrar.ReplayCommand(RegistrationPath()), stderr, StringComparison.Ordinal);
                Assert.DoesNotContain("typed 등록 완료", stdout, StringComparison.Ordinal);
            }

            server.Registry.OnRegisterToolFunctionAsync = null;
            server.Registry.OnRegisterToolFunction = _ => Response(ServerF, ServerH);
            var (replayExit, _, replayErr) = Replay(server, RegistrationPath());

            Assert.True(replayExit == 0, replayErr);
            Assert.Equal(2, server.Registry.RegisterRequests.Count);
            Assert.Equal(server.Registry.RegisterRequestBytes[0], server.Registry.RegisterRequestBytes[1]);
            Assert.Equal(RegistrationId, server.Registry.RegisterRequests[1].RequestId);
            Assert.Equal(new[] { "Resolve", "Submit", "Watch" }, server.Fake.CallOrder);
            Assert.Equal(RegistrationState.Registered, ReadRawReceipt(RegistrationPath()).State);
        }

        // registration_deadline_not_selected: --registration-timeout 없음 → 자동 deadline 없음. 앞 단계 connect/watch 예산이
        // 지난 뒤에 온 정상 응답도 등록으로 받는다(빌드 타이머가 등록을 취소하지 않는다).
        [Fact]
        public void NoRegistrationTimeout_BuildTimersDoNotCancelRegistration()
        {
            using var server = NewServer();
            server.Registry.OnRegisterToolFunctionAsync = (_, context) => RespondAfter(TimeSpan.FromMilliseconds(1500), context);

            var (exit, _, stderr) = Submit(server, Save(ReadyRecipe()), "--connect-timeout", "1", "--watch-timeout", "1s");

            Assert.True(exit == 0, stderr);
            Assert.Equal(RegistrationState.Registered, ReadRawReceipt(RegistrationPath()).State);
            Assert.Empty(server.Fake.CancelledBuildIds);
        }

        // 예산은 등록 RPC 시작부터 잰다 — 앞 빌드/watch에 쓴 시간(1.5초)이 1초 등록 예산을 깎지 않는다.
        [Fact]
        public void RegistrationBudget_StartsAtRegistrationRpc_NotAtCommandStart()
        {
            using var server = NewServer();
            server.Fake.WatchEventDelay = TimeSpan.FromMilliseconds(1500);
            server.Registry.OnRegisterToolFunctionAsync = (_, context) => RespondAfter(TimeSpan.FromMilliseconds(200), context);

            var (exit, _, stderr) = Submit(server, Save(ReadyRecipe()), "--registration-timeout", "1");

            Assert.True(exit == 0, stderr);
            Assert.Equal(RegistrationState.Registered, ReadRawReceipt(RegistrationPath()).State);
        }

        // registration_user_abort: 보낸 뒤 Ctrl-C → 130·unknown·completed1, 바로 끝난다. CancelToolBuild0·새 빌드0.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void UserAbort_AfterSend_Exit130_Unknown_NoCancelToolBuild(bool jsonl)
        {
            using var server = NewServer();
            using var cts = new CancellationTokenSource();
            server.Registry.OnReceived = _ => cts.Cancel();
            server.Registry.OnRegisterToolFunctionAsync = HangUntilCancelled;

            var watch = Stopwatch.StartNew();
            var (exit, stdout, stderr) = SubmitUntilCancel(server, Save(ReadyRecipe()), cts.Token, jsonl ? new[] { "--format", "jsonl" } : Array.Empty<string>());
            watch.Stop();

            Assert.True(exit == 130, stderr);
            Assert.True(watch.Elapsed < ShutdownBound, watch.Elapsed.ToString());
            Assert.Single(server.Registry.RegisterRequests);
            Assert.Empty(server.Fake.CancelledBuildIds);
            Assert.Equal(new[] { "Resolve", "Submit", "Watch" }, server.Fake.CallOrder);

            var receipt = ReadRawReceipt(RegistrationPath());
            Assert.Equal(RegistrationState.Unknown, receipt.State);
            Assert.Equal(1, receipt.Attempts);
            Assert.Equal(ToolFunctionRegistrar.CancelledCode, receipt.LastError!.Code);

            if (jsonl)
            {
                var completed = Completed(stdout);
                Assert.Equal("Cancelled", completed.GetProperty("status").GetString());
                Assert.Equal("registration", completed.GetProperty("phase").GetString());
                Assert.Equal(ToolFunctionRegistrar.CancelledCode, completed.GetProperty("error_code").GetString());
                Assert.Equal(RegistrationState.Unknown, completed.GetProperty("remote_registration_state").GetString());
                Assert.Equal("uncertain", completed.GetProperty("recovery").GetString());
            }
            else
            {
                Assert.Contains(ToolFunctionRegistrar.CancelledCode, stderr, StringComparison.Ordinal);
                Assert.Contains("로컬 중단", stderr, StringComparison.Ordinal);
                Assert.Contains(ToolFunctionRegistrar.ReplayCommand(RegistrationPath()), stderr, StringComparison.Ordinal);
            }
        }

        // registration_before_send_abort: typed envelope·prepared 기록을 만든 뒤 RPC 전에 취소 → 130·not_submitted·RPC0·completed1.
        // prepared 기록은 그대로 남아 replay가 처음으로 보낸다.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void UserAbort_BeforeSend_Exit130_NotSubmitted_RpcZero_PreparedKept(bool jsonl)
        {
            using var server = NewServer();
            server.Registry.OnRegisterToolFunction = _ => Response(ServerF, ServerH);
            using var cts = new CancellationTokenSource();
            using var registry = new GrpcToolFunctionRegistryClient(server.Channel);
            var (exit, stdout, stderr) = Invoke(
                server,
                Save(ReadyRecipe()),
                registry,
                () =>
                {
                    cts.Cancel();
                    return RegistrationId;
                },
                cts.Token,
                jsonl ? new[] { "--format", "jsonl" } : Array.Empty<string>());

            Assert.True(exit == 130, stderr);
            Assert.Empty(server.Registry.RegisterRequests);
            Assert.Empty(server.Fake.CancelledBuildIds);
            var receipt = ReadRawReceipt(RegistrationPath());
            Assert.Equal(RegistrationState.Prepared, receipt.State);
            Assert.Equal(0, receipt.Attempts);
            Assert.Null(receipt.LastError);

            if (jsonl)
            {
                var completed = Completed(stdout);
                Assert.Equal("Cancelled", completed.GetProperty("status").GetString());
                Assert.Equal(ToolFunctionRegistrar.CancelledCode, completed.GetProperty("error_code").GetString());
                Assert.Equal("not_submitted", completed.GetProperty("remote_registration_state").GetString());
                Assert.Contains("register-replay", completed.GetProperty("message").GetString(), StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("보내기 전에", stderr, StringComparison.Ordinal);
                Assert.Contains(ToolFunctionRegistrar.ReplayCommand(RegistrationPath()), stderr, StringComparison.Ordinal);
            }

            var (replayExit, _, replayErr) = Replay(server, RegistrationPath());
            Assert.True(replayExit == 0, replayErr);
            var sent = Assert.Single(server.Registry.RegisterRequests);
            Assert.Equal(RegistrationId, sent.RequestId);
            Assert.Equal(1, ReadRawReceipt(RegistrationPath()).Attempts);
        }

        // registration_option_invalid: 양의 정수 초 1..상한이 아니면 업무 RPC 전 exit2, 기록0.
        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("+5")]
        [InlineData(" 5")]
        [InlineData("1.5")]
        [InlineData("1e3")]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("٣")]
        [InlineData("4294968")]
        [InlineData("99999999999")]
        [InlineData("10s")]
        public void RegistrationTimeout_InvalidValue_Exit2_RpcZero(string value)
        {
            using var server = NewServer();

            var (exit, _, stderr) = Submit(server, Save(ReadyRecipe()), "--registration-timeout", value);

            Assert.Equal(2, exit);
            Assert.Contains("--registration-timeout", stderr, StringComparison.Ordinal);
            Assert.Empty(server.Fake.CallOrder);
            Assert.Empty(server.Registry.RegisterRequests);
            Assert.False(Directory.Exists(Path.Join(_workDir, ".nodekit")));
        }

        [Theory]
        [InlineData("--registration-timeout")]
        [InlineData("--registration-timeout|--format|jsonl")]
        [InlineData("--registration-timeout|5|--registration-timeout|5")]
        public void RegistrationTimeout_MissingOrDuplicate_Exit2_RpcZero(string joinedArgs)
        {
            using var server = NewServer();

            var (exit, _, _) = Submit(server, Save(ReadyRecipe()), joinedArgs.Split('|'));

            Assert.Equal(2, exit);
            Assert.Empty(server.Fake.CallOrder);
            Assert.Empty(server.Registry.RegisterRequests);
        }

        // 상한값 자체는 표현 가능하다(타이머가 거부하지 않는다) — 정상 응답이면 그대로 등록한다.
        [Fact]
        public void RegistrationTimeout_MaxValue_Accepted()
        {
            using var server = NewServer();
            server.Registry.OnRegisterToolFunction = _ => Response(ServerF, ServerH);

            var (exit, _, stderr) = Submit(
                server, Save(ReadyRecipe()), "--registration-timeout", ToolFunctionRegistrar.MaxRegistrationTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));

            Assert.True(exit == 0, stderr);
            Assert.Equal(RegistrationState.Registered, ReadRawReceipt(RegistrationPath()).State);
        }

        // help에 적은 상한이 실제 상한과 같고, 그 값이 등록 예산 타이머(4294967294 ms)를 넘지 않는다.
        [Fact]
        public void RegistrationTimeout_HelpStatesExactBound()
        {
            var max = ToolFunctionRegistrar.MaxRegistrationTimeoutSeconds;
            Assert.True((long)max * 1000 <= uint.MaxValue - 1);
            Assert.True((long)(max + 1) * 1000 > uint.MaxValue - 1);
            Assert.Contains($"<seconds: 1-{max.ToString(System.Globalization.CultureInfo.InvariantCulture)}>", ToolFunctionRegistrar.RegistrationTimeoutUsage, StringComparison.Ordinal);

            foreach (var command in new[] { "submit", "register-replay" })
            {
                using var stdout = new StringWriter();
                using var stderr = new StringWriter();
                var exit = command == "submit"
                    ? ToolFunctionRecipeSubmitCommand.Run(new[] { "function-recipe", command, "--help" }, stdout, stderr)
                    : ToolFunctionRegistrar.RunReplay(new[] { "function-recipe", command, "--help" }, stdout, stderr);
                Assert.Equal(0, exit);
                Assert.Contains(ToolFunctionRegistrar.RegistrationTimeoutUsage, stdout.ToString(), StringComparison.Ordinal);
            }
        }

        // registration_late_ack: 토큰을 무시하는 transport — CLI는 예산/취소에서 결과를 확정하고 돌아온다. 그 뒤 도착한 성공 응답은
        // 출력(두 번째 completed)·receipt 어디에도 반영되지 않고 새 ID/빌드도 없다. 확인은 명시적 replay로 한다.
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void LateAck_AfterLocalOutcome_NotReported_ReceiptUnchanged(bool userAbort, bool jsonl)
        {
            using var server = NewServer();
            using var cts = new CancellationTokenSource();
            var registry = new TokenIgnoringRegistryClient(onCall: userAbort ? cts.Cancel : null);
            var extra = new List<string>();
            if (!userAbort)
            {
                extra.AddRange(new[] { "--registration-timeout", "1" });
            }

            if (jsonl)
            {
                extra.AddRange(new[] { "--format", "jsonl" });
            }

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            using var client = new GrpcToolSpecClient(server.Channel);
            var args = new[] { "function-recipe", "submit", Save(ReadyRecipe()) }.Concat(extra).ToArray();
            var exit = ToolFunctionRecipeSubmitCommand.RunUntilUserCancel(
                args, stdout, stderr, client, () => BuildRequestId, registry, () => RegistrationId, cts.Token);

            Assert.Equal(userAbort ? 130 : 124, exit);
            var stdoutAtOutcome = stdout.ToString();
            var stderrAtOutcome = stderr.ToString();
            var receiptAtOutcome = File.ReadAllBytes(RegistrationPath());

            registry.Complete(Response(ServerF, ServerH));
            Thread.Sleep(200);

            Assert.Equal(stdoutAtOutcome, stdout.ToString());
            Assert.Equal(stderrAtOutcome, stderr.ToString());
            Assert.Equal(receiptAtOutcome, File.ReadAllBytes(RegistrationPath()));
            Assert.Equal(1, registry.Calls);
            Assert.Equal(new[] { "Resolve", "Submit", "Watch" }, server.Fake.CallOrder);
            var receipt = ReadRawReceipt(RegistrationPath());
            Assert.Equal(RegistrationState.Unknown, receipt.State);
            Assert.Null(receipt.Result);
            if (jsonl)
            {
                Assert.Single(Records(stdoutAtOutcome), r => r.GetProperty("type").GetString() == "completed");
                Assert.DoesNotContain("Succeeded\",\"phase\":\"registration", stdoutAtOutcome, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain("typed 등록 완료", stdoutAtOutcome, StringComparison.Ordinal);
            }
        }

        // register-replay도 같은 규칙: --registration-timeout 만료 124 / Ctrl-C 130, 둘 다 unknown·attempts 증가, 결과를 합성하지 않는다.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Replay_TimeoutOrUserAbort_Exit124Or130_StaysUnknown(bool userAbort)
        {
            using var server = NewServer();
            server.Registry.OnRegisterToolFunction = _ => throw new RpcException(new Status(StatusCode.Unavailable, "ack lost"));
            var (firstExit, _, _) = Submit(server, Save(ReadyRecipe()));
            Assert.Equal(1, firstExit);

            using var cts = new CancellationTokenSource();
            if (userAbort)
            {
                server.Registry.OnReceived = _ => cts.Cancel();
            }

            server.Registry.OnRegisterToolFunctionAsync = HangUntilCancelled;
            var watch = Stopwatch.StartNew();
            var (exit, _, stderr) = ReplayUntilCancel(
                server, RegistrationPath(), cts.Token, userAbort ? Array.Empty<string>() : new[] { "--registration-timeout", "1" });
            watch.Stop();

            Assert.True(exit == (userAbort ? 130 : 124), stderr);
            Assert.True(watch.Elapsed < ShutdownBound, watch.Elapsed.ToString());
            Assert.Equal(2, server.Registry.RegisterRequests.Count);
            Assert.Equal(server.Registry.RegisterRequestBytes[0], server.Registry.RegisterRequestBytes[1]);
            var receipt = ReadRawReceipt(RegistrationPath());
            Assert.Equal(RegistrationState.Unknown, receipt.State);
            Assert.Equal(2, receipt.Attempts);
            Assert.Equal(userAbort ? ToolFunctionRegistrar.CancelledCode : ToolFunctionRegistrar.TimeoutCode, receipt.LastError!.Code);
            Assert.Equal(new[] { "Resolve", "Submit", "Watch" }, server.Fake.CallOrder);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("abc")]
        [InlineData("4294968")]
        public void Replay_InvalidRegistrationTimeout_Exit2_RpcZero(string value)
        {
            using var server = NewServer();
            server.Registry.OnRegisterToolFunction = _ => throw new RpcException(new Status(StatusCode.Unavailable, "ack lost"));
            Submit(server, Save(ReadyRecipe()));

            var (exit, _, stderr) = Replay(server, RegistrationPath(), "--registration-timeout", value);

            Assert.Equal(2, exit);
            Assert.Contains("--registration-timeout", stderr, StringComparison.Ordinal);
            Assert.Single(server.Registry.RegisterRequests);
            Assert.Equal(1, ReadRawReceipt(RegistrationPath()).Attempts);
        }

        private static async Task<Pb.RegisterToolFunctionResponse> HangUntilCancelled(Pb.RegisterToolFunctionRequest request, ServerCallContext context)
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
            return Response(ServerF, ServerH);
        }

        private static async Task<Pb.RegisterToolFunctionResponse> RespondAfter(TimeSpan delay, ServerCallContext context)
        {
            await Task.Delay(delay, context.CancellationToken);
            return Response(ServerF, ServerH);
        }

        private static (int Exit, string Stdout, string Stderr) SubmitUntilCancel(
            GrpcTestServer server, string recipePath, CancellationToken userCancellation, params string[] extraArgs)
        {
            using var registry = new GrpcToolFunctionRegistryClient(server.Channel);
            return Invoke(server, recipePath, registry, () => RegistrationId, userCancellation, extraArgs);
        }

        private static (int Exit, string Stdout, string Stderr) Invoke(
            GrpcTestServer server,
            string recipePath,
            IToolFunctionRegistryClient registry,
            Func<string> registrationIdProvider,
            CancellationToken userCancellation,
            params string[] extraArgs)
        {
            using var client = new GrpcToolSpecClient(server.Channel);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var args = new[] { "function-recipe", "submit", recipePath }.Concat(extraArgs).ToArray();
            var exit = ToolFunctionRecipeSubmitCommand.RunUntilUserCancel(
                args, stdout, stderr, client, () => BuildRequestId, registry, registrationIdProvider, userCancellation);
            return (exit, stdout.ToString(), stderr.ToString());
        }

        private static (int Exit, string Stdout, string Stderr) ReplayUntilCancel(
            GrpcTestServer server, string receiptPath, CancellationToken userCancellation, params string[] extraArgs)
        {
            using var registry = new GrpcToolFunctionRegistryClient(server.Channel);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var args = new[] { "function-recipe", "register-replay", receiptPath }.Concat(extraArgs).ToArray();
            var exit = ToolFunctionRegistrar.RunReplayUntilUserCancel(args, stdout, stderr, registry, userCancellation);
            return (exit, stdout.ToString(), stderr.ToString());
        }

        /// <summary>취소 토큰을 무시하고 테스트가 직접 완료할 때까지 응답하지 않는 등록 client(늦은 ACK 재현).</summary>
        private sealed class TokenIgnoringRegistryClient : IToolFunctionRegistryClient
        {
            private readonly TaskCompletionSource<Pb.RegisterToolFunctionResponse> _response = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly Action? _onCall;

            public TokenIgnoringRegistryClient(Action? onCall) => _onCall = onCall;

            public int Calls { get; private set; }

            public Task<Pb.RegisterToolFunctionResponse> RegisterToolFunctionAsync(Pb.RegisterToolFunctionRequest request, CancellationToken ct = default)
            {
                Calls++;
                _onCall?.Invoke();
                return _response.Task;
            }

            public void Complete(Pb.RegisterToolFunctionResponse response) => _response.SetResult(response);
        }
    }
}
