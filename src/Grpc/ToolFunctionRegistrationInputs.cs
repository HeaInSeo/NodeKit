namespace NodeKit.Grpc
{
    /// <summary>
    /// typed 등록 하나에 쓰는 역할별 digest와 요청 ID. 역할을 섞지 않는다:
    /// C1 = 기반 ToolSpec의 ToolSpecDigest(bare 64 lower hex) → base_tool_spec_digest,
    /// B = 두 번째(function) 이미지 digest → image_digest, C2 = 두 번째 빌드의
    /// resolved request 식별자 — 등록 wire에 보내지 않고 second request basis로만 보존한다.
    /// A(기반 이미지)는 raw_spec 경로이며 여기 없다.
    /// </summary>
    internal sealed record ToolFunctionRegistrationInputs(
        string RequestId,
        string? ToolSpecDigest,
        string? FunctionImageDigest,
        string? SecondRequestBasis);
}
