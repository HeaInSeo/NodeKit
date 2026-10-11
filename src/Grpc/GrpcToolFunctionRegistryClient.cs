using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Nodevault.V1;

namespace NodeKit.Grpc
{
    /// <summary>
    /// <see cref="IToolFunctionRegistryClient"/>의 gRPC 구현. GrpcToolRegistryClient와 같은 channel/generated
    /// ToolRegistryService 패턴이지만 UI listing 변환은 쓰지 않는다.
    /// </summary>
    internal sealed class GrpcToolFunctionRegistryClient : IToolFunctionRegistryClient, IDisposable
    {
        private readonly GrpcChannel? _ownedChannel;
        private readonly ToolRegistryService.ToolRegistryServiceClient _client;
        private bool _disposed;

        public GrpcToolFunctionRegistryClient(string nodeVaultAddress)
        {
            _ownedChannel = GrpcChannel.ForAddress(nodeVaultAddress);
            _client = new ToolRegistryService.ToolRegistryServiceClient(_ownedChannel);
        }

        /// <summary>호출자가 소유한 channel을 쓴다(테스트의 in-process 서버 등). Dispose는 channel을 닫지 않는다.</summary>
        public GrpcToolFunctionRegistryClient(GrpcChannel channel)
        {
            ArgumentNullException.ThrowIfNull(channel);
            _client = new ToolRegistryService.ToolRegistryServiceClient(channel);
        }

        public async Task<RegisterToolFunctionResponse> RegisterToolFunctionAsync(RegisterToolFunctionRequest request, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return await _client.RegisterToolFunctionAsync(request, cancellationToken: ct).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _ownedChannel?.Dispose();
            _disposed = true;
        }
    }
}
