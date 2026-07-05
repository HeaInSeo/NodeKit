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
    /// NODEVAULT_INTEGRATION=1 환경변수가 설정된 경우에만 실행된다 (기본값: 스킵).
    /// 대상 주소는 NODEVAULT_INTEGRATION_ADDRESS로 오버라이드할 수 있다.
    /// 현재 live 환경의 접속 정보는 ~/.config/infra-lab 문서를 확인한다 —
    /// 기본값은 그 문서에 기록된 시점의 주소이며 인프라가 바뀌면 stale해질 수 있다.
    /// </summary>
    public sealed class GrpcBuildClientIntegrationTests
    {
        private const string DefaultNodeVaultAddress = "http://100.123.80.48:50051";

        private static string NodeVaultAddress =>
            Environment.GetEnvironmentVariable("NODEVAULT_INTEGRATION_ADDRESS") ?? DefaultNodeVaultAddress;

        private static bool ShouldRun =>
            Environment.GetEnvironmentVariable("NODEVAULT_INTEGRATION") == "1";

        /// <summary>
        /// L2 build → Harbor push → L3 dry-run → L4 smoke → 등록 전 구간 성공 확인.
        /// </summary>
        [Fact]
        [Trait("Category", "Integration")]
        public async Task BuildAndRegister_SmokeToolSucceeds()
        {
            if (!ShouldRun)
            {
                Assert.Skip("NODEVAULT_INTEGRATION=1 미설정 — 실제 NodeVault 연동 스킵");
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

            using var client = new GrpcBuildClient(NodeVaultAddress);
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
