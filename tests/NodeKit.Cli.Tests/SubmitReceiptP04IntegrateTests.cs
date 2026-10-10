using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
    /// P04.integrate (NodeKit v0.9.1 S2-02 + CLI·기록 선택안): `nodekit submit` drives the landed
    /// LocalOperationStore/ToolSpecOperationRunner. Default record is `.nodekit/receipts/&lt;request-id&gt;.json`
    /// next to the Recipe; `--receipt` overrides it and never overwrites an existing record (exit 2).
    /// The in-process gRPC fake does not dedup server-side; these tests prove local ordering and
    /// output only. `receipt watch|cancel|replay` is P05.reentry and is not covered here.
    /// </summary>
    public class SubmitReceiptP04IntegrateTests : IDisposable
    {
        private const string FixedRequestId = "11111111-1111-1111-1111-111111111111";

        private const string FixtureBuildId = "build-fixture-001";

        private const string ForeignBuildId = "build-foreign-999";

        private const string Digest =
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private static readonly string[] _resolveOnly = { "Resolve" };

        private static readonly string[] _resolveSubmitWatch = { "Resolve", "Submit", "Watch" };

        private readonly string _workDir = Path.Join(Path.GetTempPath(), "nodekit-p04-integrate-tests-" + Guid.NewGuid());

        private readonly IDisposable _resolveClientOverride =
            ResolveRecipeClientTestOverride.Use(NullResolveRecipeClient.Instance);

        public SubmitReceiptP04IntegrateTests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
            _resolveClientOverride.Dispose();
        }

        [Fact]
        public void Submit_DefaultReceipt_IsTerminalNextToRecipe_AndSubmitUsesTheStoredRequestId()
        {
            var recipePath = WriteRecipe();
            using var server = NewServer();

            var exitCode = Submit(server, recipePath, out var stdout, out var stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);
            Assert.Equal(_resolveSubmitWatch, server.Fake.CallOrder);
            Assert.Equal(FixedRequestId, Assert.Single(server.Fake.SubmitRequests).RequestId);

            var receiptPath = Path.Join(_workDir, ".nodekit", "receipts", FixedRequestId + ".json");
            Assert.Contains(receiptPath, stdout, StringComparison.Ordinal);
            var receipt = ReadReceipt(receiptPath);
            Assert.Equal(OperationPhase.Terminal, receipt.Phase);
            Assert.Equal(FixtureBuildId, receipt.BuildId);
            Assert.Equal(OperationObservation.SucceededOutcome, receipt.LastObservation!.Outcome);
            Assert.NotNull(receipt.ResolvedSnapshotSha256);

            // the source snapshot holds the exact Recipe bytes that were validated and rendered
            var store = LocalOperationStore.ForReceipt(receiptPath);
            Assert.Null(store.TryReadSourceSnapshot(receipt.SourceSnapshotSha256, out var source));
            var file = Assert.Single(source!.Files);
            Assert.Equal(Path.GetFileName(recipePath), file.LogicalPath);
            Assert.Equal(File.ReadAllBytes(recipePath), Convert.FromBase64String(file.ContentBase64));
        }

        [Fact]
        public void Submit_ExplicitReceiptPath_WritesThereAndKeepsSnapshotsUnderItsRoot()
        {
            var recipePath = WriteRecipe();
            var receiptDir = Directory.CreateDirectory(Path.Join(_workDir, "records")).FullName;
            var receiptPath = Path.Join(receiptDir, "op.json");
            using var server = NewServer();

            var exitCode = Submit(server, recipePath, out _, out _, "--receipt", receiptPath);

            Assert.Equal(0, exitCode);
            Assert.Equal(OperationPhase.Terminal, ReadReceipt(receiptPath).Phase);
            Assert.True(Directory.Exists(Path.Join(receiptDir, ".nodekit", "snapshots", "source")));
            Assert.False(Directory.Exists(Path.Join(_workDir, ".nodekit")));
        }

        [Fact]
        public void Submit_ExistingReceiptPath_ExitsTwo_NoRpc_FileUnchanged()
        {
            var recipePath = WriteRecipe();
            var receiptPath = Path.Join(_workDir, "existing.json");
            File.WriteAllText(receiptPath, "keep me");
            using var server = NewServer();

            var exitCode = Submit(server, recipePath, out var stdout, out _, "--receipt", receiptPath, "--format", "jsonl");

            Assert.Equal(2, exitCode);
            Assert.Empty(server.Fake.CallOrder);
            Assert.Equal("keep me", File.ReadAllText(receiptPath));
            var completed = SingleJsonl(stdout);
            Assert.Equal(LocalOperationStore.ExistsCode, completed.GetProperty("error_code").GetString());
            Assert.False(completed.TryGetProperty("build_id", out _));
        }

        [Theory]
        [InlineData("../escape")]
        [InlineData("11111111-1111-1111-1111-11111111111A")]
        [InlineData("{11111111-1111-1111-1111-111111111111}")]
        public void Submit_NonCanonicalRequestId_ExitsTwo_NoRpc_NothingWritten(string requestId)
        {
            var recipePath = WriteRecipe();
            using var server = NewServer();

            var exitCode = Submit(server, recipePath, () => requestId, out _, out var stderr);

            Assert.Equal(2, exitCode);
            Assert.Empty(server.Fake.CallOrder);
            Assert.Contains(LocalOperationStore.InvalidCode, stderr, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Join(_workDir, ".nodekit")));
        }

        [Fact]
        public void Submit_ResolvedSnapshotWriteFails_ExitsTwo_SubmitZero_NoBuildId()
        {
            var recipePath = WriteRecipe();
            Directory.CreateDirectory(Path.Join(_workDir, ".nodekit", "snapshots"));
            File.WriteAllText(Path.Join(_workDir, ".nodekit", "snapshots", "resolved"), "not a directory");
            using var server = NewServer();

            var exitCode = Submit(server, recipePath, out var stdout, out _, "--format", "jsonl");

            Assert.Equal(2, exitCode);
            Assert.Equal(_resolveOnly, server.Fake.CallOrder);

            // progress before Submit may print state records; the last record is the single completed one
            var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.All(lines, l => Assert.DoesNotContain("build_id", l, StringComparison.Ordinal));
            using var last = JsonDocument.Parse(lines[^1]);
            var completed = last.RootElement;
            Assert.Equal("completed", completed.GetProperty("type").GetString());
            Assert.Single(lines, l => l.Contains("\"type\":\"completed\"", StringComparison.Ordinal));
            Assert.StartsWith("OPERATION_", completed.GetProperty("error_code").GetString(), StringComparison.Ordinal);
            Assert.Equal("terminal", completed.GetProperty("recovery").GetString());
            Assert.Equal(OperationPhase.Prepared, ReadReceipt(DefaultReceiptPath()).Phase);
        }

        [Fact]
        public void Submit_WatchFailed_ExitsOne_ReceiptKeepsFailedOutcome_Jsonl()
        {
            var recipePath = WriteRecipe();
            using var server = NewServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
                new() { Kind = ProtoBuildEventKind.Failed, Status = "Failed", BuildId = FixtureBuildId, Message = "fixture: build \"broke\"\nline2" },
            };

            var exitCode = Submit(server, recipePath, out var stdout, out _, "--format", "jsonl");

            Assert.Equal(1, exitCode);
            var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            using var last = JsonDocument.Parse(lines[^1]);
            Assert.Equal("BUILD_FAILED", last.RootElement.GetProperty("error_code").GetString());
            Assert.Equal(FixtureBuildId, last.RootElement.GetProperty("build_id").GetString());
            var receipt = ReadReceipt(DefaultReceiptPath());
            Assert.Equal(OperationPhase.Terminal, receipt.Phase);
            Assert.Equal(OperationObservation.FailedOutcome, receipt.LastObservation!.Outcome);
        }

        [Fact]
        public void Submit_ForeignBuildEvent_ReportsRunnerMessage_NotStreamEnded_Jsonl()
        {
            var recipePath = WriteRecipe();
            using var server = NewServer();
            server.Fake.WatchEvents = ForeignBuildEvents();

            var exitCode = Submit(server, recipePath, out var stdout, out _, "--format", "jsonl");

            Assert.Equal(1, exitCode);
            var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Single(lines, l => l.Contains("\"type\":\"completed\"", StringComparison.Ordinal));
            using var last = JsonDocument.Parse(lines[^1]);
            var completed = last.RootElement;
            Assert.Equal("UNEXPECTED_ERROR", completed.GetProperty("error_code").GetString());
            Assert.Equal(FixtureBuildId, completed.GetProperty("build_id").GetString());
            Assert.Equal("uncertain", completed.GetProperty("recovery").GetString());
            Assert.Contains(ForeignBuildId, completed.GetProperty("message").GetString(), StringComparison.Ordinal);

            // the foreign event is not stored; the receipt stays acknowledged for a later re-watch
            var receipt = ReadReceipt(DefaultReceiptPath());
            Assert.Equal(OperationPhase.Acknowledged, receipt.Phase);
            Assert.Equal(FixtureBuildId, receipt.BuildId);
        }

        [Fact]
        public void Submit_ForeignBuildEvent_ReportsRunnerMessage_NotStreamEnded_Human()
        {
            var recipePath = WriteRecipe();
            using var server = NewServer();
            server.Fake.WatchEvents = ForeignBuildEvents();

            var exitCode = Submit(server, recipePath, out _, out var stderr);

            Assert.Equal(1, exitCode);
            Assert.Contains(ForeignBuildId, stderr, StringComparison.Ordinal);
            Assert.Contains(FixtureBuildId, stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("서버 스트림이 종료되었습니다", stderr, StringComparison.Ordinal);
        }

        [Fact]
        public void Submit_StreamEndsWithoutTerminal_StaysStreamEnded_Jsonl()
        {
            var recipePath = WriteRecipe();
            using var server = NewServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
            };

            var exitCode = Submit(server, recipePath, out var stdout, out _, "--format", "jsonl");

            Assert.Equal(1, exitCode);
            var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            using var last = JsonDocument.Parse(lines[^1]);
            Assert.Equal("STREAM_ENDED_WITHOUT_RESULT", last.RootElement.GetProperty("error_code").GetString());
            Assert.Equal("uncertain", last.RootElement.GetProperty("recovery").GetString());
            Assert.Equal(OperationPhase.Acknowledged, ReadReceipt(DefaultReceiptPath()).Phase);
        }

        [Fact]
        public void Submit_InjectedClientWithoutReceiptOption_WritesNoRecord()
        {
            var recipePath = WriteRecipe();
            using var server = NewServer();
            using var client = new GrpcToolSpecClient(server.Channel);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(new[] { "submit", recipePath }, stdout, stderr, client);

            Assert.Equal(0, exitCode);
            Assert.False(Directory.Exists(Path.Join(_workDir, ".nodekit")));
        }

        private static GrpcTestServer NewServer()
        {
            var server = new GrpcTestServer();
            server.Fake.OnSubmitToolBuild = _ => new Nodevault.V1.SubmitToolBuildResponse { BuildId = FixtureBuildId, Status = "Requested" };
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
                new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = FixtureBuildId },
            };
            return server;
        }

        private static List<ProtoBuildEvent> ForeignBuildEvents() => new()
        {
            new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
            new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = ForeignBuildId },
            new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = ForeignBuildId },
        };

        private static int Submit(GrpcTestServer server, string recipePath, out string stdout, out string stderr, params string[] extraArgs) =>
            Submit(server, recipePath, () => FixedRequestId, out stdout, out stderr, extraArgs);

        private static int Submit(
            GrpcTestServer server, string recipePath, Func<string> requestIdProvider, out string stdout, out string stderr, params string[] extraArgs)
        {
            using var client = new GrpcToolSpecClient(server.Channel);
            using var stdoutWriter = new StringWriter();
            using var stderrWriter = new StringWriter();
            var args = new[] { "submit", recipePath }.Concat(extraArgs).ToArray();
            var exitCode = SubmitCommand.Run(args, stdoutWriter, stderrWriter, client, requestIdProvider);
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
