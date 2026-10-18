using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using NodeKit.Authoring.ToolFunctionRecipes;
using NodeKit.Cli.Operations;
using NodeKit.Grpc;
using Nodevault.V1;

namespace NodeKit.Cli
{
    /// <summary>두 번째 이미지 빌드가 full B로 끝난 뒤 등록에 넘기는 값.</summary>
    internal sealed record SecondImageSucceeded(
        string FunctionImageDigest,
        string? SecondRequestBasis,
        string? BuildId,
        string? BuildRequestId);

    /// <summary>등록 한 번에 필요한 위치·주소·client. Client가 null이면 Endpoint로 실제 gRPC client를 만든다.</summary>
    internal sealed record RegistrationContext(
        string StoreRoot,
        string Endpoint,
        IToolFunctionRegistryClient? Client,
        Func<string> RequestIdProvider,
        string BaseImageDigest)
    {
        /// <summary>--registration-timeout. null이면 등록에 자동 시간 제한이 없다(opt-in, S3-08-G).</summary>
        public TimeSpan? Timeout { get; init; }
    }

    /// <summary>
    /// P08.registry (v0.9.1 S3-07): typed RegisterToolFunction client 경로.
    ///
    /// 순서: P06 typed mapper(w2-set-v1·C1·B·typed groups) → 정확한 요청 bytes와 prepared receipt를 S2 record
    /// root에 저장 → attempts 기록 → RegisterToolFunction → 응답 분류 → receipt 갱신. 저장하지 못하면 RPC를 보내지
    /// 않는다(exit 2). 서버가 돌려준 F/H/P/version은 그대로 저장·출력하고 계산하거나 대체하지 않는다. 최종 pin은
    /// cas_hash(H)다.
    ///
    /// 분류: InvalidArgument/FailedPrecondition/AlreadyExists = 서버 확정 거부(rejected, exit 1). 그 밖의 gRPC 오류·
    /// 응답 유실, 비었거나 지원하지 않는 canonicalization_version, 비었거나 손상된 tool_function_digest/cas_hash =
    /// 결과 불명(unknown, exit 1) — 다른 버전·새 request ID로 자동 재작성/재제출하지 않는다. 같은 요청은
    /// `function-recipe register-replay &lt;receipt&gt;`로 저장된 bytes만 다시 보낸다(Recipe/script를 다시 읽거나
    /// 새 이미지를 빌드하지 않는다). 등록 성공은 승인(Approved)이 아니다.
    ///
    /// 시간 제한·취소(P08.timeout, S3-08): 앞 단계 connect/watch 타이머는 등록 RPC를 취소하지 않는다. 등록에는
    /// 자동 deadline이 없고, `--registration-timeout &lt;seconds&gt;`를 주면 RegisterToolFunction 호출 시작부터 그 예산만
    /// 잰다. 전송 뒤 만료는 124, Ctrl-C는 130이며 둘 다 결과 불명(unknown)이다 — 원격 등록 실패로 확정하지 않고,
    /// 저장된 같은 요청은 그대로 두어 replay로 확인한다. CancelToolBuild·새 빌드·새 request ID 자동 등록은 하지
    /// 않는다. 보내기 전 취소는 130·not_submitted(RPC 0, prepared 기록 보존)다. CLI가 결과를 확정한 뒤 늦게 온
    /// 응답은 출력·저장하지 않는다.
    /// </summary>
    internal static class ToolFunctionRegistrar
    {
        public const string RejectedCode = "REGISTRATION_REJECTED";
        public const string UnknownCode = "REGISTRATION_RESULT_UNKNOWN";
        public const string VersionMismatchCode = "REGISTRATION_VERSION_MISMATCH";
        public const string IdentityMissingCode = "REGISTRATION_IDENTITY_MISSING";
        public const string EndpointMismatchCode = "REGISTRATION_ENDPOINT_MISMATCH";
        public const string TimeoutCode = "REGISTRATION_TIMEOUT";
        public const string CancelledCode = "REGISTRATION_CANCELLED";

        public const string RegistrationTimeoutOption = "--registration-timeout";

        /// <summary>
        /// --registration-timeout 상한(초). 예산은 CancellationTokenSource.CancelAfter/Task.Delay 타이머로 재므로 그
        /// 타이머가 받는 최대값(uint.MaxValue - 1 ms = 4294967294 ms)을 넘지 않는 정수 초다. gRPC deadline은 쓰지 않는다.
        /// </summary>
        public const int MaxRegistrationTimeoutSeconds = 4_294_967;

