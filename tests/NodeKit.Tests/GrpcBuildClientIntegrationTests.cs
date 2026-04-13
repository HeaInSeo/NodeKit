using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Grpc;
using Xunit;

namespace NodeKit.Tests
{
    /// <summary>
    /// GrpcBuildClient 실연동 테스트.
    ///
    /// 전제: NodeForge가 100.123.80.48:50051 에서 실행 중이어야 한다.
    /// NODEFORGE_INTEGRATION=1 환경변수가 설정된 경우에만 실행된다.
    /// </summary>
    public sealed class GrpcBuildClientIntegrationTests
    {
        private const string NodeForgeAddress = "http://100.123.80.48:50051";

        private static bool ShouldRun =>
            Environment.GetEnvironmentVariable("NODEFORGE_INTEGRATION") == "1";

        /// <summary>
        /// L2 build → Harbor push → L3 dry-run → L4 smoke → 등록 전 구간 성공 확인.
        /// </summary>
        [Fact]
        [Trait("Category", "Integration")]
        public async Task BuildAndRegister_SmokeToolSucceeds()
        {
            if (!ShouldRun)
            {
                return;
            }

            var request = new BuildRequest
            {
                RequestId = "nodekit-integration-01",
                ToolName = "nodekit-integ-tool",
                Version = "1.0.0",
                ImageUri = "busybox:1.36.1@sha256:9ae97d36d26566ff84e8893c64a6dc4fe8ca6d1144bf5b87b2b85a32def253c7",
                DockerfileContent =
                    "FROM busybox:1.36.1\nCMD [\"echo\", \"nodekit-integration-ok\"]",
                DisplayLabel = "NodeKit Integration Test Tool",
                DisplayCategory = "Test",
            };

            using var client = new GrpcBuildClient(NodeForgeAddress);
            var events = new List<BuildEvent>();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

#pragma warning disable CA2007
            await foreach (var ev in client.BuildAndRegisterAsync(request, cts.Token))
#pragma warning restore CA2007
            {
                events.Add(ev);
            }

            Assert.Contains(events, e => e.Kind == BuildEventKind.RegistryPushSucceeded);
            Assert.Contains(events, e =>
                e.Kind == BuildEventKind.DigestAcquired && !string.IsNullOrEmpty(e.Digest));
            Assert.Contains(events, e => e.Kind == BuildEventKind.Succeeded);
            Assert.DoesNotContain(events, e => e.Kind == BuildEventKind.Failed);
        }
    }
}
