using System;
using System.IO;
using System.Linq;
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
        string BaseImageDigest);

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
    /// 등록 timeout/Ctrl-C(--registration-timeout, S3-08)는 P08.timeout 범위다 — 여기서는 앞 단계 타이머가
    /// 등록 RPC를 취소하지 않는다.
    /// </summary>
    internal static class ToolFunctionRegistrar
    {
        public const string RejectedCode = "REGISTRATION_REJECTED";
        public const string UnknownCode = "REGISTRATION_RESULT_UNKNOWN";
        public const string VersionMismatchCode = "REGISTRATION_VERSION_MISMATCH";
        public const string IdentityMissingCode = "REGISTRATION_IDENTITY_MISSING";
        public const string EndpointMismatchCode = "REGISTRATION_ENDPOINT_MISMATCH";

        private const string ReplayUsageLine =
            "사용법: nodekit function-recipe register-replay <registration-receipt.json> [--url <nodevault-url>] [--format human|jsonl]";

        /// <summary>빌드가 확인한 B로 typed 요청을 만들고 durable 저장 뒤 RegisterToolFunction을 보낸다.</summary>
        public static int RegisterAfterBuild(
            ToolFunctionRecipe recipe,
            RegistrationContext context,
            SecondImageSucceeded built,
            TextWriter stdout,
            TextWriter stderr,
            bool jsonl)
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
                return WithClient(context.Client, context.Endpoint, stdout, stderr, jsonl, client => Send(handle!, client, stdout, stderr, jsonl, built));
            }
        }

        /// <summary>
        /// `nodekit function-recipe register-replay &lt;receipt&gt;`: 저장된 같은 endpoint·request ID·version·요청 bytes를
        /// 다시 보낸다. registered/rejected는 RPC 없이 저장된 결과를 보여준다.
        /// </summary>
        public static int RunReplay(string[] args, TextWriter stdout, TextWriter stderr, IToolFunctionRegistryClient? client = null)
        {
            if (args.Any(a => a is "--help" or "-h"))
            {
                stdout.WriteLine(ReplayUsageLine);
                return 0;
            }

            if (args.Length < 3 || !TryParseReplayOptions(args, out var url, out var jsonl))
            {
                stderr.WriteLine(ReplayUsageLine);
                return 2;
            }

            var receiptPath = args[2];
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

                return WithClient(client, receipt.Endpoint, stdout, stderr, jsonl, c => Send(handle, c, stdout, stderr, jsonl, built: null));
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

        private static bool TryParseReplayOptions(string[] args, out string? url, out bool jsonl)
        {
            url = null;
            jsonl = false;
            for (var i = 3; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--url" when i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]):
                        url = args[++i];
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

        private static int Send(
            RegistrationHandle handle, IToolFunctionRegistryClient client, TextWriter stdout, TextWriter stderr, bool jsonl, SecondImageSucceeded? built)
        {
            var before = handle.Receipt;
            var requestId = before.RequestId;

            // 보내기 직전에 시도 횟수를 기록한다 — 기록하지 못하면 보내지 않는다.
            if (handle.Advance(before with { Attempts = before.Attempts + 1 }) is { } markError)
            {
                return Report(stdout, stderr, jsonl, new Outcome(markError.ExitCode, markError.Code, markError.Message, "not_submitted", requestId, null, built, handle.ReceiptPath));
            }

            Classified classified;
            try
            {
                var response = client.RegisterToolFunctionAsync(handle.Request, CancellationToken.None).GetAwaiter().GetResult();
                classified = Classify(response, handle.Receipt.CanonicalizationVersion);
            }
            catch (RpcException ex)
            {
                classified = ex.StatusCode is StatusCode.InvalidArgument or StatusCode.FailedPrecondition or StatusCode.AlreadyExists
                    ? new Classified(RegistrationState.Rejected, RejectedCode, $"NodeVault가 등록을 거부했습니다 ({ex.StatusCode}): {ex.Status.Detail}", null, null)
                    : new Classified(RegistrationState.Unknown, UnknownCode, $"등록 결과를 확인하지 못했습니다 ({ex.StatusCode}): {ex.Status.Detail}", null, null);
            }

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
                : Report(stdout, stderr, jsonl, new Outcome(1, classified.ErrorCode!, classified.Message!, classified.State, requestId, null, built, handle.ReceiptPath));
        }

        private sealed record Classified(string State, string? ErrorCode, string? Message, string? Observed, RegistrationResult? Result);

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
            int ExitCode, string Code, string Message, string RemoteState, string? RequestId, RegistrationResult? Result, SecondImageSucceeded? Built, string? ReceiptPath = null);

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
                    Status = "Failed",
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
