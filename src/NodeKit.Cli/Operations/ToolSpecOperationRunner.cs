using System.Globalization;
using NodeKit.Grpc;

namespace NodeKit.Cli.Operations
{
    /// <summary>
    /// prepared ToolSpec operation 하나를 실행한 결과. ExitCode는 CLI 종료 코드 후보다:
    /// 0 성공 terminal, 1 원격/전송 실패, 2 로컬 기록 실패·불일치·재개 불가.
    /// </summary>
    internal sealed record ToolSpecOperationResult
    {
        public required int ExitCode { get; init; }

        /// <summary>로컬 기록 문제일 때 OperationStoreError.Code, 그 외에는 null.</summary>
        public string? Code { get; init; }

        public string? Message { get; init; }

        /// <summary>이번 실행에서 서버로부터 실제로 받은 build_id(저장 여부와 무관한 관측값).</summary>
        public string? ObservedBuildId { get; init; }

        /// <summary>이번 실행에서 실제로 받은 마지막 관측(저장 여부와 무관).</summary>
        public OperationObservation? ObservedResult { get; init; }

        /// <summary>ResolveToolSpec이 성공 응답을 돌려줬는가 — 저장 실패가 Resolve 전인지 후인지 구분한다.</summary>
        public bool ResolveCompleted { get; init; }

        /// <summary>실행이 끝난 시점에 durable하게 저장된 receipt.</summary>
        public required OperationReceipt Receipt { get; init; }
    }

    /// <summary>
    /// S2-02 durable 순서로 ToolSpec 빌드를 실행한다:
    /// prepared 기록(호출 전 이미 저장) → ResolveToolSpec → resolved snapshot +
    /// submit_in_flight 저장 → SubmitToolBuild(저장된 request ID) → build_id를
    /// acknowledged로 저장 → WatchToolBuild terminal 관측을 저장.
    ///
    /// 저장 실패는 다음 부작용 RPC를 막는다. 이미 받은 build_id/결과는 저장에
    /// 실패해도 진단(ObservedBuildId/ObservedResult)으로 돌려주지만 durable 완료로
    /// 주장하지 않는다. 자동 새 제출·새 request ID는 만들지 않는다. 취소
    /// (OperationCanceledException)는 잡지 않고 호출자에게 전파한다.
    /// </summary>
    internal static class ToolSpecOperationRunner
    {
        public const string NotResumableCode = "OPERATION_NOT_RESUMABLE";

