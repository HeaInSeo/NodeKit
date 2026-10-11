namespace NodeKit.Grpc
{
    /// <summary>C1/A 쌍의 local 연결 근거. 어느 값도 서버 권위 검증이 아니다.</summary>
    internal enum ToolFunctionBaseLinkState
    {
        /// <summary>수동·혼합·override·불완전하거나 receipt 없이 옮겨진 C1/A.</summary>
        ManualUnverified,

        /// <summary>C1과 A가 같은 첫 빌드의 완전한 receipt 관측과 정확히 일치.</summary>
        ObservedSameBuild,
    }
}
