using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using NodeKit.Authoring;
using NodeKit.Authoring.Recipes;
using NodeKit.Grpc;
using NodeKit.Validation.Recipes;

namespace NodeKit.Cli
{
    /// <summary>
    /// nodekit submit &lt;recipe.json&gt; [--url &lt;nodevault-url&gt;]
    ///
    /// 경로: ResolveToolSpec → SubmitToolBuild → WatchToolBuild.
    /// NodeVault 주소: --url 옵션 또는 NODEKIT_NODEVAULT_URL 환경변수.
    /// </summary>
    internal static class SubmitCommand
    {
        private static readonly JsonSerializerOptions _recipeReadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public static int Run(
            string[] args,
            TextWriter stdout,
            TextWriter stderr,
            IToolSpecBuildClient? toolSpecClient = null)
        {
            if (args.Length < 2)
            {
                stderr.WriteLine("사용법: nodekit submit <recipe.json> [--url <nodevault-url>] [--strict-reproducible]");
                return 2;
            }

            var recipePath = args[1];
            var url = ParseUrlOption(args) ?? Environment.GetEnvironmentVariable("NODEKIT_NODEVAULT_URL");

            if (toolSpecClient is null && string.IsNullOrWhiteSpace(url))
            {
                stderr.WriteLine("NodeVault 주소가 필요합니다. --url 옵션 또는 NODEKIT_NODEVAULT_URL 환경변수를 설정하세요.");
                stderr.WriteLine("예: NODEKIT_NODEVAULT_URL=http://100.123.80.48:50051 nodekit submit recipe.json");
                return 2;
            }

            RecipeDocument recipe;
            try
            {
                var content = File.ReadAllText(recipePath);
                recipe = JsonSerializer.Deserialize<RecipeDocument>(content, _recipeReadOptions)
                    ?? throw new InvalidOperationException("recipe 파일이 비어있습니다.");
            }
            catch (IOException ex)
            {
                stderr.WriteLine($"recipe 파일을 읽을 수 없습니다: {recipePath} ({ex.Message})");
                return 2;
            }
            catch (JsonException ex)
            {
                stderr.WriteLine($"recipe JSON 파싱에 실패했습니다: {recipePath} ({ex.Message})");
                return 2;
            }
            catch (InvalidOperationException ex)
            {
                stderr.WriteLine(ex.Message);
                return 2;
            }

            recipe.Normalize();

            if (recipe.BuildKind is null)
            {
                stderr.WriteLine(
                    $"recipe 파일에 buildKind가 없습니다: {recipePath} " +
                    "(Conda | Micromamba | BioContainer | SourceBuild | PackageMirror | DockerfileFallback 중 하나를 지정하세요.)");
                return 2;
            }

            var validation = RecipeValidationPipeline.ValidateRecipe(recipe, CliApp.HasStrictReproducibleFlag(args));
            if (!validation.IsValid)
            {
                CliApp.PrintViolations(validation.Violations, stderr);
                return 1;
            }

            var definition = RecipeRenderer.Render(recipe);
            var rawSpec = ToolSpecRawSpecFactory.Build(definition);

            stdout.WriteLine($"NodeVault에 빌드를 제출합니다: {url ?? "(주입된 클라이언트)"}");
            stdout.WriteLine($"  도구: {definition.Name} {definition.Version}");
            stdout.WriteLine();

            if (toolSpecClient is not null)
            {
                return SubmitAsync(definition.Name, definition.Version, rawSpec, toolSpecClient, stdout, stderr)
                    .GetAwaiter().GetResult();
            }

            using var grpc = new GrpcToolSpecClient(url!);
            return SubmitAsync(definition.Name, definition.Version, rawSpec, grpc, stdout, stderr)
                .GetAwaiter().GetResult();
        }

