using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NodeKit.Cli.Operations;
using NodeKit.Cli.Tests.Fakes;
using NodeKit.Grpc;
using Nodevault.V1;
using Xunit;
using ProtoBuildEvent = Nodevault.V1.BuildEvent;
using ProtoBuildEventKind = Nodevault.V1.BuildEventKind;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// P04.store (NodeKit v0.9.1 contract S2-02): durable request identity and
    /// receipt. The local journal is driven through the real GrpcToolSpecClient
    /// against the in-process generated gRPC fake, so RPC order and payloads are
    /// the actual wire values. The fake does not dedup on the server side; these
    /// tests prove local ordering/integrity only, not server-side duplicate
    /// prevention or a live NodeVault.
    /// </summary>
    public class LocalOperationStoreTests : IDisposable
    {
        private const string FixedRequestId = "11111111-1111-1111-1111-111111111111";

        private const string Endpoint = "http://127.0.0.1:50051";

        private const string WireToolSpecDigest =
            "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        private const string ImageDigest =
            "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        private static readonly string[] _resolveOnly = { "Resolve" };

        private static readonly string[] _resolveSubmit = { "Resolve", "Submit" };

        private static readonly string[] _resolveSubmitWatch = { "Resolve", "Submit", "Watch" };

        private static readonly byte[] _recipeV1 = Encoding.UTF8.GetBytes("{\"ToolName\":\"bwa-mem\",\"Version\":\"0.7.17\"}\n");

        private static readonly byte[] _recipeV2 = Encoding.UTF8.GetBytes("{\"ToolName\":\"bwa-mem\",\"Version\":\"0.7.18\"}\n");

        private static readonly byte[] _companion = Encoding.UTF8.GetBytes("#!/bin/sh\necho run\n");

        private static readonly OperationEnvelope _envelope = new()
        {
            ToolName = "bwa-mem",
            Version = "0.7.17",
            RawSpec = "{\"tool_name\":\"bwa-mem\",\"version\":\"0.7.17\",\"note\":\"quote \\\" and\\nnewline\"}",
        };

        private readonly string _workDir = Path.Join(Path.GetTempPath(), "nodekit-p04-store-tests-" + Guid.NewGuid());

        public LocalOperationStoreTests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
        }

        // ── S2-02-C01 · durable intent before Resolve, resolved basis before Submit ──

        [Fact]
        public async Task S2_02_C01_DurableRecordPrecedesEachSideEffectRpc_AndSubmitUsesStoredRequestId()
        {
            using var server = NewServer();
            var store = NewStore();
            string? phaseAtResolve = null;
            OperationReceipt? receiptAtSubmit = null;
            server.Fake.OnResolveToolSpec = r =>
            {
                phaseAtResolve = ReadReceiptFromDisk(store.DefaultReceiptPath(FixedRequestId)).Phase;
                return new ResolvedToolSpecResponse { ToolSpecDigest = WireToolSpecDigest, ToolName = r.ToolName, Version = r.Version, ResolvedAt = 1_760_000_000_123 };
            };
            server.Fake.OnSubmitToolBuild = _ =>
            {
                receiptAtSubmit = ReadReceiptFromDisk(store.DefaultReceiptPath(FixedRequestId));
                return new SubmitToolBuildResponse { BuildId = "build-fixture-001", Status = "Requested" };
            };

            using var handle = CreatePrepared(store, FixedRequestId);
            var result = await RunAsync(server, handle);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(_resolveSubmitWatch, server.Fake.CallOrder);
            Assert.Equal(OperationPhase.Prepared, phaseAtResolve);
            Assert.NotNull(receiptAtSubmit);
            Assert.Equal(OperationPhase.SubmitInFlight, receiptAtSubmit!.Phase);
            Assert.Equal(FixedRequestId, receiptAtSubmit.RequestId);
            Assert.Equal(Endpoint, receiptAtSubmit.Endpoint);
            Assert.Equal(_envelope, receiptAtSubmit.Envelope);

            var submit = Assert.Single(server.Fake.SubmitRequests);
            Assert.Equal(FixedRequestId, submit.RequestId);
            Assert.Equal(WireToolSpecDigest, submit.ToolSpecDigest);
            Assert.Equal(_envelope.RawSpec, Assert.Single(server.Fake.ResolveRequests).RawSpec);

            // immutable_source_binding: exact source bytes → resolved snapshot, linked by local hash.
            Assert.Null(store.TryReadSourceSnapshot(receiptAtSubmit.SourceSnapshotSha256, out var source));
            Assert.Equal(_recipeV1, Convert.FromBase64String(Assert.Single(source!.Files, f => f.LogicalPath == "recipe.json").ContentBase64));
            Assert.Equal(_companion, Convert.FromBase64String(Assert.Single(source.Files, f => f.LogicalPath == "run.sh").ContentBase64));
            Assert.Null(store.TryReadResolvedSnapshot(receiptAtSubmit.ResolvedSnapshotSha256!, out var resolved));
            Assert.Equal(receiptAtSubmit.SourceSnapshotSha256, resolved!.SourceSnapshotSha256);
            Assert.Equal(receiptAtSubmit.EnvelopeSha256, resolved.EnvelopeSha256);
            Assert.Equal(WireToolSpecDigest, resolved.ToolSpecDigest);
            Assert.Equal(1_760_000_000_123, resolved.ResolvedAt);
            Assert.Equal(Assert.Single(server.Fake.ResolveRequests).RequestedAt, resolved.RequestedAtUnixMilliseconds);

            // the phase record only references the snapshot; observation did not rewrite it.
            Assert.Equal(receiptAtSubmit.ResolvedSnapshotSha256, result.Receipt.ResolvedSnapshotSha256);
            Assert.Null(store.TryReadResolvedSnapshot(result.Receipt.ResolvedSnapshotSha256!, out var resolvedAfter));
            Assert.Equal(resolved, resolvedAfter);
        }

        // ── S2-02-C02 · build_id receipt and terminal observation survive the process ──

        [Fact]
        public async Task S2_02_C02_BuildIdAndTerminalObservation_AreReadableAfterHandleIsClosed()
        {
            using var server = NewServer();
            server.Fake.OnSubmitToolBuild = _ => new SubmitToolBuildResponse { BuildId = "build-fixture-001", Status = "Requested" };
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Pushing", BuildId = "build-fixture-001" },
                new()
                {
                    Kind = ProtoBuildEventKind.Log,
                    Status = "Succeeded",
                    BuildId = "build-fixture-001",
                    ImageRef = "harbor.example/tools/bwa-mem:0.7.17",
                    ImageDigest = ImageDigest,
                    IntegrityHealth = "Healthy",
                },
            };
            var store = NewStore();

            using (var handle = CreatePrepared(store, FixedRequestId))
            {
                Assert.Equal(0, (await RunAsync(server, handle)).ExitCode);
            }

            Assert.Null(NewStore().TryOpen(store.DefaultReceiptPath(FixedRequestId), out var reopened));
            using (reopened)
            {
                var receipt = reopened!.Receipt;
                Assert.Equal(OperationPhase.Terminal, receipt.Phase);
                Assert.Equal("build-fixture-001", receipt.BuildId);
                Assert.Equal("Succeeded", receipt.LastObservation!.Status);
                Assert.Equal(ImageDigest, receipt.LastObservation.ImageDigest);
                Assert.Equal("harbor.example/tools/bwa-mem:0.7.17", receipt.LastObservation.ImageRef);
                Assert.Equal("Healthy", receipt.LastObservation.IntegrityHealth);
            }
        }

        // ── S2-02-C03 · journal write failure before Submit → exit2, Submit0 ──

        [Fact]
        public void S2_02_C03_PreparedWriteFails_ExitTwo_NoRpc_NoRecord()
        {
            using var server = NewServer();
            var store = NewStore();
            store.BeforeWriteStage = FailOn(OperationWriteKind.Receipt, OperationPhase.Prepared, AtomicWriteStage.Replace);

            var error = store.TryCreateToolSpecOperation(FixedRequestId, Endpoint, _envelope, Source(_recipeV1), null, out var handle);

            Assert.NotNull(error);
            Assert.Null(handle);
            Assert.Equal(LocalOperationStore.WriteFailedCode, error!.Code);
            Assert.Equal(2, error.ExitCode);
            Assert.False(File.Exists(store.DefaultReceiptPath(FixedRequestId)));
            Assert.Empty(server.Fake.CallOrder);
        }

        [Theory]
        [InlineData(OperationWriteKind.Receipt, AtomicWriteStage.Replace)]
        [InlineData(OperationWriteKind.Receipt, AtomicWriteStage.Write)]
        [InlineData(OperationWriteKind.ResolvedSnapshot, AtomicWriteStage.Flush)]
        public async Task S2_02_C03_InFlightWriteFails_AfterResolve_ExitTwo_SubmitZero(OperationWriteKind kind, AtomicWriteStage stage)
        {
            using var server = NewServer();
            var store = NewStore();
            using var handle = CreatePrepared(store, FixedRequestId);
            store.BeforeWriteStage = FailOn(kind, kind == OperationWriteKind.Receipt ? OperationPhase.SubmitInFlight : null, stage);

            var result = await RunAsync(server, handle);

            Assert.Equal(2, result.ExitCode);
            Assert.Equal(LocalOperationStore.WriteFailedCode, result.Code);
            Assert.True(result.ResolveCompleted);
            Assert.Null(result.ObservedBuildId);
            Assert.Equal(_resolveOnly, server.Fake.CallOrder);
            Assert.Equal(OperationPhase.Prepared, ReadReceiptFromDisk(handle.ReceiptPath).Phase);
        }

        // ── S2-02-C04 · second run on the same prepared journal reuses ID and payload ──

        [Fact]
        public async Task S2_02_C04_SecondRunOnPreparedJournal_ReusesStoredIdAndPayload_AndRedoesInFlightTransition()
        {
            using var server = NewServer();
            var store = NewStore();
            var requestIds = new Queue<string>(new[] { FixedRequestId, "22222222-2222-2222-2222-222222222222" });
            var firstId = requestIds.Dequeue();
            CreatePrepared(store, firstId).Dispose(); // first CLI run stopped before any RPC

            string? phaseAtSubmit = null;
            server.Fake.OnSubmitToolBuild = _ =>
            {
                phaseAtSubmit = ReadReceiptFromDisk(store.DefaultReceiptPath(firstId)).Phase;
                return new SubmitToolBuildResponse { BuildId = "build-fixture-001", Status = "Requested" };
            };

            Assert.Null(NewStore().TryOpen(store.DefaultReceiptPath(firstId), out var second));
            using (second)
            {
                var result = await RunAsync(server, second!);
                Assert.Equal(0, result.ExitCode);
            }

            Assert.Equal(firstId, Assert.Single(server.Fake.SubmitRequests).RequestId);
            Assert.Equal(_envelope.RawSpec, Assert.Single(server.Fake.ResolveRequests).RawSpec);
            Assert.Equal(_envelope.ToolName, server.Fake.ResolveRequests[0].ToolName);
            Assert.Equal(OperationPhase.SubmitInFlight, phaseAtSubmit);
            Assert.Single(requestIds); // the second ID was never needed
        }

        // ── S2-02-C05 · same request_id with a different payload/endpoint is refused ──

        [Fact]
        public void S2_02_C05_ChangedPayloadOrEndpointUnderStoredId_IsRefused_RecordUnchanged()
        {
            var store = NewStore();
            using var handle = CreatePrepared(store, FixedRequestId);
            var before = File.ReadAllBytes(handle.ReceiptPath);

            var changedRaw = handle.Advance(handle.Receipt with { Envelope = _envelope with { RawSpec = "{\"other\":1}" } });
            var changedEndpoint = handle.Advance(handle.Receipt with { Endpoint = "http://10.0.0.9:50051" });
            var changedKind = handle.Advance(handle.Receipt with { OperationKind = "typed_registration" });
            var changedVersion = handle.Advance(handle.Receipt with { SchemaVersion = "nodekit.operation.v2" });

            foreach (var error in new[] { changedRaw, changedEndpoint, changedKind, changedVersion })
            {
                Assert.NotNull(error);
                Assert.Equal(LocalOperationStore.MismatchCode, error!.Code);
                Assert.Equal(2, error.ExitCode);
            }

            Assert.Equal(before, File.ReadAllBytes(handle.ReceiptPath));
            Assert.Equal(_envelope, handle.Receipt.Envelope);
        }

        [Fact]
        public void S2_02_C05_CreatingOverAnExistingRecord_ExitsTwo_WithoutOverwriting()
        {
            var store = NewStore();
            CreatePrepared(store, FixedRequestId).Dispose();
            var before = File.ReadAllBytes(store.DefaultReceiptPath(FixedRequestId));

            var error = store.TryCreateToolSpecOperation(
                FixedRequestId, "http://10.0.0.9:50051", _envelope with { RawSpec = "{}" }, Source(_recipeV2), null, out var handle);

            Assert.Null(handle);
            Assert.Equal(LocalOperationStore.ExistsCode, error!.Code);
            Assert.Equal(2, error.ExitCode);
            Assert.Equal(before, File.ReadAllBytes(store.DefaultReceiptPath(FixedRequestId)));
        }

        [Fact]
        public async Task S2_02_C05_EditedRecipePreservesPastBasis_DivergenceIsReported_ReplayDoesNotRereadSource()
        {
            using var server = NewServer();
            var store = NewStore();
            var recipePath = Path.Join(_workDir, "recipe.json");
            await File.WriteAllBytesAsync(recipePath, _recipeV1, TestContext.Current.CancellationToken);

            CreatePrepared(store, FixedRequestId).Dispose();
            var oldReceipt = ReadReceiptFromDisk(store.DefaultReceiptPath(FixedRequestId));

            // The user edits the Recipe after the snapshot was taken.
            await File.WriteAllBytesAsync(recipePath, _recipeV2, TestContext.Current.CancellationToken);
            var current = Source(await File.ReadAllBytesAsync(recipePath, TestContext.Current.CancellationToken));

            Assert.Equal(SourceBindingStatus.Diverged, LocalOperationStore.CompareSource(oldReceipt, current));
            Assert.Equal(SourceBindingStatus.Matches, LocalOperationStore.CompareSource(oldReceipt, Source(_recipeV1)));

            // Replaying the stored envelope sends the stored bytes, not the edited file.
            Assert.Null(store.TryOpen(store.DefaultReceiptPath(FixedRequestId), out var replay));
            using (replay)
            {
                Assert.Equal(0, (await RunAsync(server, replay!)).ExitCode);
            }

            Assert.Equal(_envelope.RawSpec, Assert.Single(server.Fake.ResolveRequests).RawSpec);

            // A fresh preparation binds the edited source as a new snapshot with a new request ID.
            using var fresh = CreatePrepared(store, "33333333-3333-3333-3333-333333333333", current);
            Assert.NotEqual(oldReceipt.SourceSnapshotSha256, fresh.Receipt.SourceSnapshotSha256);

            Assert.Null(store.TryReadSourceSnapshot(oldReceipt.SourceSnapshotSha256, out var oldSnapshot));
            Assert.Equal(_recipeV1, Convert.FromBase64String(Assert.Single(oldSnapshot!.Files, f => f.LogicalPath == "recipe.json").ContentBase64));
            Assert.Equal(SourceBindingStatus.Matches, LocalOperationStore.CompareSource(fresh.Receipt, current));
        }

        // ── S2-02-C06 · concurrent writers on the same journal ──

        [Fact]
        public void S2_02_C06_SecondWriterOnSameRecord_IsLockedOut_WithoutSubmitting()
        {
            using var server = NewServer();
            var store = NewStore();
            using var first = CreatePrepared(store, FixedRequestId);

            var error = NewStore().TryOpen(first.ReceiptPath, out var second);

            Assert.Null(second);
            Assert.Equal(LocalOperationStore.LockedCode, error!.Code);
            Assert.Equal(2, error.ExitCode);
            Assert.Empty(server.Fake.CallOrder);

            first.Dispose();
            Assert.Null(NewStore().TryOpen(first.ReceiptPath, out var afterRelease));
            afterRelease!.Dispose();
        }

        [Fact]
        public void S2_02_C06_HeldRecordLock_DoesNotBlockTheHoldersOwnWrites()
        {
            var store = NewStore();
            using var handle = CreatePrepared(store, FixedRequestId);

            var resolvedError = store.WriteResolvedSnapshot(
                ResolvedSnapshot.FromBasis(new ToolSpecSubmitBasis { RequestId = FixedRequestId, ToolSpecDigest = WireToolSpecDigest }, handle.Receipt),
                out var sha);
            var advanceError = handle.Advance(handle.Receipt with { Phase = OperationPhase.SubmitInFlight, ResolvedSnapshotSha256 = sha });

            Assert.Null(resolvedError);
            Assert.Null(advanceError);
            Assert.Equal(OperationPhase.SubmitInFlight, ReadReceiptFromDisk(handle.ReceiptPath).Phase);
        }

        // ── S2-02-C07 · interrupted replace and partial records ──

        [Fact]
        public async Task S2_02_C07_InterruptedReplace_LeavesPreviousCompleteRecord_AndNothingIsSubmitted()
        {
            using var server = NewServer();
            var store = NewStore();
            using var handle = CreatePrepared(store, FixedRequestId);
            var before = File.ReadAllBytes(handle.ReceiptPath);
            store.BeforeWriteStage = FailOn(OperationWriteKind.Receipt, OperationPhase.SubmitInFlight, AtomicWriteStage.Replace);

            var result = await RunAsync(server, handle);

            Assert.Equal(2, result.ExitCode);
            Assert.Equal(_resolveOnly, server.Fake.CallOrder);
            Assert.Equal(before, File.ReadAllBytes(handle.ReceiptPath));
            handle.Dispose();
            Assert.Null(NewStore().TryOpen(handle.ReceiptPath, out var reopened));
            Assert.Equal(OperationPhase.Prepared, reopened!.Receipt.Phase);
            reopened.Dispose();
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(handle.ReceiptPath)!, "*" + AtomicFileWriter.TempSuffix));
        }

        public static TheoryData<string> CorruptRecords => new()
        {
            "truncated",
            "unsupported_schema",
            "envelope_hash_mismatch",
            "missing_source_snapshot",
            "in_flight_without_resolved",
            "unknown_field",
        };

        [Theory]
        [MemberData(nameof(CorruptRecords))]
        public void S2_02_C07_PartialOrInconsistentRecord_IsIntegrityErrorExitTwo_NoRpc(string corruption)
        {
            using var server = NewServer();
            var store = NewStore();
            string path;
            string sourceSha;
            using (var handle = CreatePrepared(store, FixedRequestId))
            {
                path = handle.ReceiptPath;
                sourceSha = handle.Receipt.SourceSnapshotSha256;
            }

            var text = File.ReadAllText(path);
            switch (corruption)
            {
                case "truncated":
                    File.WriteAllText(path, text[..(text.Length / 2)]);
                    break;
                case "unsupported_schema":
                    File.WriteAllText(path, text.Replace(OperationReceipt.CurrentSchemaVersion, "nodekit.operation.v99", StringComparison.Ordinal));
                    break;
                case "envelope_hash_mismatch":
                    File.WriteAllText(path, text.Replace("bwa-mem", "bwa-mex", StringComparison.Ordinal));
                    break;
                case "missing_source_snapshot":
                    File.Delete(store.SourceSnapshotPath(sourceSha));
                    break;
                case "in_flight_without_resolved":
                    File.WriteAllText(path, text.Replace("\"phase\": \"prepared\"", "\"phase\": \"submit_in_flight\"", StringComparison.Ordinal));
                    break;
                case "unknown_field":
                    File.WriteAllText(path, text.Replace("\"phase\":", "\"build_id_guess\": \"x\",\n  \"phase\":", StringComparison.Ordinal));
                    break;
            }

            var error = NewStore().TryOpen(path, out var reopened);

            Assert.Null(reopened);
            Assert.Equal(LocalOperationStore.InvalidCode, error!.Code);
            Assert.Equal(2, error.ExitCode);
            Assert.Empty(server.Fake.CallOrder);
        }

        [Fact]
        public async Task S2_02_C07_InFlightRecordWithoutBuildId_IsNotBlindlyResubmitted()
        {
            using var server = NewServer();
            server.Fake.OnSubmitToolBuild = _ =>
                throw new global::Grpc.Core.RpcException(new global::Grpc.Core.Status(global::Grpc.Core.StatusCode.Unavailable, "fixture: response lost"));
            var store = NewStore();
            using (var handle = CreatePrepared(store, FixedRequestId))
            {
                var lost = await RunAsync(server, handle);
                Assert.Equal(1, lost.ExitCode);
                Assert.Equal(OperationPhase.SubmitInFlight, lost.Receipt.Phase);
                Assert.Null(lost.Receipt.BuildId);
            }

            Assert.Equal(_resolveSubmit, server.Fake.CallOrder);

            Assert.Null(NewStore().TryOpen(store.DefaultReceiptPath(FixedRequestId), out var again));
            using (again)
            {
                var result = await RunAsync(server, again!);
                Assert.Equal(2, result.ExitCode);
                Assert.Equal(ToolSpecOperationRunner.NotResumableCode, result.Code);
                Assert.Contains(FixedRequestId, result.Message, StringComparison.Ordinal);
                Assert.Contains(Endpoint, result.Message, StringComparison.Ordinal);
            }

            Assert.Equal(_resolveSubmit, server.Fake.CallOrder);
        }

        // ── S2-02-C08 · server ACK/result observed but not durably saved ──

        [Fact]
        public async Task S2_02_C08_AckWriteFails_ExitTwo_ObservedBuildIdKeptInDiagnostic_NoNewSubmit()
        {
            using var server = NewServer();
            server.Fake.OnSubmitToolBuild = _ => new SubmitToolBuildResponse { BuildId = "build-fixture-001", Status = "Requested" };
            var store = NewStore();
            using var handle = CreatePrepared(store, FixedRequestId);
            store.BeforeWriteStage = FailOn(OperationWriteKind.Receipt, OperationPhase.Acknowledged, AtomicWriteStage.Flush);

            var result = await RunAsync(server, handle);

            Assert.Equal(2, result.ExitCode);
            Assert.Equal(LocalOperationStore.WriteFailedCode, result.Code);
            Assert.Equal("build-fixture-001", result.ObservedBuildId);
            Assert.Contains("build-fixture-001", result.Message, StringComparison.Ordinal);
            Assert.Single(server.Fake.SubmitRequests);
            Assert.Empty(server.Fake.CancelledBuildIds);

            var durable = ReadReceiptFromDisk(handle.ReceiptPath);
            Assert.Equal(OperationPhase.SubmitInFlight, durable.Phase);
            Assert.Null(durable.BuildId);
        }

        [Fact]
        public async Task S2_02_C08_TerminalWriteFails_ExitTwo_ObservedResultKept_DurableStaysAcknowledged()
        {
            using var server = NewServer();
            server.Fake.OnSubmitToolBuild = _ => new SubmitToolBuildResponse { BuildId = "build-fixture-001", Status = "Requested" };
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = "build-fixture-001", ImageDigest = ImageDigest },
            };
            var store = NewStore();
            using var handle = CreatePrepared(store, FixedRequestId);
            store.BeforeWriteStage = FailOn(OperationWriteKind.Receipt, OperationPhase.Terminal, AtomicWriteStage.Replace);

            var result = await RunAsync(server, handle);

            Assert.Equal(2, result.ExitCode);
            Assert.Equal("build-fixture-001", result.ObservedBuildId);
            Assert.Equal("Succeeded", result.ObservedResult!.Status);
            Assert.Equal(ImageDigest, result.ObservedResult.ImageDigest);
            Assert.Single(server.Fake.SubmitRequests);

            var durable = ReadReceiptFromDisk(handle.ReceiptPath);
            Assert.Equal(OperationPhase.Acknowledged, durable.Phase);
            Assert.Equal("build-fixture-001", durable.BuildId);
            Assert.Null(durable.LastObservation);
        }

        // ── helpers ──

        private static Action<OperationWriteContext> FailOn(OperationWriteKind kind, string? phase, AtomicWriteStage stage) =>
            ctx =>
            {
                if (ctx.Kind == kind && ctx.Stage == stage && (phase is null || ctx.TargetPhase == phase))
                {
                    throw new IOException($"fixture: {kind}/{phase}/{stage} fault");
                }
            };

        private static SourceSnapshot Source(byte[] recipe) =>
            SourceSnapshot.FromFiles(new[] { ("run.sh", _companion), ("recipe.json", recipe) });

        private static OperationReceipt ReadReceiptFromDisk(string path) =>
            JsonSerializer.Deserialize<OperationReceipt>(File.ReadAllBytes(path))!;

        private static async Task<ToolSpecOperationResult> RunAsync(GrpcTestServer server, OperationHandle handle)
        {
            using var client = new GrpcToolSpecClient(server.Channel);
            return await ToolSpecOperationRunner.RunAsync(handle, client, cancellationToken: TestContext.Current.CancellationToken);
        }

        private static GrpcTestServer NewServer()
        {
            var server = new GrpcTestServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = "fake-build-id" },
                new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = "fake-build-id" },
            };
            return server;
        }

        private LocalOperationStore NewStore() => new(Path.Join(_workDir, LocalOperationStore.RootDirectoryName));

        private OperationHandle CreatePrepared(LocalOperationStore store, string requestId, SourceSnapshot? source = null)
        {
            var error = store.TryCreateToolSpecOperation(requestId, Endpoint, _envelope, source ?? Source(_recipeV1), null, out var handle);
            Assert.Null(error);
            return handle!;
        }
    }
}
