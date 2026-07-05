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
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            try
            {
                await foreach (var ev in client.ResolveAndBuildAsync(toolName, version, rawSpec, cts.Token))
                {
                    PrintEvent(ev, stdout);
                    if (!string.IsNullOrEmpty(ev.BuildId))
                    {
                        buildId = ev.BuildId;
                    }

                    if (ev.Kind == BuildEventKind.Succeeded)
                    {
                        return 0;
                    }

                    if (ev.Kind == BuildEventKind.Failed)
                    {
                        stderr.WriteLine($"빌드 실패: {ev.Message}");
                        return 1;
                    }
                }

                return 0;
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
        // NodeVault buildRequestFromResolved가 encoding/json으로 직접 파싱한다.
        private static string BuildRawSpec(ToolDefinition definition)
        {
            var payload = new Dictionary<string, object?>
            {
                ["tool_name"] = definition.Name,
                ["version"] = definition.Version,
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