        private static async Task<int> SubmitAsync(
            string toolName,
            string version,
            string rawSpec,
            IToolSpecBuildClient client,
            TextWriter stdout,
            TextWriter stderr)
        {
            using var cts = new CancellationTokenSource();
            string? buildId = null;
            var digestReceived = false;
            string? lastImageDigest = null;
            string? lastImageRef = null;
            ConsoleCancelEventHandler onCancelKeyPress = (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };
            Console.CancelKeyPress += onCancelKeyPress;

            try
            {
                await foreach (var ev in client.ResolveAndBuildAsync(toolName, version, rawSpec, cts.Token))
                {
                    PrintEvent(ev, stdout);
                    if (!string.IsNullOrEmpty(ev.BuildId))
                    {
                        buildId = ev.BuildId;
                    }

                    if (ev.Kind == BuildEventKind.DigestAcquired && !string.IsNullOrEmpty(ev.Digest))
                    {
                        digestReceived = true;
                    }

                    // ToolSpec 경로(WatchToolBuild)는 Kind가 항상 LOG이고 위
                    // DigestAcquired/Digest는 절대 채워지지 않는다 — 대신 매
                    // 이벤트마다 buildstate.Record를 그대로 실어 보내는
                    // ImageDigest/ImageRef를 채운다(NodeVault Sprint 7 P1a,
                    // commit 03f5025). 이 값이 오면 legacy digestReceived와
                    // 동등하게 취급해 아래 fallback 안내를 건너뛴다.
                    if (!string.IsNullOrEmpty(ev.ImageDigest))
                    {
                        digestReceived = true;
                        lastImageDigest = ev.ImageDigest;
                        lastImageRef = ev.ImageRef;
                    }

                    if (ev.Kind == BuildEventKind.Succeeded)
                    {
                        if (!string.IsNullOrEmpty(lastImageDigest))
                        {
                            stdout.WriteLine(string.IsNullOrEmpty(lastImageRef)
                                ? $"이미지 digest: {lastImageDigest}"
                                : $"이미지 digest: {lastImageRef}@{lastImageDigest}");
                        }
                        else if (!digestReceived)
                        {
                            // NodeVault의 WatchToolBuild가 아직 digest 정보를
                            // 안정적으로 보내지 않는 경우가 있다(라이브 테스트에서
                            // 확인) — 조용히 넘어가지 않고 어디서 확인해야 하는지
                            // 안내한다. NodeVault Sprint 7 P1a 이후로는 정상 경로
                            // (위 ImageDigest 분기)가 대신 실행되므로, 이 분기는
                            // 옛 NodeVault 버전이나 예상 못한 회귀에 대한
                            // safety-net으로만 남는다.
                            stdout.WriteLine(string.IsNullOrEmpty(buildId)
                                ? "이미지 digest가 서버에서 제공되지 않았습니다 — NodeVault 인덱스에서 직접 확인하세요."
                                : $"이미지 digest가 서버에서 제공되지 않았습니다 — NodeVault 인덱스에서 직접 확인하세요 (build ID: {buildId}).");
                        }

                        return 0;
                    }

                    if (ev.Kind == BuildEventKind.Failed)
                    {
                        stderr.WriteLine($"빌드 실패: {ev.Message}");
                        return 1;
                    }
                }

                // 스트림이 Succeeded/Failed 등 최종 상태 이벤트 없이 그냥 끝났다(서버
                // 재시작, 네트워크 문제 등) — 빌드 결과를 실제로 확인하지 못한 것이므로
                // 성공으로 간주하지 않는다.
                stderr.WriteLine(string.IsNullOrEmpty(buildId)
                    ? "빌드 결과를 확인하지 못한 채 서버 스트림이 종료되었습니다."
                    : $"빌드 결과를 확인하지 못한 채 서버 스트림이 종료되었습니다 (build ID: {buildId}). NodeVault에서 빌드 상태를 직접 확인하세요.");
                return 1;
            }
            catch (OperationCanceledException)
            {
                await CancelServerBuildBestEffort(client, buildId, stderr);
                stderr.WriteLine("빌드 요청이 취소되었습니다.");
                return 130;
            }
            catch (RpcException rpc) when (rpc.StatusCode == StatusCode.Cancelled)
            {
                await CancelServerBuildBestEffort(client, buildId, stderr);
                stderr.WriteLine("빌드 요청이 취소되었습니다.");
                return 130;
            }
            // Final fallback after the specific OperationCanceledException/
            // RpcException(Cancelled) cases above — any other failure
            // (network error, unexpected RpcException status, etc.) gets
            // the same treatment: describe it and exit 1, since the CLI
            // command needs to terminate cleanly either way rather than
            // crash with a raw stack trace.
            catch (Exception ex)
            {
                stderr.WriteLine(BuildErrorMessages.Describe(ex));
                return 1;
            }
            finally
            {
                Console.CancelKeyPress -= onCancelKeyPress;
            }
        }

        // 클라이언트 취소는 로컬 스트림만 끊을 뿐 서버 빌드를 멈추지 않는다 —
        // CancelToolBuild를 명시적으로 호출해야 서버가 실제로 빌드를 중단한다.
        // 이미 취소된 cts.Token을 재사용할 수 없으므로 별도 토큰으로 호출한다.
        private static async Task CancelServerBuildBestEffort(
            IToolSpecBuildClient client, string? buildId, TextWriter stderr)
        {
            if (string.IsNullOrEmpty(buildId))
            {
                return;
            }

            // Best-effort notification (same pattern as the GUI's
            // BuildSubmissionViewModel.CancelServerBuildBestEffort) — any
            // failure here just means the server-side build keeps running
            // instead of stopping early, which is reported as a warning,
            // not treated as a command failure.
            try
            {
                await client.CancelBuildAsync(buildId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"경고: 서버에 빌드 취소 요청을 보내지 못했습니다 (build ID: {buildId}): {ex.Message}");
            }
        }

        private static void PrintEvent(BuildEvent ev, TextWriter stdout)
        {
            var prefix = ev.Kind switch
            {
                BuildEventKind.JobCreated => "[빌드 시작]",
                BuildEventKind.JobRunning => "[실행 중]",
                BuildEventKind.RegistryPushSucceeded => "[Push 완료]",
                BuildEventKind.DigestAcquired => "[Digest]",
                BuildEventKind.Succeeded => "[성공]",
                BuildEventKind.Failed => "[실패]",
                _ => "[로그]",
            };

            if (ev.Kind == BuildEventKind.DigestAcquired && !string.IsNullOrEmpty(ev.Digest))
            {
                stdout.WriteLine($"{prefix} {ev.Digest}");
            }
            else if (!string.IsNullOrEmpty(ev.Message))
            {
                stdout.WriteLine($"{prefix} {ev.Message}");
            }
            else
            {
                stdout.WriteLine(prefix);
            }
        }

        private static string? ParseUrlOption(string[] args)
        {
            var idx = Array.IndexOf(args, "--url");
            return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
        }
    }
}
