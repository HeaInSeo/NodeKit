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
            "  nodekit receipt watch <receipt.json>\n" +
            "  nodekit receipt cancel <receipt.json>\n" +
            "\n" +
            "watch: 저장된 build ID로 서버 빌드를 다시 관찰하고 최종 관측을 receipt에 기록합니다. 새로 제출하지 않습니다.\n" +
            "  서버가 보내는 관측을 처음부터 다시 받아 표시할 뿐, 이전 관찰 위치를 이어받거나 이벤트를 한 번만 보여 준다고 보장하지 않습니다.\n" +
            "  Ctrl-C는 이 관찰만 멈추고 서버 빌드는 취소하지 않습니다.\n" +
            "cancel: 저장된 build ID에 서버 취소 요청을 한 번 보냅니다. 서버가 실제로 멈췄는지는 watch로 확인하세요.\n" +
            "build ID가 없는 receipt(제출 결과를 모르는 submit_in_flight 포함)는 아무 요청도 보내지 않고 다시 제출하지도 않습니다.";

        // 사용자가 기다리는 명시적 취소라도 서버/네트워크가 응답하지 않으면 제어를 돌려줘야 한다.
        private static readonly TimeSpan _cancelRequestTimeout = TimeSpan.FromSeconds(5);

        /// <summary>receipt 하위 명령을 실행하고 CLI 종료 코드를 돌려준다.</summary>
        /// <param name="toolSpecClient">테스트 전용 client. 없으면 receipt endpoint로 연결한다.</param>
        /// <param name="cancellationToken">테스트 전용 사용자 취소 신호(Ctrl-C와 같은 경로).</param>
        public static int Run(
            string[] args,
            TextWriter stdout,
            TextWriter stderr,
            IToolSpecBuildClient? toolSpecClient = null,
            CancellationToken cancellationToken = default)
        {
            if (args.Any(a => a is "--help" or "-h"))
            {
                stdout.WriteLine(UsageText);
                return 0;
            }

            if (args.Length != 3 || args[1] is not ("watch" or "cancel") || args[2].StartsWith("--", StringComparison.Ordinal))
            {
                stderr.WriteLine(UsageText);
                return 2;
            }

            var watch = args[1] == "watch";
            var receiptPath = args[2];
            var store = LocalOperationStore.ForReceipt(receiptPath);
            if (store.TryOpen(receiptPath, out var handle) is { } openError)
            {
                stderr.WriteLine($"로컬 기록 오류 ({openError.Code}): {openError.Message}");
                return openError.ExitCode;
            }

            using (handle)
            {
                var receipt = handle!.Receipt;
                if (ToolSpecOperationRunner.RefuseWithoutBuildId(receipt) is { } refusal)
                {
                    stderr.WriteLine($"재진입 불가 ({refusal.Code}): {refusal.Message}");
                    return refusal.ExitCode;
                }

                if (!watch && receipt.Phase == OperationPhase.Terminal)
                {
                    stderr.WriteLine(
                        $"이미 최종 상태({receipt.LastObservation!.Outcome})를 관측한 빌드입니다 (build ID: {receipt.BuildId}). 취소 요청을 보내지 않았습니다.");
                    return 2;
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
                        stderr.WriteLine($"receipt의 NodeVault endpoint로 연결할 수 없습니다: {receipt.Endpoint} ({ex.Message}). 아무 요청도 보내지 않았습니다.");
                        return 2;
                    }
                }

                using (grpc)
                {
                    IToolSpecBuildClient client = toolSpecClient ?? grpc!;
                    stdout.WriteLine($"receipt {(watch ? "재관찰" : "취소 요청")}: {handle.ReceiptPath}");
                    stdout.WriteLine($"  request ID: {receipt.RequestId}, build ID: {receipt.BuildId}, endpoint: {receipt.Endpoint}");
                    stdout.WriteLine();

                    return watch
                        ? WatchAsync(handle, client, stdout, stderr, cancellationToken).GetAwaiter().GetResult()
                        : CancelAsync(receipt.BuildId!, handle.ReceiptPath, client, stdout, stderr, cancellationToken).GetAwaiter().GetResult();
                }
            }
        }

        private static async Task<int> WatchAsync(
            OperationHandle handle, IToolSpecBuildClient client, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
        {
            var buildId = handle.Receipt.BuildId!;
            using var userCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ConsoleCancelEventHandler onCancelKeyPress = (_, e) =>
            {
                e.Cancel = true;
                userCts.Cancel();
            };
            Console.CancelKeyPress += onCancelKeyPress;
            try
            {
                var result = await ToolSpecOperationRunner.WatchKnownBuildAsync(
                        handle,
                        client,
                        onEvent: ev => PrintEvent(ev, stdout),
                        cancellationToken: userCts.Token)
                    .ConfigureAwait(false);

                if (result.Code is { } storeCode)
                {
                    stderr.WriteLine($"로컬 기록 실패 ({storeCode}): {result.Message}");
                    return result.ExitCode;
                }

                if (result.ExitCode == 0)
                {
                    var observed = result.ObservedResult;
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
                    return result.ExitCode;
                }

                // 스트림 종료·다른 빌드 이벤트: 결과를 확인하지 못했다. receipt는 바뀌지 않았다.
                stderr.WriteLine($"{result.Message} 원격 빌드 결과를 확인하지 못했습니다 (build ID: {buildId}). receipt는 그대로 두었습니다 — 나중에 다시 watch하세요.");
                return result.ExitCode;
            }
#pragma warning disable CA1031 // our own token is the only reliable cancel signal; the exception shape varies (SubmitCommand)
            catch (Exception) when (userCts.IsCancellationRequested)
#pragma warning restore CA1031
            {
                stderr.WriteLine($"관찰을 멈췄습니다 (build ID: {buildId}). 서버 빌드는 취소하지 않았습니다 — 취소하려면 nodekit receipt cancel {handle.ReceiptPath}");
                return 130;
            }
#pragma warning disable CA1031 // a failed watch must end with a diagnostic, not a stack trace; the receipt stays re-watchable
            catch (Exception ex)
#pragma warning restore CA1031
            {
                stderr.WriteLine($"{BuildErrorMessages.Describe(ex)} 원격 빌드 상태는 확인하지 못했습니다 (build ID: {buildId}). receipt는 그대로 두었습니다.");
                return 1;
            }
            finally
            {
                Console.CancelKeyPress -= onCancelKeyPress;
            }
        }

        private static async Task<int> CancelAsync(
            string buildId, string receiptPath, IToolSpecBuildClient client, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
        {
            using var timeoutCts = new CancellationTokenSource(_cancelRequestTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            try
            {
                await client.CancelBuildAsync(buildId, linkedCts.Token).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // our own token is the only reliable cancel signal; the exception shape varies (SubmitCommand)
            catch (Exception) when (cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                stderr.WriteLine($"취소 요청을 중단했습니다 (build ID: {buildId}). 서버가 요청을 받았는지 알 수 없습니다.");
                return 130;
            }
#pragma warning disable CA1031 // any failure keeps the remote state unknown; it is reported, never turned into "cancelled"
            catch (Exception ex)
#pragma warning restore CA1031
            {
                var reason = timeoutCts.IsCancellationRequested
                    ? $"{(int)_cancelRequestTimeout.TotalSeconds}초 안에 응답이 없었습니다"
                    : BuildErrorMessages.Describe(ex);
                stderr.WriteLine($"취소 요청이 실패했습니다 (build ID: {buildId}): {reason}. 서버 빌드가 멈췄는지 알 수 없습니다 — receipt는 그대로 두었습니다.");
                return 1;
            }

            stdout.WriteLine($"취소 요청을 보냈습니다 (build ID: {buildId}). 서버가 실제로 멈췄는지는 아직 확인하지 않았습니다 — nodekit receipt watch {receiptPath} 로 확인하세요.");
            return 0;
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
