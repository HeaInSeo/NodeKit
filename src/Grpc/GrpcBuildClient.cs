using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using Grpc.Core;
using Grpc.Net.Client;
using Nodeforge.V1;
using NodeKit.Authoring;

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

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _channel.Dispose();
            _disposed = true;
        }

        public async IAsyncEnumerable<BuildEvent> BuildAndRegisterAsync(
            BuildRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var grpcRequest = ToProto(request);
            using var call = _client.BuildAndRegister(grpcRequest, cancellationToken: cancellationToken);

#pragma warning disable CA2007 // IAsyncEnumerable does not support ConfigureAwait directly
            await foreach (var ev in call.ResponseStream.ReadAllAsync(cancellationToken))
#pragma warning restore CA2007
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
                Version = r.Version,
                ImageUri = r.ImageUri,
                DockerfileContent = r.DockerfileContent,
                Script = r.Script,
                EnvironmentSpec = r.EnvironmentSpec,
                Display = new DisplaySpec
                {
                    Label = r.DisplayLabel,
                    Description = r.DisplayDescription,
                    Category = r.DisplayCategory,
                },
            };

            proto.Display.Tags.AddRange(r.DisplayTags);
            proto.Inputs.AddRange(r.Inputs.Select(ToPortSpec));
            proto.Outputs.AddRange(r.Outputs.Select(ToPortSpec));
            if (r.Command.Count > 0)
            {
                proto.Command = JsonSerializer.Serialize(r.Command);
            }

            return proto;
        }

        private static PortSpec ToPortSpec(ToolInput i) => new()
        {
            Name = i.Name,
            Role = i.Role,
            Format = i.Format,
            Shape = i.Shape,
            Required = i.Required,
        };

        private static PortSpec ToPortSpec(ToolOutput o) => new()
        {
            Name = o.Name,
            Role = o.Role,
            Format = o.Format,
            Shape = o.Shape,
            Class = o.Class,
        };

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
            Nodeforge.V1.BuildEventKind.Log => BuildEventKind.Log,
            Nodeforge.V1.BuildEventKind.JobCreated => BuildEventKind.JobCreated,
            Nodeforge.V1.BuildEventKind.JobRunning => BuildEventKind.JobRunning,
            Nodeforge.V1.BuildEventKind.PushSucceeded => BuildEventKind.RegistryPushSucceeded,
            Nodeforge.V1.BuildEventKind.DigestAcquired => BuildEventKind.DigestAcquired,
            Nodeforge.V1.BuildEventKind.Succeeded => BuildEventKind.Succeeded,
            Nodeforge.V1.BuildEventKind.Failed => BuildEventKind.Failed,
            _ => BuildEventKind.Log,
        };
    }
}
