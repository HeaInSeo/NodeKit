namespace NodeKit.Policy
{
    /// <summary>
    /// DockGuard 정책 검사 추상화.
    /// WasmPolicyChecker: bundle 로드 후 Wasmtime으로 실행.
    /// </summary>
    public interface IPolicyChecker
    {
        /// <summary>Dockerfile 내용을 DockGuard 정책으로 검사한다.</summary>
        PolicyResult Check(string dockerfileContent);
    }
}
