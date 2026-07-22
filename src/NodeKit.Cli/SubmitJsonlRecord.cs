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

        [JsonPropertyName("message")]
        public string? Message { get; init; }

        [JsonPropertyName("image_ref")]
        public string? ImageRef { get; init; }

        [JsonPropertyName("image_digest")]
        public string? ImageDigest { get; init; }

        [JsonPropertyName("integrity_health")]
        public string? IntegrityHealth { get; init; }

        internal static SubmitJsonlRecord Submitted(string buildId) =>
            new() { Type = "submitted", BuildId = buildId };

        internal static SubmitJsonlRecord ProgressState(
            string? buildId,
            string? state,
            string? message = null,
            string? imageRef = null,
            string? imageDigest = null,
            string? integrityHealth = null) =>
            new()
            {
                Type = "state",
                BuildId = buildId,
                State = string.IsNullOrEmpty(state) ? null : state,
                Message = string.IsNullOrEmpty(message) ? null : message,
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
            string? integrityHealth = null) =>
            new()
            {
                Type = "completed",
                Status = status,
                BuildId = buildId,
                ErrorCode = errorCode,
                Message = string.IsNullOrEmpty(message) ? null : message,
                ImageRef = string.IsNullOrEmpty(imageRef) ? null : imageRef,
                ImageDigest = string.IsNullOrEmpty(imageDigest) ? null : imageDigest,
                IntegrityHealth = string.IsNullOrEmpty(integrityHealth) ? null : integrityHealth,
            };
    }
}
