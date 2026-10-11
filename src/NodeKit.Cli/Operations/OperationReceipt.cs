using System.Text.Json.Serialization;

namespace NodeKit.Cli.Operations
{
    /// <summary>
    /// operation 진행 단계. 순서는 prepared → submit_in_flight → acknowledged → terminal이며
    /// 뒤로 돌아가지 않는다. submit_in_flight에서 build_id가 없으면 원격 상태는 unknown이다.
    /// </summary>
    internal static class OperationPhase
    {
        /// <summary>요청 ID·envelope·source snapshot이 저장됐고 ResolveToolSpec 전이다.</summary>
        public const string Prepared = "prepared";

        /// <summary>resolved snapshot이 저장됐고 SubmitToolBuild를 보냈거나 보내려는 중이다.</summary>
        public const string SubmitInFlight = "submit_in_flight";

        /// <summary>SubmitToolBuild가 build_id를 돌려줬다.</summary>
        public const string Acknowledged = "acknowledged";

        /// <summary>WatchToolBuild로 terminal 상태를 관측했다.</summary>
        public const string Terminal = "terminal";

        public static int Rank(string phase) => phase switch
        {
            Prepared => 0,
            SubmitInFlight => 1,
            Acknowledged => 2,
            Terminal => 3,
            _ => -1,
        };
    }

    /// <summary>
    /// mutable operation receipt(S2-02). request_id·endpoint·operation_kind·envelope·
    /// source snapshot은 생성 후 바뀌지 않는다. phase·resolved snapshot·build_id·마지막
    /// 관측만 앞으로 갱신된다. 불변 snapshot은 hash로 참조만 한다.
    /// </summary>
    internal sealed record OperationReceipt
    {
        public const string CurrentSchemaVersion = "nodekit.operation.v1";

        public const string ToolSpecBuildKind = "toolspec_build";

        [JsonPropertyName("schema_version")]
        public required string SchemaVersion { get; init; }

        [JsonPropertyName("operation_kind")]
        public required string OperationKind { get; init; }

        [JsonPropertyName("request_id")]
        public required string RequestId { get; init; }

        [JsonPropertyName("endpoint")]
        public required string Endpoint { get; init; }

        [JsonPropertyName("phase")]
        public required string Phase { get; init; }

        [JsonPropertyName("envelope")]
        public required OperationEnvelope Envelope { get; init; }

        [JsonPropertyName("envelope_sha256")]
        public required string EnvelopeSha256 { get; init; }

        [JsonPropertyName("source_snapshot_sha256")]
        public required string SourceSnapshotSha256 { get; init; }

        [JsonPropertyName("resolved_snapshot_sha256")]
        public string? ResolvedSnapshotSha256 { get; init; }

        [JsonPropertyName("build_id")]
        public string? BuildId { get; init; }

        [JsonPropertyName("last_observation")]
        public OperationObservation? LastObservation { get; init; }

        /// <summary>
        /// 이 CLI가 terminal 관측 전에 로컬에서 실행을 멈춘 마지막 기록(S2-04). 원격 결과는 알 수 없음으로만
        /// 남기고 서버 성공/취소를 확정하지 않는다. terminal에는 둘 수 없다 — 이후 terminal 관측이 대신한다.
        /// </summary>
        [JsonPropertyName("local_abort")]
        public OperationLocalAbort? LocalAbort { get; init; }
    }

    /// <summary>
    /// 로컬 중단 기록. 사용자 취소·timeout으로 CLI가 관찰/요청을 멈췄다는 사실만 담는다.
    /// 원격 빌드 상태는 관측하지 않았으므로 항상 unknown이다.
    /// </summary>
    internal sealed record OperationLocalAbort
    {
        public const string UserCancel = "user_cancel";

        public const string ConnectTimeout = "connect_timeout";

        public const string WatchTimeout = "watch_timeout";

        /// <summary>CLI가 요청하지 않았지만 전송 계층이 취소로 끝낸 경우.</summary>
        public const string TransportCancelled = "transport_cancelled";

        public const string UnknownRemoteState = "unknown";

        public static bool IsKnownReason(string? reason) =>
            reason is UserCancel or ConnectTimeout or WatchTimeout or TransportCancelled;

        [JsonPropertyName("reason")]
        public required string Reason { get; init; }

        [JsonPropertyName("remote_build_state")]
        public required string RemoteBuildState { get; init; }

        [JsonPropertyName("recorded_at")]
        public required string RecordedAt { get; init; }
    }

    /// <summary>
    /// WatchToolBuild에서 실제로 받은 마지막 관측. 서버가 보낸 값만 담고
    /// 관측하지 않은 원격 상태를 추정해 채우지 않는다.
    /// </summary>
    internal sealed record OperationObservation
    {
        public const string SucceededOutcome = "Succeeded";

        public const string FailedOutcome = "Failed";

        public static bool IsTerminalOutcome(string? outcome) =>
            outcome is SucceededOutcome or FailedOutcome;

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        /// <summary>
        /// terminal watch 이벤트 종류에서 정한 결과(Succeeded/Failed). 서버 status는 Interrupted처럼
        /// 다른 terminal 값일 수 있으므로 따로 저장한다. terminal 관측에만 있다.
        /// </summary>
        [JsonPropertyName("outcome")]
        public string? Outcome { get; init; }

        [JsonPropertyName("message")]
        public string? Message { get; init; }

        [JsonPropertyName("image_ref")]
        public string? ImageRef { get; init; }

        [JsonPropertyName("image_digest")]
        public string? ImageDigest { get; init; }

        [JsonPropertyName("integrity_health")]
        public string? IntegrityHealth { get; init; }

        /// <summary>NodeVault가 보고한 ToolSpec referrer artifact digest.</summary>
        [JsonPropertyName("spec_referrer_digest")]
        public string? SpecReferrerDigest { get; init; }

        [JsonPropertyName("observed_at")]
        public required string ObservedAt { get; init; }
    }
}
