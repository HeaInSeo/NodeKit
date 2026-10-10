using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Authoring.Recipes;
using NodeKit.Cli;
using NodeKit.Cli.Tests.Fakes;
using NodeKit.Grpc;
using Xunit;
using ProtoBuildEvent = Nodevault.V1.BuildEvent;
using ProtoBuildEventKind = Nodevault.V1.BuildEventKind;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// P05.wire (NodeKit v0.9.1 contract S2-01): the real outgoing ToolSpec RPC
    /// contract, captured from an in-process generated gRPC server — preflight
    /// RPC0, exact Resolve payload, Resolve→Submit→Watch identity hand-off and
    /// order, transport faults that stop before Watch, plus the staged client
    /// seam (caller request ID, full resolved basis, store-before-Submit).
    /// The fake checks only the transport contract: it does not parse raw_spec,
    /// enforce DisallowUnknownFields/full pins, dedup on the server side or
    /// look up registry existence, and no live NodeVault is contacted (S2-01-C06).
    /// </summary>
    public class ToolSpecWireP05Tests : IDisposable
    {
        private const string Digest =
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private const string WireToolSpecDigest =
            "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        private static readonly string[] _resolveSubmitWatch = { "Resolve", "Submit", "Watch" };

        private static readonly string[] _resolveOnly = { "Resolve" };

        private static readonly string[] _resolveSubmit = { "Resolve", "Submit" };

        private readonly string _workDir = Path.Join(Path.GetTempPath(), "nodekit-p05-wire-tests-" + Guid.NewGuid());
        private readonly IDisposable _resolveClientOverride =
            ResolveRecipeClientTestOverride.Use(NullResolveRecipeClient.Instance);

        public ToolSpecWireP05Tests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
            _resolveClientOverride.Dispose();
        }

        // ── S2-01-C01 · local preflight failures send no RPC ─────────────────

        [Fact]
        public void S2_01_C01_MissingSourceChecksum_ExitsOneWithZeroRpc()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.SourceBuild,
                ToolName = "bwa",
                Version = "0.7.17",
                Script = "run.sh",
                BaseImage = "registry.example.com/build:1@" + Digest,
                SourceUri = "https://example.com/bwa-0.7.17.tar.gz",
                SourceBuildCommands = new List<string> { "make" },
            };
            using var server = NewServer();

            var exitCode = Submit(server, WriteRecipe(recipe), out _, out var stderr);

            Assert.Equal(1, exitCode);
            Assert.Contains("L1-SRC-001", stderr, StringComparison.Ordinal);
            Assert.Empty(server.Fake.CallOrder);
        }

        [Fact]
        public void S2_01_C01_VersionOnlyPackage_StrictReproducible_ExitsOneWithZeroRpc()
        {
            using var server = NewServer();

            var exitCode = Submit(server, WriteRecipe(VersionOnlyRecipe()), out _, out var stderr, "--strict-reproducible");

            Assert.Equal(1, exitCode);
            Assert.Contains("L1-RCP-016", stderr, StringComparison.Ordinal);
            Assert.Empty(server.Fake.CallOrder);
        }

        [Fact]
        public void S2_01_C01_VersionOnlyPackage_DefaultSubmit_SendsTheRenderedPayloadUnchanged()
        {
            // 기본 호환 submit은 로컬 L1에서 허용하고 기록된 payload만 보존한다 —
            // 재현성이나 서버 수용 성공을 보장한다는 뜻이 아니다.
            var recipe = VersionOnlyRecipe();
            using var server = NewServer();

            var exitCode = Submit(server, WriteRecipe(recipe), out _, out var stderr);

            Assert.Equal(0, exitCode);
            Assert.DoesNotContain("L1-RCP-016", stderr, StringComparison.Ordinal);
            var resolve = Assert.Single(server.Fake.ResolveRequests);
            Assert.Equal(ToolSpecRawSpecFactory.Build(RecipeRenderer.Render(recipe)), resolve.RawSpec);
            Assert.Contains("bwa=0.7.17", resolve.RawSpec, StringComparison.Ordinal);
        }

        // ── S2-01-C02 · exact ResolveToolSpec request ────────────────────────

        [Fact]
        public void S2_01_C02_ResolveRequest_CarriesRecipeIdentityAndRenderedRawSpecBytes()
        {
            var recipePath = WriteRecipe(PackageRecipe());
            var renderedRawSpec = RenderRawSpec(recipePath);
            using var server = NewServer();
            var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var exitCode = Submit(server, recipePath, out _, out _);

            var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Assert.Equal(0, exitCode);
            var resolve = Assert.Single(server.Fake.ResolveRequests);
            Assert.Equal("bwa-mem", resolve.ToolName);
            Assert.Equal("0.7.17", resolve.Version);
            Assert.Equal(renderedRawSpec, resolve.RawSpec);

            using var document = JsonDocument.Parse(resolve.RawSpec);
            Assert.Equal(
                new[] { "tool_name", "version", "kind", "image_uri", "dockerfile_content", "script", "environment_spec" },
                document.RootElement.EnumerateObject().Select(p => p.Name).ToArray());

            // requested_at는 millisecond Unix timestamp — 초 단위였다면 이 범위에 들어올 수 없다.
            Assert.InRange(resolve.RequestedAt, before, after);
        }

        [Fact]
        public void S2_01_C02_ResolveRequest_IsNotTheLegacyBuildRequestPreview()
        {
            using var server = NewServer();

            Assert.Equal(0, Submit(server, WriteRecipe(PackageRecipe()), out _, out _));

            var rawSpec = Assert.Single(server.Fake.ResolveRequests).RawSpec;
            using var document = JsonDocument.Parse(rawSpec);
            foreach (var previewKey in new[] { "ToolName", "ImageUri", "DockerfileContent", "Command", "Inputs", "Outputs", "DisplayLabel" })
            {
                Assert.False(document.RootElement.TryGetProperty(previewKey, out _), previewKey);
            }
        }

        // ── S2-01-C03 · Resolve→Submit→Watch identity hand-off and order ─────

        [Fact]
        public void S2_01_C03_ResolvedDigestAndBuildId_FlowIntoSubmitAndWatch_InOrderOnceEach()
        {
            using var server = NewServer();
            server.Fake.OnResolveToolSpec = r => new Nodevault.V1.ResolvedToolSpecResponse
            {
                ToolSpecDigest = WireToolSpecDigest,
                ToolName = r.ToolName,
                Version = r.Version,
            };
            server.Fake.OnSubmitToolBuild = _ => new Nodevault.V1.SubmitToolBuildResponse
            {
                BuildId = "build-fixture-001",
                Status = "Requested",
            };

            var exitCode = Submit(server, WriteRecipe(PackageRecipe()), out var stdout, out _, "--format", "jsonl");

            Assert.Equal(0, exitCode);
            Assert.Equal(_resolveSubmitWatch, server.Fake.CallOrder);
            Assert.Equal(WireToolSpecDigest, Assert.Single(server.Fake.SubmitRequests).ToolSpecDigest);
            Assert.Equal("build-fixture-001", Assert.Single(server.Fake.WatchRequests).BuildId);
            Assert.Contains("\"build_id\":\"build-fixture-001\"", stdout, StringComparison.Ordinal);
        }

        // ── S2-01-C04 · Resolve transport failure stops before Submit ────────

        [Fact]
        public async Task S2_01_C04_ResolveUnavailable_NoSubmitNoWatch_FailedWithDiagnostic()
        {
            using var server = NewServer();
            server.Fake.OnResolveToolSpec = _ =>
                throw new global::Grpc.Core.RpcException(new global::Grpc.Core.Status(global::Grpc.Core.StatusCode.Unavailable, "fixture: nodevault down"));
            using var client = new GrpcToolSpecClient(server.Channel);

            var events = await CollectAsync(client.ResolveAndBuildAsync("bwa", "0.7.17", "{\"raw\":1}", TestContext.Current.CancellationToken));

            var failed = Assert.Single(events);
            Assert.Equal(BuildEventKind.Failed, failed.Kind);
            Assert.False(string.IsNullOrWhiteSpace(failed.Message));
            Assert.Equal(_resolveOnly, server.Fake.CallOrder);
            Assert.Equal("{\"raw\":1}", Assert.Single(server.Fake.ResolveRequests).RawSpec);
        }

        [Fact]
        public void S2_01_C04_ResolveUnavailable_CliReportsPreWatchUnknown_WithoutRetry()
        {
            using var server = NewServer();
            server.Fake.OnResolveToolSpec = _ =>
                throw new global::Grpc.Core.RpcException(new global::Grpc.Core.Status(global::Grpc.Core.StatusCode.Unavailable, "fixture: nodevault down"));

            var exitCode = Submit(server, WriteRecipe(PackageRecipe()), out var stdout, out _, "--format", "jsonl");

            Assert.Equal(1, exitCode);
            Assert.Equal(_resolveOnly, server.Fake.CallOrder);
            Assert.Contains("\"error_code\":\"PRE_WATCH_FAILED\"", stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("build_id", stdout, StringComparison.Ordinal);
        }

        // ── S2-01-C05 · Submit response lost → no Watch, no invented build ID ─

        [Fact]
        public async Task S2_01_C05_SubmitResponseLost_NoWatch_NoBuildIdInvented()
        {
            using var server = NewServer();
            server.Fake.OnSubmitToolBuild = _ =>
                throw new global::Grpc.Core.RpcException(new global::Grpc.Core.Status(global::Grpc.Core.StatusCode.Unavailable, "fixture: response lost"));
            using var client = new GrpcToolSpecClient(server.Channel);

            var events = await CollectAsync(client.ResolveAndBuildAsync("bwa", "0.7.17", "{}", TestContext.Current.CancellationToken));

            Assert.Equal(_resolveSubmit, server.Fake.CallOrder);
            Assert.Empty(server.Fake.WatchRequests);
            Assert.All(events, e => Assert.Equal(string.Empty, e.BuildId));
            Assert.Equal(BuildEventKind.Failed, events[^1].Kind);
            Assert.DoesNotContain(events, e => e.Kind is BuildEventKind.JobCreated or BuildEventKind.Succeeded);
        }

        [Fact]
        public void S2_01_C05_SubmitResponseLost_CliKeepsRemoteStateUnknown()
        {
            using var server = NewServer();
            server.Fake.OnSubmitToolBuild = _ =>
                throw new global::Grpc.Core.RpcException(new global::Grpc.Core.Status(global::Grpc.Core.StatusCode.Unavailable, "fixture: response lost"));

            var exitCode = Submit(server, WriteRecipe(PackageRecipe()), out var stdout, out _, "--format", "jsonl");

            Assert.Equal(1, exitCode);
            Assert.Empty(server.Fake.WatchRequests);
            Assert.Contains("\"remote_build_state\":\"unknown\"", stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("build_id", stdout, StringComparison.Ordinal);
        }

        // ── staged client seam (P05.wire contract supplement) ───────────────

        [Fact]
        public async Task Staged_CallerRequestId_IsTheSubmitRequestId_AndSurfacesOnJobCreated()
        {
            using var server = NewServer();
            server.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = "fake-build-id" },
            };
            using var client = new GrpcToolSpecClient(server.Channel);
            var options = new ToolSpecBuildOptions { RequestId = "req-caller-0001" };

            var events = await CollectAsync(client.ResolveAndBuildAsync("bwa", "0.7.17", "{}", options, TestContext.Current.CancellationToken));

            Assert.Equal("req-caller-0001", Assert.Single(server.Fake.SubmitRequests).RequestId);
            Assert.Equal("req-caller-0001", Assert.Single(events, e => e.Kind == BuildEventKind.JobCreated).RequestId);
            Assert.Equal(_resolveSubmitWatch, server.Fake.CallOrder);
        }

        [Fact]
        public async Task Staged_BeforeSubmit_GetsFullResolvedBasis_BeforeAnySubmit()
        {
            using var server = NewServer();
            server.Fake.OnResolveToolSpec = _ => new Nodevault.V1.ResolvedToolSpecResponse
            {
                ToolSpecDigest = WireToolSpecDigest,
                ToolName = "bwa-server",
                Version = "0.7.17-server",
                ResolvedAt = 1_760_000_000_123,
            };
            using var client = new GrpcToolSpecClient(server.Channel);
            ToolSpecSubmitBasis? stored = null;
            var submitsAtStore = -1;
            var options = new ToolSpecBuildOptions
            {
                RequestId = "req-store-0001",
                BeforeSubmitAsync = (basis, _) =>
                {
                    stored = basis;
                    submitsAtStore = server.Fake.SubmitRequests.Count;
                    return Task.CompletedTask;
                },
            };

            var events = await CollectAsync(client.ResolveAndBuildAsync("bwa", "0.7.17", "{}", options, TestContext.Current.CancellationToken));

            Assert.NotNull(stored);
            Assert.Equal(0, submitsAtStore);
            Assert.Equal("req-store-0001", stored!.RequestId);
            Assert.Equal("bwa", stored.RequestedToolName);
            Assert.Equal("0.7.17", stored.RequestedVersion);
            Assert.Equal(Assert.Single(server.Fake.ResolveRequests).RequestedAt, stored.RequestedAtUnixMilliseconds);
            Assert.Equal(WireToolSpecDigest, stored.ToolSpecDigest);
            Assert.Equal("bwa-server", stored.ResolvedToolName);
            Assert.Equal("0.7.17-server", stored.ResolvedVersion);
            Assert.Equal(1_760_000_000_123, stored.ResolvedAt);

            var submit = Assert.Single(server.Fake.SubmitRequests);
            Assert.Equal(stored.RequestId, submit.RequestId);
            Assert.Equal(stored.ToolSpecDigest, submit.ToolSpecDigest);
            Assert.Same(stored, Assert.Single(events, e => e.SubmitBasis is not null).SubmitBasis);
        }

        [Fact]
        public async Task Staged_BeforeSubmitFails_NoSubmitNoWatch_FailedNamesRequestId()
        {
            using var server = NewServer();
            using var client = new GrpcToolSpecClient(server.Channel);
            var options = new ToolSpecBuildOptions
            {
                RequestId = "req-store-fail",
                BeforeSubmitAsync = (_, _) => throw new IOException("fixture: journal disk full"),
            };

            var events = await CollectAsync(client.ResolveAndBuildAsync("bwa", "0.7.17", "{}", options, TestContext.Current.CancellationToken));

            Assert.Equal(_resolveOnly, server.Fake.CallOrder);
            var failed = events[^1];
            Assert.Equal(BuildEventKind.Failed, failed.Kind);
            Assert.Contains("req-store-fail", failed.Message, StringComparison.Ordinal);
            Assert.Contains("journal disk full", failed.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(events, e => !string.IsNullOrEmpty(e.BuildId));
        }

        [Fact]
        public async Task Staged_BeforeSubmitCancelled_PropagatesInsteadOfFailedEvent()
        {
            using var server = NewServer();
            using var client = new GrpcToolSpecClient(server.Channel);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var options = new ToolSpecBuildOptions
            {
                BeforeSubmitAsync = async (_, token) =>
                {
                    await cts.CancelAsync();
                    token.ThrowIfCancellationRequested();
                },
            };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => CollectAsync(client.ResolveAndBuildAsync("bwa", "0.7.17", "{}", options, cts.Token)));
            Assert.Equal(_resolveOnly, server.Fake.CallOrder);
        }

        [Fact]
        public async Task Staged_BeforeSubmitCancelledByOwnToken_PropagatesWhileOuterTokenActive()
        {
            // Codex P2 r4236516591: the durable store can time out on its own token
            // while the build token is still live. That is cancellation, not a store
            // failure, so it must propagate and nothing may be submitted.
            using var server = NewServer();
            using var client = new GrpcToolSpecClient(server.Channel);
            using var storeTimeout = new CancellationTokenSource();
            await storeTimeout.CancelAsync();
            var options = new ToolSpecBuildOptions
            {
                BeforeSubmitAsync = (_, _) => throw new OperationCanceledException(storeTimeout.Token),
            };
            var outer = TestContext.Current.CancellationToken;

            var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => CollectAsync(client.ResolveAndBuildAsync("bwa", "0.7.17", "{}", options, outer)));

            Assert.False(outer.IsCancellationRequested);
            Assert.Equal(storeTimeout.Token, thrown.CancellationToken);
            Assert.Equal(_resolveOnly, server.Fake.CallOrder);
            Assert.Empty(server.Fake.SubmitRequests);
        }

        [Fact]
        public async Task Staged_DefaultOverload_GeneratesAFreshGuidRequestIdPerSubmit()
        {
            using var server = NewServer();
            using var client = new GrpcToolSpecClient(server.Channel);

            await CollectAsync(client.ResolveAndBuildAsync("bwa", "0.7.17", "{}", TestContext.Current.CancellationToken));
            await CollectAsync(client.ResolveAndBuildAsync("bwa", "0.7.17", "{}", new ToolSpecBuildOptions(), TestContext.Current.CancellationToken));

            var ids = server.Fake.SubmitRequests.Select(r => r.RequestId).ToList();
            Assert.Equal(2, ids.Count);
            Assert.All(ids, id => Assert.True(Guid.TryParse(id, out _), id));
            Assert.NotEqual(ids[0], ids[1]);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Staged_BlankRequestId_IsRejectedBeforeAnyRpc(string requestId)
        {
            using var server = NewServer();
            using var client = new GrpcToolSpecClient(server.Channel);

            Assert.Throws<ArgumentException>(() =>
                client.ResolveAndBuildAsync("bwa", "0.7.17", "{}", new ToolSpecBuildOptions { RequestId = requestId }, TestContext.Current.CancellationToken));
            Assert.Empty(server.Fake.CallOrder);
        }

        [Fact]
        public async Task Staged_LegacyOnlyClient_DefaultOptionsDelegate_NonDefaultOptionsAreNotSilentlyIgnored()
        {
            IToolSpecBuildClient legacy = new LegacyOnlyClient();

            var events = await CollectAsync(legacy.ResolveAndBuildAsync("bwa", "0.7.17", "{}", new ToolSpecBuildOptions(), TestContext.Current.CancellationToken));
            Assert.Equal(BuildEventKind.Succeeded, Assert.Single(events).Kind);

            Assert.Throws<NotSupportedException>(() =>
                legacy.ResolveAndBuildAsync("bwa", "0.7.17", "{}", new ToolSpecBuildOptions { RequestId = "req-1" }, TestContext.Current.CancellationToken));
            Assert.Throws<NotSupportedException>(() =>
                legacy.ResolveAndBuildAsync(
                    "bwa",
                    "0.7.17",
                    "{}",
                    new ToolSpecBuildOptions { BeforeSubmitAsync = (_, _) => Task.CompletedTask },
                    TestContext.Current.CancellationToken));
        }

        // ── helpers ──────────────────────────────────────────────────────────

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

        private static int Submit(GrpcTestServer server, string recipePath, out string stdout, out string stderr, params string[] extraArgs)
        {
            using var client = new GrpcToolSpecClient(server.Channel);
            using var stdoutWriter = new StringWriter();
            using var stderrWriter = new StringWriter();
            var args = new[] { "submit", recipePath }.Concat(extraArgs).ToArray();
            var exitCode = SubmitCommand.Run(args, stdoutWriter, stderrWriter, client);
            stdout = stdoutWriter.ToString();
            stderr = stderrWriter.ToString();
            return exitCode;
        }

        private static async Task<List<BuildEvent>> CollectAsync(IAsyncEnumerable<BuildEvent> source)
        {
            var events = new List<BuildEvent>();
            await foreach (var ev in source)
            {
                events.Add(ev);
            }

            return events;
        }

        private static RecipeDocument PackageRecipe() => new()
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

        private static RecipeDocument VersionOnlyRecipe()
        {
            var recipe = PackageRecipe();
            recipe.Packages = new List<string> { "bwa=0.7.17" };
            return recipe;
        }

        private string WriteRecipe(RecipeDocument recipe)
        {
            var path = Path.Join(_workDir, "input-" + Guid.NewGuid() + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(recipe, RecipeCreateCommand.JsonOptions));
            return path;
        }

        private string RenderRawSpec(string recipePath)
        {
            var rawSpecPath = Path.Join(_workDir, "raw-spec-" + Guid.NewGuid() + ".json");
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            Assert.Equal(0, CliApp.Run(new[] { "render", recipePath, "--out", rawSpecPath, "--format", "raw-spec" }, TextReader.Null, stdout, stderr));
            Assert.Empty(stderr.ToString());
            return File.ReadAllText(rawSpecPath);
        }

        private sealed class LegacyOnlyClient : IToolSpecBuildClient
        {
#pragma warning disable CS1998
            public async IAsyncEnumerable<BuildEvent> ResolveAndBuildAsync(
                string toolName,
                string version,
                string rawSpec,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield return new BuildEvent { Kind = BuildEventKind.Succeeded };
            }
#pragma warning restore CS1998

            public Task CancelBuildAsync(string buildId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }
    }
}