        /// <param name="endpoint">client가 실제로 연결된 endpoint. receipt에 저장된 endpoint와
        /// 다르면 어떤 RPC도 보내기 전에 OPERATION_RECORD_MISMATCH(2)로 멈춘다(S2-02-C05).</param>
        /// <param name="onEvent">이 receipt의 이벤트를 호출자(예: submit 출력)에 넘긴다. build_id가
        /// 있는 이벤트는 acknowledged 저장 뒤에, terminal 이벤트는 terminal 저장 뒤에만 넘긴다 —
        /// 저장에 실패한 이벤트와 다른 빌드의 이벤트는 넘기지 않는다.</param>
        public static async Task<ToolSpecOperationResult> RunAsync(
            OperationHandle handle,
            IToolSpecBuildClient client,
            string endpoint,
            TimeProvider? timeProvider = null,
            Action<BuildEvent>? onEvent = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(handle);
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(endpoint);
            var clock = timeProvider ?? TimeProvider.System;

            var start = handle.Receipt;
            if (!string.Equals(endpoint, start.Endpoint, StringComparison.Ordinal))
            {
                // 같은 request ID를 다른 서버로 보내면 저장된 attempt와 다른 요청이 된다.
                return new ToolSpecOperationResult
                {
                    ExitCode = 2,
                    Code = LocalOperationStore.MismatchCode,
                    Message = $"저장된 요청(request ID: {start.RequestId})의 endpoint는 {start.Endpoint}인데 지금 연결은 {endpoint}입니다. 아무 요청도 보내지 않았습니다 — 새 요청은 새 attempt로 시작하세요.",
                    ObservedBuildId = start.BuildId,
                    Receipt = start,
                };
            }

            if (start.Phase != OperationPhase.Prepared)
            {
                // submit_in_flight에서 build_id가 없으면 원격 생성 여부를 알 수 없다 — 같은
                // 요청을 다시 보내거나 prepared로 되돌려 blind submit하지 않는다.
                return new ToolSpecOperationResult
                {
                    ExitCode = 2,
                    Code = NotResumableCode,
                    Message = start.Phase == OperationPhase.SubmitInFlight
                        ? $"이전 제출의 원격 결과를 알 수 없습니다 (request ID: {start.RequestId}, endpoint: {start.Endpoint}). 자동으로 다시 제출하지 않습니다 — NodeVault에서 직접 확인하세요."
                        : $"이미 제출된 operation입니다 (phase: {start.Phase}, build ID: {start.BuildId}). 다시 제출하지 않습니다.",
                    ObservedBuildId = start.BuildId,
                    Receipt = start,
                };
            }

            OperationStoreError? storeError = null;
            var resolveCompleted = false;
            var options = new ToolSpecBuildOptions
            {
                RequestId = start.RequestId,
                BeforeSubmitAsync = (basis, _) =>
                {
                    resolveCompleted = true;
                    storeError = RecordResolved(handle, basis);
                    if (storeError is not null)
                    {
                        throw new IOException(storeError.Message);
                    }

                    return Task.CompletedTask;
                },
            };

            string? buildId = null;
            OperationObservation? observed = null;
            var envelope = start.Envelope;

            await foreach (var ev in client.ResolveAndBuildAsync(envelope.ToolName, envelope.Version, envelope.RawSpec, options, cancellationToken)
                .ConfigureAwait(false))
            {
                if (storeError is not null)
                {
                    // BeforeSubmitAsync 실패는 client가 Failed 이벤트로 바꾸고 Submit 전에 멈춘다.
                    return StoreFailure(handle, storeError, resolveCompleted, buildId, observed);
                }

                if (!string.IsNullOrEmpty(ev.BuildId) && buildId is null)
                {
                    buildId = ev.BuildId;
                    var ackError = handle.Advance(handle.Receipt with
                    {
                        Phase = OperationPhase.Acknowledged,
                        BuildId = buildId,
                    });
                    if (ackError is not null)
                    {
                        // 받은 build_id를 저장하지 못했다 — 관찰을 계속하지 않고 ID를 진단으로
                        // 남긴다. 서버 빌드는 계속 진행 중일 수 있으며 취소 요청은 보내지 않는다.
                        return StoreFailure(handle, ackError, resolveCompleted, buildId, observed);
                    }
                }

                if (!string.IsNullOrEmpty(ev.BuildId) && !string.Equals(ev.BuildId, buildId, StringComparison.Ordinal))
                {
                    // 다른 빌드의 이벤트다 — 그 상태/결과를 이 receipt의 관측으로 저장하지 않는다.
                    // receipt는 acknowledged로 남아 build ID로 다시 관찰할 수 있다.
                    return new ToolSpecOperationResult
                    {
                        ExitCode = 1,
                        Message = $"서버 스트림이 다른 빌드의 이벤트를 보냈습니다 (receipt build ID: {buildId}, 이벤트 build ID: {ev.BuildId}). 이 이벤트는 저장하지 않았습니다.",
                        ObservedBuildId = buildId,
                        ObservedResult = observed,
                        ResolveCompleted = resolveCompleted,
                        Receipt = handle.Receipt,
                    };
                }

                if (!string.IsNullOrEmpty(ev.BuildId) || !string.IsNullOrEmpty(ev.Status))
                {
                    observed = Observe(ev, observed, clock);
                }

                if (ev.Kind is not (BuildEventKind.Succeeded or BuildEventKind.Failed))
                {
                    onEvent?.Invoke(ev);
                    continue;
                }

                if (buildId is null)
                {
                    // build_id 전 Failed: Resolve 실패면 prepared, Submit 실패면 submit_in_flight(원격
                    // unknown)가 그대로 남는다. 관측한 적 없는 원격 상태를 기록하지 않는다.
                    onEvent?.Invoke(ev);
                    return new ToolSpecOperationResult
                    {
                        ExitCode = 1,
                        Message = ev.Message,
                        ResolveCompleted = resolveCompleted,
                        Receipt = handle.Receipt,
                    };
                }

                // status 없이 terminal kind로만 끝을 알린 이벤트도 결과를 남긴다 — 이전 관측의
                // Running 같은 값을 그대로 두면 다시 열었을 때 성공/실패를 구분할 수 없다.
                var outcome = ev.Kind == BuildEventKind.Succeeded
                    ? OperationObservation.SucceededOutcome
                    : OperationObservation.FailedOutcome;
                var terminal = Observe(ev, observed, clock) with
                {
                    Status = NullIfEmpty(ev.Status) ?? outcome,
                    Outcome = outcome,
                };
                var terminalError = handle.Advance(handle.Receipt with
                {
                    Phase = OperationPhase.Terminal,
                    LastObservation = terminal,
                });
                if (terminalError is not null)
                {
                    return StoreFailure(handle, terminalError, resolveCompleted, buildId, terminal);
                }

                onEvent?.Invoke(ev);
                return new ToolSpecOperationResult
                {
                    ExitCode = ev.Kind == BuildEventKind.Succeeded ? 0 : 1,
                    Message = ev.Message,
                    ObservedBuildId = buildId,
                    ObservedResult = terminal,
                    ResolveCompleted = resolveCompleted,
                    Receipt = handle.Receipt,
                };
            }

            if (storeError is not null)
            {
                return StoreFailure(handle, storeError, resolveCompleted, buildId, observed);
            }

            // terminal 없이 스트림 종료 — 결과를 확인하지 못했다. acknowledged는 재진입 근거로 남는다.
            return new ToolSpecOperationResult
            {
                ExitCode = 1,
                Message = "서버 스트림이 최종 상태 이벤트 없이 종료되었습니다.",
                ObservedBuildId = buildId,
                ObservedResult = observed,
                ResolveCompleted = resolveCompleted,
                Receipt = handle.Receipt,
            };
        }

