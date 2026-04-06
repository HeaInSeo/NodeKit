namespace NodeKit.Policy
{
    /// <summary>
    /// OPA/DockGuard 정책 번들.
    /// NodeForge PolicyService에서 gRPC로 수신하거나 로컬 파일에서 로드한다.
    /// </summary>
    public class PolicyBundle
    {
        /// <summary>opa build로 생성된 .wasm 바이트.</summary>
        public byte[] WasmBytes { get; }

        /// <summary>번들 버전 또는 식별자 (로깅용).</summary>
        public string Version { get; }

        public PolicyBundle(byte[] wasmBytes, string version)
        {
            WasmBytes = wasmBytes;
            Version = version;
        }
    }
}
