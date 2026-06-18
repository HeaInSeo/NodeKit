using System.Text.Json.Serialization;

namespace NodeKit.Grpc
{
    internal sealed class CatalogDataDto
    {
        [JsonPropertyName("cas_hash")]
        public string CasHash { get; set; } = string.Empty;

        [JsonPropertyName("data_name")]
        public string DataName { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("stable_ref")]
        public string StableRef { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("format")]
        public string? Format { get; set; }

        [JsonPropertyName("source_uri")]
        public string? SourceUri { get; set; }

        [JsonPropertyName("checksum")]
        public string? Checksum { get; set; }

        [JsonPropertyName("storage_uri")]
        public string? StorageUri { get; set; }

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
    }
}
