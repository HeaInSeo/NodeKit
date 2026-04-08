using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Grpc.Core;
using Grpc.Net.Client;
using Nodeforge.V1;

namespace NodeKit.Grpc
{
    /// <summary>
    /// NodeForge BuildService gRPC 클라이언트 구현.
    /// BuildRequest를 전송하고 빌드 이벤트 스트림을 IAsyncEnumerable로 노출한다.
    /// </summary>
    public sealed class GrpcBuildClient : IBuildClient, IDisposable
    {
        private readonly GrpcChannel _channel;
        private readonly BuildService.BuildServiceClient _client;
        private bool _disposed;

        public GrpcBuildClient(string nodeForgeAddress)
        {
            _channel = GrpcChannel.ForAddress(nodeForgeAddress);
            _client = new BuildService.BuildServiceClient(_channel);
        }

        public async IAsyncEnumerable<BuildEvent> BuildAndRegisterAsync(
            BuildRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var grpcRequest = ToProto(request);
            using var call = _client.BuildAndRegister(grpcRequest, cancellationToken: cancellationToken);

            await foreach (var ev in call.ResponseStream.ReadAllAsync(cancellationToken))
            {
                yield return FromProto(ev);
            }
        }

        private static Nodeforge.V1.BuildRequest ToProto(BuildRequest r)
        {
            var proto = new Nodeforge.V1.BuildRequest
            {
                RequestId = r.RequestId,
                ToolDefinitionId = r.ToolDefinitionId.ToString(),
                ToolName = r.ToolName,
                ImageUri = r.ImageUri,
                DockerfileContent = r.DockerfileContent,
                Script = r.Script,
            };
            proto.InputNames.AddRange(r.InputNames);
            proto.OutputNames.AddRange(r.OutputNames);
            return proto;
        }

        private static BuildEvent FromProto(Nodeforge.V1.BuildEvent ev)
        {
            return new BuildEvent
            {
                Kind = MapKind(ev.Kind),
                Message = ev.Message,
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(ev.Timestamp).UtcDateTime,
                Digest = ev.Digest,
            };
        }

        private static BuildEventKind MapKind(Nodeforge.V1.BuildEventKind kind) => kind switch
        {
            Nodeforge.V1.BuildEventKind.BuildEventKindLog => BuildEventKind.Log,
            Nodeforge.V1.BuildEventKind.BuildEventKindJobCreated => BuildEventKind.JobCreated,
            Nodeforge.V1.BuildEventKind.BuildEventKindJobRunning => BuildEventKind.JobRunning,
            Nodeforge.V1.BuildEventKind.BuildEventKindPushSucceeded => BuildEventKind.RegistryPushSucceeded,
            Nodeforge.V1.BuildEventKind.BuildEventKindDigestAcquired => BuildEventKind.DigestAcquired,
            Nodeforge.V1.BuildEventKind.BuildEventKindSucceeded => BuildEventKind.Succeeded,
            Nodeforge.V1.BuildEventKind.BuildEventKindFailed => BuildEventKind.Failed,
            _ => BuildEventKind.Log,
        };

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _channel.Dispose();
            _disposed = true;
        }
    }
}
