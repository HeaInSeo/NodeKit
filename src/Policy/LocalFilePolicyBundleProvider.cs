using System.IO;
using System.Threading.Tasks;

namespace NodeKit.Policy
{
    /// <summary>
    /// 로컬 .wasm 파일에서 정책 번들을 로드한다.
    /// 스프린트 초기 구현 — NodeForge PolicyService 완성 후 GrpcPolicyBundleProvider로 교체.
    /// </summary>
    public class LocalFilePolicyBundleProvider : IPolicyBundleProvider
    {
        private readonly string _wasmPath;

        public LocalFilePolicyBundleProvider(string wasmPath)
        {
            _wasmPath = wasmPath;
        }

        public async Task<PolicyBundle> GetLatestBundleAsync()
        {
            var bytes = await File.ReadAllBytesAsync(_wasmPath).ConfigureAwait(false);
            var version = $"local:{Path.GetFileName(_wasmPath)}";
            return new PolicyBundle(bytes, version);
        }
    }
}
