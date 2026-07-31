using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// GrpcResolveRecipeClient 실연동 테스트.
    ///
    /// NODEKIT_NODEVAULT_URL 환경변수가 설정된 경우에만 실행된다 (기본값: 스킵).
    /// 예: NODEKIT_NODEVAULT_URL=http://100.123.80.48:50051
    /// 현재 live 환경의 접속 정보는 ~/.config/infra-lab 문서를 확인한다.
    /// </summary>
    public sealed class GrpcResolveRecipeClientIntegrationTests
    {
        private static bool ShouldRun =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NODEKIT_NODEVAULT_URL"));

        [Fact]
        [Trait("Category", "Integration")]
        public async Task ResolveAsync_KnownCondaPackage_ReturnsCandidates()
        {
            if (!ShouldRun)
            {
                Assert.Skip("NODEKIT_NODEVAULT_URL 미설정 — 실제 NodeVault 연동 스킵");
            }

            using var client = GrpcResolveRecipeClient.TryCreate()!;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var result = await client.ResolveAsync(
                toolName: "bwa-integ-test",
                version: "0.7.17",
                packages: new List<string> { "bwa=0.7.17" },
                cancellationToken: cts.Token,
                buildKind: NodeKit.Authoring.Recipes.RecipeKind.Conda);

            Assert.NotEqual(RecipeResolutionSource.Unsupported, result.Source);
        }
    }
}
