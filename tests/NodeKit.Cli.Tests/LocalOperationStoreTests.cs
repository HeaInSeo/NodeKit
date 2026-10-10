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

        private const string FixtureBuildId = "build-fixture-001";

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
        [InlineData("receipt_replace")]
        [InlineData("receipt_write")]
        [InlineData("resolved_snapshot_flush")]
        public async Task S2_02_C03_InFlightWriteFails_AfterResolve_ExitTwo_SubmitZero(string fault)
        {
            using var server = NewServer();
            var store = NewStore();
            using var handle = CreatePrepared(store, FixedRequestId);
            store.BeforeWriteStage = fault switch
            {
                "receipt_replace" => FailOn(OperationWriteKind.Receipt, OperationPhase.SubmitInFlight, AtomicWriteStage.Replace),
                "receipt_write" => FailOn(OperationWriteKind.Receipt, OperationPhase.SubmitInFlight, AtomicWriteStage.Write),
                _ => FailOn(OperationWriteKind.ResolvedSnapshot, null, AtomicWriteStage.Flush),
            };

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

        [Fact]
        public async Task S2_02_C05_ReentryAgainstDifferentEndpoint_IsRefusedBeforeResolve_RecordUnchanged()
        {
            using var server = NewServer();
            var store = NewStore();
            CreatePrepared(store, FixedRequestId).Dispose();
            var path = store.DefaultReceiptPath(FixedRequestId);
            var before = File.ReadAllBytes(path);

            Assert.Null(NewStore().TryOpen(path, out var reentry));
            using (reentry)
            {
                var result = await RunAsync(server, reentry!, "http://10.0.0.9:50051");

                Assert.Equal(2, result.ExitCode);
                Assert.Equal(LocalOperationStore.MismatchCode, result.Code);
                Assert.False(result.ResolveCompleted);
                Assert.Contains(Endpoint, result.Message, StringComparison.Ordinal);
                Assert.Contains("http://10.0.0.9:50051", result.Message, StringComparison.Ordinal);
            }

            Assert.Empty(server.Fake.CallOrder);
            Assert.Equal(before, File.ReadAllBytes(path));

            // Control: the same record against its stored endpoint runs normally.
            Assert.Null(NewStore().TryOpen(path, out var matching));
            using (matching)
            {
                Assert.Equal(0, (await RunAsync(server, matching!)).ExitCode);
            }

            Assert.Equal(FixedRequestId, Assert.Single(server.Fake.SubmitRequests).RequestId);
        }

        [Fact]
        public async Task S2_02_C05_EndpointMismatch_TakesPrecedenceOverResumeState_NoRpc()
        {
            using var server = NewServer();
            var store = NewStore();
            using var handle = CreatePrepared(store, FixedRequestId);
            AdvanceToInFlight(store, handle);
            var before = File.ReadAllBytes(handle.ReceiptPath);

            var result = await RunAsync(server, handle, "http://10.0.0.9:50051");

            Assert.Equal(2, result.ExitCode);
            Assert.Equal(LocalOperationStore.MismatchCode, result.Code);
            Assert.Empty(server.Fake.CallOrder);
            Assert.Equal(before, File.ReadAllBytes(handle.ReceiptPath));
        }

        // ── Advance guards: receipt only moves forward, recorded facts never change ──

        [Fact]
        public void Advance_PhaseRegression_IsRefused_RecordUnchanged()
        {
            var store = NewStore();
            using var handle = CreatePrepared(store, FixedRequestId);
            AdvanceToInFlight(store, handle);
            Assert.Null(handle.Advance(handle.Receipt with { Phase = OperationPhase.Acknowledged, BuildId = "build-fixture-001" }));
            var before = File.ReadAllBytes(handle.ReceiptPath);

            var error = handle.Advance(handle.Receipt with { Phase = OperationPhase.SubmitInFlight });

            AssertRefusedUnchanged(error, handle, before, LocalOperationStore.MismatchCode);
            Assert.Equal(OperationPhase.Acknowledged, handle.Receipt.Phase);
        }

        [Fact]
        public void Advance_RecordedBuildIdRewrite_IsRefused_RecordUnchanged()
        {
            var store = NewStore();
            using var handle = CreatePrepared(store, FixedRequestId);
            AdvanceToInFlight(store, handle);
            Assert.Null(handle.Advance(handle.Receipt with { Phase = OperationPhase.Acknowledged, BuildId = "build-fixture-001" }));
            var before = File.ReadAllBytes(handle.ReceiptPath);

            var error = handle.Advance(handle.Receipt with { BuildId = "build-fixture-999" });

            AssertRefusedUnchanged(error, handle, before, LocalOperationStore.MismatchCode);
            Assert.Equal("build-fixture-001", handle.Receipt.BuildId);
        }

        [Fact]
        public void Advance_RecordedResolvedSnapshotRewrite_IsRefused_RecordUnchanged()
        {
            var store = NewStore();
            using var handle = CreatePrepared(store, FixedRequestId);
            var original = AdvanceToInFlight(store, handle);
            var otherBasis = new ToolSpecSubmitBasis
            {
                RequestId = FixedRequestId,
                RequestedToolName = _envelope.ToolName,
                RequestedVersion = _envelope.Version,
                ToolSpecDigest = ImageDigest,
            };
            Assert.Null(store.WriteResolvedSnapshot(ResolvedSnapshot.FromBasis(otherBasis, handle.Receipt), out var other));
            Assert.NotEqual(original, other);
            var before = File.ReadAllBytes(handle.ReceiptPath);

            var error = handle.Advance(handle.Receipt with { ResolvedSnapshotSha256 = other });

            AssertRefusedUnchanged(error, handle, before, LocalOperationStore.MismatchCode);
            Assert.Equal(original, handle.Receipt.ResolvedSnapshotSha256);
        }

        [Fact]
        public void Advance_RecordThatWouldNotReopen_IsNotWritten()
        {
            var store = NewStore();
            using var handle = CreatePrepared(store, FixedRequestId);
            var before = File.ReadAllBytes(handle.ReceiptPath);

            var error = handle.Advance(handle.Receipt with { Phase = OperationPhase.Terminal });

            AssertRefusedUnchanged(error, handle, before, LocalOperationStore.InvalidCode);
            Assert.Equal(OperationPhase.Prepared, handle.Receipt.Phase);
        }

        // ── receipt location: snapshots are found again from the receipt path alone ──

        [Fact]
        public void ReceiptLocation_DefaultReceipt_DerivesItsStoreRoot()
        {
            var store = NewStore();

            Assert.Equal(store.RootDirectory, LocalOperationStore.RootForReceipt(store.DefaultReceiptPath(FixedRequestId)));
            Assert.Equal(store.RootDirectory, LocalOperationStore.ForRecipe(Path.Join(_workDir, "recipe.json")).RootDirectory);
        }

        [Fact]
        public async Task ReceiptLocation_CustomReceipt_IsReopenedFromItsPathAlone_AndRuns()
        {
            using var server = NewServer();
            var receiptDir = Path.Join(_workDir, "elsewhere");
            Directory.CreateDirectory(receiptDir);
            var receiptPath = Path.Join(receiptDir, "my-build.json");
            var store = LocalOperationStore.ForReceipt(receiptPath);

            Assert.Null(store.TryCreateToolSpecOperation(FixedRequestId, Endpoint, _envelope, Source(_recipeV1), receiptPath, out var created));
            created!.Dispose();

            // A later process only knows the receipt path.
            Assert.Null(LocalOperationStore.ForReceipt(receiptPath).TryOpen(receiptPath, out var reopened));
            using (reopened)
            {
                Assert.Equal(0, (await RunAsync(server, reopened!)).ExitCode);
            }

            Assert.Null(LocalOperationStore.ForReceipt(receiptPath).TryOpen(receiptPath, out var after));
            using (after)
            {
                Assert.Equal(OperationPhase.Terminal, after!.Receipt.Phase);
            }
        }

        [Fact]
        public void ReceiptLocation_CustomReceiptOutsideTheStoreRoot_IsRefused_NothingWritten()
        {
            var store = NewStore();
            var receiptDir = Path.Join(_workDir, "elsewhere");
            Directory.CreateDirectory(receiptDir);
            var receiptPath = Path.Join(receiptDir, "my-build.json");

            var error = store.TryCreateToolSpecOperation(FixedRequestId, Endpoint, _envelope, Source(_recipeV1), receiptPath, out var handle);

            Assert.Null(handle);
            Assert.Equal(LocalOperationStore.MismatchCode, error!.Code);
            Assert.Equal(2, error.ExitCode);
            Assert.False(File.Exists(receiptPath));
            Assert.False(Directory.Exists(Path.Join(store.RootDirectory, "snapshots")));
        }

        [Fact]
        public void ReceiptLocation_OpeningWithAStoreThatIsNotTheReceiptsRoot_IsRefused()
        {
            var store = NewStore();
            CreatePrepared(store, FixedRequestId).Dispose();

            var error = new LocalOperationStore(Path.Join(_workDir, "other-root")).TryOpen(store.DefaultReceiptPath(FixedRequestId), out var handle);

            Assert.Null(handle);
            Assert.Equal(LocalOperationStore.MismatchCode, error!.Code);
            Assert.Equal(2, error.ExitCode);
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

        public static TheoryData<string> CorruptInFlightRecords => new()
        {
            "acknowledged_without_build_id",
            "terminal_without_build_id",
            "missing_resolved_snapshot",
            "tampered_resolved_snapshot",
            "tampered_source_snapshot",
            "resolved_snapshot_of_another_operation",
            "resolved_snapshot_of_another_attempt_same_recipe",
            "resolved_snapshot_bound_to_another_request_id",
            "resolved_snapshot_bound_to_another_endpoint",
            "malformed_source_snapshot_id",
            "malformed_resolved_snapshot_id",
        };

        [Theory]
        [MemberData(nameof(CorruptInFlightRecords))]
        public async Task S2_02_C07_InconsistentInFlightRecordOrSnapshot_IsIntegrityErrorExitTwo_NoRpc(string corruption)
        {
            using var server = NewServer();
            var store = NewStore();
            string path;
            string sourceSha;
            string resolvedSha;
            using (var handle = CreatePrepared(store, FixedRequestId))
            {
                path = handle.ReceiptPath;
                sourceSha = handle.Receipt.SourceSnapshotSha256;
                resolvedSha = AdvanceToInFlight(store, handle);
            }

            // Control: the untouched in-flight record reopens.
            Assert.Null(NewStore().TryOpen(path, out var control));
            control!.Dispose();

            var text = File.ReadAllText(path);
            switch (corruption)
            {
                case "acknowledged_without_build_id":
                    File.WriteAllText(path, text.Replace("\"phase\": \"submit_in_flight\"", "\"phase\": \"acknowledged\"", StringComparison.Ordinal));
                    break;
                case "terminal_without_build_id":
                    File.WriteAllText(path, text.Replace("\"phase\": \"submit_in_flight\"", "\"phase\": \"terminal\"", StringComparison.Ordinal));
                    break;
                case "missing_resolved_snapshot":
                    File.Delete(store.ResolvedSnapshotPath(resolvedSha));
                    break;
                case "tampered_resolved_snapshot":
                    // Still valid JSON with the right schema and binding; only the content hash differs.
                    var resolvedPath = store.ResolvedSnapshotPath(resolvedSha);
                    File.WriteAllText(resolvedPath, File.ReadAllText(resolvedPath).Replace(WireToolSpecDigest, ImageDigest, StringComparison.Ordinal));
                    break;
                case "tampered_source_snapshot":
                    var sourcePath = store.SourceSnapshotPath(sourceSha);
                    File.WriteAllText(sourcePath, File.ReadAllText(sourcePath).Replace("run.sh", "run.sx", StringComparison.Ordinal));
                    break;
                case "resolved_snapshot_of_another_operation":
                    // A valid snapshot that belongs to a different source/envelope in the same store.
                    Assert.Null(store.TryCreateToolSpecOperation(
                        "44444444-4444-4444-4444-444444444444", Endpoint, _envelope with { Version = "0.7.18" }, Source(_recipeV2), null, out var other));
                    string otherSha;
                    using (other)
                    {
                        otherSha = AdvanceToInFlight(store, other!);
                    }

                    Assert.Null(store.TryReadResolvedSnapshot(otherSha, out _));
                    File.WriteAllText(path, text.Replace(resolvedSha, otherSha, StringComparison.Ordinal));
                    break;
                case "resolved_snapshot_of_another_attempt_same_recipe":
                    // A retry of the same Recipe on another NodeVault: same source/envelope/tool/version,
                    // different request_id and endpoint, so a different resolved digest.
                    Assert.Null(store.TryCreateToolSpecOperation(
                        "44444444-4444-4444-4444-444444444444", "http://other-nodevault:50051", _envelope, Source(_recipeV1), null, out var retry));
                    string retrySha;
                    using (retry)
                    {
                        Assert.Equal(sourceSha, retry!.Receipt.SourceSnapshotSha256);
                        retrySha = AdvanceToInFlight(store, retry, ImageDigest);
                    }

                    File.WriteAllText(path, text.Replace(resolvedSha, retrySha, StringComparison.Ordinal));
                    break;
                case "resolved_snapshot_bound_to_another_request_id":
                    // Differs from this attempt's snapshot only in request_id.
                    File.WriteAllText(path, text.Replace(
                        resolvedSha,
                        WriteResolvedFor(store, ReadReceiptFromDisk(path) with { RequestId = "44444444-4444-4444-4444-444444444444" }),
                        StringComparison.Ordinal));
                    break;
                case "resolved_snapshot_bound_to_another_endpoint":
                    // Differs from this attempt's snapshot only in endpoint.
                    File.WriteAllText(path, text.Replace(
                        resolvedSha,
                        WriteResolvedFor(store, ReadReceiptFromDisk(path) with { Endpoint = "http://other-nodevault:50051" }),
                        StringComparison.Ordinal));
                    break;
                case "malformed_source_snapshot_id":
                    File.WriteAllText(path, text.Replace(sourceSha, "\\u0000", StringComparison.Ordinal));
                    break;
                case "malformed_resolved_snapshot_id":
                    File.WriteAllText(path, text.Replace(resolvedSha, "../../" + resolvedSha[6..], StringComparison.Ordinal));
                    break;
            }

            var error = NewStore().TryOpen(path, out var reopened);

            Assert.Null(reopened);
            Assert.Equal(LocalOperationStore.InvalidCode, error!.Code);
            Assert.Equal(2, error.ExitCode);
            Assert.Empty(server.Fake.CallOrder);
        }

        [Fact]
        public void S2_02_C07_TerminalRecordWithoutObservation_IsIntegrityErrorExitTwo_NoRpc()
        {
            using var server = NewServer();
            var store = NewStore();
            string path;
            using (var handle = CreatePrepared(store, FixedRequestId))
            {
                path = handle.ReceiptPath;
                AdvanceToInFlight(store, handle);
                Assert.Null(handle.Advance(handle.Receipt with { Phase = OperationPhase.Acknowledged, BuildId = "build-fixture-001" }));
            }

            // Control: an acknowledged record with a build_id and no observation is valid and reopens.
            Assert.Null(ReadReceiptFromDisk(path).LastObservation);
            Assert.Null(NewStore().TryOpen(path, out var control));
            control!.Dispose();

            var text = File.ReadAllText(path);
            File.WriteAllText(path, text.Replace("\"phase\": \"acknowledged\"", "\"phase\": \"terminal\"", StringComparison.Ordinal));
            Assert.Equal(OperationPhase.Terminal, ReadReceiptFromDisk(path).Phase);

            var error = NewStore().TryOpen(path, out var reopened);

            Assert.Null(reopened);
            Assert.Equal(LocalOperationStore.InvalidCode, error!.Code);
            Assert.Equal(2, error.ExitCode);
            Assert.Empty(server.Fake.CallOrder);
        }

        [Fact]
        public void Advance_TerminalWithoutObservation_IsNotWritten()
        {
            var store = NewStore();
            using var handle = CreatePrepared(store, FixedRequestId);
            AdvanceToInFlight(store, handle);
            Assert.Null(handle.Advance(handle.Receipt with { Phase = OperationPhase.Acknowledged, BuildId = "build-fixture-001" }));
            var before = File.ReadAllBytes(handle.ReceiptPath);

            var error = handle.Advance(handle.Receipt with { Phase = OperationPhase.Terminal });

            AssertRefusedUnchanged(error, handle, before, LocalOperationStore.InvalidCode);
            Assert.Equal(OperationPhase.Acknowledged, handle.Receipt.Phase);
        }

        public static TheoryData<string> RecordsWithLaterPhaseFields => new()
        {
            "prepared_with_build_id",
            "prepared_with_last_observation",
            "prepared_with_resolved_snapshot",
            "in_flight_with_build_id",
            "in_flight_with_last_observation",
            "acknowledged_with_last_observation",
        };

        // A record carrying fields of a later phase is evidence of an earlier submission. Reopening a
        // prepared one as fresh would send Resolve/Submit again, so it is an integrity error.
        [Theory]
        [MemberData(nameof(RecordsWithLaterPhaseFields))]
        public void S2_02_C07_RecordWithLaterPhaseFields_IsIntegrityErrorExitTwo_NoRpc(string corruption)
        {
            using var server = NewServer();
            var store = NewStore();
            string path;
            string resolvedSha;
            using (var handle = CreatePrepared(store, FixedRequestId))
            {
                path = handle.ReceiptPath;
                resolvedSha = WriteResolvedFor(store, handle.Receipt);
            }

            var prepared = ReadReceiptFromDisk(path);
            var inFlight = prepared with { Phase = OperationPhase.SubmitInFlight, ResolvedSnapshotSha256 = resolvedSha };
            var acknowledged = inFlight with { Phase = OperationPhase.Acknowledged, BuildId = FixtureBuildId };
            var observation = new OperationObservation { Status = "Running", ObservedAt = "2026-10-10T00:00:00.000Z" };

            // Control: each phase with exactly its own fields reopens.
            foreach (var valid in new[] { prepared, inFlight, acknowledged, acknowledged with { Phase = OperationPhase.Terminal, LastObservation = observation } })
            {
                File.WriteAllText(path, JsonSerializer.Serialize(valid));
                Assert.Null(NewStore().TryOpen(path, out var control));
                control!.Dispose();
            }

            var corrupted = corruption switch
            {
                "prepared_with_build_id" => prepared with { BuildId = FixtureBuildId },
                "prepared_with_last_observation" => prepared with { LastObservation = observation },
                "prepared_with_resolved_snapshot" => prepared with { ResolvedSnapshotSha256 = resolvedSha },
                "in_flight_with_build_id" => inFlight with { BuildId = FixtureBuildId },
                "in_flight_with_last_observation" => inFlight with { LastObservation = observation },
                _ => acknowledged with { LastObservation = observation },
            };
            File.WriteAllText(path, JsonSerializer.Serialize(corrupted));

            var error = NewStore().TryOpen(path, out var reopened);

            Assert.Null(reopened);
            Assert.Equal(LocalOperationStore.InvalidCode, error!.Code);
            Assert.Equal(2, error.ExitCode);
            Assert.Empty(server.Fake.CallOrder);
        }

        [Theory]
        [InlineData("build_id")]
        [InlineData("last_observation")]
        [InlineData("resolved_snapshot")]
        public void Advance_PreparedWithLaterPhaseField_IsNotWritten(string field)
        {
            var store = NewStore();
            using var handle = CreatePrepared(store, FixedRequestId);
            var resolvedSha = WriteResolvedFor(store, handle.Receipt);
            var before = File.ReadAllBytes(handle.ReceiptPath);

            var error = handle.Advance(field switch
            {
                "build_id" => handle.Receipt with { BuildId = FixtureBuildId },
                "last_observation" => handle.Receipt with { LastObservation = new OperationObservation { Status = "Running", ObservedAt = "2026-10-10T00:00:00.000Z" } },
                _ => handle.Receipt with { ResolvedSnapshotSha256 = resolvedSha },
            });

            AssertRefusedUnchanged(error, handle, before, LocalOperationStore.InvalidCode);
            Assert.Equal(OperationPhase.Prepared, handle.Receipt.Phase);
            Assert.Null(handle.Receipt.BuildId);
        }

        // ── S2-02-C02 · only this build's watch events become its observation ──

        [Fact]
        public async Task S2_02_C02_WatchEventOfAnotherBuild_IsRejected_AndNotPersisted()
        {
            using var server = NewServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
                new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = "build-fixture-999", ImageDigest = ImageDigest },
            };
            var store = NewStore();
            using var handle = CreatePrepared(store, FixedRequestId);

            var result = await RunAsync(server, handle);

            Assert.Equal(1, result.ExitCode);
            Assert.Null(result.Code);
            Assert.Equal(FixtureBuildId, result.ObservedBuildId);
            Assert.Contains("build-fixture-999", result.Message, StringComparison.Ordinal);
            Assert.Equal("Running", result.ObservedResult!.Status);
            Assert.Null(result.ObservedResult.ImageDigest);
            Assert.Single(server.Fake.SubmitRequests);
            Assert.Empty(server.Fake.CancelledBuildIds);

            var durable = ReadReceiptFromDisk(handle.ReceiptPath);
            Assert.Equal(OperationPhase.Acknowledged, durable.Phase);
            Assert.Equal(FixtureBuildId, durable.BuildId);
            Assert.Null(durable.LastObservation);
        }

        [Theory]
        [InlineData("Succeeded", 0)]
        [InlineData("Failed", 1)]
        public async Task S2_02_C02_TerminalKindWithoutStatus_PersistsTheOutcome(string status, int exitCode)
        {
            using var server = NewServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
                new() { Kind = status == "Succeeded" ? ProtoBuildEventKind.Succeeded : ProtoBuildEventKind.Failed, BuildId = FixtureBuildId },
            };
            var store = NewStore();

            using (var handle = CreatePrepared(store, FixedRequestId))
            {
                var result = await RunAsync(server, handle);
                Assert.Equal(exitCode, result.ExitCode);
                Assert.Equal(status, result.ObservedResult!.Status);
            }

            Assert.Null(NewStore().TryOpen(store.DefaultReceiptPath(FixedRequestId), out var reopened));
            using (reopened)
            {
                Assert.Equal(OperationPhase.Terminal, reopened!.Receipt.Phase);
                Assert.Equal(status, reopened.Receipt.LastObservation!.Status);
            }
        }

        [Fact]
        public void SnapshotRead_WithMalformedId_IsIntegrityError_NotAnException()
        {
            var store = NewStore();
            var ids = new[] { "\0", "../escaped", new string('A', 64), new string('a', 63), string.Empty };

            foreach (var id in ids)
            {
                var sourceError = store.TryReadSourceSnapshot(id, out var source);
                var resolvedError = store.TryReadResolvedSnapshot(id, out var resolved);

                Assert.Null(source);
                Assert.Null(resolved);
                Assert.Equal(LocalOperationStore.InvalidCode, sourceError!.Code);
                Assert.Equal(LocalOperationStore.InvalidCode, resolvedError!.Code);
            }
        }

        public static TheoryData<string> MalformedSourceEntries => new()
        {
            "undecodable_base64",
            "content_digest_mismatch",
            "uppercase_digest",
        };

        [Theory]
        [MemberData(nameof(MalformedSourceEntries))]
        public void S2_02_C01_MalformedSourceEntry_IsRefusedBeforePersist_ExitTwo_NoRpc(string corruption)
        {
            using var server = NewServer();
            var store = NewStore();

            var error = store.TryCreateToolSpecOperation(FixedRequestId, Endpoint, _envelope, CorruptSource(corruption), null, out var handle);

            Assert.Null(handle);
            Assert.Equal(LocalOperationStore.InvalidCode, error!.Code);
            Assert.Equal(2, error.ExitCode);
            Assert.False(File.Exists(store.DefaultReceiptPath(FixedRequestId)));
            Assert.False(Directory.Exists(Path.Join(store.RootDirectory, "snapshots", "source")));
            Assert.Empty(server.Fake.CallOrder);
        }

        [Theory]
        [MemberData(nameof(MalformedSourceEntries))]
        public void S2_02_C07_ReopenWithMalformedSourceEntry_IsIntegrityErrorExitTwo_NoRpc(string corruption)
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

            // The outer file hash is correct for these bytes; only the entry inside is wrong.
            var corruptBytes = JsonSerializer.SerializeToUtf8Bytes(CorruptSource(corruption));
            var corruptSha = OperationHashing.Sha256Hex(corruptBytes);
            File.WriteAllBytes(store.SourceSnapshotPath(corruptSha), corruptBytes);
            File.WriteAllText(path, File.ReadAllText(path).Replace(sourceSha, corruptSha, StringComparison.Ordinal));

            var readError = store.TryReadSourceSnapshot(corruptSha, out var snapshot);
            var openError = NewStore().TryOpen(path, out var reopened);

            Assert.Null(snapshot);
            Assert.Equal(LocalOperationStore.InvalidCode, readError!.Code);
            Assert.Null(reopened);
            Assert.Equal(LocalOperationStore.InvalidCode, openError!.Code);
            Assert.Equal(2, openError.ExitCode);
            Assert.Empty(server.Fake.CallOrder);
        }

        [Fact]
        public async Task S2_02_C02_SpecReferrerDigest_IsPreservedInTheTerminalReceipt()
        {
            const string specReferrerDigest =
                "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
            using var server = NewServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Pushing", BuildId = FixtureBuildId, SpecReferrerDigest = specReferrerDigest },
                new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = FixtureBuildId, ImageDigest = ImageDigest },
            };
            var store = NewStore();

            using (var handle = CreatePrepared(store, FixedRequestId))
            {
                var result = await RunAsync(server, handle);
                Assert.Equal(0, result.ExitCode);
                Assert.Equal(specReferrerDigest, result.ObservedResult!.SpecReferrerDigest);
            }

            Assert.Contains("\"spec_referrer_digest\"", File.ReadAllText(store.DefaultReceiptPath(FixedRequestId)), StringComparison.Ordinal);
            Assert.Null(NewStore().TryOpen(store.DefaultReceiptPath(FixedRequestId), out var reopened));
            using (reopened)
            {
                Assert.Equal(OperationPhase.Terminal, reopened!.Receipt.Phase);
                Assert.Equal(specReferrerDigest, reopened.Receipt.LastObservation!.SpecReferrerDigest);
                Assert.Equal(ImageDigest, reopened.Receipt.LastObservation.ImageDigest);
            }
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

        // A snapshot whose first entry no longer matches its declared digest.
        private static SourceSnapshot CorruptSource(string corruption)
        {
            var valid = Source(_recipeV1);
            var first = valid.Files[0];
            var broken = corruption switch
            {
                "undecodable_base64" => first with { ContentBase64 = "!!not-base64!!" },
                "content_digest_mismatch" => first with { ContentBase64 = Convert.ToBase64String(_recipeV2) },
                "uppercase_digest" => first with { Sha256 = first.Sha256.ToUpperInvariant() },
                _ => throw new ArgumentOutOfRangeException(nameof(corruption), corruption, null),
            };
            return valid with { Files = new[] { broken, valid.Files[1] } };
        }

        private static OperationReceipt ReadReceiptFromDisk(string path) =>
            JsonSerializer.Deserialize<OperationReceipt>(File.ReadAllBytes(path))!;

        private static async Task<ToolSpecOperationResult> RunAsync(GrpcTestServer server, OperationHandle handle, string endpoint = Endpoint)
        {
            using var client = new GrpcToolSpecClient(server.Channel);
            return await ToolSpecOperationRunner.RunAsync(handle, client, endpoint, cancellationToken: TestContext.Current.CancellationToken);
        }

        // Moves a prepared handle to submit_in_flight through the store API, binding a resolved
        // snapshot built from the handle's own envelope. Returns the resolved snapshot ID.
        private static string AdvanceToInFlight(LocalOperationStore store, OperationHandle handle, string digest = WireToolSpecDigest)
        {
            var receipt = handle.Receipt;
            var sha = WriteResolvedFor(store, receipt, digest);
            Assert.Null(handle.Advance(receipt with { Phase = OperationPhase.SubmitInFlight, ResolvedSnapshotSha256 = sha }));
            return sha;
        }

        // Writes a resolved snapshot built from the given receipt's attempt and envelope. Returns its ID.
        private static string WriteResolvedFor(LocalOperationStore store, OperationReceipt receipt, string digest = WireToolSpecDigest)
        {
            var basis = new ToolSpecSubmitBasis
            {
                RequestId = receipt.RequestId,
                RequestedToolName = receipt.Envelope.ToolName,
                RequestedVersion = receipt.Envelope.Version,
                ToolSpecDigest = digest,
                ResolvedToolName = receipt.Envelope.ToolName,
                ResolvedVersion = receipt.Envelope.Version,
            };
            Assert.Null(store.WriteResolvedSnapshot(ResolvedSnapshot.FromBasis(basis, receipt), out var sha));
            return sha;
        }

        private static void AssertRefusedUnchanged(OperationStoreError? error, OperationHandle handle, byte[] before, string code)
        {
            Assert.NotNull(error);
            Assert.Equal(code, error!.Code);
            Assert.Equal(2, error.ExitCode);
            Assert.Equal(before, File.ReadAllBytes(handle.ReceiptPath));
        }

        private static GrpcTestServer NewServer()
        {
            // Submit and watch report the same build, as a real server does for one attempt.
            var server = new GrpcTestServer();
            server.Fake.OnSubmitToolBuild = _ => new SubmitToolBuildResponse { BuildId = FixtureBuildId, Status = "Requested" };
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
                new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = FixtureBuildId },
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
