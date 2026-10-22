using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Grpc.Core;
using NodeKit.Authoring.Recipes;
using NodeKit.Cli.Localization;
using NodeKit.Cli.Operations;
using NodeKit.Cli.Tests.Fakes;
using NodeKit.Grpc;
using Xunit;
using ProtoBuildEvent = Nodevault.V1.BuildEvent;
using ProtoBuildEventKind = Nodevault.V1.BuildEventKind;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// P05.result slice2 (NodeKit v0.9.1 S2-05, CAD-NK-P05RESULT-S2-JSONL-20261011-r1):
    /// `nodekit receipt watch|cancel &lt;receipt.json&gt; --format jsonl` emits nodekit.receipt.v1. Each test makes the
    /// receipt with a real `nodekit submit` run against one in-process fake, then re-enters with a fresh client
    /// against a second fake whose capture shows exactly which RPCs were sent. The fake keeps no server state;
    /// these tests prove local output, exit codes, RPC choice and receipt recording only.
    /// </summary>
    public class ReceiptJsonlP05Tests : IDisposable
    {
        private const string FixedRequestId = "33333333-3333-3333-3333-333333333333";

        private const string FixtureBuildId = "build-fixture-001";

        private const string ForeignBuildId = "build-foreign-999";

        private const string Digest =
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private const string ImageDigest =
            "sha256:fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

        private const string ImageRef = "harbor.local/tools/bwa-mem:0.7.17";

        private static readonly string[] _watchOnly = { "Watch" };

        private static readonly string[] _fixtureBuildIdOnce = { FixtureBuildId };

        private static readonly HashSet<string> _allowedKeys = new(StringComparer.Ordinal)
        {
            "schema_version", "command", "type", "build_id", "status", "error_code", "remote_build_state",
            "message", "image_ref", "image_digest", "integrity_health", "exit_code", "recovery",
        };

        private readonly string _workDir = Path.Join(Path.GetTempPath(), "nodekit-p05-receipt-jsonl-tests-" + Guid.NewGuid());

        private readonly IDisposable _resolveClientOverride =
            ResolveRecipeClientTestOverride.Use(NullResolveRecipeClient.Instance);

        public ReceiptJsonlP05Tests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
            _resolveClientOverride.Dispose();
        }

        [Fact]
        public void Watch_Succeeded_StateThenCompleted_None_ReceiptTerminal()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            using var server = new GrpcTestServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId, Message = "say \"hi\"\nnext line" },
                new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = FixtureBuildId, ImageRef = ImageRef, ImageDigest = ImageDigest, IntegrityHealth = "Healthy" },
            };

            var exitCode = Reenter(server, "watch", receiptPath, out var stdout, out var stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);
            Assert.Equal(_watchOnly, server.Fake.CallOrder);
            Assert.Empty(server.Fake.CancelledBuildIds);
            var records = ParseRecords(stdout, "watch");
            Assert.Equal(2, records.Count);

            var state = records[0];
            Assert.Equal("state", Str(state, "type"));
            Assert.Equal(FixtureBuildId, Str(state, "build_id"));
            Assert.Equal("Running", Str(state, "status"));
            Assert.Equal("say \"hi\"\nnext line", Str(state, "message"));
            Assert.False(state.TryGetProperty("exit_code", out _));
            Assert.False(state.TryGetProperty("recovery", out _));

            var completed = records[1];
            Assert.Equal("completed", Str(completed, "type"));
            Assert.Equal(FixtureBuildId, Str(completed, "build_id"));
            Assert.Equal("Succeeded", Str(completed, "status"));
            Assert.Equal(0, completed.GetProperty("exit_code").GetInt32());
            Assert.Equal("none", Str(completed, "recovery"));
            Assert.Equal(ImageRef, Str(completed, "image_ref"));
            Assert.Equal(ImageDigest, Str(completed, "image_digest"));
            Assert.Equal("Healthy", Str(completed, "integrity_health"));
            Assert.False(completed.TryGetProperty("error_code", out _));
            Assert.False(completed.TryGetProperty("remote_build_state", out _));

            var receipt = ReadReceipt(receiptPath);
            Assert.Equal(OperationPhase.Terminal, receipt.Phase);
            Assert.Equal(OperationObservation.SucceededOutcome, receipt.LastObservation!.Outcome);
        }

        [Fact]
        public void Watch_SucceededPartial_KeepsIntegrityField_NoHumanWarning()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            using var server = new GrpcTestServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = FixtureBuildId, ImageDigest = ImageDigest, IntegrityHealth = "Partial" },
            };

            var exitCode = Reenter(server, "watch", receiptPath, out var stdout, out var stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);
            var completed = Assert.Single(ParseRecords(stdout, "watch"));
            Assert.Equal("Succeeded", Str(completed, "status"));
            Assert.Equal("Partial", Str(completed, "integrity_health"));
            Assert.Equal(ImageDigest, Str(completed, "image_digest"));
        }

        [Fact]
        public void Watch_Failed_BuildFailed_Terminal_ReceiptTerminal()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            using var server = new GrpcTestServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Failed", BuildId = FixtureBuildId, Message = "fixture: build broke" },
            };

            var exitCode = Reenter(server, "watch", receiptPath, out var stdout, out var stderr);

            Assert.Equal(1, exitCode);
            Assert.Contains("fixture: build broke", stderr, StringComparison.Ordinal);
            var completed = Assert.Single(ParseRecords(stdout, "watch"));
            Assert.Equal("Failed", Str(completed, "status"));
            Assert.Equal("BUILD_FAILED", Str(completed, "error_code"));
            Assert.Equal("failed", Str(completed, "remote_build_state"));
            Assert.Equal("terminal", Str(completed, "recovery"));
            Assert.Equal(1, completed.GetProperty("exit_code").GetInt32());
            Assert.Equal("fixture: build broke", Str(completed, "message"));
            Assert.Equal(OperationObservation.FailedOutcome, ReadReceipt(receiptPath).LastObservation!.Outcome);
        }

        // Codex r4240668558: an observed terminal name other than Failed (Interrupted) is kept in status
        [Fact]
        public void Watch_Interrupted_KeepsObservedStatus_ClassifiedBuildFailed()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            using var server = new GrpcTestServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Interrupted", BuildId = FixtureBuildId, Message = "fixture: interrupted" },
            };

            var exitCode = Reenter(server, "watch", receiptPath, out var stdout, out _);

            Assert.Equal(1, exitCode);
            var completed = Assert.Single(ParseRecords(stdout, "watch"));
            Assert.Equal("Interrupted", Str(completed, "status"));
            Assert.Equal("BUILD_FAILED", Str(completed, "error_code"));
            Assert.Equal("failed", Str(completed, "remote_build_state"));
            Assert.Equal("terminal", Str(completed, "recovery"));
            var receipt = ReadReceipt(receiptPath);
            Assert.Equal(OperationObservation.FailedOutcome, receipt.LastObservation!.Outcome);
            Assert.Equal("Interrupted", receipt.LastObservation.Status);
        }

        [Fact]
        public void Watch_StreamEndsWithoutTerminal_Uncertain_NoStatus_ReceiptUnchanged()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            var before = File.ReadAllBytes(receiptPath);
            using var server = new GrpcTestServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
            };

            var exitCode = Reenter(server, "watch", receiptPath, out var stdout, out _);

            Assert.Equal(1, exitCode);
            var records = ParseRecords(stdout, "watch");
            Assert.Equal("state", Str(records[0], "type"));
            AssertUnknownCompleted(records[^1], "STREAM_ENDED_WITHOUT_RESULT", 1, "uncertain");
            Assert.Equal(before, File.ReadAllBytes(receiptPath));
        }

        [Fact]
        public void Watch_RpcError_WatchFailed_Uncertain_ReceiptUnchanged()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            var before = File.ReadAllBytes(receiptPath);
            using var server = new GrpcTestServer();
            server.Fake.WatchFailure = new RpcException(new Status(StatusCode.Unavailable, "fixture: watch unavailable"));

            var exitCode = Reenter(server, "watch", receiptPath, out var stdout, out var stderr);

            Assert.Equal(1, exitCode);
            Assert.NotEmpty(stderr);
            AssertUnknownCompleted(Assert.Single(ParseRecords(stdout, "watch")), "WATCH_FAILED", 1, "uncertain");
            Assert.Equal(before, File.ReadAllBytes(receiptPath));
        }

        [Fact]
        public void Watch_ForeignBuildEvent_UnexpectedError_ForeignEventNotStored()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            using var server = new GrpcTestServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = ForeignBuildId },
            };

            var exitCode = Reenter(server, "watch", receiptPath, out var stdout, out _);

            Assert.Equal(1, exitCode);
            var completed = Assert.Single(ParseRecords(stdout, "watch"));
            AssertUnknownCompleted(completed, "UNEXPECTED_ERROR", 1, "uncertain");
            Assert.Equal(FixtureBuildId, Str(completed, "build_id"));
            Assert.Contains(ForeignBuildId, Str(completed, "message"), StringComparison.Ordinal);
            Assert.Equal(OperationPhase.Acknowledged, ReadReceipt(receiptPath).Phase);
        }

        // watch Ctrl-C stops only the observation: no server cancel, no remote terminal name
        [Fact]
        public void Watch_UserStop_WatchStopped_130_NoCancelRpc_NoStatus()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            var before = File.ReadAllBytes(receiptPath);
            using var server = new GrpcTestServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
            };
            server.Fake.HangAfterEvents = true;
            using var userCancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            userCancel.CancelAfter(TimeSpan.FromMilliseconds(500));

            var exitCode = ReenterUntil(server, "watch", receiptPath, userCancel.Token, out var stdout, out var stderr);

            Assert.Equal(130, exitCode);
            Assert.Empty(server.Fake.CancelledBuildIds);
            var records = ParseRecords(stdout, "watch");
            AssertUnknownCompleted(records[^1], "WATCH_STOPPED", 130, "terminal");
            Assert.DoesNotContain("Cancelled", stdout, StringComparison.Ordinal);
            Assert.EndsWith(ReceiptCommand.FollowUpCommand("cancel", receiptPath), stderr.TrimEnd(), StringComparison.Ordinal);
            Assert.Equal(before, File.ReadAllBytes(receiptPath));
        }

        // CANCEL_REQUESTED means request sent/answered only — never "the server stopped"
        [Fact]
        public void Cancel_Success_CancelRequested_Exit0_Uncertain_NoStatus_FollowUpOnStderr()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            var before = File.ReadAllBytes(receiptPath);
            using var server = new GrpcTestServer();

            var exitCode = Reenter(server, "cancel", receiptPath, out var stdout, out var stderr);

            Assert.Equal(0, exitCode);
            Assert.Equal(_fixtureBuildIdOnce, server.Fake.CancelledBuildIds);
            Assert.Empty(server.Fake.CallOrder);
            var completed = Assert.Single(ParseRecords(stdout, "cancel"));
            AssertUnknownCompleted(completed, "CANCEL_REQUESTED", 0, "uncertain");
            Assert.Equal(FixtureBuildId, Str(completed, "build_id"));
            Assert.EndsWith(ReceiptCommand.FollowUpCommand("watch", receiptPath), stderr.TrimEnd(), StringComparison.Ordinal);
            Assert.Equal(before, File.ReadAllBytes(receiptPath));
        }

        [Fact]
        public void Cancel_RpcFails_CancelRequestFailed_Exit1()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            var before = File.ReadAllBytes(receiptPath);
            using var server = new GrpcTestServer();
            server.Fake.CancelToolBuildFailure = new RpcException(new Status(StatusCode.Unavailable, "fixture: cancel unavailable"));

            var exitCode = Reenter(server, "cancel", receiptPath, out var stdout, out _);

            Assert.Equal(1, exitCode);
            AssertUnknownCompleted(Assert.Single(ParseRecords(stdout, "cancel")), "CANCEL_REQUEST_FAILED", 1, "uncertain");
            Assert.Equal(before, File.ReadAllBytes(receiptPath));
        }

        [Fact]
        public void Cancel_NoAnswerWithinFiveSeconds_CancelRequestTimeout_Exit1()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            using var server = new GrpcTestServer();
            server.Fake.HangOnCancelToolBuild = true;

            var exitCode = Reenter(server, "cancel", receiptPath, out var stdout, out _);

            Assert.Equal(1, exitCode);
            Assert.Equal(_fixtureBuildIdOnce, server.Fake.CancelledBuildIds);
            AssertUnknownCompleted(Assert.Single(ParseRecords(stdout, "cancel")), "CANCEL_REQUEST_TIMEOUT", 1, "uncertain");
        }

        [Fact]
        public void Cancel_UserAbort_CancelRequestAborted_130()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            using var server = new GrpcTestServer();
            server.Fake.HangOnCancelToolBuild = true;
            using var userCancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            userCancel.CancelAfter(TimeSpan.FromMilliseconds(500));

            var exitCode = ReenterUntil(server, "cancel", receiptPath, userCancel.Token, out var stdout, out _);

            Assert.Equal(130, exitCode);
            AssertUnknownCompleted(Assert.Single(ParseRecords(stdout, "cancel")), "CANCEL_REQUEST_ABORTED", 130, "uncertain");
        }

        // r4240863799: Ctrl-C itself (not an injected token) must reach the cancel request. The caller token never
        // fires here, so the only way to abort the hanging request is the registered interrupt callback.
        [Fact]
        public void Cancel_CtrlC_ReachesCancelRequest_OneAbortedCompletedRecord_HandlerRemoved()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            var before = File.ReadAllBytes(receiptPath);
            using var server = new GrpcTestServer();
            server.Fake.HangOnCancelToolBuild = true;
            var interrupt = new RecordingInterrupt();
            using var press = new Timer(_ => interrupt.Press(), null, TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);

            var exitCode = Run(
                server,
                new[] { "receipt", "cancel", receiptPath, "--format", "jsonl" },
                CancellationToken.None,
                interrupt.Register,
                out var stdout,
                out var stderr);

            Assert.Equal(130, exitCode);
            Assert.Equal(_fixtureBuildIdOnce, server.Fake.CancelledBuildIds);
            AssertUnknownCompleted(Assert.Single(ParseRecords(stdout, "cancel")), "CANCEL_REQUEST_ABORTED", 130, "uncertain");
            Assert.Contains("취소 요청을 중단했습니다", stderr, StringComparison.Ordinal);
            Assert.Equal(1, interrupt.Registrations);
            Assert.True(interrupt.Removed);
            Assert.Equal(before, File.ReadAllBytes(receiptPath));
        }

        // the handler is removed on every cancel outcome, not only after an abort
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Cancel_AnyOutcome_RegistersCtrlCOnce_AndRemovesIt(bool jsonl)
        {
            var receiptPath = CreateAcknowledgedReceipt();
            using var server = new GrpcTestServer();
            var interrupt = new RecordingInterrupt();
            var args = jsonl
                ? new[] { "receipt", "cancel", receiptPath, "--format", "jsonl" }
                : new[] { "receipt", "cancel", receiptPath };

            var exitCode = Run(server, args, TestContext.Current.CancellationToken, interrupt.Register, out _, out _);

            Assert.Equal(0, exitCode);
            Assert.Equal(1, interrupt.Registrations);
            Assert.True(interrupt.Removed);
        }

        // watch uses the same interrupt wiring: Ctrl-C stops observation only (no Cancel RPC) and the handler is removed
        [Fact]
        public void Watch_CtrlC_StopsObservationOnly_HandlerRemoved()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            using var server = new GrpcTestServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
            };
            server.Fake.HangAfterEvents = true;
            var interrupt = new RecordingInterrupt();
            using var press = new Timer(_ => interrupt.Press(), null, TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);

            var exitCode = Run(
                server,
                new[] { "receipt", "watch", receiptPath, "--format", "jsonl" },
                CancellationToken.None,
                interrupt.Register,
                out var stdout,
                out _);

            Assert.Equal(130, exitCode);
            Assert.Empty(server.Fake.CancelledBuildIds);
            AssertUnknownCompleted(ParseRecords(stdout, "watch")[^1], "WATCH_STOPPED", 130, "terminal");
            Assert.Equal(1, interrupt.Registrations);
            Assert.True(interrupt.Removed);
        }

        // receipt refusals: existing local code, exit 2, RPC 0, exactly one completed line
        [Theory]
        [InlineData("watch", "corrupt", LocalOperationStore.InvalidCode)]
        [InlineData("cancel", "corrupt", LocalOperationStore.InvalidCode)]
        [InlineData("watch", "submit-in-flight", ToolSpecOperationRunner.NotResumableCode)]
        [InlineData("cancel", "submit-in-flight", ToolSpecOperationRunner.NotResumableCode)]
        [InlineData("cancel", "terminal", ToolSpecOperationRunner.NotResumableCode)]
        public void Refusal_ExistingLocalCode_Exit2_NoRpc_OneCompletedLine(string verb, string kind, string expectedCode)
        {
            var receiptPath = kind switch
            {
                "corrupt" => CorruptReceipt(CreateAcknowledgedReceipt()),
                "submit-in-flight" => CreateSubmitInFlightReceipt(),
                "terminal" => CreateTerminalReceipt(),
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            var before = File.ReadAllBytes(receiptPath);
            using var server = new GrpcTestServer();

            var exitCode = Reenter(server, verb, receiptPath, out var stdout, out _);

            Assert.Equal(2, exitCode);
            Assert.Empty(server.Fake.CallOrder);
            Assert.Empty(server.Fake.CancelledBuildIds);
            var completed = Assert.Single(ParseRecords(stdout, verb));
            Assert.Equal("completed", Str(completed, "type"));
            Assert.Equal(expectedCode, Str(completed, "error_code"));
            Assert.Equal(2, completed.GetProperty("exit_code").GetInt32());
            Assert.Equal("terminal", Str(completed, "recovery"));
            Assert.False(completed.TryGetProperty("status", out _));
            Assert.False(completed.TryGetProperty("remote_build_state", out _));
            Assert.Equal(kind == "terminal", completed.TryGetProperty("build_id", out _));
            Assert.Equal(before, File.ReadAllBytes(receiptPath));
        }

        // usage/option errors end before the format is fixed: exit 2, no JSONL, no RPC
        [Theory]
        [InlineData("--format", "xml")]
        [InlineData("--format", null)]
        [InlineData("--url", "http://x")]
        public void UsageErrors_Exit2_NoJsonl_NoRpc(string option, string? value)
        {
            var receiptPath = CreateAcknowledgedReceipt();
            using var server = new GrpcTestServer();
            var args = value is null
                ? new[] { "receipt", "cancel", receiptPath, option }
                : new[] { "receipt", "cancel", receiptPath, option, value };

            var exitCode = Run(server, args, TestContext.Current.CancellationToken, out var stdout, out var stderr);

            Assert.Equal(2, exitCode);
            Assert.Empty(stdout);
            Assert.Contains("사용법", stderr, StringComparison.Ordinal);
            Assert.Empty(server.Fake.CancelledBuildIds);
            Assert.Empty(server.Fake.CallOrder);
        }

        // the human default is unchanged: explicit --format human equals no --format
        [Theory]
        [InlineData("watch")]
        [InlineData("cancel")]
        public void FormatHuman_SameOutputAndExitAsDefault(string verb)
        {
            var results = new List<(int Exit, string Stdout, string Stderr)>();
            foreach (var extra in new[] { Array.Empty<string>(), new[] { "--format", "HUMAN" } })
            {
                var receiptPath = CreateAcknowledgedReceipt(Path.Join(_workDir, "run-" + results.Count));
                using var server = new GrpcTestServer();
                server.Fake.WatchEvents = new List<ProtoBuildEvent>
                {
                    new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = FixtureBuildId, ImageDigest = ImageDigest },
                };
                var args = new[] { "receipt", verb, receiptPath }.Concat(extra).ToArray();
                var exitCode = Run(server, args, TestContext.Current.CancellationToken, out var stdout, out var stderr);
                results.Add((exitCode, stdout.Replace(receiptPath, "<receipt>", StringComparison.Ordinal), stderr));
            }

            Assert.Equal(results[0], results[1]);
            Assert.DoesNotContain("nodekit.receipt.v1", results[0].Stdout, StringComparison.Ordinal);
        }

        [Fact]
        public void CliApp_RoutesReceiptFormatJsonl()
        {
            var receiptPath = CreateSubmitInFlightReceipt();
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            Assert.Equal(2, CliApp.Run(new[] { "receipt", "watch", receiptPath, "--format", "jsonl" }, stdout, stderr));
            var completed = Assert.Single(ParseRecords(stdout.ToString(), "watch"));
            Assert.Equal(ToolSpecOperationRunner.NotResumableCode, Str(completed, "error_code"));
        }

        // every nonempty stdout line is one JSON object of this schema/command with only known, non-null keys,
        // and exactly the last one is completed
        private static List<JsonElement> ParseRecords(string stdout, string command)
        {
            var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
            Assert.NotEmpty(lines);
            var records = new List<JsonElement>();
            foreach (var root in lines.Select(ParseObject))
            {
                Assert.Equal(JsonValueKind.Object, root.ValueKind);
                foreach (var property in root.EnumerateObject())
                {
                    Assert.Contains(property.Name, _allowedKeys);
                    Assert.NotEqual(JsonValueKind.Null, property.Value.ValueKind);
                }

                Assert.Equal("nodekit.receipt.v1", Str(root, "schema_version"));
                Assert.Equal(command, Str(root, "command"));
                records.Add(root);
            }

            Assert.Equal(1, records.Count(r => Str(r, "type") == "completed"));
            Assert.Equal("completed", Str(records[^1], "type"));
            return records;
        }

        private static JsonElement ParseObject(string line)
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.Clone();
        }

        private static void AssertUnknownCompleted(JsonElement completed, string errorCode, int exitCode, string recovery)
        {
            Assert.Equal("completed", Str(completed, "type"));
            Assert.Equal(errorCode, Str(completed, "error_code"));
            Assert.Equal("unknown", Str(completed, "remote_build_state"));
            Assert.Equal(recovery, Str(completed, "recovery"));
            Assert.Equal(exitCode, completed.GetProperty("exit_code").GetInt32());
            Assert.False(completed.TryGetProperty("status", out _));
        }

        private static string Str(JsonElement element, string name) => element.GetProperty(name).GetString()!;

        // UF01b (#96) U02: receipt human 문구(stderr)는 언어를 따르지만 JSONL bytes·exit code는 언어와 무관하다.
        [Theory]
        [InlineData("success", "취소 요청을 보냈습니다 (build ID: ", "Cancel request sent (build ID: ")]
        [InlineData("rpc-fail", "취소 요청이 실패했습니다 (build ID: ", "Cancel request failed (build ID: ")]
        [InlineData("terminal", "이미 최종 상태(", "This build already has a final observed state (")]
        public void Cancel_Language_ChangesHumanStderrOnly_JsonlBytesAndExitIdentical(string scenario, string koPrefix, string enPrefix)
        {
            var receiptPath = scenario == "terminal" ? CreateTerminalReceipt() : CreateAcknowledgedReceipt();
            var before = File.ReadAllBytes(receiptPath);

            var ko = CancelIn(CliLanguage.Ko, scenario, receiptPath);
            var en = CancelIn(CliLanguage.En, scenario, receiptPath);

            Assert.Equal(ko.Exit, en.Exit);
            Assert.Equal(ko.Stdout, en.Stdout);
            Assert.Single(ParseRecords(en.Stdout, "cancel"));
            Assert.StartsWith(koPrefix, ko.Stderr, StringComparison.Ordinal);
            Assert.StartsWith(enPrefix, en.Stderr, StringComparison.Ordinal);
            Assert.Equal(before, File.ReadAllBytes(receiptPath));
        }

        // UF01b P3-U02-PIN (#96): JSONL message를 싣는 나머지 receipt 경로도 같은 규칙이다 — stdout bytes·exit는
        // 언어와 무관하고 stderr만 선택 언어를 따른다. ko에서는 Format과 FormatMachine이 같은 문구라 ko 단독 시험으로는
        // 구분되지 않으므로 en과 비교한다.
        [Theory]
        [InlineData("watch-stopped", "WATCH_STOPPED", 130, "관찰을 멈췄습니다 (build ID: ", "Stopped watching (build ID: ")]
        [InlineData("cancel-aborted", "CANCEL_REQUEST_ABORTED", 130, "취소 요청을 중단했습니다 (build ID: ", "Cancel request was interrupted (build ID: ")]
        [InlineData("cancel-timeout", "CANCEL_REQUEST_TIMEOUT", 1, "5초 안에 응답이 없었습니다", "no response within 5 seconds")]
        [InlineData("endpoint-unusable", LocalOperationStore.InvalidCode, 2, "receipt의 NodeVault endpoint로 연결할 수 없습니다", "Cannot connect to the receipt's NodeVault endpoint")]
        public void Language_ChangesHumanStderrOnly_MachineMessageBytesIdentical(
            string scenario, string errorCode, int exitCode, string koText, string enText)
        {
            var receiptPath = CreateAcknowledgedReceipt();
            var before = File.ReadAllBytes(receiptPath);
            var verb = scenario == "watch-stopped" ? "watch" : "cancel";

            var ko = ScenarioIn(CliLanguage.Ko, scenario, verb, receiptPath);
            var en = ScenarioIn(CliLanguage.En, scenario, verb, receiptPath);

            Assert.Equal(exitCode, ko.Exit);
            Assert.Equal(exitCode, en.Exit);
            Assert.Equal(ko.Stdout, en.Stdout);
            var completed = ParseRecords(en.Stdout, verb)[^1];
            Assert.Equal(errorCode, Str(completed, "error_code"));
            Assert.False(string.IsNullOrEmpty(Str(completed, "message")));
            Assert.Contains(koText, ko.Stderr, StringComparison.Ordinal);
            Assert.Contains(enText, en.Stderr, StringComparison.Ordinal);
            Assert.DoesNotContain(koText, en.Stderr, StringComparison.Ordinal);
            Assert.Equal(before, File.ReadAllBytes(receiptPath));
        }

        // UF01b P3-PREFIX-EN (#96): human watch under en — header, event prefixes, digest line and final line.
        [Fact]
        public void Watch_Human_En_HeaderEventPrefixesAndFinalLine_NoHangul()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            using var server = new GrpcTestServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.JobCreated, Status = "Requested", BuildId = FixtureBuildId },
                new() { Kind = ProtoBuildEventKind.JobRunning, Status = "Running", BuildId = FixtureBuildId },
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId, Message = "step 1" },
                new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = FixtureBuildId, ImageRef = ImageRef, ImageDigest = ImageDigest },
            };

            int exitCode;
            string stdout;
            string stderr;
            using (CliText.Use(CliLanguage.En))
            {
                exitCode = Run(server, new[] { "receipt", "watch", receiptPath }, TestContext.Current.CancellationToken, out stdout, out stderr);
            }

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);
            var lines = stdout.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            Assert.StartsWith("receipt watch: ", lines[0], StringComparison.Ordinal);
            Assert.StartsWith($"  request ID: {FixedRequestId}, build ID: {FixtureBuildId}, endpoint: ", lines[1], StringComparison.Ordinal);
            Assert.Equal(
                new[]
                {
                    string.Empty,
                    "[build started] Requested",
                    "[running] Running",
                    "[observed] Running step 1",
                    "[succeeded] Succeeded",
                    $"Image digest: {ImageRef}@{ImageDigest}",
                    "Recorded the final state in the receipt: Succeeded",
                    string.Empty,
                },
                lines.Skip(2));
            Assert.DoesNotMatch("[가-힣]", stdout);
        }

        private static (int Exit, string Stdout, string Stderr) ScenarioIn(
            CliLanguage language, string scenario, string verb, string receiptPath)
        {
            using var server = new GrpcTestServer();
            using var userCancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            switch (scenario)
            {
                // 둘 다 끝없이 기다리는 RPC를 사용자 취소로 끝낸다: 취소가 언제 오든 같은 경로다.
                case "watch-stopped":
                    server.Fake.WatchEvents = new List<ProtoBuildEvent>
                    {
                        new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
                    };
                    server.Fake.HangAfterEvents = true;
                    userCancel.CancelAfter(TimeSpan.FromMilliseconds(500));
                    break;
                case "cancel-aborted":
                    server.Fake.HangOnCancelToolBuild = true;
                    userCancel.CancelAfter(TimeSpan.FromMilliseconds(500));
                    break;
                case "cancel-timeout":
                    server.Fake.HangOnCancelToolBuild = true;
                    break;
                case "endpoint-unusable":
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario));
            }

            using var scope = CliText.Use(language);
            if (scenario == "endpoint-unusable")
            {
                // client를 주입하지 않으면 receipt의 endpoint로 연결한다. 테스트 receipt의 endpoint는 URI가 아니다.
                using var stdoutWriter = new StringWriter();
                using var stderrWriter = new StringWriter();
                var refused = ReceiptCommand.Run(
                    new[] { "receipt", verb, receiptPath, "--format", "jsonl" },
                    stdoutWriter,
                    stderrWriter,
                    toolSpecClient: null,
                    TestContext.Current.CancellationToken,
                    new RecordingInterrupt().Register);
                Assert.Empty(server.Fake.CancelledBuildIds);
                return (refused, stdoutWriter.ToString(), stderrWriter.ToString());
            }

            var exit = ReenterUntil(server, verb, receiptPath, userCancel.Token, out var stdout, out var stderr);
            return (exit, stdout, stderr);
        }

        private static (int Exit, string Stdout, string Stderr) CancelIn(CliLanguage language, string scenario, string receiptPath)
        {
            using var server = new GrpcTestServer();
            if (scenario == "rpc-fail")
            {
                server.Fake.CancelToolBuildFailure = new RpcException(new Status(StatusCode.Unavailable, "fixture: cancel unavailable"));
            }

            using var scope = CliText.Use(language);
            var exit = Reenter(server, "cancel", receiptPath, out var stdout, out var stderr);
            return (exit, stdout, stderr);
        }

        private static int Reenter(GrpcTestServer server, string verb, string receiptPath, out string stdout, out string stderr) =>
            ReenterUntil(server, verb, receiptPath, TestContext.Current.CancellationToken, out stdout, out stderr);

        private static int ReenterUntil(
            GrpcTestServer server, string verb, string receiptPath, CancellationToken cancellationToken, out string stdout, out string stderr) =>
            Run(server, new[] { "receipt", verb, receiptPath, "--format", "jsonl" }, cancellationToken, out stdout, out stderr);

        private static int Run(GrpcTestServer server, string[] args, CancellationToken cancellationToken, out string stdout, out string stderr) =>
            Run(server, args, cancellationToken, new RecordingInterrupt().Register, out stdout, out stderr);

        private static int Run(
            GrpcTestServer server,
            string[] args,
            CancellationToken cancellationToken,
            Func<Action, IDisposable> interruptRegistration,
            out string stdout,
            out string stderr)
        {
            using var client = new GrpcToolSpecClient(server.Channel);
            using var stdoutWriter = new StringWriter();
            using var stderrWriter = new StringWriter();
            var exitCode = ReceiptCommand.Run(args, stdoutWriter, stderrWriter, client, cancellationToken, interruptRegistration);
            stdout = stdoutWriter.ToString();
            stderr = stderrWriter.ToString();
            return exitCode;
        }

        // Console.CancelKeyPress cannot be raised from a test; this stands in for it and records the handler lifetime.
        private sealed class RecordingInterrupt
        {
            private Action? _onInterrupt;

            private int _registrations;

            private int _removed;

            public int Registrations => Volatile.Read(ref _registrations);

            public bool Removed => Volatile.Read(ref _removed) == 1;

            public IDisposable Register(Action onInterrupt)
            {
                Volatile.Write(ref _onInterrupt, onInterrupt);
                Interlocked.Increment(ref _registrations);
                return new Removal(this);
            }

            // like a real Ctrl-C after the handler is gone, a press after removal does nothing
            public void Press()
            {
                if (!Removed)
                {
                    Volatile.Read(ref _onInterrupt)?.Invoke();
                }
            }

            private sealed class Removal : IDisposable
            {
                private readonly RecordingInterrupt _owner;

                public Removal(RecordingInterrupt owner) => _owner = owner;

                public void Dispose() => Interlocked.Exchange(ref _owner._removed, 1);
            }
        }

        private static string CorruptReceipt(string receiptPath)
        {
            var text = File.ReadAllText(receiptPath);
            File.WriteAllText(receiptPath, text[..(text.Length / 2)], new UTF8Encoding(false));
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

        // submit whose watch stream ends without a terminal event → acknowledged receipt with build_id
        private string CreateAcknowledgedReceipt(string? workDir = null)
        {
            if (workDir is not null)
            {
                Directory.CreateDirectory(workDir);
            }

            using var server = new GrpcTestServer();
            server.Fake.OnSubmitToolBuild = _ => new Nodevault.V1.SubmitToolBuildResponse { BuildId = FixtureBuildId, Status = "Requested" };
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
            };
            Assert.Equal(1, Submit(server, workDir));
            var receiptPath = DefaultReceiptPath(workDir);
            Assert.Equal(OperationPhase.Acknowledged, ReadReceipt(receiptPath).Phase);
            return receiptPath;
        }

        private string CreateTerminalReceipt()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            using var server = new GrpcTestServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = FixtureBuildId },
            };
            Assert.Equal(0, Reenter(server, "watch", receiptPath, out _, out _));
            Assert.Equal(OperationPhase.Terminal, ReadReceipt(receiptPath).Phase);
            return receiptPath;
        }

        // Submit response lost after Resolve → submit_in_flight receipt with no build_id
        private string CreateSubmitInFlightReceipt()
        {
            using var server = new GrpcTestServer();
            server.Fake.OnSubmitToolBuild = _ => throw new RpcException(new Status(StatusCode.Unavailable, "fixture: submit response lost"));
            Assert.Equal(1, Submit(server));
            var receiptPath = DefaultReceiptPath();
            Assert.Equal(OperationPhase.SubmitInFlight, ReadReceipt(receiptPath).Phase);
            return receiptPath;
        }

        private int Submit(GrpcTestServer server, string? workDir = null)
        {
            using var client = new GrpcToolSpecClient(server.Channel);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            return SubmitCommand.Run(new[] { "submit", WriteRecipe(workDir) }, stdout, stderr, client, () => FixedRequestId);
        }

        private string DefaultReceiptPath(string? workDir = null) =>
            Path.Join(workDir ?? _workDir, ".nodekit", "receipts", FixedRequestId + ".json");

        private string WriteRecipe(string? workDir = null)
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
            var path = Path.Join(workDir ?? _workDir, "input-" + Guid.NewGuid() + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(recipe, RecipeCreateCommand.JsonOptions));
            return path;
        }
    }
}
