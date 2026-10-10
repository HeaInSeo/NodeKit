namespace NodeKit.Grpc
{
    /// <summary>
    /// SubmitToolBuild 직전에 확정되는 제출 근거 — 호출자가 정한 request ID와
    /// ResolveToolSpec 응답 전체(축약 로그가 아닌 원래 필드)를 함께 담는다.
    /// 호출자는 이 값을 durable 저장(journal/receipt)에 그대로 남기고, 실제
    /// SubmitToolBuildRequest.request_id는 항상 여기 RequestId와 같다.
    /// </summary>
    internal sealed class ToolSpecSubmitBasis
    {
        /// <summary>SubmitToolBuildRequest.request_id로 그대로 전송되는 값.</summary>
        public string RequestId { get; init; } = string.Empty;

        /// <summary>ResolveToolSpec에 보낸 tool_name.</summary>
        public string RequestedToolName { get; init; } = string.Empty;

        /// <summary>ResolveToolSpec에 보낸 version.</summary>
        public string RequestedVersion { get; init; } = string.Empty;

        /// <summary>ResolveToolSpec에 보낸 requested_at (Unix milliseconds).</summary>
        public long RequestedAtUnixMilliseconds { get; init; }

        /// <summary>서버가 확정한 전체 ToolSpec digest — SubmitToolBuildRequest.tool_spec_digest로 그대로 전송된다.</summary>
        public string ToolSpecDigest { get; init; } = string.Empty;

        /// <summary>ResolvedToolSpecResponse.tool_name.</summary>
        public string ResolvedToolName { get; init; } = string.Empty;

        /// <summary>ResolvedToolSpecResponse.version.</summary>
        public string ResolvedVersion { get; init; } = string.Empty;

        /// <summary>ResolvedToolSpecResponse.resolved_at (서버 값 그대로, 형식 계약 없음).</summary>
        public long ResolvedAt { get; init; }
    }
}
