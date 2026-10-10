using System.Text.Json.Serialization;

namespace NodeKit.Cli.Operations
{
    /// <summary>
    /// 한 operation이 서버로 보내는 정확한 요청 내용(S2-02). receipt에 그대로
    /// 남고 이후 어떤 갱신에서도 바뀌지 않는다 — 재진입/재실행은 원본 Recipe를
    /// 다시 읽거나 렌더하지 않고 이 값만 쓴다.
    /// </summary>
    internal sealed record OperationEnvelope
    {
        [JsonPropertyName("tool_name")]
        public required string ToolName { get; init; }

        [JsonPropertyName("version")]
        public required string Version { get; init; }

        /// <summary>ResolveToolSpec raw_spec으로 보내는 정확한 문자열.</summary>
        [JsonPropertyName("raw_spec")]
        public required string RawSpec { get; init; }

        /// <summary>
        /// 비어 있거나 공백뿐인 고정 요청 필드의 JSON 이름, 모두 채워졌으면 null.
        /// hash는 자기 자신과만 맞으므로 빈 값을 막지 못한다 — 생성과 재진입에서 따로 확인한다.
        /// </summary>
        public string? FindBlankField() =>
            string.IsNullOrWhiteSpace(ToolName) ? "tool_name"
            : string.IsNullOrWhiteSpace(Version) ? "version"
            : string.IsNullOrWhiteSpace(RawSpec) ? "raw_spec"
            : null;
    }
}
