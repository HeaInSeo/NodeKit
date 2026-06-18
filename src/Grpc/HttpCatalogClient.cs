using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NodeKit.Grpc
{
    /// <summary>
    /// Catalog REST API 클라이언트. AdminToolList 표시용 read-only 서비스.
    /// GET {catalogBaseUrl}/v1/catalog/tools を호출한다.
    /// </summary>
    internal sealed class HttpCatalogClient : IToolRegistryClient, IDisposable
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private readonly HttpClient _http;
        private readonly Uri _baseUri;
        private bool _disposed;

        public HttpCatalogClient(string catalogBaseUrl)
            : this(new Uri(catalogBaseUrl, UriKind.Absolute))
        {
        }

        public HttpCatalogClient(Uri catalogBaseUrl)
        {
            ArgumentNullException.ThrowIfNull(catalogBaseUrl);
            _baseUri = CatalogClientUris.EnsureTrailingSlash(catalogBaseUrl);
            _http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10),
            };
        }

        /// <summary>테스트 전용 생성자. 실제 코드에서 직접 사용 금지.</summary>
        internal HttpCatalogClient(string catalogBaseUrl, HttpMessageHandler handler)
            : this(new Uri(catalogBaseUrl, UriKind.Absolute), handler)
        {
        }

        /// <summary>테스트 전용 생성자. 실제 코드에서 직접 사용 금지.</summary>
        internal HttpCatalogClient(Uri catalogBaseUrl, HttpMessageHandler handler)
        {
            _baseUri = CatalogClientUris.EnsureTrailingSlash(catalogBaseUrl);
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
            var url = new Uri(_baseUri, "v1/catalog/tools");
            var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<CatalogToolListResponse>(body, _jsonOptions)
                         ?? new CatalogToolListResponse();

            return result.Tools.Select(CatalogMappers.ToRegisteredTool).ToList();
        }

        public async Task<IReadOnlyList<RegisteredData>> ListDataAsync(CancellationToken ct = default)
        {
            var url = new Uri(_baseUri, "v1/catalog/data");
            var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<CatalogDataListResponse>(body, _jsonOptions)
                         ?? new CatalogDataListResponse();

            return result.Data.Select(CatalogMappers.ToRegisteredData).ToList();
        }
    }
}
