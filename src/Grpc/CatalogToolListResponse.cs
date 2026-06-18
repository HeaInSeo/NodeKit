using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NodeKit.Grpc
{
    internal sealed class CatalogToolListResponse
    {
        [JsonPropertyName("tools")]
        public List<CatalogToolDto> Tools { get; set; } = new();
    }
}
