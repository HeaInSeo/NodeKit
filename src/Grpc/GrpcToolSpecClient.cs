using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Nodevault.V1;

namespace NodeKit.Grpc
{
    /// <summary>
    /// NodeVault 신규 빌드 경로 클라이언트:
    /// ResolveToolSpec → SubmitToolBuild → WatchToolBuild.
    /// CLI 활성화: NODEKIT_NODEVAULT_URL 환경변수 또는 --url 옵션.
    /// GUI 활성화: 설정 화면의 NodeVault 주소.
    /// </summary>
    internal sealed class GrpcToolSpecClient : IToolSpecBuildClient, IDisposable
    {
        private readonly GrpcChannel? _channel;
        private readonly BuildService.BuildServiceClient _client;
        private bool _disposed;

        public GrpcToolSpecClient(string address)
        {
            _channel = GrpcChannel.ForAddress(address);
            _client = new BuildService.BuildServiceClient(_channel);
        }

        // 테스트 전용: in-process fake 서버(TestServer)가 만든 채널을 그대로 쓴다.
        // 이 인스턴스는 채널을 소유하지 않으므로 Dispose()에서 닫지 않는다.
        internal GrpcToolSpecClient(ChannelBase channel)
        {
            _channel = null;
            _client = new BuildService.BuildServiceClient(channel);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _channel?.Dispose();
            _disposed = true;
        }

        public async IAsyncEnumerable<BuildEvent> ResolveAndBuildAsync(
            string toolName,
            string version,
            string rawSpec,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Step 1: ResolveToolSpec — spec digest를 계산하고 index에 저장한다.
            ResolvedToolSpecResponse? resolveResp = null;
            Exception? resolveEx = null;
            try
            {
                resolveResp = await _client.ResolveToolSpecAsync(
                    new ToolSpecRequest
                    {
                        ToolName = toolName,
                        Version = version,
                        RawSpec = rawSpec,
                        RequestedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    },
                    cancellationToken: cancellationToken);
            }

            // 내가 넘겨받은 cancellationToken이 취소된 상태에서 발생한 예외는
            // 여기서 잡지 않는다 — 그대로 전파시켜서 SubmitCommand의
            // --connect-timeout/Ctrl-C 처리(그리고 GUI의 빌드 대체-취소 처리)가
            // 실제로 관측할 수 있게 한다. 예전엔 이것도 다른 RPC 실패와 똑같이
            // Failed 이벤트로 바뀌어서, 취소는 항상 구분 불가능한 "빌드
            // 실패"(exit 1)로만 보고됐다 — 회귀로 발견됨(외부 리뷰). 예외
            // 타입/RpcException 상태 코드로 "이게 취소였다"를 판단하지 않는다 —
            // 실제로 서버(가짜 테스트 서버 포함)가 취소를 항상
            // RpcException(Cancelled)로 깔끔하게 돌려주지 않고
            // StatusCode.Unknown 같은 형태로 보낼 수 있어(회귀 테스트로 확인),
            // "내 토큰이 취소됐는가"만이 유일하게 신뢰할 수 있는 신호다.
#pragma warning disable CA1031 // any non-cancellation failure (RPC error, etc.) must surface as a Failed event, not crash the caller
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                resolveEx = ex;
            }

            if (resolveEx != null)
            {
                yield return new BuildEvent
                {
                    Kind = BuildEventKind.Failed,
                    Message = BuildErrorMessages.Describe(resolveEx),
                };
                yield break;
            }

            yield return new BuildEvent
            {
                Kind = BuildEventKind.Log,
                Message = $"spec 해결 완료 (digest: {resolveResp!.ToolSpecDigest[..Math.Min(16, resolveResp.ToolSpecDigest.Length)]}...)",
            };

