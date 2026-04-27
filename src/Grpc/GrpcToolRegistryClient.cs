using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Nodevault.V1;

namespace NodeKit.Grpc
{
    /// <summary>NodeKit 내부 표현의 등록된 툴 정보.</summary>
    public sealed class RegisteredTool
    {
        public string CasHash { get; init; } = string.Empty;
        public string ToolName { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public string StableRef { get; init; } = string.Empty;
        public string ImageUri { get; init; } = string.Empty;
        public string Digest { get; init; } = string.Empty;
        public string DisplayLabel { get; init; } = string.Empty;
        public string DisplayCategory { get; init; } = string.Empty;
        /// <summary>
        /// 운영 의도 축. "Pending" | "Active" | "Retracted" | "Deleted".
        /// NodeVault 명시적 호출만 변경.
        /// </summary>
        public string LifecyclePhase { get; init; } = string.Empty;
        /// <summary>
        /// Harbor 정합성 관찰 축. "Healthy" | "Partial" | "Missing" | "Unreachable" | "Orphaned".
        /// reconcile loop만 변경. Catalog 노출 결정에는 영향 없음.
        /// </summary>
        public string IntegrityHealth { get; init; } = string.Empty;
        public DateTimeOffset RegisteredAt { get; init; }
    }

    /// <summary>NodeForge ToolRegistryService 클라이언트 추상화.</summary>
    public interface IToolRegistryClient
    {
        Task<IReadOnlyList<RegisteredTool>> ListToolsAsync(CancellationToken ct = default);
    }

    /// <summary>NodeForge ToolRegistryService gRPC 클라이언트 구현.</summary>
    public sealed class GrpcToolRegistryClient : IToolRegistryClient, IDisposable
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

        private static RegisteredTool ToRegisteredTool(RegisteredToolDefinition t)
        {
            var label = t.Display?.Label;
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
                DisplayCategory = t.Display?.Category ?? string.Empty,
                LifecyclePhase = t.LifecyclePhase,
                IntegrityHealth = t.IntegrityHealth,
                RegisteredAt = DateTimeOffset.FromUnixTimeSeconds(t.RegisteredAt),
            };
        }
    }
}