        // 상한은 MaxRegistrationTimeoutSeconds와 같다(시험이 고정한다).
        internal const string RegistrationTimeoutUsage = "[" + RegistrationTimeoutOption + " <seconds: 1-4294967>]";

        private const string ReplayUsageLine =
            "사용법: nodekit function-recipe register-replay <registration-receipt.json> [--url <nodevault-url>] " + RegistrationTimeoutUsage + " [--format human|jsonl]";

        /// <summary>
        /// --registration-timeout 값: 문화권과 무관한 10진 양의 정수 초(부호·공백·소수·지수·NaN/Infinity 불가),
        /// 1..<see cref="MaxRegistrationTimeoutSeconds"/>. 아니면 진단을 남기고 false(호출자는 업무 RPC 전에 exit 2).
        /// </summary>
        internal static bool TryParseRegistrationTimeout(string raw, TextWriter stderr, out TimeSpan timeout)
        {
            timeout = default;
            if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                || seconds <= 0
                || seconds > MaxRegistrationTimeoutSeconds)
            {
                stderr.WriteLine(
                    $"{RegistrationTimeoutOption} 값이 올바르지 않습니다: '{raw}' (초 단위 정수 1..{MaxRegistrationTimeoutSeconds}). 요청을 보내지 않았습니다.");
                return false;
            }

            timeout = TimeSpan.FromSeconds(seconds);
            return true;
        }

        /// <summary>빌드가 확인한 B로 typed 요청을 만들고 durable 저장 뒤 RegisterToolFunction을 보낸다.</summary>
        public static int RegisterAfterBuild(
            ToolFunctionRecipe recipe,
            RegistrationContext context,
            SecondImageSucceeded built,
            TextWriter stdout,
            TextWriter stderr,
            bool jsonl,
            CancellationToken userCancellation = default)
        {
            ArgumentNullException.ThrowIfNull(recipe);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(built);

            var inputs = new ToolFunctionRegistrationInputs(context.RequestIdProvider(), recipe.ToolSpecDigest, built.FunctionImageDigest, built.SecondRequestBasis);
            if (!ToolFunctionRegistrationRequestFactory.TryBuild(recipe, inputs, out var plan, out var mappingError))
            {
                return Report(stdout, stderr, jsonl, new Outcome(mappingError!.ExitCode, mappingError.Code, mappingError.Message, "not_submitted", null, null, built));
            }

            var provenance = new RegistrationProvenance
            {
                BaseImageDigest = context.BaseImageDigest,
                BaseToolSpecDigest = plan!.Request.BaseToolSpecDigest,
                FunctionImageDigest = plan.Request.ImageDigest,
                SecondRequestBasis = plan.SecondRequestBasis,
                BuildRequestId = built.BuildRequestId,
                BuildId = built.BuildId,
            };
            var store = new RegistrationStore(context.StoreRoot);
            if (store.TryCreate(plan.Request, context.Endpoint, provenance, out var handle) is { } createError)
            {
                return Report(stdout, stderr, jsonl, new Outcome(createError.ExitCode, createError.Code, createError.Message, "not_submitted", plan.Request.RequestId, null, built));
            }

            using (handle)
            {
                var call = new RegistrationCall(context.Timeout, userCancellation);
                return WithClient(context.Client, context.Endpoint, stdout, stderr, jsonl, client => Send(handle!, client, stdout, stderr, jsonl, built, call));
            }
        }

        /// <summary>
        /// `nodekit function-recipe register-replay &lt;receipt&gt;`: 저장된 같은 endpoint·request ID·version·요청 bytes를
        /// 다시 보낸다. registered/rejected는 RPC 없이 저장된 결과를 보여준다. --registration-timeout과 Ctrl-C는
        /// submit의 등록 단계와 같은 규칙이다.
        /// </summary>
        public static int RunReplay(string[] args, TextWriter stdout, TextWriter stderr, IToolFunctionRegistryClient? client = null) =>
            RunReplayUntilUserCancel(args, stdout, stderr, client, CancellationToken.None);

