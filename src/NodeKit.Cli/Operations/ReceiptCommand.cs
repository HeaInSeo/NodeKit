using NodeKit.Grpc;

namespace NodeKit.Cli.Operations
{
    /// <summary>
    /// nodekit receipt watch|cancel &lt;receipt.json&gt; (S2-03 재진입).
    ///
    /// 저장된 receipt의 build ID와 endpoint만 쓴다. ResolveToolSpec/SubmitToolBuild는 보내지 않고
    /// Recipe도 다시 읽지 않는다. 손상·미지원 schema·hash 불일치 receipt와 build ID가 없는 receipt는
    /// 어떤 RPC도 보내지 않고 2로 끝난다.
    /// </summary>
    internal static class ReceiptCommand
    {
        private const string UsageText =
            "사용법:\n" +
            "  nodekit receipt watch <receipt.json> [--format human|jsonl]\n" +
            "  nodekit receipt cancel <receipt.json> [--format human|jsonl]\n" +
            "\n" +
            "watch: 저장된 build ID로 서버 빌드를 다시 관찰하고 최종 관측을 receipt에 기록합니다. 새로 제출하지 않습니다.\n" +
            "  서버가 보내는 관측을 처음부터 다시 받아 표시할 뿐, 이전 관찰 위치를 이어받거나 이벤트를 한 번만 보여 준다고 보장하지 않습니다.\n" +
            "  Ctrl-C는 이 관찰만 멈추고 서버 빌드는 취소하지 않습니다.\n" +
            "cancel: 저장된 build ID에 서버 취소 요청을 한 번 보냅니다. 서버가 실제로 멈췄는지는 watch로 확인하세요.\n" +
            "build ID가 없는 receipt(제출 결과를 모르는 submit_in_flight 포함)는 아무 요청도 보내지 않고 다시 제출하지도 않습니다.\n" +
            "--format jsonl: stdout에 nodekit.receipt.v1 JSON 레코드만 한 줄씩 냅니다(마지막은 항상 completed). 진단과 후속 명령은 stderr로 갑니다.";

        // 사용자가 기다리는 명시적 취소라도 서버/네트워크가 응답하지 않으면 제어를 돌려줘야 한다.
        private static readonly TimeSpan _cancelRequestTimeout = TimeSpan.FromSeconds(5);

