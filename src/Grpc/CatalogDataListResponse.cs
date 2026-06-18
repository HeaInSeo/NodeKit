using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NodeKit.Grpc
{
    internal sealed class CatalogDataListResponse
    {
        [JsonPropertyName("data")]
        public List<CatalogDataDto> Data { get; set; } = new();
    }
}
