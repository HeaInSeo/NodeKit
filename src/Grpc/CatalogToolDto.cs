using System.Text.Json.Serialization;

namespace NodeKit.Grpc
{
    internal sealed class CatalogToolDto
    {
        [JsonPropertyName("cas_hash")]
        public string CasHash { get; set; } = string.Empty;

        [JsonPropertyName("tool_name")]
        public string ToolName { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("stable_ref")]
        public string StableRef { get; set; } = string.Empty;

        [JsonPropertyName("image_uri")]
        public string ImageUri { get; set; } = string.Empty;

        [JsonPropertyName("digest")]
        public string Digest { get; set; } = string.Empty;

        [JsonPropertyName("lifecycle_phase")]
        public string LifecyclePhase { get; set; } = string.Empty;

        [JsonPropertyName("integrity_health")]
        public string IntegrityHealth { get; set; } = string.Empty;

        [JsonPropertyName("registered_at")]
        public long RegisteredAt { get; set; }

        [JsonPropertyName("display_label")]
        public string? DisplayLabel { get; set; }

        [JsonPropertyName("display_category")]
        public string? DisplayCategory { get; set; }

        [JsonPropertyName("command")]
        public string? Command { get; set; }
    }
}
