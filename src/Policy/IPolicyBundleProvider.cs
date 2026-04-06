using System.Threading.Tasks;

namespace NodeKit.Policy
{
    /// <summary>
    /// DockGuard 정책 번들을 어디서 가져올지 추상화.
    ///
    /// 스프린트 초기: LocalFilePolicyBundleProvider (로컬 .wasm 파일)
    /// NodeForge PolicyService 완성 후: GrpcPolicyBundleProvider로 교체
    /// </summary>
    public interface IPolicyBundleProvider
    {
        Task<PolicyBundle> GetLatestBundleAsync();
    }
}
