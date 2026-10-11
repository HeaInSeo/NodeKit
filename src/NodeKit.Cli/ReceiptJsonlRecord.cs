using System.Text.Json;
using System.Text.Json.Serialization;

namespace NodeKit.Cli
{
    /// <summary>
    /// `nodekit receipt watch|cancel &lt;receipt.json&gt; --format jsonl`의 stdout 레코드
    /// (S2-05 CAD-NK-P05RESULT-S2-JSONL-20261011-r1). submit stream(nodekit.submit.v1)과 다른
    /// schema다 — receipt 명령은 새로 제출하지 않으므로 submitted 레코드가 없고, 관찰 중단·취소
    /// 요청처럼 원격 terminal이 아닌 결과가 있다. 한 줄에 독립 JSON 하나이며 마지막 레코드는 항상
    /// completed다. status는 서버가 실제로 보낸 상태(state 진행 상태 또는 관측한 Succeeded/Failed
    /// terminal)에만 싣는다 — 관찰 중단·취소 요청·로컬 거절에는 원격 terminal 이름을 만들지 않는다.
    /// </summary>
    internal sealed record ReceiptJsonlRecord
    {
        public const string WatchCommand = "watch";

        public const string CancelCommand = "cancel";

        private static readonly JsonSerializerOptions _jsonlOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        [JsonPropertyName("schema_version")]
        public string SchemaVersion { get; init; } = "nodekit.receipt.v1";

        [JsonPropertyName("command")]
        public required string Command { get; init; }

        [JsonPropertyName("type")]
        public required string Type { get; init; }

        [JsonPropertyName("build_id")]
        public string? BuildId { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("error_code")]
        public string? ErrorCode { get; init; }

        // "failed"(Failed terminal을 실제로 관측) 또는 "unknown"(원격 상태를 확인하지 못함).
        // 관측한 Succeeded와 원격을 건드리지 않은 로컬 거절에는 싣지 않는다.
        [JsonPropertyName("remote_build_state")]
        public string? RemoteBuildState { get; init; }

        [JsonPropertyName("message")]
        public string? Message { get; init; }

        [JsonPropertyName("image_ref")]
        public string? ImageRef { get; init; }

        [JsonPropertyName("image_digest")]
        public string? ImageDigest { get; init; }

        [JsonPropertyName("integrity_health")]
        public string? IntegrityHealth { get; init; }

        // completed 레코드에만 실린다 — 프로세스 종료 코드와 같은 값.
        [JsonPropertyName("exit_code")]
        public int? ExitCode { get; init; }

        // completed 레코드에만 실린다. "none"/"terminal"/"uncertain" — submit과 같은 의미(V14).
        [JsonPropertyName("recovery")]
        public string? Recovery { get; init; }

        internal static ReceiptJsonlRecord State(
            string? buildId,
            string? status,
            string? message = null,
            string? imageRef = null,
            string? imageDigest = null,
            string? integrityHealth = null) =>
            new()
            {
                Command = WatchCommand,
                Type = "state",
                BuildId = NullIfEmpty(buildId),
                Status = NullIfEmpty(status),
                Message = NullIfEmpty(message),
                ImageRef = NullIfEmpty(imageRef),
                ImageDigest = NullIfEmpty(imageDigest),
                IntegrityHealth = NullIfEmpty(integrityHealth),
            };

        internal static ReceiptJsonlRecord Completed(
            string command,
            int exitCode,
            RecoveryDisposition recovery,
            string? buildId = null,
            string? status = null,
            string? errorCode = null,
            string? remoteBuildState = null,
            string? message = null,
            string? imageRef = null,
            string? imageDigest = null,
            string? integrityHealth = null) =>
            new()
            {
                Command = command,
                Type = "completed",
                BuildId = NullIfEmpty(buildId),
                Status = NullIfEmpty(status),
                ErrorCode = errorCode,
                RemoteBuildState = remoteBuildState,
                Message = NullIfEmpty(message),
                ImageRef = NullIfEmpty(imageRef),
                ImageDigest = NullIfEmpty(imageDigest),
                IntegrityHealth = NullIfEmpty(integrityHealth),
                ExitCode = exitCode,
                Recovery = recovery switch
                {
                    RecoveryDisposition.None => "none",
                    RecoveryDisposition.Terminal => "terminal",
                    RecoveryDisposition.Uncertain => "uncertain",
                    _ => throw new System.ArgumentOutOfRangeException(nameof(recovery), recovery, null),
                },
            };

        internal void WriteTo(TextWriter stdout) =>
            stdout.WriteLine(JsonSerializer.Serialize(this, _jsonlOptions));

        private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
    }
}
