using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace NodeKit.Grpc
{
    // ── JSON DTOs (Catalog REST API wire format) ──────────────────────────────

    internal sealed class CatalogToolListResponse
    {
        [JsonPropertyName("tools")]
        public List<CatalogToolDto> Tools { get; set; } = new();
    }

    internal sealed class CatalogToolDto
    {
        [JsonPropertyName("cas_hash")]         public string CasHash { get; set; } = string.Empty;
        [JsonPropertyName("tool_name")]        public string ToolName { get; set; } = string.Empty;
        [JsonPropertyName("version")]          public string Version { get; set; } = string.Empty;
        [JsonPropertyName("stable_ref")]       public string StableRef { get; set; } = string.Empty;
        [JsonPropertyName("image_uri")]        public string ImageUri { get; set; } = string.Empty;
        [JsonPropertyName("digest")]           public string Digest { get; set; } = string.Empty;
        [JsonPropertyName("lifecycle_phase")]  public string LifecyclePhase { get; set; } = string.Empty;
        [JsonPropertyName("integrity_health")] public string IntegrityHealth { get; set; } = string.Empty;
        [JsonPropertyName("registered_at")]    public long RegisteredAt { get; set; }
        [JsonPropertyName("display_label")]    public string? DisplayLabel { get; set; }
        [JsonPropertyName("display_category")] public string? DisplayCategory { get; set; }
        [JsonPropertyName("command")]          public string? Command { get; set; }
    }

    // ── Data artifact DTOs ────────────────────────────────────────────────────

    internal sealed class CatalogDataListResponse
    {
        [JsonPropertyName("data")]
        public List<CatalogDataDto> Data { get; set; } = new();
    }

    internal sealed class CatalogDataDto
    {
        [JsonPropertyName("cas_hash")]         public string CasHash { get; set; } = string.Empty;
        [JsonPropertyName("data_name")]        public string DataName { get; set; } = string.Empty;
        [JsonPropertyName("version")]          public string Version { get; set; } = string.Empty;
        [JsonPropertyName("stable_ref")]       public string StableRef { get; set; } = string.Empty;
        [JsonPropertyName("description")]      public string? Description { get; set; }
        [JsonPropertyName("format")]           public string? Format { get; set; }
        [JsonPropertyName("source_uri")]       public string? SourceUri { get; set; }
        [JsonPropertyName("checksum")]         public string? Checksum { get; set; }
        [JsonPropertyName("storage_uri")]      public string? StorageUri { get; set; }
        [JsonPropertyName("lifecycle_phase")]  public string LifecyclePhase { get; set; } = string.Empty;
        [JsonPropertyName("integrity_health")] public string IntegrityHealth { get; set; } = string.Empty;
        [JsonPropertyName("registered_at")]    public long RegisteredAt { get; set; }
        [JsonPropertyName("display_label")]    public string? DisplayLabel { get; set; }
        [JsonPropertyName("display_category")] public string? DisplayCategory { get; set; }
    }

    /// <summary>NodeKit 내부 표현의 등록된 참조 데이터 정보.</summary>
    public sealed class RegisteredData
    {
        public string CasHash { get; init; } = string.Empty;
        public string DataName { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public string StableRef { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Format { get; init; } = string.Empty;
        public string SourceUri { get; init; } = string.Empty;
        public string Checksum { get; init; } = string.Empty;
        public string StorageUri { get; init; } = string.Empty;
        public string DisplayLabel { get; init; } = string.Empty;
        public string DisplayCategory { get; init; } = string.Empty;
        /// <summary>운영 의도 축. NodeVault 명시적 호출만 변경.</summary>
        public string LifecyclePhase { get; init; } = string.Empty;
        /// <summary>Harbor 정합성 관찰 축. reconcile loop만 변경.</summary>
        public string IntegrityHealth { get; init; } = string.Empty;
        public DateTimeOffset RegisteredAt { get; init; }
    }

    /// <summary>
    /// Catalog REST API 클라이언트. AdminToolList 표시용 read-only 서비스.
    /// GET {catalogBaseUrl}/v1/catalog/tools を호출한다.
    /// </summary>
    public sealed class HttpCatalogClient : IToolRegistryClient, IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private bool _disposed;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        public HttpCatalogClient(string catalogBaseUrl)
        {
            ArgumentNullException.ThrowIfNull(catalogBaseUrl);
            _baseUrl = catalogBaseUrl.TrimEnd('/');
            _http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10),
            };
        }

        /// <summary>테스트 전용 생성자. 실제 코드에서 직접 사용 금지.</summary>
        internal HttpCatalogClient(string catalogBaseUrl, HttpMessageHandler handler)
        {
            _baseUrl = catalogBaseUrl.TrimEnd('/');
            _http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10),
            };
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _http.Dispose();
            _disposed = true;
        }

        public async Task<IReadOnlyList<RegisteredTool>> ListToolsAsync(CancellationToken ct = default)
        {
            var url = $"{_baseUrl}/v1/catalog/tools";
            var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<CatalogToolListResponse>(body, JsonOptions)
                         ?? new CatalogToolListResponse();

            return result.Tools.Select(ToRegisteredTool).ToList();
        }

        public async Task<IReadOnlyList<RegisteredData>> ListDataAsync(CancellationToken ct = default)
        {
            var url = $"{_baseUrl}/v1/catalog/data";
            var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<CatalogDataListResponse>(body, JsonOptions)
                         ?? new CatalogDataListResponse();

            return result.Data.Select(ToRegisteredData).ToList();
        }

        private static RegisteredTool ToRegisteredTool(CatalogToolDto dto)
        {
            var label = dto.DisplayLabel;
            if (string.IsNullOrEmpty(label))
            {
                label = string.IsNullOrEmpty(dto.Version)
                    ? dto.ToolName
                    : $"{dto.ToolName} {dto.Version}";
            }

            return new RegisteredTool
            {
                CasHash          = dto.CasHash,
                ToolName         = dto.ToolName,
                Version          = dto.Version,
                StableRef        = dto.StableRef,
                ImageUri         = dto.ImageUri,
                Digest           = dto.Digest,
                DisplayLabel     = label,
                DisplayCategory  = dto.DisplayCategory ?? string.Empty,
                LifecyclePhase   = dto.LifecyclePhase,
                IntegrityHealth  = dto.IntegrityHealth,
                RegisteredAt     = DateTimeOffset.FromUnixTimeSeconds(dto.RegisteredAt),
            };
        }

        private static RegisteredData ToRegisteredData(CatalogDataDto dto)
        {
            var label = dto.DisplayLabel;
            if (string.IsNullOrEmpty(label))
            {
                label = string.IsNullOrEmpty(dto.Version)
                    ? dto.DataName
                    : $"{dto.DataName} {dto.Version}";
            }

            return new RegisteredData
            {
                CasHash         = dto.CasHash,
                DataName        = dto.DataName,
                Version         = dto.Version,
                StableRef       = dto.StableRef,
                Description     = dto.Description ?? string.Empty,
                Format          = dto.Format ?? string.Empty,
                SourceUri       = dto.SourceUri ?? string.Empty,
                Checksum        = dto.Checksum ?? string.Empty,
                StorageUri      = dto.StorageUri ?? string.Empty,
                DisplayLabel    = label,
                DisplayCategory = dto.DisplayCategory ?? string.Empty,
                LifecyclePhase  = dto.LifecyclePhase,
                IntegrityHealth = dto.IntegrityHealth,
                RegisteredAt    = DateTimeOffset.FromUnixTimeSeconds(dto.RegisteredAt),
            };
        }
    }
}
