using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Nodevault.V1;

namespace NodeKit.Grpc
{
    /// <summary>NodeVault ToolRegistryService gRPC 클라이언트 구현.</summary>
    internal sealed class GrpcToolRegistryClient : IToolRegistryClient, IDisposable
    {
        private readonly GrpcChannel _channel;
        private readonly ToolRegistryService.ToolRegistryServiceClient _client;
        private bool _disposed;

        public GrpcToolRegistryClient(string nodeForgeAddress)
        {
            _channel = GrpcChannel.ForAddress(nodeForgeAddress);
            _client = new ToolRegistryService.ToolRegistryServiceClient(_channel);
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

        public async Task<IReadOnlyList<RegisteredTool>> ListToolsAsync(CancellationToken ct = default)
        {
            var resp = await _client.ListToolsAsync(new ListToolsRequest(), cancellationToken: ct)
                .ConfigureAwait(false);
            return resp.Tools.Select(ToRegisteredTool).ToList();
        }

        // RegisteredToolDefinition.display(14)는 NodeVault 912b23c에서 [deprecated]가 되었다.
        // 기존 label/category 표시 의미를 보존하기 위해 아래 두 call-site에서만 CS0612를 억제한다.
        // 제거 trigger: W4 ToolFunctionPresentation projection이 이 legacy display 읽기를 대체할 때.
        internal static RegisteredTool ToRegisteredTool(RegisteredToolDefinition t)
        {
#pragma warning disable CS0612 // legacy display 읽기 1/2 (label) — W4 projection 대체 시 제거
            var label = t.Display?.Label;
#pragma warning restore CS0612
            if (string.IsNullOrEmpty(label))
            {
                label = string.IsNullOrEmpty(t.Version)
                    ? t.ToolName
                    : $"{t.ToolName} {t.Version}";
            }

            return new RegisteredTool
            {
                CasHash = t.CasHash,
                ToolName = t.ToolName,
                Version = t.Version,
                StableRef = t.StableRef,
                ImageUri = t.ImageUri,
                Digest = t.Digest,
                DisplayLabel = label,
#pragma warning disable CS0612 // legacy display 읽기 2/2 (category) — W4 projection 대체 시 제거
                DisplayCategory = t.Display?.Category ?? string.Empty,
#pragma warning restore CS0612
                LifecyclePhase = t.LifecyclePhase,
                IntegrityHealth = t.IntegrityHealth,
                RegisteredAt = DateTimeOffset.FromUnixTimeSeconds(t.RegisteredAt),
            };
        }
    }
}
