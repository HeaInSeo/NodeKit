namespace NodeKit.Grpc
{
    /// <summary>
    /// 첫(기반) 빌드 하나에서 관측한 값. 모두 같은 receipt에서 와야 한다:
    /// endpoint, request ID, build ID, C1(ToolSpecDigest), A(최종 기반 이미지 digest).
    /// </summary>
    internal sealed record ToolFunctionBaseBuildObservation(
        string? Endpoint,
        string? RequestId,
        string? BuildId,
        string? ToolSpecDigest,
        string? ImageDigest);
}