        /// <summary>테스트 전용: <see cref="RunReplay"/>와 같지만 사용자 취소 신호(Ctrl-C와 같은 경로)를 받는다.</summary>
        internal static int RunReplayUntilUserCancel(
            string[] args, TextWriter stdout, TextWriter stderr, IToolFunctionRegistryClient? client, CancellationToken userCancellation)
        {
            if (args.Any(a => a is "--help" or "-h"))
            {
                stdout.WriteLine(ReplayUsageLine);
                return 0;
            }

            if (args.Length < 3 || !TryParseReplayOptions(args, stderr, out var url, out var timeout, out var jsonl))
            {
                stderr.WriteLine(ReplayUsageLine);
                return 2;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(userCancellation);
            ConsoleCancelEventHandler onCancelKeyPress = (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };
            Console.CancelKeyPress += onCancelKeyPress;
            try
            {
                return RunReplayCore(args[2], url, jsonl, stdout, stderr, client, new RegistrationCall(timeout, cts.Token));
            }
            finally
            {
                Console.CancelKeyPress -= onCancelKeyPress;
            }
        }

        private static int RunReplayCore(
            string receiptPath, string? url, bool jsonl, TextWriter stdout, TextWriter stderr, IToolFunctionRegistryClient? client, RegistrationCall call)
        {

            if (TryOpenReceipt(receiptPath, out var handle) is { } openError)
            {
                return Report(stdout, stderr, jsonl, new Outcome(openError.ExitCode, openError.Code, openError.Message, "not_submitted", null, null, null));
            }

            using (handle)
            {
                var receipt = handle!.Receipt;

                // --url은 이 receipt가 그 NodeVault의 것인지 확인하는 용도이기도 하다. 저장된 terminal 결과를
                // 보여주기 전에 비교해야 다른 endpoint의 성공/거부를 받아들이지 않는다.
                if (url is not null && url != receipt.Endpoint)
                {
                    var message = $"저장된 등록은 {receipt.Endpoint}로 보낸 요청입니다. 다른 주소({url})로 replay하지 않았습니다.";
                    return Report(stdout, stderr, jsonl, new Outcome(2, EndpointMismatchCode, message, receipt.State, receipt.RequestId, null, null));
                }

                if (RegistrationState.IsTerminal(receipt.State))
                {
                    return ReportStored(handle, stdout, stderr, jsonl);
                }

                if (client is null && receipt.Endpoint == SubmitCommand.InjectedClientEndpoint)
                {
                    var message = "이 등록 기록은 테스트 주입 client로 만든 것이라 실제 주소가 없습니다. replay하지 않았습니다.";
                    return Report(stdout, stderr, jsonl, new Outcome(2, EndpointMismatchCode, message, receipt.State, receipt.RequestId, null, null));
                }

                return WithClient(client, receipt.Endpoint, stdout, stderr, jsonl, c => Send(handle, c, stdout, stderr, jsonl, built: null, call));
            }
        }

        // ReceiptCommand.TryOpenReceipt와 같다: OS가 거부하는 경로(NUL 문자·공백뿐·과도한 길이)는 예외로 죽지 않고
        // 로컬 입력 오류(2)로 돌려준다.
        private static OperationStoreError? TryOpenReceipt(string receiptPath, out RegistrationHandle? handle)
        {
            handle = null;
            try
            {
                return RegistrationStore.ForReceipt(receiptPath).TryOpen(receiptPath, out handle);
            }
            catch (ArgumentException ex)
            {
                return InvalidReceiptPath(receiptPath, ex);
            }
            catch (NotSupportedException ex)
            {
                return InvalidReceiptPath(receiptPath, ex);
            }
            catch (PathTooLongException ex)
            {
                return InvalidReceiptPath(receiptPath, ex);
            }
        }

        internal static string ReplayCommand(string receiptPath) =>
            ReplayCommand(receiptPath, OperatingSystem.IsWindows());

        /// <summary>
        /// replay 안내 명령. ReceiptCommand.FollowUpCommand와 같은 인용 규칙: 경로를 셸 인자 하나로 안전하게 적을 수 있으면
        /// 그대로 실행 가능한 명령을, 그럴 수 없으면(Windows에서 셸마다 해석이 다른 문자) 실행 가능하다고 주장하지 않는 형태를 돌려준다.
        /// </summary>
        internal static string ReplayCommand(string receiptPath, bool windows)
        {
            var quoted = ReceiptCommand.QuoteArgument(receiptPath, windows);
            return quoted is null
                ? $"nodekit function-recipe register-replay <registration-receipt.json> (receipt 경로: {receiptPath} — 셸마다 해석이 달라 그대로 붙여 넣을 수 있는 형태로 적지 않았습니다. 사용하는 셸에 맞게 인용해 입력하세요)"
                : $"nodekit function-recipe register-replay {quoted}";
        }

        private static OperationStoreError InvalidReceiptPath(string receiptPath, Exception ex) =>
            new(RegistrationStore.InvalidCode, $"등록 receipt 경로를 사용할 수 없습니다: {receiptPath} ({ex.Message}). 아무 요청도 보내지 않았습니다.");

        private static bool TryParseReplayOptions(string[] args, TextWriter stderr, out string? url, out TimeSpan? timeout, out bool jsonl)
        {
            url = null;
            timeout = null;
            jsonl = false;
            for (var i = 3; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--url" when i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]):
                        url = args[++i];
                        break;
                    case RegistrationTimeoutOption when timeout is null && i + 1 < args.Length:
                        if (!TryParseRegistrationTimeout(args[++i], stderr, out var parsed))
                        {
                            return false;
                        }

                        timeout = parsed;
                        break;
                    case "--format" when i + 1 < args.Length && args[i + 1] is "human" or "jsonl":
                        jsonl = args[++i] == "jsonl";
                        break;
                    default:
                        return false;
                }
            }

            return true;
        }

