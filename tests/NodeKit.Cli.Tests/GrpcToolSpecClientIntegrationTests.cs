using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Authoring.Recipes;
using NodeKit.Grpc;
using NodeKit.Validation.Recipes;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// GrpcToolSpecClient(ResolveToolSpec → SubmitToolBuild → WatchToolBuild)
    /// 실연동 테스트 — live NodeVault의 ToolSpec API 계약을 검증한다. 적대적
    /// 리뷰 follow-up: 로컬 wire-level 테스트(GrpcToolSpecClientWireTests, 실제
    /// 프로토콜 직렬화는 태우지만 in-process fake 서버 대상)는 통과해도 live
    /// NodeVault와의 계약 검증은 스킵되고 있었다.
    ///
    /// NODEKIT_NODEVAULT_URL 환경변수가 설정된 경우에만 실행된다 (기본값: 스킵).
    /// 예: NODEKIT_NODEVAULT_URL=http://100.123.80.48:50051
    /// 현재 live 환경의 접속 정보는 ~/.config/infra-lab 문서를 확인한다.
    ///
    /// docs/fixtures/seoy-smoke/digest-referrer-check.json을 사용한다 — 소스
    /// 빌드가 없는 가장 빠른 fixture라, 이 opt-in 테스트가 실제로 켜졌을 때도
    /// 매번 수 분씩 걸리지 않는다. 나머지 두 fixture(structured 성공/legacy
    /// 거부)는 docs/NODEKIT_SEOY_SMOKE_FIXTURES.md의 수동 절차로 확인한다 —
    /// 소스 컴파일 소요 시간과 "거부되어야 정상"이라는 자동 테스트로는 어색한
    /// 기대값 때문에 지금은 자동화 대상에서 제외했다.
    /// </summary>
    public sealed class GrpcToolSpecClientIntegrationTests
    {
        private static bool ShouldRun =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NODEKIT_NODEVAULT_URL"));

        [Fact]
        [Trait("Category", "Integration")]
        public async Task ResolveAndBuildAsync_DigestReferrerCheckFixture_SucceedsAndExposesImageDigest()
        {
            if (!ShouldRun)
            {
                Assert.Skip("NODEKIT_NODEVAULT_URL 미설정 — 실제 NodeVault 연동 스킵");
            }

            var url = Environment.GetEnvironmentVariable("NODEKIT_NODEVAULT_URL")!;
            var recipeJson = File.ReadAllText(FindFixturePath("digest-referrer-check.json"));
            var recipe = JsonSerializer.Deserialize<RecipeDocument>(
                recipeJson,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter() },
                });
            Assert.NotNull(recipe);
            recipe!.Normalize();

            var validation = RecipeValidationPipeline.ValidateRecipe(recipe);
            Assert.True(validation.IsValid, "fixture가 로컬 L1 검증에 실패했습니다: " +
                string.Join("; ", validation.Violations));

            var definition = RecipeRenderer.Render(recipe);
            var rawSpec = ToolSpecRawSpecFactory.Build(definition);

            using var client = new GrpcToolSpecClient(url);
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

            string? imageDigest = null;
            var succeeded = false;
            var failureMessage = string.Empty;

            await foreach (var ev in client.ResolveAndBuildAsync(definition.Name, definition.Version, rawSpec, cts.Token))
            {
                if (!string.IsNullOrEmpty(ev.ImageDigest))
                {
                    imageDigest = ev.ImageDigest;
                }

                if (ev.Kind == BuildEventKind.Succeeded)
                {
                    succeeded = true;
                    break;
                }

                if (ev.Kind == BuildEventKind.Failed)
                {
                    failureMessage = ev.Message;
                    break;
                }
            }

            Assert.True(succeeded, $"빌드가 실패했습니다: {failureMessage}");
            Assert.False(
                string.IsNullOrEmpty(imageDigest),
                "WatchToolBuild가 ImageDigest를 채우지 않았습니다 — NodeVault Sprint 7 P1a 회귀 가능성.");
        }

        private static string FindFixturePath(string fileName)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Join(dir.FullName, "NodeKit.sln")))
            {
                dir = dir.Parent;
            }

            if (dir is null)
            {
                throw new FileNotFoundException("repo root(NodeKit.sln)를 찾지 못했습니다.");
            }

            var path = Path.Join(dir.FullName, "docs", "fixtures", "seoy-smoke", fileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"fixture not found: {path}");
            }

            return path;
        }
    }
}
