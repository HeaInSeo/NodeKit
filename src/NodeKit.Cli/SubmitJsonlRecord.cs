using System.Text.Json.Serialization;

namespace NodeKit.Cli
{
    /// <summary>
    /// `nodekit submit --format jsonl`의 stdout 레코드 — 한 줄에 독립적으로
    /// parse 가능한 JSON 객체 하나(NDJSON). Issue #82 결정 사항: 마지막
    /// 레코드는 성공/실패/timeout/취소/terminal event 없는 종료 전부
    /// "completed"로 통일한다(별도 "error" type 없음). build_id는 모든
    /// 레코드에서 optional — CONNECT_TIMEOUT처럼 build ID를 받기 전에
    /// 끝나는 실패도 있다.
    /// </summary>
    internal sealed record SubmitJsonlRecord
    {
        [JsonPropertyName("schema_version")]
        public string SchemaVersion { get; init; } = "nodekit.submit.v1";

        [JsonPropertyName("type")]
        public required string Type { get; init; }

        [JsonPropertyName("build_id")]
        public string? BuildId { get; init; }

        [JsonPropertyName("state")]
        public string? State { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("error_code")]
        public string? ErrorCode { get; init; }

        // "watch" (WatchToolBuild 관찰 단계에서 실패 확인) 또는 "pre_watch"
        // (ResolveToolSpec/SubmitToolBuild 단계 — build ID를 아직 못 받은
        // 상태에서 실패). 외부 리뷰 지적: build ID가 없다고 원격 빌드가
        // 시작되지 않았다고 100% 단정할 수 없다(SubmitToolBuild 응답이
        // 유실됐을 뿐 서버는 이미 빌드를 시작했을 수 있음) — 그래서 이
        // 필드는 "우리가 어느 단계에서 실패를 확인했는지"만 말하고,
        // remote_build_state가 실제 원격 상태에 대한 불확실성을 담당한다.
        [JsonPropertyName("phase")]
        public string? Phase { get; init; }

        // "failed"(watch에서 terminal Failed를 실제로 수신 — 확정) 또는
        // "unknown"(pre_watch 실패 — 원격 빌드가 실제로 생성됐는지 여부를
        // 알 수 없음, 확인/재시도는 idempotency key로 상태를 조회해야 함).
        [JsonPropertyName("remote_build_state")]
        public string? RemoteBuildState { get; init; }

        [JsonPropertyName("message")]
        public string? Message { get; init; }

        // ResolveToolSpec이 확정한 전체 ToolSpec digest — image_digest(빌드된
        // image)와 다른 값이다. spec 해결 state 레코드와 성공 completed
        // 레코드에 실린다. function-recipe create --tool-spec-digest 입력용.
        // 추가 optional 필드(하위 호환).
        [JsonPropertyName("tool_spec_digest")]
        public string? ToolSpecDigest { get; init; }

        [JsonPropertyName("image_ref")]
        public string? ImageRef { get; init; }

        [JsonPropertyName("image_digest")]
        public string? ImageDigest { get; init; }

        [JsonPropertyName("integrity_health")]
        public string? IntegrityHealth { get; init; }

        // V14 recovery-disposition — terminal completed 레코드에만 실린다.
        // "none"/"terminal"/"uncertain" 중 하나. RecoveryDisposition enum 참조.
        // 추가 optional 필드(하위 호환) — submitted/state 레코드에는 없다.
        [JsonPropertyName("recovery")]
        public string? Recovery { get; init; }

        // P08.registry(S3-07) typed 등록 결과 — 모두 추가 optional 필드(하위 호환). 서버가 돌려준 값
        // 그대로이며 client가 계산하지 않는다. cas_hash(H)가 최종 pin이고 tool_function_digest(F)는 pin이 아니다.
        [JsonPropertyName("registration_request_id")]
        public string? RegistrationRequestId { get; init; }

        [JsonPropertyName("tool_function_digest")]
        public string? ToolFunctionDigest { get; init; }

        [JsonPropertyName("cas_hash")]
        public string? CasHash { get; init; }

        [JsonPropertyName("presentation_revision_id")]
        public string? PresentationRevisionId { get; init; }

        [JsonPropertyName("canonicalization_version")]
        public string? CanonicalizationVersion { get; init; }

        // "registered"(확정) / "rejected"(서버 확정 거부) / "unknown"(보냈지만 결과 불명 — 같은 요청 replay로 확인) /
        // "not_submitted"(RPC 전 실패).
        [JsonPropertyName("remote_registration_state")]
        public string? RemoteRegistrationState { get; init; }

        internal static SubmitJsonlRecord Submitted(string buildId) =>
            new() { Type = "submitted", BuildId = buildId };

        internal static SubmitJsonlRecord ProgressState(
            string? buildId,
            string? state,
            string? message = null,
            string? imageRef = null,
            string? imageDigest = null,
            string? integrityHealth = null,
            string? toolSpecDigest = null) =>
            new()
            {
                Type = "state",
                BuildId = string.IsNullOrEmpty(buildId) ? null : buildId,
                State = string.IsNullOrEmpty(state) ? null : state,
                Message = string.IsNullOrEmpty(message) ? null : message,
                ToolSpecDigest = string.IsNullOrEmpty(toolSpecDigest) ? null : toolSpecDigest,
                ImageRef = string.IsNullOrEmpty(imageRef) ? null : imageRef,
                ImageDigest = string.IsNullOrEmpty(imageDigest) ? null : imageDigest,
                IntegrityHealth = string.IsNullOrEmpty(integrityHealth) ? null : integrityHealth,
            };

        internal static SubmitJsonlRecord Completed(
            string status,
            string? buildId = null,
            string? errorCode = null,
            string? message = null,
            string? imageRef = null,
            string? imageDigest = null,
            string? integrityHealth = null,
            string? phase = null,
            string? remoteBuildState = null,
            RecoveryDisposition? recovery = null,
            string? toolSpecDigest = null) =>
            new()
            {
                Type = "completed",
                Status = status,
                BuildId = string.IsNullOrEmpty(buildId) ? null : buildId,
                ErrorCode = errorCode,
                Message = string.IsNullOrEmpty(message) ? null : message,
                ToolSpecDigest = string.IsNullOrEmpty(toolSpecDigest) ? null : toolSpecDigest,
                ImageRef = string.IsNullOrEmpty(imageRef) ? null : imageRef,
                ImageDigest = string.IsNullOrEmpty(imageDigest) ? null : imageDigest,
                IntegrityHealth = string.IsNullOrEmpty(integrityHealth) ? null : integrityHealth,
                Phase = phase,
                RemoteBuildState = remoteBuildState,
                Recovery = recovery is null
                    ? null
                    : recovery.Value switch
                    {
                        RecoveryDisposition.None => "none",
                        RecoveryDisposition.Terminal => "terminal",
                        RecoveryDisposition.Uncertain => "uncertain",
                        _ => throw new System.ArgumentOutOfRangeException(nameof(recovery), recovery, null),
                    },
            };
    }
}
