using System;
using System.IO;
using System.Linq;
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
    /// nodekit submit &lt;recipe.json&gt; [--url &lt;nodevault-url&gt;] [--connect-timeout &lt;seconds&gt;]
    /// [--watch-timeout &lt;duration&gt;]
    ///
    /// 경로: ResolveToolSpec → SubmitToolBuild → WatchToolBuild.
    /// NodeVault 주소: --url 옵션 또는 NODEKIT_NODEVAULT_URL 환경변수.
    /// --connect-timeout은 build ID를 받기 전(ResolveToolSpec/SubmitToolBuild)
    /// 단계에만 적용된다. --watch-timeout은 그 반대 — build ID를 받은
    /// 뒤(WatchToolBuild로 실제 빌드를 관찰하는 동안)에만 적용된다. 둘 다
    /// 기본값 없음(옵트인) — --watch-timeout이 발동해도 서버 쪽 빌드는
    /// 취소하지 않는다(실제로 여전히 진행 중일 수 있으므로), CLI의 로컬
    /// watch만 끝낸다. 자세한 설계 배경은 Issue #71 참고.
    /// </summary>
    internal static class SubmitCommand
    {
        private const string UsageLine =
            "사용법: nodekit submit <recipe.json> [--url <nodevault-url>] [--connect-timeout <seconds>] [--watch-timeout <duration>] [--strict-reproducible]";

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
            if (args.Any(a => a is "--help" or "-h"))
            {
                stdout.WriteLine(UsageLine);
                return 0;
            }

            if (args.Length < 2)
            {
                stderr.WriteLine(UsageLine);
                return 2;
            }

            var recipePath = args[1];
            if (!TryParseOptions(args, stderr, out var urlOption, out var connectTimeout, out var watchTimeout, out var strictReproducible))
            {
                return 2;
            }

            var url = urlOption ?? Environment.GetEnvironmentVariable("NODEKIT_NODEVAULT_URL");

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

            var validation = RecipeValidationPipeline.ValidateRecipe(recipe, strictReproducible);
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
                return SubmitAsync(definition.Name, definition.Version, rawSpec, toolSpecClient, stdout, stderr, connectTimeout, watchTimeout)
                    .GetAwaiter().GetResult();
            }

            using var grpc = new GrpcToolSpecClient(url!);
            return SubmitAsync(definition.Name, definition.Version, rawSpec, grpc, stdout, stderr, connectTimeout, watchTimeout)
                .GetAwaiter().GetResult();
        }

        private static async Task<int> SubmitAsync(
            string toolName,
            string version,
            string rawSpec,
            IToolSpecBuildClient client,
            TextWriter stdout,
            TextWriter stderr,
            TimeSpan? connectTimeout = null,
            TimeSpan? watchTimeout = null)
        {
            using var cts = new CancellationTokenSource();

            // 별도 CTS로 분리한 이유: ResolveToolSpec/SubmitToolBuild 단계(빌드
            // ID가 아직 없는 상태)가 네트워크/서버 문제로 멈추면 Ctrl-C 외에는
            // 빠져나갈 방법이 없었다 — WatchToolBuild(실제 빌드 관찰) 단계는
            // 정상적으로 오래 걸릴 수 있어 같은 타임아웃을 적용하면 안 되므로,
            // buildId를 처음 받는 순간 아래에서 이 타이머를 명시적으로 해제하고
            // (CancelAfter(Timeout.InfiniteTimeSpan)로 예약된 취소를 취소),
            // 대신 watchTimeoutCts를 그 시점에 새로 무장한다 — 정확히 반대
            // 조건에서 서로 반대로 동작한다. watchTimeoutCts가 발동해도
            // CancelServerBuildBestEffort를 부르지 않는다(Issue #71 결정) —
            // 서버 쪽 빌드는 실제로 여전히 진행 중일 수 있으므로 그대로 둔다.
            using var connectTimeoutCts = new CancellationTokenSource();
            using var watchTimeoutCts = new CancellationTokenSource();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cts.Token, connectTimeoutCts.Token, watchTimeoutCts.Token);
            if (connectTimeout is { } timeout)
            {
                connectTimeoutCts.CancelAfter(timeout);
            }

            string? buildId = null;
            var digestReceived = false;
            string? lastImageDigest = null;
            string? lastImageRef = null;
            string? lastIntegrityHealth = null;
            DateTimeOffset? lastEventReceivedAt = null;
            ConsoleCancelEventHandler onCancelKeyPress = (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };
            Console.CancelKeyPress += onCancelKeyPress;

            try
            {
                await foreach (var ev in client.ResolveAndBuildAsync(toolName, version, rawSpec, linkedCts.Token))
                {
                    PrintEvent(ev, stdout);
                    lastEventReceivedAt = DateTimeOffset.Now;
                    if (!string.IsNullOrEmpty(ev.BuildId))
                    {
                        if (buildId is null)
                        {
                            // 빌드 ID를 처음 받았다는 건 ResolveToolSpec/SubmitToolBuild가
                            // 이미 끝났다는 뜻 — connect-timeout이 아직 발동 전이면
                            // 여기서 해제해서 이후 WatchToolBuild 관찰 단계에는 영향을
                            // 주지 않게 한다(이미 발동됐다면 이 호출은 안전한 no-op).
                            // watchTimeout이 설정됐다면 정확히 이 시점부터 무장한다.
                            connectTimeoutCts.CancelAfter(Timeout.InfiniteTimeSpan);
                            if (watchTimeout is { } wt)
                            {
                                watchTimeoutCts.CancelAfter(wt);
                            }
                        }

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

                    if (!string.IsNullOrEmpty(ev.IntegrityHealth))
                    {
                        lastIntegrityHealth = ev.IntegrityHealth;
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

                        // 빌드 자체(Succeeded)는 exit code 0을 유지한다 — 기존
                        // 스크립트/CI의 성공 판정을 깨지 않기 위해서다. 다만
                        // integrity_health가 Healthy가 아니면(예: Partial) 후속
                        // reconcile/등록 단계에 문제가 있다는 뜻이라 조용히
                        // 넘어가지 않고 눈에 띄는 경고를 남긴다 — 빈 문자열은
                        // "정보 없음"(구버전 NodeVault 등)이지 "문제 있음"이
                        // 아니므로 경고 대상이 아니다. stderr에 쓴다 — stdout은
                        // digest 같은 실제 결과값 전용으로 남겨서, 파이프/자동화가
                        // stdout만 파싱해도 진단성 경고에 오염되지 않게 한다.
                        if (!string.IsNullOrEmpty(lastIntegrityHealth) && lastIntegrityHealth != "Healthy")
                        {
                            stderr.WriteLine($"경고: 무결성 상태가 {lastIntegrityHealth}입니다 — 빌드는 성공했지만 후속 검증/등록에 문제가 있을 수 있습니다. NodeVault 인덱스에서 확인하세요.");
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
            // 취소로 취급하는 조건은 두 신호 중 하나만 있어도 충분하다:
            // (1) linkedCts(내가 넘긴 토큰)가 취소된 상태 — 내가 Ctrl-C나
            //     --connect-timeout으로 직접 취소를 요청했다는 뜻. 정확히 어떤
            //     예외 타입/RpcException 상태 코드로 나타나는지에 기대지 않는다
            //     — 서버(가짜 테스트 서버, 어쩌면 실제 NodeVault도 상황에
            //     따라)가 취소를 항상 RpcException(Cancelled)로 깔끔하게
            //     돌려주지 않고 StatusCode.Unknown("Exception was thrown by
            //     handler") 같은 형태로 보낼 수 있다는 게 회귀 테스트로 드러남.
            // (2) 예외 자체가 OperationCanceledException/RpcException(Cancelled)
            //     모양인 경우 — 내가 취소를 요청하지 않았어도(내 토큰은
            //     멀쩡해도) gRPC/서버 계층이 스스로 "취소됨"으로 보고한
            //     상황이라, 이것도 "빌드 요청이 취소되었습니다"로 보고하는
            //     쪽이 일반 실패(exit 1)로 뭉개는 것보다 정확하다.
#pragma warning disable CA1031 // any exception is treated as cancellation when either signal above holds, not a real failure
            catch (Exception ex) when (linkedCts.IsCancellationRequested || IsCancellationShaped(ex))
#pragma warning restore CA1031
            {
                if (connectTimeoutCts.IsCancellationRequested && !cts.IsCancellationRequested)
                {
                    return ReportConnectTimeout(stderr, connectTimeout!.Value);
                }

                if (watchTimeoutCts.IsCancellationRequested && !cts.IsCancellationRequested)
                {
                    return ReportWatchTimeout(stderr, watchTimeout!.Value, buildId, lastEventReceivedAt);
                }

                await CancelServerBuildBestEffort(client, buildId, stderr);
                stderr.WriteLine("빌드 요청이 취소되었습니다.");
                return 130;
            }
            // Final fallback after the cancellation-filtered catch above — any
            // other failure (network error, unexpected RpcException status,
            // etc.) that did NOT happen because we cancelled our own token
            // gets the same treatment: describe it and exit 1, since the CLI
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

        // 타임아웃이 발동하는 시점은 항상 buildId를 받기 전(ResolveToolSpec/
        // SubmitToolBuild 단계)이므로 — 그 이후엔 disarm된다 — 서버에 실제로
        // 시작된 빌드가 없다. CancelServerBuildBestEffort를 부를 대상 자체가
        // 없다는 뜻이라 사용자 Ctrl-C 취소(exit 130)와 다른, 구분되는 exit
        // code(124, `timeout(1)` 셸 명령의 관례와 동일)를 쓴다.
        private static int ReportConnectTimeout(TextWriter stderr, TimeSpan timeout)
        {
            stderr.WriteLine(
                $"NodeVault 연결이 {(int)timeout.TotalSeconds}초 동안 응답이 없어 타임아웃되었습니다 (--connect-timeout). " +
                "주소와 네트워크 상태를 확인하세요.");
            return 124;
        }

        // --watch-timeout은 build ID를 받은 뒤(WatchToolBuild 관찰 단계)에만
        // 적용된다 — 이 시점엔 서버에 실제로 진행 중인 빌드가 있을 수 있으므로
        // (Issue #71 결정에 따라) CancelServerBuildBestEffort를 부르지 않는다 —
        // CLI의 로컬 관찰만 끝내고 서버 빌드는 건드리지 않는다. exit code는
        // --connect-timeout(124)/Ctrl-C(130)와 구분되는 별도 값을 쓴다.
        private static int ReportWatchTimeout(
            TextWriter stderr, TimeSpan timeout, string? buildId, DateTimeOffset? lastEventReceivedAt)
        {
            stderr.WriteLine($"Watch가 {FormatDuration(timeout)} 후 타임아웃되었습니다 (--watch-timeout).");
            stderr.WriteLine();
            stderr.WriteLine("서버에서는 빌드가 계속 진행 중일 수 있습니다.");
            stderr.WriteLine($"Build ID: {(string.IsNullOrEmpty(buildId) ? "(알 수 없음)" : buildId)}");
            stderr.WriteLine(
                $"마지막 이벤트 수신 시각: {(lastEventReceivedAt is { } t ? t.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture) : "(없음)")}");
            stderr.WriteLine();
            stderr.WriteLine("Build ID로 나중에 빌드 상태를 다시 확인하세요.");
            return 125;
        }

        private static string FormatDuration(TimeSpan d)
        {
            if (d.TotalHours >= 1)
            {
                return d.Minutes == 0 ? $"{(int)d.TotalHours}시간" : $"{(int)d.TotalHours}시간 {d.Minutes}분";
            }

            return d.TotalMinutes >= 1 ? $"{(int)d.TotalMinutes}분" : $"{(int)d.TotalSeconds}초";
        }

        // linkedCts가 취소되지 않은 상태에서도(=내가 취소를 요청하지 않았어도)
        // gRPC 계층이 스스로 취소를 이렇게 보고할 수 있다 — 그 경우도
        // "빌드 요청이 취소되었습니다"로 다루는 게 일반 실패보다 정확하다.
        private static bool IsCancellationShaped(Exception ex) =>
            ex is OperationCanceledException || (ex is RpcException rpc && rpc.StatusCode == StatusCode.Cancelled);

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
            // not treated as a command failure. The caller's own cts is
            // already cancelled at this point (Ctrl-C or a cancelled RPC
            // already fired), so it can't be reused here — but
            // CancellationToken.None would let this hang forever if the
            // server or network is unresponsive, defeating the "user pressed
            // Ctrl-C to get control back" intent. Bound it with its own
            // short timeout instead.
            using var cancelCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await client.CancelBuildAsync(buildId, cancelCts.Token);
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

        // args[0]은 "submit", args[1]은 recipe 경로 — 옵션은 인덱스 2부터 시작한다.
        // 공유 CliOptionParser가 알려지지 않은 옵션, 값 누락/중복/다른 옵션처럼
        // 보이는 값을 명시적 에러로 걸러준다 — --connect-timeout의 "초 단위
        // 양의 정수", --watch-timeout의 "duration 형식(2h/90m/120s)" 검증만
        // 이 메서드에서 추가로 한다.
        private static bool TryParseOptions(
            string[] args,
            TextWriter stderr,
            out string? url,
            out TimeSpan? connectTimeout,
            out TimeSpan? watchTimeout,
            out bool strictReproducible)
        {
            url = null;
            connectTimeout = null;
            watchTimeout = null;
            strictReproducible = false;

            if (!CliOptionParser.TryParse(
                args,
                startIndex: 2,
                stderr,
                valueOptions: new[] { "--url", "--connect-timeout", "--watch-timeout" },
                flagOptions: new[] { "--strict-reproducible" },
                out var values,
                out var flags))
            {
                return false;
            }

            strictReproducible = flags.Contains("--strict-reproducible");

            if (values.TryGetValue("--url", out var urlValue))
            {
                url = urlValue;
            }

            if (values.TryGetValue("--connect-timeout", out var timeoutValue))
            {
                if (!int.TryParse(timeoutValue, out var seconds) || seconds <= 0)
                {
                    stderr.WriteLine($"--connect-timeout 값이 올바르지 않습니다: '{timeoutValue}' (초 단위 양의 정수여야 합니다).");
                    return false;
                }

                connectTimeout = TimeSpan.FromSeconds(seconds);
            }

            if (values.TryGetValue("--watch-timeout", out var watchTimeoutValue))
            {
                if (!TryParseDuration(watchTimeoutValue, out var duration))
                {
                    stderr.WriteLine($"--watch-timeout 값이 올바르지 않습니다: '{watchTimeoutValue}' (예: 2h, 90m, 120s).");
                    return false;
                }

                watchTimeout = duration;
            }

            return true;
        }

        // "2h"/"90m"/"120s"(선택적으로 "1.5h"처럼 소수도 허용) 형식만 받는다 —
        // --connect-timeout처럼 초 단위 정수만 받으면 --watch-timeout이 흔히
        // 감당해야 하는 시간(수십 분~수 시간) 단위를 매번 초로 환산해야 해서
        // 설정 실수가 생기기 쉽다(Issue #71).
        private static bool TryParseDuration(string raw, out TimeSpan duration)
        {
            duration = default;
            if (string.IsNullOrEmpty(raw) || raw.Length < 2)
            {
                return false;
            }

            var unit = raw[^1];
            var numberPart = raw[..^1];
            if (!double.TryParse(
                numberPart, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
                || value <= 0)
            {
                return false;
            }

            duration = unit switch
            {
                's' => TimeSpan.FromSeconds(value),
                'm' => TimeSpan.FromMinutes(value),
                'h' => TimeSpan.FromHours(value),
                _ => TimeSpan.Zero,
            };

            return duration > TimeSpan.Zero;
        }
    }
}
