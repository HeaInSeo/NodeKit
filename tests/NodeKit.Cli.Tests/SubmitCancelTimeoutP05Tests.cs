using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Grpc.Core;
using NodeKit.Authoring.Recipes;
using NodeKit.Cli.Operations;
using NodeKit.Cli.Tests.Fakes;
using NodeKit.Grpc;
using Xunit;
using ProtoBuildEvent = Nodevault.V1.BuildEvent;
using ProtoBuildEventKind = Nodevault.V1.BuildEventKind;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// P05.reentry slice2 (NodeKit v0.9.1 S2-04): user cancel and the two-stage timeout of a journaled
    /// `nodekit submit`. Each case runs the real GrpcToolSpecClient against the in-process fake and then reads
    /// the durable receipt: a local stop is recorded as local_abort with remote_build_state unknown, the
    /// phase/build_id/observation are never moved to a server result that was not observed, and the receipt
    /// stays re-enterable. The fake does not model a real server's cancel or build lifecycle; these tests prove
    /// local exit codes, RPC counts, receipt contents and output only.
    /// </summary>
    public class SubmitCancelTimeoutP05Tests : IDisposable
    {
        private const string FixedRequestId = "33333333-3333-3333-3333-333333333333";

        private const string FixtureBuildId = "build-fixture-001";

        private const string Digest =
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private static readonly TimeSpan _userCancelDelay = TimeSpan.FromMilliseconds(500);

        private static readonly string[] _fixtureBuildIdOnce = { FixtureBuildId };

        private readonly string _workDir = Path.Join(Path.GetTempPath(), "nodekit-p05-s204-tests-" + Guid.NewGuid());

        private readonly IDisposable _resolveClientOverride =
            ResolveRecipeClientTestOverride.Use(NullResolveRecipeClient.Instance);

        public SubmitCancelTimeoutP05Tests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
            _resolveClientOverride.Dispose();
        }

        // S2-04-C01
        [Fact]
        public void UserCancel_AfterBuildId_CancelsKnownIdOnce_Exit130_ReceiptRecordsLocalAbortOnly()
        {
            using var server = NewServer();
            server.Fake.WatchEvents = RunningEvents();
            server.Fake.HangAfterEvents = true;

            var exitCode = SubmitUntilUserCancel(server, out var stdout, out var stderr);

            Assert.Equal(130, exitCode);
            Assert.Equal(_fixtureBuildIdOnce, server.Fake.CancelledBuildIds);
            Assert.Contains("서버 빌드가 실제로 멈췄는지는 확인하지 않았습니다", stderr, StringComparison.Ordinal);
            Assert.Contains(ReceiptCommand.FollowUpCommand("watch", DefaultReceiptPath()), stderr, StringComparison.Ordinal);
            AssertNoServerResultClaimed(stdout, stderr);

            var receipt = ReadReceipt(DefaultReceiptPath());
            Assert.Equal(OperationPhase.Acknowledged, receipt.Phase);
            Assert.Equal(FixtureBuildId, receipt.BuildId);
            Assert.Null(receipt.LastObservation);
            AssertLocalAbort(receipt, OperationLocalAbort.UserCancel);
        }

        // S2-04-C01: the best-effort cancel runs on its own cancellable token, not the already-cancelled one
        [Fact]
        public void UserCancel_AfterBuildId_CancelRpcStillReachesServerAfterUserTokenFired()
        {
            using var server = NewServer();
            server.Fake.WatchEvents = RunningEvents();
            server.Fake.HangAfterEvents = true;
            using var userCancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            userCancel.CancelAfter(_userCancelDelay);

            Assert.Equal(130, Submit(server, userCancel.Token, out _, out var stderr));

            Assert.True(userCancel.IsCancellationRequested);
            Assert.Equal(_fixtureBuildIdOnce, server.Fake.CancelledBuildIds);
            Assert.DoesNotContain("서버에 빌드 취소 요청을 보내지 못했습니다", stderr, StringComparison.Ordinal);
        }

        // S2-04-C02
        [Fact]
        public void UserCancel_BeforeBuildId_NoCancelRpc_Exit130_ReceiptKeepsRemoteUnknown()
        {
            using var server = NewServer();
            server.Fake.HangOnResolveToolSpec = true;

            var exitCode = SubmitUntilUserCancel(server, out var stdout, out var stderr);

            Assert.Equal(130, exitCode);
            Assert.Empty(server.Fake.CancelledBuildIds);
            Assert.Empty(server.Fake.SubmitRequests);
            Assert.Contains("빌드가 만들어졌는지는 알 수 없습니다", stderr, StringComparison.Ordinal);
            Assert.Contains(FixedRequestId, stderr, StringComparison.Ordinal);
            AssertNoServerResultClaimed(stdout, stderr);

            var receipt = ReadReceipt(DefaultReceiptPath());
            Assert.Equal(OperationPhase.Prepared, receipt.Phase);
            Assert.Null(receipt.BuildId);
            AssertLocalAbort(receipt, OperationLocalAbort.UserCancel);
        }

        // S2-04-C02: Submit reached the server but its answer had not arrived — still no cancel target, remote unknown
        [Fact]
        public void UserCancel_WhileSubmitInFlight_NoCancelRpc_ReceiptStaysSubmitInFlight()
        {
            using var server = NewServer();
            server.Fake.HangOnSubmitToolBuild = true;

            var exitCode = SubmitUntilUserCancel(server, out _, out var stderr);

            Assert.Equal(130, exitCode);
            Assert.Single(server.Fake.SubmitRequests);
            Assert.Empty(server.Fake.CancelledBuildIds);
            Assert.Contains("빌드가 만들어졌는지는 알 수 없습니다", stderr, StringComparison.Ordinal);

            var receipt = ReadReceipt(DefaultReceiptPath());
            Assert.Equal(OperationPhase.SubmitInFlight, receipt.Phase);
            Assert.Null(receipt.BuildId);
            AssertLocalAbort(receipt, OperationLocalAbort.UserCancel);
        }

        // S2-04-C03 (submit): the known-ID cancel RPC never answers; its own 5 s bound returns control
        [Fact]
        public void UserCancel_CancelRpcHangs_BoundedUnderTenSeconds_WarnsAndClaimsNoServerCancel()
        {
            using var server = NewServer();
            server.Fake.WatchEvents = RunningEvents();
            server.Fake.HangAfterEvents = true;
            server.Fake.HangOnCancelToolBuild = true;

            var stopwatch = Stopwatch.StartNew();
            var exitCode = SubmitUntilUserCancel(server, out var stdout, out var stderr);
            stopwatch.Stop();

            Assert.Equal(130, exitCode);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"submit cancel took {stopwatch.Elapsed}");
            Assert.True(stopwatch.Elapsed >= TimeSpan.FromSeconds(4), $"cancel RPC was not awaited: {stopwatch.Elapsed}");
            Assert.Equal(_fixtureBuildIdOnce, server.Fake.CancelledBuildIds);
            Assert.Contains($"서버에 빌드 취소 요청을 보내지 못했습니다 (build ID: {FixtureBuildId})", stderr, StringComparison.Ordinal);
            AssertNoServerResultClaimed(stdout, stderr);
            AssertLocalAbort(ReadReceipt(DefaultReceiptPath()), OperationLocalAbort.UserCancel);
        }

        // S2-04-C03 (receipt cancel): the 5 s bound of `nodekit receipt cancel` (Guardrail P3-2)
        [Fact]
        public void ReceiptCancel_CancelRpcHangs_BoundedUnderTenSeconds_Exit1_ReceiptUnchanged()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            var before = File.ReadAllBytes(receiptPath);
            using var server = new GrpcTestServer();
            server.Fake.HangOnCancelToolBuild = true;

            var stopwatch = Stopwatch.StartNew();
            var exitCode = Reenter(server, "cancel", receiptPath, out var stdout, out var stderr);
            stopwatch.Stop();

            Assert.Equal(1, exitCode);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"receipt cancel took {stopwatch.Elapsed}");
            Assert.True(stopwatch.Elapsed >= TimeSpan.FromSeconds(4), $"cancel RPC was not awaited: {stopwatch.Elapsed}");
            Assert.Equal(_fixtureBuildIdOnce, server.Fake.CancelledBuildIds);
            Assert.Contains("5초 안에 응답이 없었습니다", stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("취소 요청을 보냈습니다", stdout, StringComparison.Ordinal);
            Assert.Equal(before, File.ReadAllBytes(receiptPath));
        }

        // S2-04-C04: Submit reached the server and hangs; connect-timeout fires before any build_id
        [Fact]
        public void ConnectTimeout_SubmitHangs_Exit124_NoCancelRpc_RemoteUnknownNotDenied()
        {
            using var server = NewServer();
            server.Fake.HangOnSubmitToolBuild = true;

            var exitCode = Submit(server, out var stdout, out _, "--connect-timeout", "1", "--format", "jsonl");

            Assert.Equal(124, exitCode);
            Assert.Single(server.Fake.SubmitRequests);
            Assert.Empty(server.Fake.CancelledBuildIds);
            var completed = SingleJsonl(stdout);
            Assert.Equal("CONNECT_TIMEOUT", completed.GetProperty("error_code").GetString());
            Assert.Equal("uncertain", completed.GetProperty("recovery").GetString());
            Assert.False(completed.TryGetProperty("build_id", out _));
            Assert.Contains("빌드가 만들어졌는지는 알 수 없습니다", completed.GetProperty("message").GetString(), StringComparison.Ordinal);

            var receipt = ReadReceipt(DefaultReceiptPath());
            Assert.Equal(OperationPhase.SubmitInFlight, receipt.Phase);
            Assert.Null(receipt.BuildId);
            AssertLocalAbort(receipt, OperationLocalAbort.ConnectTimeout);
        }

        // S2-04-C04 (human): Resolve hangs; the message does not say no build exists
        [Fact]
        public void ConnectTimeout_ResolveHangs_Human_Exit124_NoCancelRpc_ReceiptPrepared()
        {
            using var server = NewServer();
            server.Fake.HangOnResolveToolSpec = true;

            var exitCode = Submit(server, out _, out var stderr, "--connect-timeout", "1");

            Assert.Equal(124, exitCode);
            Assert.Empty(server.Fake.CancelledBuildIds);
            Assert.Contains("타임아웃되었습니다 (--connect-timeout)", stderr, StringComparison.Ordinal);
            Assert.Contains("빌드가 만들어졌는지는 알 수 없습니다", stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("빌드가 없", stderr, StringComparison.Ordinal);

            var receipt = ReadReceipt(DefaultReceiptPath());
            Assert.Equal(OperationPhase.Prepared, receipt.Phase);
            AssertLocalAbort(receipt, OperationLocalAbort.ConnectTimeout);
        }

        // S2-04-C05: watch-timeout after build_id → 125, no cancel, build_id/last event shown, receipt re-enterable
        [Fact]
        public void WatchTimeout_AfterBuildId_Exit125_NoCancelRpc_ReceiptReentersToTerminalAndClearsLocalAbort()
        {
            string receiptPath;
            using (var server = NewServer())
            {
                server.Fake.WatchEvents = RunningEvents();
                server.Fake.HangAfterEvents = true;

                var exitCode = Submit(server, out _, out var stderr, "--watch-timeout", "1s");

                Assert.Equal(125, exitCode);
                Assert.Empty(server.Fake.CancelledBuildIds);
                Assert.Contains("타임아웃되었습니다 (--watch-timeout)", stderr, StringComparison.Ordinal);
                Assert.Contains($"Build ID: {FixtureBuildId}", stderr, StringComparison.Ordinal);
                Assert.DoesNotContain("마지막 이벤트 수신 시각: (없음)", stderr, StringComparison.Ordinal);
                receiptPath = DefaultReceiptPath();
                Assert.Contains(ReceiptCommand.FollowUpCommand("watch", receiptPath), stderr, StringComparison.Ordinal);

                var receipt = ReadReceipt(receiptPath);
                Assert.Equal(OperationPhase.Acknowledged, receipt.Phase);
                Assert.Equal(FixtureBuildId, receipt.BuildId);
                Assert.Null(receipt.LastObservation);
                AssertLocalAbort(receipt, OperationLocalAbort.WatchTimeout);
            }

            using (var server = new GrpcTestServer())
            {
                server.Fake.WatchEvents = new List<ProtoBuildEvent>
                {
                    new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = FixtureBuildId },
                };

                Assert.Equal(0, Reenter(server, "watch", receiptPath, out _, out _));
                Assert.Empty(server.Fake.SubmitRequests);

                var receipt = ReadReceipt(receiptPath);
                Assert.Equal(OperationPhase.Terminal, receipt.Phase);
                Assert.Equal(OperationObservation.SucceededOutcome, receipt.LastObservation!.Outcome);
                Assert.Null(receipt.LocalAbort);
            }
        }

        // S2-04-C06: build_id arrives before connect-timeout; the watch outlives it and ends Succeeded before watch-timeout
        [Fact]
        public void StagedTimers_BuildIdDisarmsConnect_ArmsWatch_SucceededExit0_LateTimersChangeNothing()
        {
            using var server = NewServer();
            server.Fake.WatchEventDelay = TimeSpan.FromMilliseconds(1500);
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = FixtureBuildId },
            };

            var stopwatch = Stopwatch.StartNew();
            var exitCode = Submit(server, out _, out var stderr, "--connect-timeout", "1", "--watch-timeout", "3s");
            stopwatch.Stop();

            Assert.Equal(0, exitCode);
            Assert.True(stopwatch.Elapsed > TimeSpan.FromSeconds(1), $"watch finished before connect-timeout: {stopwatch.Elapsed}");
            Assert.Empty(server.Fake.CancelledBuildIds);
            Assert.DoesNotContain("타임아웃", stderr, StringComparison.Ordinal);

            var receiptPath = DefaultReceiptPath();
            var terminalBytes = File.ReadAllBytes(receiptPath);
            var receipt = ReadReceipt(receiptPath);
            Assert.Equal(OperationPhase.Terminal, receipt.Phase);
            Assert.Equal(OperationObservation.SucceededOutcome, receipt.LastObservation!.Outcome);
            Assert.Null(receipt.LocalAbort);

            // past the watch-timeout deadline: nothing rewrites the terminal receipt or sends a cancel
            Thread.Sleep(TimeSpan.FromSeconds(2));
            Assert.Equal(terminalBytes, File.ReadAllBytes(receiptPath));
            Assert.Empty(server.Fake.CancelledBuildIds);
        }

        // Guardrail P3-1: a watch RPC error is not a build failure; the receipt stays re-watchable
        [Fact]
        public void ReceiptWatch_RpcErrorMidStream_Exit1_NotRecordedAsFailed_ReceiptUnchanged()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            var before = File.ReadAllBytes(receiptPath);
            using var server = new GrpcTestServer();
            server.Fake.WatchEvents = RunningEvents();
            server.Fake.WatchFailure = new RpcException(new Status(StatusCode.Unavailable, "fixture: watch stream dropped"));

            var exitCode = Reenter(server, "watch", receiptPath, out _, out var stderr);

            Assert.Equal(1, exitCode);
            Assert.Contains("원격 빌드 상태는 확인하지 못했습니다", stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("빌드 실패", stderr, StringComparison.Ordinal);
            Assert.Empty(server.Fake.CancelledBuildIds);
            Assert.Equal(before, File.ReadAllBytes(receiptPath));
        }

        // local_abort may not sit next to an observed terminal, and its remote state is never anything but unknown
        [Theory]
        [InlineData("terminal")]
        [InlineData("remote_succeeded")]
        [InlineData("unknown_reason")]
        public void LocalAbort_InvalidRecord_IsRejectedOnOpen_NoRpc(string variant)
        {
            var receiptPath = CreateAcknowledgedReceipt();
            var receipt = ReadReceipt(receiptPath);
            var abort = new OperationLocalAbort
            {
                Reason = variant == "unknown_reason" ? "server_cancelled" : OperationLocalAbort.UserCancel,
                RemoteBuildState = variant == "remote_succeeded" ? "succeeded" : OperationLocalAbort.UnknownRemoteState,
                RecordedAt = "2026-10-11T00:00:00.000Z",
            };
            var tampered = variant == "terminal"
                ? receipt with
                {
                    Phase = OperationPhase.Terminal,
                    LastObservation = new OperationObservation { Status = "Succeeded", Outcome = OperationObservation.SucceededOutcome, ObservedAt = "2026-10-11T00:00:00.000Z" },
                    LocalAbort = abort,
                }
                : receipt with { LocalAbort = abort };
            File.WriteAllText(receiptPath, JsonSerializer.Serialize(tampered));
            using var server = new GrpcTestServer();

            Assert.Equal(2, Reenter(server, "watch", receiptPath, out _, out var stderr));
            Assert.Contains(LocalOperationStore.InvalidCode, stderr, StringComparison.Ordinal);
            Assert.Empty(server.Fake.CallOrder);
        }

        private static void AssertLocalAbort(OperationReceipt receipt, string reason)
        {
            Assert.NotNull(receipt.LocalAbort);
            Assert.Equal(reason, receipt.LocalAbort!.Reason);
            Assert.Equal(OperationLocalAbort.UnknownRemoteState, receipt.LocalAbort.RemoteBuildState);
            Assert.False(string.IsNullOrWhiteSpace(receipt.LocalAbort.RecordedAt));
        }

        // the fake's CancelToolBuild answers "Interrupted"; the CLI must not turn that into an observed result
        private static void AssertNoServerResultClaimed(string stdout, string stderr)
        {
            foreach (var text in new[] { stdout, stderr })
            {
                Assert.DoesNotContain("Interrupted", text, StringComparison.Ordinal);
                Assert.DoesNotContain("[성공]", text, StringComparison.Ordinal);
                Assert.DoesNotContain("최종 상태를 receipt에 기록했습니다", text, StringComparison.Ordinal);
                Assert.DoesNotContain("서버 빌드가 취소되었습니다", text, StringComparison.Ordinal);
            }
        }

        private static List<ProtoBuildEvent> RunningEvents() => new()
        {
            new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
        };

        private static GrpcTestServer NewServer()
        {
            var server = new GrpcTestServer();
            server.Fake.OnSubmitToolBuild = _ => new Nodevault.V1.SubmitToolBuildResponse { BuildId = FixtureBuildId, Status = "Requested" };
            return server;
        }

        private int SubmitUntilUserCancel(GrpcTestServer server, out string stdout, out string stderr)
        {
            using var userCancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            userCancel.CancelAfter(_userCancelDelay);
            return Submit(server, userCancel.Token, out stdout, out stderr);
        }

        private int Submit(GrpcTestServer server, out string stdout, out string stderr, params string[] extraArgs) =>
            Submit(server, TestContext.Current.CancellationToken, out stdout, out stderr, extraArgs);

        private int Submit(
            GrpcTestServer server, CancellationToken cancellationToken, out string stdout, out string stderr, params string[] extraArgs)
        {
            using var client = new GrpcToolSpecClient(server.Channel);
            using var stdoutWriter = new StringWriter();
            using var stderrWriter = new StringWriter();
            var args = new[] { "submit", WriteRecipe() }.Concat(extraArgs).ToArray();
            var exitCode = SubmitCommand.Run(args, stdoutWriter, stderrWriter, client, () => FixedRequestId, cancellationToken);
            stdout = stdoutWriter.ToString();
            stderr = stderrWriter.ToString();
            return exitCode;
        }

        private static int Reenter(GrpcTestServer server, string verb, string receiptPath, out string stdout, out string stderr)
        {
            using var client = new GrpcToolSpecClient(server.Channel);
            using var stdoutWriter = new StringWriter();
            using var stderrWriter = new StringWriter();
            var exitCode = ReceiptCommand.Run(
                new[] { "receipt", verb, receiptPath }, stdoutWriter, stderrWriter, client, TestContext.Current.CancellationToken);
            stdout = stdoutWriter.ToString();
            stderr = stderrWriter.ToString();
            return exitCode;
        }

        private string CreateAcknowledgedReceipt()
        {
            using var server = NewServer();
            server.Fake.WatchEvents = RunningEvents();
            Assert.Equal(1, Submit(server, out _, out _));
            var receiptPath = DefaultReceiptPath();
            Assert.Equal(OperationPhase.Acknowledged, ReadReceipt(receiptPath).Phase);
            return receiptPath;
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

        private static JsonElement SingleJsonl(string stdout)
        {
            var line = Assert.Single(stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }

        private string DefaultReceiptPath() => Path.Join(_workDir, ".nodekit", "receipts", FixedRequestId + ".json");

        private string WriteRecipe()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.Conda,
                ToolName = "bwa-mem",
                Version = "0.7.17",
                Script = "run.sh",
                BaseImage = "condaforge/miniforge3:24.3.0-0@" + Digest,
                PackageEngine = "conda",
                Channels = new List<string> { "bioconda" },
                Packages = new List<string> { "bwa=0.7.17=h5bf99c6_8" },
            };
            var path = Path.Join(_workDir, "input-" + Guid.NewGuid() + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(recipe, RecipeCreateCommand.JsonOptions));
            return path;
        }
    }
}
