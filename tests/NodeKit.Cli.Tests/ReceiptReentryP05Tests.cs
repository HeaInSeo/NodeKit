using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
    /// P05.reentry slice1 (NodeKit v0.9.1 S2-03): `nodekit receipt watch|cancel &lt;receipt.json&gt;` re-enters a
    /// saved operation by its build_id only. Each test makes the receipt with a real `nodekit submit` run against
    /// one in-process fake, then re-enters with a fresh client against a second fake, so the second fake's
    /// capture shows exactly which RPCs the re-entry sent. The fake does not dedup or keep server state; these
    /// tests prove local RPC choice, receipt recording and output only. S2-04 receipt assertions are slice2.
    /// </summary>
    public class ReceiptReentryP05Tests : IDisposable
    {
        private const string FixedRequestId = "22222222-2222-2222-2222-222222222222";

        private const string FixtureBuildId = "build-fixture-001";

        private const string ForeignBuildId = "build-foreign-999";

        private const string Digest =
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private const string ImageDigest =
            "sha256:fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

        private static readonly string[] _watchOnly = { "Watch" };

        private static readonly string[] _watchTwice = { "Watch", "Watch" };

        private readonly string _workDir = Path.Join(Path.GetTempPath(), "nodekit-p05-reentry-tests-" + Guid.NewGuid());

        private readonly IDisposable _resolveClientOverride =
            ResolveRecipeClientTestOverride.Use(NullResolveRecipeClient.Instance);

        public ReceiptReentryP05Tests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
            _resolveClientOverride.Dispose();
        }

        // S2-03-C01
        [Fact]
        public void Watch_AcknowledgedReceipt_SendsWatchOnly_AndRecordsTerminalResult()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            using var server = new GrpcTestServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
                new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = FixtureBuildId, ImageRef = "harbor.local/tools/bwa-mem:0.7.17", ImageDigest = ImageDigest, IntegrityHealth = "Healthy" },
            };

            var exitCode = Reenter(server, "watch", receiptPath, out var stdout, out var stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);
            Assert.Equal(_watchOnly, server.Fake.CallOrder);
            Assert.Empty(server.Fake.ResolveRequests);
            Assert.Empty(server.Fake.SubmitRequests);
            Assert.Equal(FixtureBuildId, Assert.Single(server.Fake.WatchRequests).BuildId);
            Assert.Contains("harbor.local/tools/bwa-mem:0.7.17@" + ImageDigest, stdout, StringComparison.Ordinal);

            var receipt = ReadReceipt(receiptPath);
            Assert.Equal(OperationPhase.Terminal, receipt.Phase);
            Assert.Equal(FixtureBuildId, receipt.BuildId);
            Assert.Equal(FixedRequestId, receipt.RequestId);
            Assert.Equal(OperationObservation.SucceededOutcome, receipt.LastObservation!.Outcome);
            Assert.Equal(ImageDigest, receipt.LastObservation.ImageDigest);
            Assert.Equal("Healthy", receipt.LastObservation.IntegrityHealth);
        }

        // S2-03-C01 (failed terminal)
        [Fact]
        public void Watch_AcknowledgedReceipt_FailedTerminal_ExitsOne_RecordsFailedOutcome()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            using var server = new GrpcTestServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Failed", BuildId = FixtureBuildId, Message = "fixture: build broke" },
            };

            var exitCode = Reenter(server, "watch", receiptPath, out var stdout, out var stderr);

            Assert.Equal(1, exitCode);
            Assert.Equal(_watchOnly, server.Fake.CallOrder);
            Assert.Contains("fixture: build broke", stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("fixture: build broke", stdout, StringComparison.Ordinal);
            var receipt = ReadReceipt(receiptPath);
            Assert.Equal(OperationPhase.Terminal, receipt.Phase);
            Assert.Equal(OperationObservation.FailedOutcome, receipt.LastObservation!.Outcome);
        }

        // S2-03-C02
        [Theory]
        [InlineData("watch")]
        [InlineData("cancel")]
        public void Reenter_SubmitInFlightWithoutBuildId_ExitsTwo_NoRpc_NoResubmit_ReceiptUnchanged(string verb)
        {
            var receiptPath = CreateSubmitInFlightReceipt();
            var before = File.ReadAllBytes(receiptPath);
            using var server = new GrpcTestServer();

            var exitCode = Reenter(server, verb, receiptPath, out _, out var stderr);

            Assert.Equal(2, exitCode);
            Assert.Empty(server.Fake.CallOrder);
            Assert.Empty(server.Fake.CancelledBuildIds);
            Assert.Contains(ToolSpecOperationRunner.NotResumableCode, stderr, StringComparison.Ordinal);
            Assert.Contains(FixedRequestId, stderr, StringComparison.Ordinal);
            Assert.Contains("endpoint: injected-client", stderr, StringComparison.Ordinal);
            Assert.Contains("다시 제출하지 않습니다", stderr, StringComparison.Ordinal);
            Assert.Equal(before, File.ReadAllBytes(receiptPath));
        }

        [Theory]
        [InlineData("watch")]
        [InlineData("cancel")]
        public void Reenter_PreparedReceipt_ExitsTwo_NoRpc(string verb)
        {
            var receiptPath = CreatePreparedReceipt();
            using var server = new GrpcTestServer();

            var exitCode = Reenter(server, verb, receiptPath, out _, out var stderr);

            Assert.Equal(2, exitCode);
            Assert.Empty(server.Fake.CallOrder);
            Assert.Empty(server.Fake.CancelledBuildIds);
            Assert.Contains(OperationPhase.Prepared, stderr, StringComparison.Ordinal);
            Assert.Equal(OperationPhase.Prepared, ReadReceipt(receiptPath).Phase);
        }

        // S2-03-C03
        [Fact]
        public void Watch_TwiceOnSameBuildId_EachRewatches_NeverSubmits_AndHelpPromisesNoCursor()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            using var server = new GrpcTestServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId, Message = "step 1" },
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId, Message = "step 2" },
                new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = FixtureBuildId },
            };

            var first = Reenter(server, "watch", receiptPath, out var firstStdout, out _);
            var second = Reenter(server, "watch", receiptPath, out var secondStdout, out _);

            Assert.Equal(0, first);
            Assert.Equal(0, second);
            Assert.Equal(_watchTwice, server.Fake.CallOrder);
            Assert.All(server.Fake.WatchRequests, r => Assert.Equal(FixtureBuildId, r.BuildId));
            Assert.Empty(server.Fake.ResolveRequests);
            Assert.Empty(server.Fake.SubmitRequests);

            // the replayed progress is shown again as received; nothing is deduplicated or resumed
            Assert.Contains("step 1", firstStdout, StringComparison.Ordinal);
            Assert.Contains("step 1", secondStdout, StringComparison.Ordinal);
            Assert.Equal(OperationPhase.Terminal, ReadReceipt(receiptPath).Phase);

            Assert.Equal(0, CliApp.Run(new[] { "receipt", "--help" }, new StringWriter(), new StringWriter()));
            using var help = new StringWriter();
            CliApp.Run(new[] { "receipt", "--help" }, help, new StringWriter());
            Assert.Contains("이전 관찰 위치를 이어받거나 이벤트를 한 번만 보여 준다고 보장하지 않습니다", help.ToString(), StringComparison.Ordinal);
        }

        // S2-03-C04
        [Fact]
        public void Cancel_AcknowledgedReceipt_SendsSavedBuildIdOnly_ReceiptUnchanged()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            var before = File.ReadAllBytes(receiptPath);
            using var server = new GrpcTestServer();

            var exitCode = Reenter(server, "cancel", receiptPath, out var stdout, out var stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);
            Assert.Equal(FixtureBuildId, Assert.Single(server.Fake.CancelledBuildIds));
            Assert.Empty(server.Fake.CallOrder);
            Assert.Contains("확인하지 않았습니다", stdout, StringComparison.Ordinal);
            Assert.Equal(before, File.ReadAllBytes(receiptPath));
        }

        // S2-03-C04 (cancel RPC fails)
        [Fact]
        public void Cancel_RpcFails_ExitsOne_DoesNotClaimStopped_ReceiptUnchanged()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            var before = File.ReadAllBytes(receiptPath);
            using var server = new GrpcTestServer();
            server.Fake.CancelToolBuildFailure = new RpcException(new Status(StatusCode.Unavailable, "fixture: cancel unavailable"));

            var exitCode = Reenter(server, "cancel", receiptPath, out var stdout, out var stderr);

            Assert.Equal(1, exitCode);
            Assert.Equal(FixtureBuildId, Assert.Single(server.Fake.CancelledBuildIds));
            Assert.Empty(server.Fake.CallOrder);
            Assert.Contains("멈췄는지 알 수 없습니다", stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("취소 요청을 보냈습니다", stdout, StringComparison.Ordinal);
            Assert.Equal(before, File.ReadAllBytes(receiptPath));
        }

        [Fact]
        public void Cancel_TerminalReceipt_ExitsTwo_NoCancelRpc()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            using (var watchServer = new GrpcTestServer())
            {
                watchServer.Fake.WatchEvents = new List<ProtoBuildEvent>
                {
                    new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = FixtureBuildId },
                };
                Assert.Equal(0, Reenter(watchServer, "watch", receiptPath, out _, out _));
            }

            using var server = new GrpcTestServer();
            var exitCode = Reenter(server, "cancel", receiptPath, out _, out var stderr);

            Assert.Equal(2, exitCode);
            Assert.Empty(server.Fake.CancelledBuildIds);
            Assert.Empty(server.Fake.CallOrder);
            Assert.Contains(OperationObservation.SucceededOutcome, stderr, StringComparison.Ordinal);
        }

        // S2-03-C05
        [Theory]
        [InlineData("watch", "truncated")]
        [InlineData("cancel", "truncated")]
        [InlineData("watch", "schema")]
        [InlineData("cancel", "schema")]
        [InlineData("watch", "envelope-hash")]
        [InlineData("cancel", "envelope-hash")]
        public void Reenter_CorruptReceipt_ExitsTwo_NoRpc_DoesNotGuessBuildId(string verb, string corruption)
        {
            var receiptPath = CreateAcknowledgedReceipt();
            var text = File.ReadAllText(receiptPath);
            Assert.Contains(FixtureBuildId, text, StringComparison.Ordinal);
            var corrupted = corruption switch
            {
                // the cut keeps the build_id string; a guessing parser could still find it
                "truncated" => text[..(text.IndexOf(FixtureBuildId, StringComparison.Ordinal) + FixtureBuildId.Length + 1)],
                "schema" => text.Replace(OperationReceipt.CurrentSchemaVersion, "nodekit.operation.v999", StringComparison.Ordinal),
                "envelope-hash" => text.Replace("bwa-mem", "bwa-xxx", StringComparison.Ordinal),
                _ => throw new ArgumentOutOfRangeException(nameof(corruption)),
            };
            File.WriteAllText(receiptPath, corrupted, new UTF8Encoding(false));
            using var server = new GrpcTestServer();

            var exitCode = Reenter(server, verb, receiptPath, out _, out var stderr);

            Assert.Equal(2, exitCode);
            Assert.Empty(server.Fake.CallOrder);
            Assert.Empty(server.Fake.WatchRequests);
            Assert.Empty(server.Fake.CancelledBuildIds);
            Assert.Contains(LocalOperationStore.InvalidCode, stderr, StringComparison.Ordinal);
            Assert.Equal(corrupted, File.ReadAllText(receiptPath));
        }

        [Fact]
        public void Watch_ForeignBuildEvent_ExitsOne_NotStored_ReceiptStaysAcknowledged()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            using var server = new GrpcTestServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
                new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = ForeignBuildId },
            };

            var exitCode = Reenter(server, "watch", receiptPath, out _, out var stderr);

            Assert.Equal(1, exitCode);
            Assert.Contains(ForeignBuildId, stderr, StringComparison.Ordinal);
            var receipt = ReadReceipt(receiptPath);
            Assert.Equal(OperationPhase.Acknowledged, receipt.Phase);
            Assert.Null(receipt.LastObservation);
        }

        [Fact]
        public void Watch_StreamEndsWithoutTerminal_ExitsOne_ReceiptStaysAcknowledged()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            using var server = new GrpcTestServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
            };

            var exitCode = Reenter(server, "watch", receiptPath, out _, out var stderr);

            Assert.Equal(1, exitCode);
            Assert.Contains("최종 상태 이벤트 없이 종료", stderr, StringComparison.Ordinal);
            Assert.Equal(OperationPhase.Acknowledged, ReadReceipt(receiptPath).Phase);
        }

        [Fact]
        public void Watch_UserCancel_Exits130_DoesNotCancelServerBuild_ReceiptStaysAcknowledged()
        {
            var receiptPath = CreateAcknowledgedReceipt();
            using var server = new GrpcTestServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
            };
            server.Fake.HangAfterEvents = true;
            using var userCancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            userCancel.CancelAfter(TimeSpan.FromMilliseconds(500));

            var exitCode = ReenterUntil(server, "watch", receiptPath, userCancel.Token, out _, out var stderr);

            Assert.Equal(130, exitCode);
            Assert.Empty(server.Fake.CancelledBuildIds);
            Assert.Contains("서버 빌드는 취소하지 않았습니다", stderr, StringComparison.Ordinal);
            Assert.Equal(OperationPhase.Acknowledged, ReadReceipt(receiptPath).Phase);
        }

        [Fact]
        public void CliApp_RoutesReceipt_UsageErrorsExitTwo()
        {
            var receiptPath = CreateSubmitInFlightReceipt();
            using var stderr = new StringWriter();

            Assert.Equal(2, CliApp.Run(new[] { "receipt", "watch", receiptPath }, new StringWriter(), stderr));
            Assert.Contains(ToolSpecOperationRunner.NotResumableCode, stderr.ToString(), StringComparison.Ordinal);
            Assert.Equal(2, CliApp.Run(new[] { "receipt", "replay-later", receiptPath }, new StringWriter(), new StringWriter()));
            Assert.Equal(2, CliApp.Run(new[] { "receipt", "watch" }, new StringWriter(), new StringWriter()));
            Assert.Equal(2, CliApp.Run(new[] { "receipt", "watch", receiptPath, "--url", "http://x" }, new StringWriter(), new StringWriter()));
        }

        [Fact]
        public void GrpcClient_WatchBuildAsync_RejectsEmptyBuildId()
        {
            using var server = new GrpcTestServer();
            using var client = new GrpcToolSpecClient(server.Channel);

            Assert.Throws<ArgumentException>(() => client.WatchBuildAsync(string.Empty, TestContext.Current.CancellationToken));
            Assert.Empty(server.Fake.CallOrder);
        }

        private static int Reenter(GrpcTestServer server, string verb, string receiptPath, out string stdout, out string stderr) =>
            ReenterUntil(server, verb, receiptPath, TestContext.Current.CancellationToken, out stdout, out stderr);

        private static int ReenterUntil(
            GrpcTestServer server, string verb, string receiptPath, CancellationToken cancellationToken, out string stdout, out string stderr)
        {
            using var client = new GrpcToolSpecClient(server.Channel);
            using var stdoutWriter = new StringWriter();
            using var stderrWriter = new StringWriter();
            var exitCode = ReceiptCommand.Run(new[] { "receipt", verb, receiptPath }, stdoutWriter, stderrWriter, client, cancellationToken);
            stdout = stdoutWriter.ToString();
            stderr = stderrWriter.ToString();
            return exitCode;
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
        private string CreateAcknowledgedReceipt()
        {
            using var server = new GrpcTestServer();
            server.Fake.OnSubmitToolBuild = _ => new Nodevault.V1.SubmitToolBuildResponse { BuildId = FixtureBuildId, Status = "Requested" };
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
            };
            Assert.Equal(1, Submit(server));
            var receiptPath = DefaultReceiptPath();
            Assert.Equal(OperationPhase.Acknowledged, ReadReceipt(receiptPath).Phase);
            return receiptPath;
        }

        // Submit response lost after Resolve → submit_in_flight receipt with no build_id
        private string CreateSubmitInFlightReceipt()
        {
            using var server = new GrpcTestServer();
            server.Fake.OnSubmitToolBuild = _ => throw new RpcException(new Status(StatusCode.Unavailable, "fixture: submit response lost"));
            Assert.Equal(1, Submit(server));
            var receiptPath = DefaultReceiptPath();
            var receipt = ReadReceipt(receiptPath);
            Assert.Equal(OperationPhase.SubmitInFlight, receipt.Phase);
            Assert.Null(receipt.BuildId);
            return receiptPath;
        }

        // Resolve fails → prepared receipt
        private string CreatePreparedReceipt()
        {
            using var server = new GrpcTestServer();
            server.Fake.OnResolveToolSpec = _ => throw new RpcException(new Status(StatusCode.Unavailable, "fixture: resolve unavailable"));
            Assert.Equal(1, Submit(server));
            var receiptPath = DefaultReceiptPath();
            Assert.Equal(OperationPhase.Prepared, ReadReceipt(receiptPath).Phase);
            return receiptPath;
        }

        private int Submit(GrpcTestServer server)
        {
            using var client = new GrpcToolSpecClient(server.Channel);
            return SubmitCommand.Run(new[] { "submit", WriteRecipe() }, new StringWriter(), new StringWriter(), client, () => FixedRequestId);
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
