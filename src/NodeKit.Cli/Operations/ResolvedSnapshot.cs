using System.Text.Json.Serialization;
using NodeKit.Grpc;

namespace NodeKit.Cli.Operations
{
    /// <summary>
    /// 실제 ResolveToolSpec 성공 반환을 그대로 고정한 불변 snapshot(S2-02-C01).
    /// 어떤 attempt(request_id·endpoint)가 어떤 source snapshot과 envelope로 요청했는지
    /// local binding을 함께 남긴다 — 같은 Recipe의 다른 attempt와 snapshot을 공유하지 않는다.
    /// 서버가 반환하지 않은 solver/runtime/provenance 값은 넣지 않는다.
    /// </summary>
    internal sealed record ResolvedSnapshot
    {
        public const string CurrentSchemaVersion = "nodekit.resolved-snapshot.v1";

        [JsonPropertyName("schema_version")]
        public required string SchemaVersion { get; init; }

        [JsonPropertyName("request_id")]
        public required string RequestId { get; init; }

        [JsonPropertyName("endpoint")]
        public required string Endpoint { get; init; }

        [JsonPropertyName("source_snapshot_sha256")]
        public required string SourceSnapshotSha256 { get; init; }

        [JsonPropertyName("envelope_sha256")]
        public required string EnvelopeSha256 { get; init; }

        [JsonPropertyName("requested_tool_name")]
        public required string RequestedToolName { get; init; }

        [JsonPropertyName("requested_version")]
        public required string RequestedVersion { get; init; }

        [JsonPropertyName("requested_at_unix_ms")]
        public long RequestedAtUnixMilliseconds { get; init; }

        [JsonPropertyName("tool_spec_digest")]
        public required string ToolSpecDigest { get; init; }

        [JsonPropertyName("resolved_tool_name")]
        public required string ResolvedToolName { get; init; }

        [JsonPropertyName("resolved_version")]
        public required string ResolvedVersion { get; init; }

        /// <summary>ResolvedToolSpecResponse.resolved_at — 서버 값 그대로, 형식 계약 없음.</summary>
        [JsonPropertyName("resolved_at")]
        public long ResolvedAt { get; init; }

        public static ResolvedSnapshot FromBasis(ToolSpecSubmitBasis basis, OperationReceipt receipt)
        {
            ArgumentNullException.ThrowIfNull(basis);
            ArgumentNullException.ThrowIfNull(receipt);
            return new ResolvedSnapshot
            {
                SchemaVersion = CurrentSchemaVersion,
                RequestId = receipt.RequestId,
                Endpoint = receipt.Endpoint,
                SourceSnapshotSha256 = receipt.SourceSnapshotSha256,
                EnvelopeSha256 = receipt.EnvelopeSha256,
                RequestedToolName = basis.RequestedToolName,
                RequestedVersion = basis.RequestedVersion,
                RequestedAtUnixMilliseconds = basis.RequestedAtUnixMilliseconds,
                ToolSpecDigest = basis.ToolSpecDigest,
                ResolvedToolName = basis.ResolvedToolName,
                ResolvedVersion = basis.ResolvedVersion,
                ResolvedAt = basis.ResolvedAt,
            };
        }
    }
}
