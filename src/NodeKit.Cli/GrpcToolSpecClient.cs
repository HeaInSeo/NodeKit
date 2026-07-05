using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Nodevault.V1;
using GrpcBuildEvent = NodeKit.Grpc.BuildEvent;
using GrpcBuildEventKind = NodeKit.Grpc.BuildEventKind;

namespace NodeKit.Cli
{
    /// <summary>
    /// NodeVault 신규 빌드 경로 클라이언트:
    /// ResolveToolSpec → SubmitToolBuild → WatchToolBuild.
    /// 활성화: NODEKIT_NODEVAULT_URL 환경변수 또는 --url 옵션.
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

        public async IAsyncEnumerable<GrpcBuildEvent> ResolveAndBuildAsync(
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
            catch (Exception ex)
            {
                resolveEx = ex;
            }

            if (resolveEx != null)
            {
                yield return new GrpcBuildEvent
                {
                    Kind = GrpcBuildEventKind.Failed,
                    Message = NodeKit.Grpc.BuildErrorMessages.Describe(resolveEx),
                };
                yield break;
            }

            yield return new GrpcBuildEvent
            {
                Kind = GrpcBuildEventKind.Log,
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
            catch (Exception ex)
            {
                submitEx = ex;
            }

            if (submitEx != null)
            {
                yield return new GrpcBuildEvent
                {
                    Kind = GrpcBuildEventKind.Failed,
                    Message = NodeKit.Grpc.BuildErrorMessages.Describe(submitEx),
                };
                yield break;
            }

            yield return new GrpcBuildEvent
            {
                Kind = GrpcBuildEventKind.JobCreated,
                Message = $"빌드 제출됨 (build ID: {submitResp!.BuildId})",
                BuildId = submitResp.BuildId,
                Status = submitResp.Status,
            };

            // Step 3: WatchToolBuild — 빌드 상태 변화를 스트리밍한다.
            using var watchCall = _client.WatchToolBuild(
                new WatchToolBuildRequest { BuildId = submitResp.BuildId },
                cancellationToken: cancellationToken);

            while (await watchCall.ResponseStream.MoveNext(cancellationToken))
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

        internal static GrpcBuildEvent MapWatchEvent(BuildEvent ev)
        {
            // WatchToolBuild은 모든 이벤트를 LOG 종류로 보낸다.
            // status 필드(buildstate.Status 그대로, PascalCase)로 terminal 상태를
            // 판별해 적절한 Kind로 변환한다.
            var kind = ev.Status switch
            {
                "Succeeded" => GrpcBuildEventKind.Succeeded,
                "Failed" => GrpcBuildEventKind.Failed,
                "Interrupted" => GrpcBuildEventKind.Failed,
                _ => MapProtoKind(ev.Kind),
            };

            return new GrpcBuildEvent
            {
                Kind = kind,
                Message = ev.Message,
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(ev.Timestamp).UtcDateTime,
                Digest = ev.Digest,
                BuildId = ev.BuildId,
                Status = ev.Status,
            };
        }

        private static GrpcBuildEventKind MapProtoKind(Nodevault.V1.BuildEventKind kind) => kind switch
        {
            Nodevault.V1.BuildEventKind.Log => GrpcBuildEventKind.Log,
            Nodevault.V1.BuildEventKind.JobCreated => GrpcBuildEventKind.JobCreated,
            Nodevault.V1.BuildEventKind.JobRunning => GrpcBuildEventKind.JobRunning,
            Nodevault.V1.BuildEventKind.PushSucceeded => GrpcBuildEventKind.RegistryPushSucceeded,
            Nodevault.V1.BuildEventKind.DigestAcquired => GrpcBuildEventKind.DigestAcquired,
            Nodevault.V1.BuildEventKind.Succeeded => GrpcBuildEventKind.Succeeded,
            Nodevault.V1.BuildEventKind.Failed => GrpcBuildEventKind.Failed,
            _ => GrpcBuildEventKind.Log,
        };
    }
}
