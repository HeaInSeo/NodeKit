using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
    /// P05.result slice1 (NodeKit v0.9.1 S2-05): the observed-result contract of a journaled `nodekit submit`.
    /// Each case runs the real GrpcToolSpecClient against the in-process fake and checks exit code, the
    /// nodekit.submit.v1 JSONL records (or the human stdout/stderr split) and, where it matters, the durable
    /// receipt. The fake only replays fixed watch events; image/server results are judged from those fixtures,
    /// not from a real NodeVault build or registration.
    /// </summary>
    public class SubmitResultP05Tests : IDisposable
    {
        private const string FixedRequestId = "55555555-5555-5555-5555-555555555555";

        private const string FixtureBuildId = "build-fixture-005";

        private const string ImageRef = "harbor.local/nodevault/bwa-mem:0.7.17";

        private const string ImageDigest =
            "sha256:1111111111111111111111111111111111111111111111111111111111111111";

        private const string LegacyDigest =
            "sha256:2222222222222222222222222222222222222222222222222222222222222222";

        private const string BaseDigest =
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        // quotes, a backslash and newlines must stay inside one JSON line
        private const string AwkwardMessage = "fixture \"quoted\" step\nsecond line \\ tab\tend";

        private readonly string _workDir = Path.Join(Path.GetTempPath(), "nodekit-p05-s205-tests-" + Guid.NewGuid());

        private readonly IDisposable _resolveClientOverride =
            ResolveRecipeClientTestOverride.Use(NullResolveRecipeClient.Instance);

        public SubmitResultP05Tests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
            _resolveClientOverride.Dispose();
        }

        // S2-05-C01
        [Fact]
        public void Jsonl_JobCreatedPushingSucceeded_SubmittedStateCompleted_Exit0_ArtifactFieldsKept_NoNullKeys()
        {
            using var server = NewServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Pushing", BuildId = FixtureBuildId },
                new()
                {
                    Kind = ProtoBuildEventKind.Log,
                    Status = "Succeeded",
                    BuildId = FixtureBuildId,
                    ImageRef = ImageRef,
                    ImageDigest = ImageDigest,
                    IntegrityHealth = "Healthy",
                },
            };

            var exitCode = Submit(server, out var stdout, out var stderr, "--format", "jsonl");

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr);
            var records = JsonlRecords(stdout);
            Assert.Equal(
                new[] { "state", "submitted", "state", "completed" },
                records.Select(r => r.GetProperty("type").GetString()).ToArray());
            Assert.All(records, AssertNoNullValues);
            Assert.All(records, r => Assert.Equal("nodekit.submit.v1", r.GetProperty("schema_version").GetString()));
            Assert.Equal(FixtureBuildId, records[1].GetProperty("build_id").GetString());
            Assert.Equal("Pushing", records[2].GetProperty("state").GetString());

            var completed = records[3];
            Assert.Equal("Succeeded", completed.GetProperty("status").GetString());
            Assert.Equal("none", completed.GetProperty("recovery").GetString());
            Assert.Equal(FixtureBuildId, completed.GetProperty("build_id").GetString());
            Assert.Equal(ImageRef, completed.GetProperty("image_ref").GetString());
            Assert.Equal(ImageDigest, completed.GetProperty("image_digest").GetString());
            Assert.Equal("Healthy", completed.GetProperty("integrity_health").GetString());
            Assert.Equal("fake-digest", completed.GetProperty("tool_spec_digest").GetString());
            foreach (var absent in new[] { "error_code", "phase", "remote_build_state", "message" })
            {
                Assert.False(completed.TryGetProperty(absent, out _), absent);
            }

            var receipt = ReadReceipt(DefaultReceiptPath());
            Assert.Equal(OperationPhase.Terminal, receipt.Phase);
            Assert.Equal(OperationObservation.SucceededOutcome, receipt.LastObservation!.Outcome);
            Assert.Equal(ImageDigest, receipt.LastObservation.ImageDigest);
        }

        // S2-05-C02
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void KnownBuildId_WatchFailed_Exit1_BuildFailedTerminal_MessageAndIdKept(bool jsonl)
        {
            using var server = NewServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
                new() { Kind = ProtoBuildEventKind.Log, Status = "Failed", BuildId = FixtureBuildId, Message = "fixture: step 3 exited 2" },
            };

            var exitCode = jsonl
                ? Submit(server, out var stdout, out var stderr, "--format", "jsonl")
                : Submit(server, out stdout, out stderr);

            Assert.Equal(1, exitCode);
            Assert.Empty(server.Fake.CancelledBuildIds);
            if (jsonl)
            {
                var completed = Completed(stdout);
                Assert.Equal("Failed", completed.GetProperty("status").GetString());
                Assert.Equal("BUILD_FAILED", completed.GetProperty("error_code").GetString());
                Assert.Equal("watch", completed.GetProperty("phase").GetString());
                Assert.Equal("failed", completed.GetProperty("remote_build_state").GetString());
                Assert.Equal("terminal", completed.GetProperty("recovery").GetString());
                Assert.Equal("fixture: step 3 exited 2", completed.GetProperty("message").GetString());
                Assert.Equal(FixtureBuildId, completed.GetProperty("build_id").GetString());
            }
            else
            {
                Assert.Contains("빌드 실패: fixture: step 3 exited 2", stderr, StringComparison.Ordinal);
                Assert.Contains(FixtureBuildId, stdout, StringComparison.Ordinal);
                Assert.DoesNotContain("fixture: step 3 exited 2", stdout, StringComparison.Ordinal);
            }

            var receipt = ReadReceipt(DefaultReceiptPath());
            Assert.Equal(OperationPhase.Terminal, receipt.Phase);
            Assert.Equal(OperationObservation.FailedOutcome, receipt.LastObservation!.Outcome);
            Assert.Equal(FixtureBuildId, receipt.BuildId);
        }

        // S2-05-C03
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SubmitRpcFails_NoBuildId_Exit1_PreWatchUnknown_HumanDoesNotClaimRemoteFailure(bool jsonl)
        {
            using var server = NewServer();
            server.Fake.OnSubmitToolBuild = _ => throw new RpcException(new Status(StatusCode.Unavailable, "fixture: submit reply lost"));

            var exitCode = jsonl
                ? Submit(server, out var stdout, out var stderr, "--format", "jsonl")
                : Submit(server, out stdout, out stderr);

            Assert.Equal(1, exitCode);
            Assert.Single(server.Fake.SubmitRequests);
            Assert.Empty(server.Fake.WatchRequests);
            Assert.Empty(server.Fake.CancelledBuildIds);
            if (jsonl)
            {
                var completed = Completed(stdout);
                Assert.Equal("Failed", completed.GetProperty("status").GetString());
                Assert.Equal("PRE_WATCH_FAILED", completed.GetProperty("error_code").GetString());
                Assert.Equal("pre_watch", completed.GetProperty("phase").GetString());
                Assert.Equal("unknown", completed.GetProperty("remote_build_state").GetString());
                Assert.Equal("uncertain", completed.GetProperty("recovery").GetString());
                Assert.False(completed.TryGetProperty("build_id", out _));
            }
            else
            {
                Assert.DoesNotContain("빌드 실패", stderr, StringComparison.Ordinal);
                Assert.DoesNotContain("빌드 실패", stdout, StringComparison.Ordinal);
                Assert.Contains("원격 빌드가 만들어졌는지는 확인하지 못했습니다", stderr, StringComparison.Ordinal);
                Assert.Contains("NodeVault에 연결할 수 없습니다", stderr, StringComparison.Ordinal);
            }

            Assert.Null(ReadReceipt(DefaultReceiptPath()).BuildId);
        }

        // S2-05-C04
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void JobCreated_StreamEof_NoTerminal_Exit1_StreamEndedUncertain_NoSuccessOrCancel(bool jsonl)
        {
            using var server = NewServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
            };

            var exitCode = jsonl
                ? Submit(server, out var stdout, out var stderr, "--format", "jsonl")
                : Submit(server, out stdout, out stderr);

            Assert.Equal(1, exitCode);
            Assert.Empty(server.Fake.CancelledBuildIds);
            if (jsonl)
            {
                var completed = Completed(stdout);
                Assert.Equal("Failed", completed.GetProperty("status").GetString());
                Assert.Equal("STREAM_ENDED_WITHOUT_RESULT", completed.GetProperty("error_code").GetString());
                Assert.Equal("uncertain", completed.GetProperty("recovery").GetString());
                Assert.Equal(FixtureBuildId, completed.GetProperty("build_id").GetString());
                Assert.DoesNotContain("Succeeded", stdout, StringComparison.Ordinal);
                Assert.DoesNotContain("Cancelled", stdout, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains($"build ID: {FixtureBuildId}", stderr, StringComparison.Ordinal);
                Assert.Contains("확인하지 못한 채", stderr, StringComparison.Ordinal);
                Assert.DoesNotContain("[성공]", stdout, StringComparison.Ordinal);
                Assert.DoesNotContain("취소", stdout + stderr, StringComparison.Ordinal);
            }

            var receipt = ReadReceipt(DefaultReceiptPath());
            Assert.Equal(OperationPhase.Acknowledged, receipt.Phase);
            Assert.Equal(FixtureBuildId, receipt.BuildId);
        }

        // S2-05-C05: progress/terminal messages with quotes and newlines stay one JSON record per line
        [Fact]
        public void Jsonl_MessagesWithQuotesAndNewlines_EveryStdoutLineIsOneJsonRecord_NoHumanText()
        {
            using var server = NewServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId, Message = AwkwardMessage },
                new() { Kind = ProtoBuildEventKind.Log, Status = "Failed", BuildId = FixtureBuildId, Message = AwkwardMessage },
            };

            var exitCode = Submit(server, out var stdout, out var stderr, "--format", "jsonl");

            Assert.Equal(1, exitCode);
            Assert.Equal(string.Empty, stderr);
            var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(4, lines.Length);
            var records = JsonlRecords(stdout);
            Assert.Equal(AwkwardMessage, records[2].GetProperty("message").GetString());
            Assert.Equal(AwkwardMessage, Completed(stdout).GetProperty("message").GetString());
            Assert.DoesNotContain("NodeVault에 빌드 요청을 시작합니다", stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("[로그]", stdout, StringComparison.Ordinal);
        }

        // S2-05-C05: a recipe parse preflight error is one completed record, diagnostics off stdout, no network call
        [Fact]
        public void Jsonl_RecipeParsePreflightError_OneCompletedRecord_Exit2_NoRpc()
        {
            using var server = NewServer();
            var recipePath = Path.Join(_workDir, "broken.json");
            File.WriteAllText(recipePath, "{ \"ToolName\": \"bwa-mem\", \"Version\": \"0.7.17\",\n \"Script\": \"echo \\\"hi\\\"\" ");

            var exitCode = Submit(server, recipePath, out var stdout, out _, "--format", "jsonl");

            Assert.Equal(2, exitCode);
            var record = Assert.Single(JsonlRecords(stdout));
            Assert.Equal("completed", record.GetProperty("type").GetString());
            Assert.Equal("Failed", record.GetProperty("status").GetString());
            Assert.False(string.IsNullOrEmpty(record.GetProperty("error_code").GetString()));
            Assert.Equal("terminal", record.GetProperty("recovery").GetString());
            Assert.Empty(server.Fake.CallOrder);
            Assert.False(File.Exists(DefaultReceiptPath()));
        }

        // S2-05-C06: legacy DigestAcquired → Succeeded without a final image_digest
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void LegacyDigestAcquired_ThenSucceeded_Exit0_LegacyDigestKept_NoRegistrationClaim(bool jsonl)
        {
            using var server = NewServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.DigestAcquired, BuildId = FixtureBuildId, Digest = LegacyDigest },
                new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = FixtureBuildId },
            };

            var exitCode = jsonl
                ? Submit(server, out var stdout, out var stderr, "--format", "jsonl")
                : Submit(server, out stdout, out stderr);

            Assert.Equal(0, exitCode);
            Assert.DoesNotContain("등록 완료", stdout + stderr, StringComparison.Ordinal);
            if (jsonl)
            {
                Assert.Equal(string.Empty, stderr);
                var completed = Completed(stdout);
                Assert.Equal("Succeeded", completed.GetProperty("status").GetString());
                Assert.Equal(LegacyDigest, completed.GetProperty("image_digest").GetString());
                Assert.False(completed.TryGetProperty("image_ref", out _));
                Assert.False(completed.TryGetProperty("integrity_health", out _));
            }
            else
            {
                Assert.Contains($"이미지 digest: {LegacyDigest}", stdout, StringComparison.Ordinal);
                Assert.DoesNotContain("제공되지 않았습니다", stdout, StringComparison.Ordinal);
            }
        }

        // S2-05-C06: ImageDigest + IntegrityHealth=Partial — success kept, health kept, warning only on human stderr
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void PartialIntegrity_Succeeded_Exit0_HealthKept_WarningOnlyOnHumanStderr(bool jsonl)
        {
            using var server = NewServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new()
                {
                    Kind = ProtoBuildEventKind.Log,
                    Status = "Succeeded",
                    BuildId = FixtureBuildId,
                    ImageRef = ImageRef,
                    ImageDigest = ImageDigest,
                    IntegrityHealth = "Partial",
                },
            };

            var exitCode = jsonl
                ? Submit(server, out var stdout, out var stderr, "--format", "jsonl")
                : Submit(server, out stdout, out stderr);

            Assert.Equal(0, exitCode);
            if (jsonl)
            {
                Assert.Equal(string.Empty, stderr);
                Assert.DoesNotContain("경고", stdout, StringComparison.Ordinal);
                var completed = Completed(stdout);
                Assert.Equal("Succeeded", completed.GetProperty("status").GetString());
                Assert.Equal("Partial", completed.GetProperty("integrity_health").GetString());
                Assert.Equal(ImageDigest, completed.GetProperty("image_digest").GetString());
            }
            else
            {
                Assert.Contains("경고: 무결성 상태가 Partial", stderr, StringComparison.Ordinal);
                Assert.DoesNotContain("경고", stdout, StringComparison.Ordinal);
                Assert.Contains($"이미지 digest: {ImageRef}@{ImageDigest}", stdout, StringComparison.Ordinal);
            }

            var observation = ReadReceipt(DefaultReceiptPath()).LastObservation!;
            Assert.Equal(OperationObservation.SucceededOutcome, observation.Outcome);
            Assert.Equal("Partial", observation.IntegrityHealth);
        }

        // S2-05-C07: an unknown status is progress only; only a real terminal ends the run
        [Fact]
        public void Jsonl_FutureStateThenSucceeded_FutureStateIsStateOnly_ExactlyOneCompleted()
        {
            using var server = NewServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "FutureState", BuildId = FixtureBuildId },
                new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = FixtureBuildId, ImageDigest = ImageDigest },
            };

            var exitCode = Submit(server, out var stdout, out _, "--format", "jsonl");

            Assert.Equal(0, exitCode);
            var records = JsonlRecords(stdout);
            var future = Assert.Single(records, r => r.TryGetProperty("state", out var s) && s.GetString() == "FutureState");
            Assert.Equal("state", future.GetProperty("type").GetString());
            Assert.Equal("Succeeded", Completed(stdout).GetProperty("status").GetString());
        }

        // S2-05-C07: an unknown status followed by EOF is not a success
        [Fact]
        public void Jsonl_FutureStateThenEof_Exit1_StreamEnded_NotSuccess()
        {
            using var server = NewServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "FutureState", BuildId = FixtureBuildId },
            };

            var exitCode = Submit(server, out var stdout, out _, "--format", "jsonl");

            Assert.Equal(1, exitCode);
            var completed = Completed(stdout);
            Assert.Equal("STREAM_ENDED_WITHOUT_RESULT", completed.GetProperty("error_code").GetString());
            Assert.Equal("uncertain", completed.GetProperty("recovery").GetString());
        }

        // S2-05-C07 terminal-name table: Succeeded/Failed/Interrupted by status, otherwise the proto kind
        [Theory]
        [InlineData("Succeeded", (int)ProtoBuildEventKind.Log, "Succeeded")]
        [InlineData("Failed", (int)ProtoBuildEventKind.Log, "Failed")]
        [InlineData("Interrupted", (int)ProtoBuildEventKind.Log, "Failed")]
        [InlineData("", (int)ProtoBuildEventKind.Succeeded, "Succeeded")]
        [InlineData("", (int)ProtoBuildEventKind.Failed, "Failed")]
        [InlineData("FutureState", (int)ProtoBuildEventKind.Log, "Log")]
        [InlineData("succeeded", (int)ProtoBuildEventKind.Log, "Log")]
        public void WatchTerminalNames_MapByStatusThenProtoKind(string status, int protoKind, string expectedKind)
        {
            var mapped = GrpcToolSpecClient.MapWatchEvent(new ProtoBuildEvent { Kind = (ProtoBuildEventKind)protoKind, Status = status, BuildId = FixtureBuildId });

            Assert.Equal(expectedKind, mapped.Kind.ToString());
        }

        private static void AssertNoNullValues(JsonElement record)
        {
            foreach (var property in record.EnumerateObject())
            {
                Assert.NotEqual(JsonValueKind.Null, property.Value.ValueKind);
            }
        }

        private static GrpcTestServer NewServer()
        {
            var server = new GrpcTestServer();
            server.Fake.OnSubmitToolBuild = _ => new Nodevault.V1.SubmitToolBuildResponse { BuildId = FixtureBuildId, Status = "Requested" };
            return server;
        }

        private int Submit(GrpcTestServer server, out string stdout, out string stderr, params string[] extraArgs) =>
            Submit(server, WriteRecipe(), out stdout, out stderr, extraArgs);

        private static int Submit(GrpcTestServer server, string recipePath, out string stdout, out string stderr, params string[] extraArgs)
        {
            using var client = new GrpcToolSpecClient(server.Channel);
            using var stdoutWriter = new StringWriter();
            using var stderrWriter = new StringWriter();
            var args = new[] { "submit", recipePath }.Concat(extraArgs).ToArray();
            var exitCode = SubmitCommand.RunUntilUserCancel(
                args, stdoutWriter, stderrWriter, client, () => FixedRequestId, TestContext.Current.CancellationToken);
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

        // every nonempty stdout line must parse on its own as one JSON object
        private static List<JsonElement> JsonlRecords(string stdout) =>
            stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
                    using var document = JsonDocument.Parse(line);
                    Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
                    return document.RootElement.Clone();
                })
                .ToList();

        private static JsonElement Completed(string stdout) =>
            Assert.Single(JsonlRecords(stdout), r => r.GetProperty("type").GetString() == "completed");

        private string DefaultReceiptPath() => Path.Join(_workDir, ".nodekit", "receipts", FixedRequestId + ".json");

        private string WriteRecipe()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.Conda,
                ToolName = "bwa-mem",
                Version = "0.7.17",
                Script = "run.sh",
                BaseImage = "condaforge/miniforge3:24.3.0-0@" + BaseDigest,
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