            // Step 2: SubmitToolBuild — 비동기 빌드를 큐에 넣는다.
            SubmitToolBuildResponse? submitResp = null;
            Exception? submitEx = null;
            try
            {
                submitResp = await _client.SubmitToolBuildAsync(
                    new SubmitToolBuildRequest
                    {
                        RequestId = Guid.NewGuid().ToString(),
                        ToolSpecDigest = resolveResp.ToolSpecDigest,
                    },
                    cancellationToken: cancellationToken);
            }

            // Step 1과 동일한 이유로, 내 토큰이 취소된 상태의 예외는 여기서도
            // 잡지 않고 전파시킨다.
#pragma warning disable CA1031 // any non-cancellation failure (RPC error, etc.) must surface as a Failed event, not crash the caller
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                submitEx = ex;
            }

            if (submitEx != null)
            {
                yield return new BuildEvent
                {
                    Kind = BuildEventKind.Failed,
                    Message = BuildErrorMessages.Describe(submitEx),
                };
                yield break;
            }

            yield return new BuildEvent
            {
                Kind = BuildEventKind.JobCreated,
                Message = $"빌드 제출됨 (build ID: {submitResp!.BuildId})",
                BuildId = submitResp.BuildId,
                Status = submitResp.Status,
            };

            // Step 3: WatchToolBuild — 빌드 상태 변화를 스트리밍한다.
            using var watchCall = _client.WatchToolBuild(
                new WatchToolBuildRequest { BuildId = submitResp.BuildId },
                cancellationToken: cancellationToken);

#pragma warning disable CA2007 // IAsyncEnumerable does not support ConfigureAwait directly
            while (await watchCall.ResponseStream.MoveNext(cancellationToken))
#pragma warning restore CA2007
            {
                yield return MapWatchEvent(watchCall.ResponseStream.Current);
            }
        }

        public async Task CancelBuildAsync(string buildId, CancellationToken cancellationToken = default)
        {
            await _client.CancelToolBuildAsync(
                new CancelToolBuildRequest { BuildId = buildId, Reason = "user cancelled (Ctrl-C)" },
                cancellationToken: cancellationToken);
        }

        internal static BuildEvent MapWatchEvent(Nodevault.V1.BuildEvent ev)
        {
            // WatchToolBuild은 모든 이벤트를 LOG 종류로 보낸다.
            // status 필드(buildstate.Status 그대로, PascalCase)로 terminal 상태를
            // 판별해 적절한 Kind로 변환한다.
            var kind = ev.Status switch
            {
                "Succeeded" => BuildEventKind.Succeeded,
                "Failed" => BuildEventKind.Failed,
                "Interrupted" => BuildEventKind.Failed,
                _ => MapProtoKind(ev.Kind),
            };

            return new BuildEvent
            {
                Kind = kind,
                Message = ev.Message,
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(ev.Timestamp).UtcDateTime,
                Digest = ev.Digest,
                BuildId = ev.BuildId,
                Status = ev.Status,
                ImageRef = ev.ImageRef,
                ImageDigest = ev.ImageDigest,
                SpecReferrerDigest = ev.SpecReferrerDigest,
                IntegrityHealth = ev.IntegrityHealth,
            };
        }

        private static BuildEventKind MapProtoKind(Nodevault.V1.BuildEventKind kind) => kind switch
        {
            Nodevault.V1.BuildEventKind.Log => BuildEventKind.Log,
            Nodevault.V1.BuildEventKind.JobCreated => BuildEventKind.JobCreated,
            Nodevault.V1.BuildEventKind.JobRunning => BuildEventKind.JobRunning,
            Nodevault.V1.BuildEventKind.PushSucceeded => BuildEventKind.RegistryPushSucceeded,
            Nodevault.V1.BuildEventKind.DigestAcquired => BuildEventKind.DigestAcquired,
            Nodevault.V1.BuildEventKind.Succeeded => BuildEventKind.Succeeded,
            Nodevault.V1.BuildEventKind.Failed => BuildEventKind.Failed,
            _ => BuildEventKind.Log,
        };
    }
}
