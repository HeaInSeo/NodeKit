using System;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Nodeforge.V1;

namespace NodeKit.Policy
{
    /// <summary>
    /// NodeForge PolicyService gRPC로부터 DockGuard 번들을 가져온다.
    /// LocalFilePolicyBundleProvider의 런타임 교체 대상.
    /// </summary>
    public sealed class GrpcPolicyBundleProvider : IPolicyBundleProvider, IDisposable
    {
        private readonly GrpcChannel _channel;
        private readonly PolicyService.PolicyServiceClient _client;
        private bool _disposed;

        public GrpcPolicyBundleProvider(string nodeForgeAddress)
        {
            _channel = GrpcChannel.ForAddress(nodeForgeAddress);
            _client = new PolicyService.PolicyServiceClient(_channel);
        }

        public async Task<PolicyBundle> GetLatestBundleAsync()
        {
            var response = await _client.GetPolicyBundleAsync(new GetPolicyBundleRequest()).ConfigureAwait(false);
            var version = string.IsNullOrEmpty(response.Version)
                ? $"grpc:{DateTimeOffset.FromUnixTimeSeconds(response.BuiltAt):yyyy-MM-dd}"
                : response.Version;
            return new PolicyBundle(response.WasmBytes.ToByteArray(), version);
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
    }
}
