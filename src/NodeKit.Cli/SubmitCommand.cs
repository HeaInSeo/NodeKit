using System;
using System.Collections.Generic;
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
                stderr.WriteLine("사용법: nodekit submit <recipe.json> [--url <nodevault-url>]");
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

            var validation = RecipeValidationPipeline.ValidateRecipe(recipe);
            if (!validation.IsValid)
            {
                CliApp.PrintViolations(validation.Violations, stderr);
                return 1;
            }

            var definition = RecipeRenderer.Render(recipe);
            var rawSpec = BuildRawSpec(definition);

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

                    if (ev.Kind == BuildEventKind.Succeeded)
                    {
                        // NodeVault의 WatchToolBuild가 아직 DigestAcquired 이벤트를
                        // 안정적으로 보내지 않는 경우가 있다(라이브 테스트에서 확인) —
                        // 조용히 넘어가지 않고 어디서 확인해야 하는지 안내한다.
                        if (!digestReceived)
                        {
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

            try
            {
                await client.CancelBuildAsync(buildId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"경고: 서버에 빌드 취소 요청을 보내지 못했습니다 (build ID: {buildId}): {ex.Message}");
            }
        }

        // raw_spec은 proto BuildRequest 필드명(snake_case) 기반 JSON이다.
        // NodeVault buildRequestFromResolved가 encoding/json(protojson이 아님)으로
        // 직접 파싱한다. inputs/outputs/display/command는 proto BuildRequest에서
        // 이미 reserved 처리되어 있어 여기 담아도 받을 필드가 없다 — NodeVault가
        // 스키마에서 뺀 것이므로 이 payload에 채워 넣을 대상이 아니다.
        //
        // "kind"는 생략하면 BuildKind_BUILD_KIND_UNSPECIFIED(0)가 되는데, NodeVault
        // 쪽은 UNSPECIFIED를 BUILD_KIND_TOOLSPEC과 동일하게 처리하고 있어(우연히)
        // 지금은 문제가 없다. 다만 그 동작에 기대는 대신 실제 의미(recipe 기반
        // base image + Dockerfile 빌드)를 명시한다 — encoding/json은 protojson이
        // 아니라 커스텀 (Un)MarshalJSON도 없으므로 열거형 이름이 아니라 정수값(1)을
        // 그대로 보낸다.
        private const int BuildKindToolSpec = 1;

        private static string BuildRawSpec(ToolDefinition definition)
        {
            var payload = new Dictionary<string, object?>
            {
                ["tool_name"] = definition.Name,
                ["version"] = definition.Version,
                ["kind"] = BuildKindToolSpec,
                ["image_uri"] = definition.ImageUri,
                ["dockerfile_content"] = definition.DockerfileContent,
                ["script"] = definition.Script,
                ["environment_spec"] = definition.EnvironmentSpec,
            };
            return JsonSerializer.Serialize(payload);
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
