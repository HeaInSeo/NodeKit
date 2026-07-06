using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Cli.Tests.Fakes;
using Nodevault.V1;
using Xunit;

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
        public async Task CancelBuildAsync_CallsFakeServerCancelToolBuild()
        {
            using var server = new GrpcTestServer();
            using var client = new GrpcToolSpecClient(server.Channel);

            await client.CancelBuildAsync("build-xyz", CancellationToken.None);

            Assert.Equal(new[] { "build-xyz" }, server.Fake.CancelledBuildIds);
        }
    }
}
