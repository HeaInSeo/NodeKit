using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Nodeforge.V1;

namespace NodeKit.Policy
{
    /// <summary>정책 목록 조회 결과.</summary>
    public sealed class PolicyListResult
    {
        public string BundleVersion { get; init; } = string.Empty;
        public IReadOnlyList<PolicyEntry> Policies { get; init; } = Array.Empty<PolicyEntry>();
    }

    /// <summary>단일 정책 규칙 메타데이터.</summary>
    public sealed class PolicyEntry
    {
        public string RuleId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
    }

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

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _channel.Dispose();
            _disposed = true;
        }

        public async Task<PolicyBundle> GetLatestBundleAsync()
        {
            var response = await _client.GetPolicyBundleAsync(new GetPolicyBundleRequest()).ConfigureAwait(false);
            var version = string.IsNullOrEmpty(response.Version)
                ? $"grpc:{DateTimeOffset.FromUnixTimeSeconds(response.BuiltAt):yyyy-MM-dd}"
                : response.Version;
            return new PolicyBundle(response.WasmBytes.ToByteArray(), version);
        }

        /// <summary>NodeForge PolicyService에서 현재 정책 목록과 버전을 조회한다.</summary>
        public async Task<PolicyListResult> ListPoliciesAsync()
        {
            var resp = await _client.ListPoliciesAsync(new ListPoliciesRequest()).ConfigureAwait(false);
            var entries = resp.Policies
                .Select(p => new PolicyEntry { RuleId = p.RuleId, Name = p.Name, Description = p.Description })
                .ToList();
            return new PolicyListResult { BundleVersion = resp.BundleVersion, Policies = entries };
        }
    }
}
