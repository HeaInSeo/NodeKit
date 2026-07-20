using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Cli.Tests.Fakes;
using Nodevault.V1;
using Xunit;
using GrpcToolSpecClient = NodeKit.Grpc.GrpcToolSpecClient;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// GrpcToolSpecClient를 in-process fake gRPC 서버(TestServer)에 실제로 붙여서
    /// 검증한다 — 진짜 프로토콜 직렬화/전송 경로를 태우므로, C# 레벨 목(mock)
    /// 테스트로는 못 잡는 문제(예: 필드 매핑 오탈자, 대소문자 불일치 등)까지
    /// 잡을 수 있다. seoy/NodeVault/환경변수 없이 매 테스트 실행마다 자동으로 돈다.
    /// </summary>
    public class GrpcToolSpecClientWireTests
    {
        [Fact]
        public async Task ResolveAndBuildAsync_SucceededBuild_YieldsSucceededEvent()
        {
            using var server = new GrpcTestServer();
            server.Fake.WatchEvents = new List<BuildEvent>
            {
                new() { Kind = BuildEventKind.Log, Status = "Building", BuildId = "b-1" },
                new() { Kind = BuildEventKind.Log, Status = "Succeeded", BuildId = "b-1" },
            };
            using var client = new GrpcToolSpecClient(server.Channel);

            var events = new List<NodeKit.Grpc.BuildEvent>();
            await foreach (var ev in client.ResolveAndBuildAsync("bwa", "0.7.17", "{}", CancellationToken.None))
            {
                events.Add(ev);
            }

            Assert.Contains(events, e => e.Kind == NodeKit.Grpc.BuildEventKind.Succeeded);
            Assert.DoesNotContain(events, e => e.Kind == NodeKit.Grpc.BuildEventKind.Failed);
        }

        [Fact]
        public async Task ResolveAndBuildAsync_FailedBuild_YieldsFailedEvent()
        {
            using var server = new GrpcTestServer();
            server.Fake.WatchEvents = new List<BuildEvent>
            {
                new() { Kind = BuildEventKind.Log, Status = "Building", BuildId = "b-2" },
                new() { Kind = BuildEventKind.Log, Status = "Failed", BuildId = "b-2", Message = "build image: exit status 1" },
            };
            using var client = new GrpcToolSpecClient(server.Channel);

            var events = new List<NodeKit.Grpc.BuildEvent>();
            await foreach (var ev in client.ResolveAndBuildAsync("bwa", "0.7.17", "{}", CancellationToken.None))
            {
                events.Add(ev);
            }

            Assert.Contains(events, e => e.Kind == NodeKit.Grpc.BuildEventKind.Failed);
            Assert.DoesNotContain(events, e => e.Kind == NodeKit.Grpc.BuildEventKind.Succeeded);
        }

        [Fact]
        public async Task ResolveAndBuildAsync_InterruptedBuild_YieldsFailedEvent()
        {
            using var server = new GrpcTestServer();
            server.Fake.WatchEvents = new List<BuildEvent>
            {
                new() { Kind = BuildEventKind.Log, Status = "Interrupted", BuildId = "b-3" },
            };
            using var client = new GrpcToolSpecClient(server.Channel);

            var events = new List<NodeKit.Grpc.BuildEvent>();
            await foreach (var ev in client.ResolveAndBuildAsync("bwa", "0.7.17", "{}", CancellationToken.None))
            {
                events.Add(ev);
            }

            Assert.Contains(events, e => e.Kind == NodeKit.Grpc.BuildEventKind.Failed);
        }

        [Fact]
        public async Task ResolveAndBuildAsync_StreamEndsWithoutTerminalStatus_DoesNotYieldSucceeded()
        {
            // Investigation probe: what happens if WatchToolBuild's stream just
            // ends (server restart, network blip, proxy timeout) without ever
            // sending a Succeeded/Failed/Interrupted status? MoveNext() returns
            // false and the while loop exits with no exception - if the client
            // doesn't notice, SubmitCommand's caller falls through to its own
            // "stream ended, return 0" fallback and reports success for a build
            // whose outcome was never actually observed.
            using var server = new GrpcTestServer();
            server.Fake.WatchEvents = new List<BuildEvent>
            {
                new() { Kind = BuildEventKind.Log, Status = "Building", BuildId = "b-4" },
                new() { Kind = BuildEventKind.Log, Status = "Building", BuildId = "b-4", Message = "still building" },
            };
            using var client = new GrpcToolSpecClient(server.Channel);

            var events = new List<NodeKit.Grpc.BuildEvent>();
            await foreach (var ev in client.ResolveAndBuildAsync("bwa", "0.7.17", "{}", CancellationToken.None))
            {
                events.Add(ev);
            }

            Assert.DoesNotContain(events, e => e.Kind == NodeKit.Grpc.BuildEventKind.Succeeded);
            Assert.DoesNotContain(events, e => e.Kind == NodeKit.Grpc.BuildEventKind.Failed);
        }

        [Fact]
        public async Task ResolveAndBuildAsync_ImageDigestFields_SurviveRealProtoRoundTrip()
        {
            // Adversarial review Major-1 follow-up (Issue #41 item 3/4): NodeVault
            // Sprint 7 P1a added image_ref/image_digest/spec_referrer_digest/
            // integrity_health to BuildEvent (proto field numbers 7-10) so
            // WatchToolBuild can expose them directly. This round-trips them
            // through a real in-process gRPC server/client (not a hand-built C#
            // object) so a field-number or mapping mistake in
            // GrpcToolSpecClient.MapWatchEvent would actually fail here.
            using var server = new GrpcTestServer();
            server.Fake.WatchEvents = new List<BuildEvent>
            {
                new()
                {
                    Kind = BuildEventKind.Log,
                    Status = "Running",
                    BuildId = "b-5",
                    ImageRef = "registry.internal/library/bwa-mem:0.7.17",
                    ImageDigest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                    SpecReferrerDigest = "sha256:fedcba9876543210fedcba9876543210fedcba9876543210fedcba98765432",
                    IntegrityHealth = "Healthy",
                },
                new() { Kind = BuildEventKind.Log, Status = "Succeeded", BuildId = "b-5" },
            };
            using var client = new GrpcToolSpecClient(server.Channel);

            var events = new List<NodeKit.Grpc.BuildEvent>();
            await foreach (var ev in client.ResolveAndBuildAsync("bwa", "0.7.17", "{}", CancellationToken.None))
            {
                events.Add(ev);
            }

            var withDigest = Assert.Single(events, e => !string.IsNullOrEmpty(e.ImageDigest));
            Assert.Equal("registry.internal/library/bwa-mem:0.7.17", withDigest.ImageRef);
            Assert.Equal("sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", withDigest.ImageDigest);
            Assert.Equal("sha256:fedcba9876543210fedcba9876543210fedcba9876543210fedcba98765432", withDigest.SpecReferrerDigest);
            Assert.Equal("Healthy", withDigest.IntegrityHealth);
        }

        [Fact]
        public async Task CancelBuildAsync_CallsFakeServerCancelToolBuild()
        {
            using var server = new GrpcTestServer();
            using var client = new GrpcToolSpecClient(server.Channel);

            await client.CancelBuildAsync("build-xyz", CancellationToken.None);

            Assert.Equal(new[] { "build-xyz" }, server.Fake.CancelledBuildIds);
        }

        [Fact]
        public async Task ResolveAndBuildAsync_CancelledDuringResolve_PropagatesCancellation_NotFailedEvent()
        {
            // Regression test (external review): GrpcToolSpecClient's Resolve/Submit
            // steps used to catch ALL exceptions -- including OperationCanceledException
            // and RpcException(Cancelled) -- and convert them into a plain Failed
            // BuildEvent indistinguishable from any other RPC failure. That silently
            // broke every caller-side cancellation handler: SubmitCommand's
            // --connect-timeout could never actually report exit code 124 (it just
            // fell through to the generic "빌드 실패" / exit 1 path instead), and the
            // GUI's cancel-a-superseded-build handling had the same latent gap. Only
            // a real GrpcToolSpecClient exercised against a real (if fake) gRPC
            // server reproduces this -- the SubmitCommandTests fakes throw
            // OperationCanceledException directly from the async-enumerable, bypassing
            // the try/catch this bug lived in entirely.
            //
            // The fix checks cancellationToken.IsCancellationRequested rather than the
            // exception's shape -- empirically, cancelling a hung in-process fake-server
            // call does NOT reliably surface as OperationCanceledException or
            // RpcException(Cancelled); it can come back as RpcException(Unknown,
            // "Exception was thrown by handler.") depending on how the server-side
            // handler's own cancellation unwinds. So this test only asserts that
            // *something* propagates (i.e. it wasn't swallowed into a Failed event) --
            // SubmitCommand separately treats "my own token is cancelled" as sufficient
            // evidence of cancellation regardless of what shape reaches it.
            using var server = new GrpcTestServer();
            server.Fake.HangOnResolveToolSpec = true;
            using var client = new GrpcToolSpecClient(server.Channel);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

            // cts.Token (not TestContext.Current.CancellationToken directly) is the point
            // of this test -- it's cancelled explicitly below to simulate --connect-timeout
            // firing, and is itself linked to the ambient test-cancellation token above.
#pragma warning disable xUnit1051
            var enumerator = client.ResolveAndBuildAsync("bwa", "0.7.17", "{}", cts.Token).GetAsyncEnumerator();
#pragma warning restore xUnit1051
            var moveNextTask = enumerator.MoveNextAsync().AsTask();
            cts.Cancel();

            Exception? thrown = null;
            try
            {
                await moveNextTask;
            }
            catch (Exception ex)
            {
                thrown = ex;
            }

            Assert.NotNull(thrown);
        }
    }
}