        private static int WithClient(
            IToolFunctionRegistryClient? injected, string endpoint, TextWriter stdout, TextWriter stderr, bool jsonl, Func<IToolFunctionRegistryClient, int> run)
        {
            if (injected is not null)
            {
                return run(injected);
            }

            GrpcToolFunctionRegistryClient grpc;
            try
            {
                grpc = new GrpcToolFunctionRegistryClient(endpoint);
            }
#pragma warning disable CA1031 // SubmitCommand.RunOperation과 같은 이유: 주소 resolver 예외 종류가 하나로 고정되지 않는다.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                return Report(stdout, stderr, jsonl, new Outcome(2, "INVALID_URL", $"NodeVault 주소 형식이 올바르지 않습니다: {endpoint} ({ex.Message})", "not_submitted", null, null, null));
            }

            using (grpc)
            {
                return run(grpc);
            }
        }

        /// <summary>등록 RPC 한 번의 시간 제한(없으면 자동 deadline 없음)과 사용자 취소 신호.</summary>
        internal sealed record RegistrationCall(TimeSpan? Timeout, CancellationToken UserCancellation);

        private static int Send(
            RegistrationHandle handle,
            IToolFunctionRegistryClient client,
            TextWriter stdout,
            TextWriter stderr,
            bool jsonl,
            SecondImageSucceeded? built,
            RegistrationCall call)
        {
            var before = handle.Receipt;
            var requestId = before.RequestId;

            // 보내기 전에 이미 취소됐다: 요청은 나가지 않았다(RPC 0). prepared/이전 기록은 그대로 두고 replay를 안내한다.
            if (call.UserCancellation.IsCancellationRequested)
            {
                var remote = before.State == RegistrationState.Prepared ? "not_submitted" : before.State;
                var message = "등록 요청을 보내기 전에 사용자가 취소했습니다. 요청을 보내지 않았고 저장된 요청은 그대로 남겼습니다." +
                    $" 나중에 보내려면: {ReplayCommand(handle.ReceiptPath)}";
                return Report(stdout, stderr, jsonl, new Outcome(130, CancelledCode, message, remote, requestId, null, built, Status: "Cancelled"));
            }

            // 보내기 직전에 시도 횟수를 기록한다 — 기록하지 못하면 보내지 않는다.
            if (handle.Advance(before with { Attempts = before.Attempts + 1 }) is { } markError)
            {
                return Report(stdout, stderr, jsonl, new Outcome(markError.ExitCode, markError.Code, markError.Message, "not_submitted", requestId, null, built, handle.ReceiptPath));
            }

            var classified = Call(client, handle.Request, handle.Receipt.CanonicalizationVersion, call);

            var now = DateTimeOffset.UtcNow.ToString("O");
            var next = handle.Receipt with
            {
                State = classified.State,
                Result = classified.Result is { } r ? r with { ObservedAt = now } : null,
                LastError = classified.ErrorCode is null
                    ? null
                    : new RegistrationError { Code = classified.ErrorCode, Message = classified.Message!, Observed = classified.Observed, ObservedAt = now },
            };
            if (handle.Advance(next) is { } writeError)
            {
                // 받은 값은 진단에 남기지만 durable 완료라고 말하지 않는다(S3-07-G).
                var observed = classified.Result is { } got
                    ? $"tool_function_digest={got.ToolFunctionDigest} cas_hash={got.CasHash} presentation_revision_id={got.PresentationRevisionId} canonicalization_version={got.CanonicalizationVersion}"
                    : classified.Observed;
                var message = $"등록 응답({classified.State})을 받았지만 receipt에 저장하지 못했습니다: {writeError.Message}. 받은 값: {observed ?? "(없음)"}. 같은 요청은 register-replay로 다시 확인하세요.";
                return Report(stdout, stderr, jsonl, new Outcome(2, writeError.Code, message, RegistrationState.Unknown, requestId, null, built, handle.ReceiptPath));
            }

            return classified.State == RegistrationState.Registered
                ? ReportRegistered(handle, stdout, jsonl, built)
                : Report(
                    stdout,
                    stderr,
                    jsonl,
                    new Outcome(classified.ExitCode, classified.ErrorCode!, classified.Message!, classified.State, requestId, null, built, handle.ReceiptPath, classified.Status));
        }

        /// <summary>
        /// RegisterToolFunction 한 번. 예산(있으면)은 지금, 즉 RPC 시작부터 잰다. 예산 만료·사용자 취소가 응답보다 먼저면
        /// 응답을 더 기다리지 않고 결과 불명으로 끝낸다 — 토큰을 무시하는 transport여도 CLI는 돌아오고, 그 뒤에 온 응답은
        /// 관측하지 않는다(늦은 ACK로 두 번째 결과를 내지 않는다).
        /// </summary>
        private static Classified Call(IToolFunctionRegistryClient client, RegisterToolFunctionRequest request, string requestedVersion, RegistrationCall call)
        {
            using var timeoutCts = new CancellationTokenSource();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(call.UserCancellation, timeoutCts.Token);
            if (call.Timeout is { } budget)
            {
                timeoutCts.CancelAfter(budget);
            }

            // 이긴 쪽은 실제로 먼저 일어난 사건이다. 둘 다 끝난 뒤에야 이 스레드가 기다리기 시작하면 WhenAny는 앞 인자를 고르므로
            // 관측 순서로 정하지 않는다 — 신호는 RPC 시작 전에 등록해 그 순간에 기록하고, 응답은 완료 continuation에서 기록한다.
            var abandon = Task.Delay(System.Threading.Timeout.Infinite, linked.Token);
            var first = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<RegisterToolFunctionResponse>? rpc = null;
            using var signal = linked.Token.Register(() => first.TrySetResult(rpc is { IsCompleted: true } done ? done : abandon));
            try
            {
                rpc = client.RegisterToolFunctionAsync(request, linked.Token);
            }
#pragma warning disable CA1031 // 동기적으로 던진 예외도 아래 비동기 실패와 같은 분류를 거친다.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                rpc = Task.FromException<RegisterToolFunctionResponse>(ex);
            }

            var response = rpc;
            _ = response.ContinueWith(_ => first.TrySetResult(response), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            var winner = first.Task.GetAwaiter().GetResult();
            return Settle(winner, response, requestedVersion, call, timeoutCts, linked.Token);
        }

        /// <summary>
        /// 먼저 끝난 쪽(<paramref name="winner"/>)이 결과를 정한다. 중단이 이긴 뒤 응답이 곧바로 끝나도(rpc 상태를 다시 보기 전)
        /// 그 응답은 늦은 ACK다 — registered로 바꾸지 않는다(registration_late_ack).
        /// </summary>
        internal static Classified Settle(
            Task winner,
            Task<RegisterToolFunctionResponse> rpc,
            string requestedVersion,
            RegistrationCall call,
            CancellationTokenSource timeoutCts,
            CancellationToken linked)
        {
            if (!ReferenceEquals(winner, rpc))
            {
                // 응답을 버린다 — 나중에 실패하더라도 관측되지 않은 예외로 남지 않게 한다.
                _ = rpc.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                return LocalAbort(call, timeoutCts);
            }

            try
            {
                return Classify(rpc.GetAwaiter().GetResult(), requestedVersion);
            }
            catch (Exception ex) when (linked.IsCancellationRequested && IsCancellationShaped(ex))
            {
                // 내가 취소/만료시킨 호출이 그 신호로 끝났다 — 서버 오류가 아니라 로컬 중단이다. 서버가 먼저 돌려준 다른 오류
                // (InvalidArgument 등)는 그 뒤 신호가 와도 서버 결과 그대로 분류한다.
                return LocalAbort(call, timeoutCts);
            }
            catch (RpcException ex)
            {
                return ex.StatusCode is StatusCode.InvalidArgument or StatusCode.FailedPrecondition or StatusCode.AlreadyExists
                    ? new Classified(RegistrationState.Rejected, RejectedCode, $"NodeVault가 등록을 거부했습니다 ({ex.StatusCode}): {ex.Status.Detail}", null, null)
                    : new Classified(RegistrationState.Unknown, UnknownCode, $"등록 결과를 확인하지 못했습니다 ({ex.StatusCode}): {ex.Status.Detail}", null, null);
            }
        }

        private static bool IsCancellationShaped(Exception ex) =>
            ex is OperationCanceledException || ex is RpcException { StatusCode: StatusCode.Cancelled };

        // 요청은 이미 보냈다(attempts 기록 뒤). 원격 등록 여부는 모른다 — 실패로 확정하지 않는다. 사용자 취소가 타이머보다 우선한다.
        private static Classified LocalAbort(RegistrationCall call, CancellationTokenSource timeoutCts)
        {
            if (call.UserCancellation.IsCancellationRequested || !timeoutCts.IsCancellationRequested)
            {
                return new Classified(
                    RegistrationState.Unknown,
                    CancelledCode,
                    "등록 요청을 보낸 뒤 사용자가 취소했습니다(로컬 중단). NodeVault에서 등록됐는지는 확인하지 않았습니다 — 실패로 보지 않으며 " +
                    "새 이미지 빌드·새 request ID로 다시 등록하지 않았습니다.",
                    null,
                    null,
                    ExitCode: 130,
                    Status: "Cancelled");
            }

            var seconds = (long)call.Timeout!.Value.TotalSeconds;
            return new Classified(
                RegistrationState.Unknown,
                TimeoutCode,
                $"등록 요청을 보낸 뒤 {seconds}초({RegistrationTimeoutOption}) 안에 응답을 받지 못했습니다. NodeVault에서 등록됐는지는 알 수 없습니다 — " +
                "실패로 보지 않으며 새 이미지 빌드·새 request ID로 다시 등록하지 않았습니다.",
                null,
                null,
                ExitCode: 124);
        }

        internal sealed record Classified(
            string State, string? ErrorCode, string? Message, string? Observed, RegistrationResult? Result, int ExitCode = 1, string Status = "Failed");

        private static Classified Classify(RegisterToolFunctionResponse response, string requestedVersion)
        {
            var observed =
                $"tool_function_digest='{response.ToolFunctionDigest}' cas_hash='{response.CasHash}' presentation_revision_id='{response.PresentationRevisionId}' canonicalization_version='{response.CanonicalizationVersion}'";
            if (response.CanonicalizationVersion != requestedVersion)
            {
                var got = string.IsNullOrEmpty(response.CanonicalizationVersion) ? "(없음)" : response.CanonicalizationVersion;
                return new Classified(
                    RegistrationState.Unknown,
                    VersionMismatchCode,
                    $"NodeVault 응답의 canonicalization_version이 요청한 {requestedVersion}이 아닙니다(받은 값: {got}). versioned 등록 성공으로 보지 않으며 다른 버전으로 다시 보내지 않았습니다.",
                    observed,
                    null);
            }

            if (!RegistrationStore.IsPresentIdentity(response.ToolFunctionDigest) || !RegistrationStore.IsPresentIdentity(response.CasHash))
            {
                return new Classified(
                    RegistrationState.Unknown,
                    IdentityMissingCode,
                    "NodeVault 응답에 tool_function_digest 또는 cas_hash가 없거나 손상됐습니다. 등록 완료로 보지 않으며 client에서 계산해 채우지 않습니다.",
                    observed,
                    null);
            }

            return new Classified(
                RegistrationState.Registered,
                null,
                null,
                null,
                new RegistrationResult
                {
                    ToolFunctionDigest = response.ToolFunctionDigest,
                    CasHash = response.CasHash,
                    PresentationRevisionId = response.PresentationRevisionId,
                    CanonicalizationVersion = response.CanonicalizationVersion,
                    ObservedAt = string.Empty,
                });
        }

        private sealed record Outcome(
            int ExitCode,
            string Code,
            string Message,
            string RemoteState,
            string? RequestId,
            RegistrationResult? Result,
            SecondImageSucceeded? Built,
            string? ReceiptPath = null,
            string Status = "Failed");

        private static int ReportStored(RegistrationHandle handle, TextWriter stdout, TextWriter stderr, bool jsonl)
        {
            var receipt = handle.Receipt;
            if (receipt.State == RegistrationState.Registered)
            {
                return ReportRegistered(handle, stdout, jsonl, built: null);
            }

            var error = receipt.LastError!;
            return Report(stdout, stderr, jsonl, new Outcome(1, error.Code, $"이미 끝난 등록입니다(다시 보내지 않음): {error.Message}", receipt.State, receipt.RequestId, null, null, handle.ReceiptPath));
        }

        private static int ReportRegistered(RegistrationHandle handle, TextWriter stdout, bool jsonl, SecondImageSucceeded? built)
        {
            var receipt = handle.Receipt;
            var result = receipt.Result!;
            if (jsonl)
            {
                SubmitCommand.WriteJsonl(stdout, new SubmitJsonlRecord
                {
                    Type = "completed",
                    Status = "Succeeded",
                    Phase = "registration",
                    BuildId = built?.BuildId ?? receipt.Provenance.BuildId,
                    ImageDigest = receipt.Provenance.FunctionImageDigest,
                    ToolSpecDigest = receipt.Provenance.SecondRequestBasis,
                    RegistrationRequestId = receipt.RequestId,
                    ToolFunctionDigest = result.ToolFunctionDigest,
                    CasHash = result.CasHash,
                    PresentationRevisionId = result.PresentationRevisionId,
                    CanonicalizationVersion = result.CanonicalizationVersion,
                    RemoteRegistrationState = RegistrationState.Registered,
                    Recovery = "none",
                });
                return 0;
            }

            stdout.WriteLine($"typed 등록 완료 (등록 request ID: {receipt.RequestId})");
            stdout.WriteLine($"  cas_hash (H, 최종 pin): {result.CasHash}");
            stdout.WriteLine($"  tool_function_digest (F): {result.ToolFunctionDigest}");
            stdout.WriteLine($"  presentation_revision_id (P): {result.PresentationRevisionId}");
            stdout.WriteLine($"  canonicalization_version: {result.CanonicalizationVersion}");
            stdout.WriteLine($"  두 번째 이미지(B): {receipt.Provenance.FunctionImageDigest}");
            stdout.WriteLine($"  기록: {handle.ReceiptPath}");
            stdout.WriteLine("등록 성공은 승인(Approved)이 아닙니다. 값은 NodeVault가 계산한 그대로이며 NodeKit은 다시 계산하지 않습니다.");
            return 0;
        }

        private static int Report(TextWriter stdout, TextWriter stderr, bool jsonl, Outcome o)
        {
            var hint = o.RemoteState == RegistrationState.Unknown && o.ReceiptPath is not null
                ? $" 같은 요청을 다시 보내려면: {ReplayCommand(o.ReceiptPath)}"
                : string.Empty;
            if (jsonl)
            {
                SubmitCommand.WriteJsonl(stdout, new SubmitJsonlRecord
                {
                    Type = "completed",
                    Status = o.Status,
                    Phase = "registration",
                    ErrorCode = o.Code,
                    Message = o.Message + hint,
                    BuildId = o.Built?.BuildId,
                    ImageDigest = o.Built?.FunctionImageDigest,
                    ToolSpecDigest = o.Built?.SecondRequestBasis,
                    RegistrationRequestId = o.RequestId,
                    RemoteRegistrationState = o.RemoteState,
                    Recovery = o.RemoteState is RegistrationState.Unknown ? "uncertain" : "terminal",
                });
                return o.ExitCode;
            }

            stderr.WriteLine($"{o.Code}: {o.Message}{hint}");
            return o.ExitCode;
        }
    }
}