        /// <summary>receipt 하위 명령을 실행하고 CLI 종료 코드를 돌려준다.</summary>
        /// <param name="toolSpecClient">테스트 전용 client. 없으면 receipt endpoint로 연결한다.</param>
        /// <param name="cancellationToken">테스트 전용 사용자 취소 신호(Ctrl-C와 같은 경로).</param>
        /// <param name="interruptRegistration">테스트 전용 Ctrl-C 연결. 없으면 Console.CancelKeyPress에 연결한다.</param>
        public static int Run(
            string[] args,
            TextWriter stdout,
            TextWriter stderr,
            IToolSpecBuildClient? toolSpecClient = null,
            CancellationToken cancellationToken = default,
            Func<Action, IDisposable>? interruptRegistration = null)
        {
            if (args.Any(a => a is "--help" or "-h"))
            {
                stdout.WriteLine(UsageText);
                return 0;
            }

            if (args.Length < 3 || args[1] is not ("watch" or "cancel") || args[2].StartsWith("--", StringComparison.Ordinal))
            {
                stderr.WriteLine(UsageText);
                return 2;
            }

            // 사용법/옵션 오류는 형식이 확정되기 전이므로 jsonl 레코드 없이 2로 끝난다(submit과 같은 규칙).
            if (!TryParseFormat(args, stderr, out var jsonl))
            {
                stderr.WriteLine(UsageText);
                return 2;
            }

            var watch = args[1] == "watch";
            var command = watch ? ReceiptJsonlRecord.WatchCommand : ReceiptJsonlRecord.CancelCommand;
            var receiptPath = args[2];

            // 스크립트의 빈 변수("$RECEIPT")가 그대로 들어오는 경우: store를 만들기 전에 사용법 오류로 끝낸다.
            if (string.IsNullOrWhiteSpace(receiptPath))
            {
                stderr.WriteLine("receipt 경로가 비어 있습니다. 아무 요청도 보내지 않았습니다.");
                stderr.WriteLine(UsageText);
                return 2;
            }

            if (TryOpenReceipt(receiptPath, out var handle) is { } openError)
            {
                stderr.WriteLine($"로컬 기록 오류 ({openError.Code}): {openError.Message}");
                return LocalRefusal(stdout, jsonl, command, openError.ExitCode, openError.Code, openError.Message, buildId: null);
            }

            using (handle)
            {
                var receipt = handle!.Receipt;
                if (ToolSpecOperationRunner.RefuseWithoutBuildId(receipt) is { } refusal)
                {
                    stderr.WriteLine($"재진입 불가 ({refusal.Code}): {refusal.Message}");
                    return LocalRefusal(stdout, jsonl, command, refusal.ExitCode, refusal.Code!, refusal.Message, buildId: null);
                }

                if (!watch && receipt.Phase == OperationPhase.Terminal)
                {
                    var message = $"이미 최종 상태({receipt.LastObservation!.Outcome})를 관측한 빌드입니다 (build ID: {receipt.BuildId}). 취소 요청을 보내지 않았습니다.";
                    stderr.WriteLine(message);
                    return LocalRefusal(stdout, jsonl, command, 2, ToolSpecOperationRunner.NotResumableCode, message, receipt.BuildId);
                }

                GrpcToolSpecClient? grpc = null;
                if (toolSpecClient is null)
                {
                    try
                    {
                        grpc = new GrpcToolSpecClient(receipt.Endpoint);
                    }
                    // SubmitCommand와 같은 이유로 넓게 잡는다: 잘못된 endpoint 하나로 스택트레이스와 함께 죽지 않는다.
#pragma warning disable CA1031
                    catch (Exception ex)
#pragma warning restore CA1031
                    {
                        var message = $"receipt의 NodeVault endpoint로 연결할 수 없습니다: {receipt.Endpoint} ({ex.Message}). 아무 요청도 보내지 않았습니다.";
                        stderr.WriteLine(message);
                        return LocalRefusal(stdout, jsonl, command, 2, LocalOperationStore.InvalidCode, message, receipt.BuildId);
                    }
                }

                using (grpc)
                {
                    IToolSpecBuildClient client = toolSpecClient ?? grpc!;
                    if (!jsonl)
                    {
                        stdout.WriteLine($"receipt {(watch ? "재관찰" : "취소 요청")}: {handle.ReceiptPath}");
                        stdout.WriteLine($"  request ID: {receipt.RequestId}, build ID: {receipt.BuildId}, endpoint: {receipt.Endpoint}");
                        stdout.WriteLine();
                    }

                    var registerInterrupt = interruptRegistration ?? RegisterConsoleInterrupt;
                    return watch
                        ? WatchAsync(handle, client, stdout, stderr, jsonl, cancellationToken, registerInterrupt).GetAwaiter().GetResult()
                        : CancelAsync(receipt.BuildId!, handle.ReceiptPath, client, stdout, stderr, jsonl, cancellationToken, registerInterrupt).GetAwaiter().GetResult();
                }
            }
        }