        private static OperationStoreError? RecordResolved(OperationHandle handle, ToolSpecSubmitBasis basis)
        {
            var receipt = handle.Receipt;
            if (basis.RequestId != receipt.RequestId)
            {
                return new OperationStoreError(
                    LocalOperationStore.MismatchCode,
                    $"client가 저장된 request ID({receipt.RequestId})와 다른 ID({basis.RequestId})로 제출하려 했습니다.");
            }

            var snapshot = ResolvedSnapshot.FromBasis(basis, receipt);
            if (handle.Store.WriteResolvedSnapshot(snapshot, out var sha) is { } snapshotError)
            {
                return snapshotError;
            }

            return handle.Advance(receipt with
            {
                Phase = OperationPhase.SubmitInFlight,
                ResolvedSnapshotSha256 = sha,
            });
        }

        private static OperationObservation Observe(BuildEvent ev, OperationObservation? previous, TimeProvider clock) =>
            new()
            {
                Status = NullIfEmpty(ev.Status) ?? previous?.Status,
                Message = NullIfEmpty(ev.Message) ?? previous?.Message,
                ImageRef = NullIfEmpty(ev.ImageRef) ?? previous?.ImageRef,
                ImageDigest = NullIfEmpty(ev.ImageDigest) ?? previous?.ImageDigest,
                IntegrityHealth = NullIfEmpty(ev.IntegrityHealth) ?? previous?.IntegrityHealth,
                SpecReferrerDigest = NullIfEmpty(ev.SpecReferrerDigest) ?? previous?.SpecReferrerDigest,
                ObservedAt = clock.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            };

        private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

        private static ToolSpecOperationResult StoreFailure(
            OperationHandle handle,
            OperationStoreError error,
            bool resolveCompleted,
            string? buildId,
            OperationObservation? observed)
        {
            var detail = buildId is null
                ? string.Empty
                : $" 관측한 build ID: {buildId}" + (observed?.Status is { } status ? $", 마지막 관측 상태: {status}" : string.Empty)
                  + ". 이 값은 로컬에 영속 저장되지 않았습니다 — build ID로 직접 다시 관찰하세요.";

            return new ToolSpecOperationResult
            {
                ExitCode = error.ExitCode,
                Code = error.Code,
                Message = error.Message + detail,
                ObservedBuildId = buildId,
                ObservedResult = observed,
                ResolveCompleted = resolveCompleted,
                Receipt = handle.Receipt,
            };
        }
    }
}
