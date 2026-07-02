using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Authoring;
using NodeKit.Authoring.Recipes;
using NodeKit.Grpc;
using NodeKit.Validation.Recipes;

namespace NodeKit.Cli
{
    /// <summary>
    /// nodekit submit &lt;recipe.json&gt; [--url &lt;nodevault-url&gt;] [--legacy]
    ///
    /// 기본 경로 (신규): ResolveToolSpec → SubmitToolBuild → WatchToolBuild.
    /// --legacy 플래그: BuildAndRegister 레거시 경로 (이전 NodeVault 호환).
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
            IBuildClient? legacyClient = null,
            IToolSpecBuildClient? toolSpecClient = null)
        {
            if (args.Length < 2)
            {
                stderr.WriteLine("사용법: nodekit submit <recipe.json> [--url <nodevault-url>] [--legacy]");
                return 2;
            }

            var recipePath = args[1];
            var url = ParseUrlOption(args) ?? Environment.GetEnvironmentVariable("NODEKIT_NODEVAULT_URL");
            var useLegacy = Array.IndexOf(args, "--legacy") >= 0;

            if (legacyClient is null && toolSpecClient is null && string.IsNullOrWhiteSpace(url))
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

            stdout.WriteLine($"NodeVault에 빌드를 제출합니다: {url ?? "(주입된 클라이언트)"}");
            stdout.WriteLine($"  도구: {definition.Name} {definition.Version}");
            stdout.WriteLine();

            // 레거시 클라이언트가 주입되거나 --legacy 플래그 사용 시 BuildAndRegister 경로
            if (legacyClient is not null || useLegacy)
            {
                var buildRequest = BuildRequestFactory.FromToolDefinition(definition);
                stdout.WriteLine($"  요청 ID: {buildRequest.RequestId}  [레거시 경로]");
                stdout.WriteLine();

                if (legacyClient is not null)
                {
                    return SubmitLegacyAsync(buildRequest, legacyClient, stdout, stderr).GetAwaiter().GetResult();
                }

                using var grpcLegacy = new GrpcBuildClient(url!);
                return SubmitLegacyAsync(buildRequest, grpcLegacy, stdout, stderr).GetAwaiter().GetResult();
            }

            // 기본: ResolveToolSpec → SubmitToolBuild → WatchToolBuild 신규 경로
            var rawSpec = BuildRawSpec(definition);
            stdout.WriteLine("  [신규 경로] ResolveToolSpec → SubmitToolBuild → WatchToolBuild");
            stdout.WriteLine();

            if (toolSpecClient is not null)
            {
                return SubmitToolSpecAsync(definition.Name, definition.Version, rawSpec, toolSpecClient, stdout, stderr)
                    .GetAwaiter().GetResult();
            }

            using var grpcToolSpec = new GrpcToolSpecClient(url!);
            return SubmitToolSpecAsync(definition.Name, definition.Version, rawSpec, grpcToolSpec, stdout, stderr)
                .GetAwaiter().GetResult();
        }

        // ── 신규 경로 ─────────────────────────────────────────────────────────

        private static async Task<int> SubmitToolSpecAsync(
            string toolName,
            string version,
            string rawSpec,
            IToolSpecBuildClient client,
            TextWriter stdout,
            TextWriter stderr)
        {
            using var cts = new CancellationTokenSource();
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
                stderr.WriteLine("빌드 요청이 취소되었습니다.");
                return 130;
            }
            catch (Exception ex)
            {
                stderr.WriteLine(BuildErrorMessages.Describe(ex));
                return 1;
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

        // ── 레거시 경로 ───────────────────────────────────────────────────────

        private static async Task<int> SubmitLegacyAsync(
            BuildRequest request,
            IBuildClient client,
            TextWriter stdout,
            TextWriter stderr)
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            try
            {
                await foreach (var ev in client.BuildAndRegisterAsync(request, cts.Token))
                {
                    PrintEvent(ev, stdout);
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
                stderr.WriteLine("빌드 요청이 취소되었습니다.");
                return 130;
            }
            catch (Exception ex)
            {
                stderr.WriteLine(BuildErrorMessages.Describe(ex));
                return 1;
            }
        }

        // ── 출력 ──────────────────────────────────────────────────────────────

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