        // 사용자가 준 경로는 OS가 거부할 수 있다(NUL 문자 등). 예외로 죽지 않고 로컬 입력 오류(2)로 돌려준다.
        private static OperationStoreError? TryOpenReceipt(string receiptPath, out OperationHandle? handle)
        {
            handle = null;
            try
            {
                return LocalOperationStore.ForReceipt(receiptPath).TryOpen(receiptPath, out handle);
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

        private static OperationStoreError InvalidReceiptPath(string receiptPath, Exception ex) =>
            new(LocalOperationStore.InvalidCode, $"receipt 경로를 사용할 수 없습니다: {receiptPath} ({ex.Message}). 아무 요청도 보내지 않았습니다.");

        // args[0]은 "receipt", args[1]은 하위 명령, args[2]는 receipt 경로 — 옵션은 인덱스 3부터다.
        private static bool TryParseFormat(string[] args, TextWriter stderr, out bool jsonl)
        {
            jsonl = false;
            if (!CliOptionParser.TryParse(args, 3, stderr, new[] { "--format" }, Array.Empty<string>(), out var values, out _))
            {
                return false;
            }

            if (values.TryGetValue("--format", out var formatValue))
            {
                var normalized = formatValue.Trim().ToLowerInvariant();
                if (normalized is not ("human" or "jsonl"))
                {
                    stderr.WriteLine($"--format 옵션 값이 올바르지 않습니다: {formatValue} (human 또는 jsonl)");
                    return false;
                }

                jsonl = normalized == "jsonl";
            }

            return true;
        }

        // receipt를 열거나 검사하다 거절했다: 어떤 RPC도 보내지 않았고 receipt도 그대로다. 같은 입력으로
        // 다시 실행해도 결과가 같으므로 recovery는 terminal이고, 원격 상태는 건드리지 않았으므로 싣지 않는다.
        private static int LocalRefusal(
            TextWriter stdout, bool jsonl, string command, int exitCode, string code, string? message, string? buildId)
        {
            if (jsonl)
            {
                ReceiptJsonlRecord.Completed(command, exitCode, RecoveryDisposition.Terminal, buildId, errorCode: code, message: message)
                    .WriteTo(stdout);
            }

            return exitCode;
        }

        private static async Task<int> WatchAsync(
            OperationHandle handle,
            IToolSpecBuildClient client,
            TextWriter stdout,
            TextWriter stderr,
            bool jsonl,
            CancellationToken cancellationToken,
            Func<Action, IDisposable> registerInterrupt)
        {
            var buildId = handle.Receipt.BuildId!;
            using var userCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var interrupt = registerInterrupt(() => CancelQuietly(userCts));
            try
            {
                var result = await ToolSpecOperationRunner.WatchKnownBuildAsync(
                        handle,
                        client,
                        onEvent: ev =>
                        {
                            if (jsonl)
                            {
                                WriteStateRecord(ev, buildId, stdout);
                            }
                            else
                            {
                                PrintEvent(ev, stdout);
                            }
                        },
                        cancellationToken: userCts.Token)
                    .ConfigureAwait(false);

                if (result.Code is { } storeCode)
                {
                    stderr.WriteLine($"로컬 기록 실패 ({storeCode}): {result.Message}");
                    return WatchCompleted(stdout, jsonl, result.ExitCode, RecoveryDisposition.Uncertain, buildId, errorCode: storeCode, message: result.Message);
                }

                if (result.ExitCode == 0)
                {
                    var observed = result.ObservedResult;
                    if (jsonl)
                    {
                        // S2-05-C06과 같은 원칙: Partial 등 integrity 상태는 필드로만 싣고 stderr에 따로 내지 않는다.
                        return WatchCompleted(
                            stdout,
                            jsonl,
                            0,
                            RecoveryDisposition.None,
                            buildId,
                            status: observed?.Status ?? OperationObservation.SucceededOutcome,
                            imageRef: observed?.ImageRef,
                            imageDigest: observed?.ImageDigest,
                            integrityHealth: observed?.IntegrityHealth);
                    }

                    if (!string.IsNullOrEmpty(observed?.ImageDigest))
                    {
                        stdout.WriteLine(string.IsNullOrEmpty(observed.ImageRef)
                            ? $"이미지 digest: {observed.ImageDigest}"
                            : $"이미지 digest: {observed.ImageRef}@{observed.ImageDigest}");
                    }

                    stdout.WriteLine($"최종 상태를 receipt에 기록했습니다: {observed?.Status}");
                    return 0;
                }

                if (result.ObservedResult?.Outcome == OperationObservation.FailedOutcome)
                {
                    stderr.WriteLine($"빌드 실패: {result.Message}");
                    stderr.WriteLine("최종 상태를 receipt에 기록했습니다.");

                    // status는 서버가 보낸 terminal 이름(예: Interrupted)을 그대로 싣고, 실패 분류는 error_code가 맡는다.
                    return WatchCompleted(
                        stdout,
                        jsonl,
                        result.ExitCode,
                        RecoveryDisposition.Terminal,
                        buildId,
                        status: result.ObservedResult.Status ?? OperationObservation.FailedOutcome,
                        errorCode: "BUILD_FAILED",
                        remoteBuildState: "failed",
                        message: result.Message);
                }

                // 스트림 종료·다른 빌드 이벤트: 결과를 확인하지 못했다. receipt는 바뀌지 않았다.
                stderr.WriteLine($"{result.Message} 원격 빌드 결과를 확인하지 못했습니다 (build ID: {buildId}). receipt는 그대로 두었습니다 — 나중에 다시 watch하세요.");
                return WatchCompleted(
                    stdout,
                    jsonl,
                    result.ExitCode,
                    RecoveryDisposition.Uncertain,
                    buildId,
                    errorCode: result.StreamEnded ? "STREAM_ENDED_WITHOUT_RESULT" : "UNEXPECTED_ERROR",
                    remoteBuildState: "unknown",
                    message: result.Message);
            }
#pragma warning disable CA1031 // our own token is the only reliable cancel signal; the exception shape varies (SubmitCommand)
            catch (Exception) when (userCts.IsCancellationRequested)
#pragma warning restore CA1031
            {
                // 관찰만 멈췄다: 서버 취소를 주장하지 않으므로 status(Cancelled 등)를 만들지 않는다.
                stderr.WriteLine($"관찰을 멈췄습니다 (build ID: {buildId}). 서버 빌드는 취소하지 않았습니다 — 취소하려면 {FollowUpCommand("cancel", handle.ReceiptPath)}");
                return WatchCompleted(
                    stdout,
                    jsonl,
                    130,
                    RecoveryDisposition.Terminal,
                    buildId,
                    errorCode: "WATCH_STOPPED",
                    remoteBuildState: "unknown",
                    message: "관찰을 멈췄습니다. 서버 빌드는 취소하지 않았습니다.");
            }
#pragma warning disable CA1031 // a failed watch must end with a diagnostic, not a stack trace; the receipt stays re-watchable
            catch (Exception ex)
#pragma warning restore CA1031
            {
                var message = BuildErrorMessages.Describe(ex);
                stderr.WriteLine($"{message} 원격 빌드 상태는 확인하지 못했습니다 (build ID: {buildId}). receipt는 그대로 두었습니다.");
                return WatchCompleted(
                    stdout,
                    jsonl,
                    1,
                    RecoveryDisposition.Uncertain,
                    buildId,
                    errorCode: "WATCH_FAILED",
                    remoteBuildState: "unknown",
                    message: message);
            }
        }

        private static async Task<int> CancelAsync(
            string buildId,
            string receiptPath,
            IToolSpecBuildClient client,
            TextWriter stdout,
            TextWriter stderr,
            bool jsonl,
            CancellationToken cancellationToken,
            Func<Action, IDisposable> registerInterrupt)
        {
            // watch와 같이 Ctrl-C를 이 요청의 token에 연결한다. 연결하지 않으면 프로세스가 그대로 끝나
            // CANCEL_REQUEST_ABORTED completed 레코드를 내지 못한다(r4240863799).
            using var userCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var interrupt = registerInterrupt(() => CancelQuietly(userCts));
            using var timeoutCts = new CancellationTokenSource(_cancelRequestTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(userCts.Token, timeoutCts.Token);
            try
            {
                await client.CancelBuildAsync(buildId, linkedCts.Token).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // our own token is the only reliable cancel signal; the exception shape varies (SubmitCommand)
            catch (Exception) when (userCts.IsCancellationRequested)
#pragma warning restore CA1031
            {
                var message = $"취소 요청을 중단했습니다 (build ID: {buildId}). 서버가 요청을 받았는지 알 수 없습니다.";
                stderr.WriteLine(message);
                return CancelCompleted(stdout, jsonl, 130, buildId, "CANCEL_REQUEST_ABORTED", message);
            }
#pragma warning disable CA1031 // any failure keeps the remote state unknown; it is reported, never turned into "cancelled"
            catch (Exception ex)
#pragma warning restore CA1031
            {
                var timedOut = timeoutCts.IsCancellationRequested;
                var reason = timedOut
                    ? $"{(int)_cancelRequestTimeout.TotalSeconds}초 안에 응답이 없었습니다"
                    : BuildErrorMessages.Describe(ex);
                var message = $"취소 요청이 실패했습니다 (build ID: {buildId}): {reason}. 서버 빌드가 멈췄는지 알 수 없습니다 — receipt는 그대로 두었습니다.";
                stderr.WriteLine(message);
                return CancelCompleted(stdout, jsonl, 1, buildId, timedOut ? "CANCEL_REQUEST_TIMEOUT" : "CANCEL_REQUEST_FAILED", message);
            }

            // 요청을 보내고 응답을 받았을 뿐 서버가 멈췄다는 뜻이 아니다 — jsonl도 status를 싣지 않는다.
            var sent = $"취소 요청을 보냈습니다 (build ID: {buildId}). 서버가 실제로 멈췄는지는 아직 확인하지 않았습니다 — 확인하려면 {FollowUpCommand("watch", receiptPath)}";
            if (!jsonl)
            {
                stdout.WriteLine(sent);
                return 0;
            }

            stderr.WriteLine(sent);
            return CancelCompleted(stdout, jsonl, 0, buildId, "CANCEL_REQUESTED", "취소 요청을 보냈습니다. 서버가 실제로 멈췄는지는 확인하지 않았습니다.");
        }

        // 기본 Ctrl-C 연결: e.Cancel로 프로세스를 살려 두어 마지막 completed 레코드를 낼 수 있게 한다.
        // 반환값을 Dispose하면 handler를 뗀다.
        private static IDisposable RegisterConsoleInterrupt(Action onInterrupt)
        {
            ConsoleCancelEventHandler onCancelKeyPress = (_, e) =>
            {
                e.Cancel = true;
                onInterrupt();
            };
            Console.CancelKeyPress += onCancelKeyPress;
            return new ConsoleInterruptRegistration(onCancelKeyPress);
        }

        private static void CancelQuietly(CancellationTokenSource userCts)
        {
            try
            {
                userCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Dispose와 동시에 들어온 신호 — 이미 흐름이 끝났으므로 할 일이 없다.
            }
        }

        private sealed class ConsoleInterruptRegistration : IDisposable
        {
            private readonly ConsoleCancelEventHandler _handler;

            public ConsoleInterruptRegistration(ConsoleCancelEventHandler handler) => _handler = handler;

            public void Dispose() => Console.CancelKeyPress -= _handler;
        }

        private static int WatchCompleted(
            TextWriter stdout,
            bool jsonl,
            int exitCode,
            RecoveryDisposition recovery,
            string buildId,
            string? status = null,
            string? errorCode = null,
            string? remoteBuildState = null,
            string? message = null,
            string? imageRef = null,
            string? imageDigest = null,
            string? integrityHealth = null)
        {
            if (jsonl)
            {
                ReceiptJsonlRecord.Completed(
                        ReceiptJsonlRecord.WatchCommand,
                        exitCode,
                        recovery,
                        buildId,
                        status,
                        errorCode,
                        remoteBuildState,
                        message,
                        imageRef,
                        imageDigest,
                        integrityHealth)
                    .WriteTo(stdout);
            }

            return exitCode;
        }

        // 취소 요청의 모든 결과는 원격 상태를 확인하지 않은 채 끝나고 receipt도 바꾸지 않는다.
        private static int CancelCompleted(TextWriter stdout, bool jsonl, int exitCode, string buildId, string errorCode, string message)
        {
            if (jsonl)
            {
                ReceiptJsonlRecord.Completed(
                        ReceiptJsonlRecord.CancelCommand,
                        exitCode,
                        RecoveryDisposition.Uncertain,
                        buildId,
                        errorCode: errorCode,
                        remoteBuildState: "unknown",
                        message: message)
                    .WriteTo(stdout);
            }

            return exitCode;
        }

        internal static string FollowUpCommand(string verb, string receiptPath) =>
            FollowUpCommand(verb, receiptPath, OperatingSystem.IsWindows());

        /// <summary>
        /// 후속 명령 안내. 경로를 셸 인자 하나로 안전하게 적을 수 있으면 그대로 실행 가능한 명령을,
        /// 그럴 수 없으면(Windows에서 셸마다 해석이 다른 문자) 실행 가능하다고 주장하지 않는 형태를 돌려준다.
        /// </summary>
        internal static string FollowUpCommand(string verb, string receiptPath, bool windows)
        {
            var quoted = QuoteArgument(receiptPath, windows);
            return quoted is null
                ? $"nodekit receipt {verb} <receipt.json> (receipt 경로: {receiptPath} — 셸마다 해석이 달라 그대로 붙여 넣을 수 있는 형태로 적지 않았습니다. 사용하는 셸에 맞게 인용해 입력하세요)"
                : $"nodekit receipt {verb} {quoted}";
        }

        /// <summary>
        /// 안내 명령을 그대로 복사해 실행할 수 있도록 receipt 경로를 셸 인자 하나로 감싼다.
        /// 안전한 문자만 있으면 그대로 둔다. POSIX는 작은따옴표(내부 ' → '\'')로 항상 감쌀 수 있다.
        /// Windows는 cmd.exe와 PowerShell이 모두 글자 그대로 읽는 경우에만 큰따옴표로 감싸고,
        /// 큰따옴표 안에서도 확장되는 문자(%, !, $, `, 유니코드 따옴표 등)가 있으면 null을 돌려준다.
        /// </summary>
        internal static string? QuoteArgument(string value, bool windows)
        {
            if (!windows)
            {
                return value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || "_-./:@%+=,".Contains(c, StringComparison.Ordinal))
                    ? value
                    : "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
            }

            if (value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || "_-.\\/:".Contains(c, StringComparison.Ordinal)))
            {
                return value;
            }

            return value.Length == 0 || value.Any(IsExpandedInsideWindowsDoubleQuotes)
                ? null
                : "\"" + value + "\"";
        }

        // cmd: %VAR%, 지연 확장 !VAR!, 따옴표 종료. PowerShell: $var/$(...), ` escape, 유니코드 따옴표도 따옴표로 취급.
        private static bool IsExpandedInsideWindowsDoubleQuotes(char c) =>
            char.IsControl(c) || "\"%!$`‘’‚‛“”„".Contains(c, StringComparison.Ordinal);

        // terminal 이벤트는 completed 레코드로 한 번만 다룬다. status 값은 submit jsonl의 state와 같이
        // 서버 Status를, 없으면 Kind 이름을 그대로 쓴다.
        private static void WriteStateRecord(BuildEvent ev, string buildId, TextWriter stdout)
        {
            if (ev.Kind is BuildEventKind.Succeeded or BuildEventKind.Failed)
            {
                return;
            }

            ReceiptJsonlRecord.State(
                    buildId,
                    string.IsNullOrEmpty(ev.Status) ? ev.Kind.ToString() : ev.Status,
                    ev.Message,
                    ev.ImageRef,
                    ev.ImageDigest,
                    ev.IntegrityHealth)
                .WriteTo(stdout);
        }

        private static void PrintEvent(BuildEvent ev, TextWriter stdout)
        {
            // Failed는 stderr로 한 번만 보고한다(SubmitCommand와 같은 원칙).
            if (ev.Kind == BuildEventKind.Failed)
            {
                return;
            }

            var prefix = ev.Kind switch
            {
                BuildEventKind.Succeeded => "[성공]",
                BuildEventKind.JobCreated => "[빌드 시작]",
                BuildEventKind.JobRunning => "[실행 중]",
                _ => "[관측]",
            };
            var status = string.IsNullOrEmpty(ev.Status) ? string.Empty : $" {ev.Status}";
            var message = string.IsNullOrEmpty(ev.Message) ? string.Empty : $" {ev.Message}";
            stdout.WriteLine($"{prefix}{status}{message}");
        }
    }
}
