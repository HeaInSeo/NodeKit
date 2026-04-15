using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Grpc;
using Xunit;

namespace NodeKit.Tests
{
    /// <summary>
    /// HttpCatalogClient 단위 테스트.
    /// FakeHttpMessageHandler로 실제 HTTP 서버 없이 JSON 파싱 경로를 검증한다.
    /// </summary>
    internal sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        internal FakeHttpMessageHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    public sealed class HttpCatalogClientTests
    {
        // ── IToolRegistryClient 구현 확인 ─────────────────────────────────────

        [Fact]
        public void HttpCatalogClient_Implements_IToolRegistryClient()
        {
            using var client = new HttpCatalogClient("http://localhost:8080");
            Assert.IsAssignableFrom<IToolRegistryClient>(client);
        }

        // ── ListToolsAsync — JSON deserialization ────────────────────────────

        [Fact]
        public async Task ListToolsAsync_ParsesToolListResponse()
        {
            const string json = """
                {
                    "tools": [
                        {
                            "cas_hash": "abc123",
                            "tool_name": "bwa-mem2",
                            "version": "2.2.1",
                            "stable_ref": "bwa-mem2@2.2.1",
                            "image_uri": "registry.example.com/bwa-mem2:2.2.1",
                            "digest": "sha256:dead",
                            "lifecycle_phase": "Active",
                            "integrity_health": "Healthy",
                            "registered_at": 1775000000,
                            "display_label": "BWA-MEM2 2.2.1",
                            "display_category": "Alignment"
                        }
                    ]
                }
                """;

            using var client = MakeClient(HttpStatusCode.OK, json);
            var result = await client.ListToolsAsync();

            Assert.Single(result);
            var tool = result[0];
            Assert.Equal("abc123", tool.CasHash);
            Assert.Equal("bwa-mem2", tool.ToolName);
            Assert.Equal("2.2.1", tool.Version);
            Assert.Equal("bwa-mem2@2.2.1", tool.StableRef);
            Assert.Equal("registry.example.com/bwa-mem2:2.2.1", tool.ImageUri);
            Assert.Equal("sha256:dead", tool.Digest);
            Assert.Equal("Active", tool.LifecyclePhase);
            Assert.Equal("BWA-MEM2 2.2.1", tool.DisplayLabel);
            Assert.Equal("Alignment", tool.DisplayCategory);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1775000000), tool.RegisteredAt);
        }

        [Fact]
        public async Task ListToolsAsync_EmptyList_ReturnsEmpty()
        {
            using var client = MakeClient(HttpStatusCode.OK, """{"tools":[]}""");
            var result = await client.ListToolsAsync();
            Assert.Empty(result);
        }

        [Fact]
        public async Task ListToolsAsync_MultipleTools_AllParsed()
        {
            const string json = """
                {
                    "tools": [
                        {"cas_hash":"h1","tool_name":"bwa","version":"1.0","lifecycle_phase":"Active","registered_at":0},
                        {"cas_hash":"h2","tool_name":"samtools","version":"1.17","lifecycle_phase":"Active","registered_at":0}
                    ]
                }
                """;
            using var client = MakeClient(HttpStatusCode.OK, json);
            var result = await client.ListToolsAsync();

            Assert.Equal(2, result.Count);
            Assert.Equal("bwa", result[0].ToolName);
            Assert.Equal("samtools", result[1].ToolName);
        }

        [Fact]
        public async Task ListToolsAsync_DisplayLabel_FallsBackToToolNameVersion()
        {
            // display_label 없을 때 tool_name + version 조합
            const string json = """
                {
                    "tools": [
                        {
                            "cas_hash": "h1",
                            "tool_name": "samtools",
                            "version": "1.17",
                            "lifecycle_phase": "Active",
                            "registered_at": 0
                        }
                    ]
                }
                """;
            using var client = MakeClient(HttpStatusCode.OK, json);
            var result = await client.ListToolsAsync();

            Assert.Single(result);
            Assert.Equal("samtools 1.17", result[0].DisplayLabel);
        }

        [Fact]
        public async Task ListToolsAsync_DisplayLabel_FallsBackToToolNameOnly_WhenNoVersion()
        {
            const string json = """
                {
                    "tools": [
                        {
                            "cas_hash": "h1",
                            "tool_name": "star",
                            "lifecycle_phase": "Active",
                            "registered_at": 0
                        }
                    ]
                }
                """;
            using var client = MakeClient(HttpStatusCode.OK, json);
            var result = await client.ListToolsAsync();

            Assert.Single(result);
            Assert.Equal("star", result[0].DisplayLabel);
        }

        [Fact]
        public async Task ListToolsAsync_RegisteredAt_ConvertsFromUnixSeconds()
        {
            const long unixSeconds = 1_775_000_000L;
            var expected = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            const string json = """
                {
                    "tools": [
                        {"cas_hash":"h1","tool_name":"bwa","lifecycle_phase":"Active","registered_at":1775000000}
                    ]
                }
                """;
            using var client = MakeClient(HttpStatusCode.OK, json);
            var result = await client.ListToolsAsync();

            Assert.Single(result);
            Assert.Equal(expected, result[0].RegisteredAt);
        }

        [Fact]
        public async Task ListToolsAsync_ServerError_ThrowsHttpRequestException()
        {
            using var client = MakeClient(HttpStatusCode.InternalServerError, "internal error");
            await Assert.ThrowsAsync<HttpRequestException>(
                () => client.ListToolsAsync());
        }

        [Fact]
        public async Task ListToolsAsync_NotFound_ThrowsHttpRequestException()
        {
            using var client = MakeClient(HttpStatusCode.NotFound, "not found");
            await Assert.ThrowsAsync<HttpRequestException>(
                () => client.ListToolsAsync());
        }

        // ── IntegrityHealth 매핑 ─────────────────────────────────────────────

        [Fact]
        public async Task ListToolsAsync_IntegrityHealth_Mapped()
        {
            const string json = """
                {
                    "tools": [
                        {
                            "cas_hash": "h1",
                            "tool_name": "bwa",
                            "lifecycle_phase": "Active",
                            "integrity_health": "Partial",
                            "registered_at": 0
                        }
                    ]
                }
                """;
            using var client = MakeClient(HttpStatusCode.OK, json);
            var result = await client.ListToolsAsync();

            Assert.Single(result);
            Assert.Equal("Partial", result[0].IntegrityHealth);
        }

        [Fact]
        public async Task ListToolsAsync_IntegrityHealth_DefaultsToEmpty()
        {
            // integrity_health 필드 없을 때 빈 문자열
            const string json = """
                {
                    "tools": [
                        {"cas_hash":"h1","tool_name":"bwa","lifecycle_phase":"Active","registered_at":0}
                    ]
                }
                """;
            using var client = MakeClient(HttpStatusCode.OK, json);
            var result = await client.ListToolsAsync();

            Assert.Single(result);
            Assert.Equal(string.Empty, result[0].IntegrityHealth);
        }

        // ── ListDataAsync ─────────────────────────────────────────────────────

        [Fact]
        public async Task ListDataAsync_ParsesDataListResponse()
        {
            const string json = """
                {
                    "data": [
                        {
                            "cas_hash": "d1",
                            "data_name": "hg38-reference",
                            "version": "2024-01",
                            "stable_ref": "hg38-reference@2024-01",
                            "description": "Human GRCh38 reference genome",
                            "format": "FASTA",
                            "source_uri": "https://ftp.ncbi.nlm.nih.gov/hg38.fa.gz",
                            "checksum": "sha256:abc",
                            "storage_uri": "registry.example.com/data/hg38:2024-01",
                            "lifecycle_phase": "Active",
                            "integrity_health": "Healthy",
                            "registered_at": 1775000000,
                            "display_label": "Human GRCh38 Reference",
                            "display_category": "Reference Genome"
                        }
                    ]
                }
                """;
            using var client = MakeClient(HttpStatusCode.OK, json);
            var result = await client.ListDataAsync();

            Assert.Single(result);
            var d = result[0];
            Assert.Equal("d1", d.CasHash);
            Assert.Equal("hg38-reference", d.DataName);
            Assert.Equal("2024-01", d.Version);
            Assert.Equal("hg38-reference@2024-01", d.StableRef);
            Assert.Equal("Human GRCh38 reference genome", d.Description);
            Assert.Equal("FASTA", d.Format);
            Assert.Equal("sha256:abc", d.Checksum);
            Assert.Equal("Active", d.LifecyclePhase);
            Assert.Equal("Healthy", d.IntegrityHealth);
            Assert.Equal("Human GRCh38 Reference", d.DisplayLabel);
            Assert.Equal("Reference Genome", d.DisplayCategory);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1775000000), d.RegisteredAt);
        }

        [Fact]
        public async Task ListDataAsync_EmptyList_ReturnsEmpty()
        {
            using var client = MakeClient(HttpStatusCode.OK, """{"data":[]}""");
            var result = await client.ListDataAsync();
            Assert.Empty(result);
        }

        [Fact]
        public async Task ListDataAsync_DisplayLabel_FallsBackToDataNameVersion()
        {
            const string json = """
                {
                    "data": [
                        {
                            "cas_hash": "d1",
                            "data_name": "hg38-reference",
                            "version": "2024-01",
                            "lifecycle_phase": "Active",
                            "registered_at": 0
                        }
                    ]
                }
                """;
            using var client = MakeClient(HttpStatusCode.OK, json);
            var result = await client.ListDataAsync();

            Assert.Single(result);
            Assert.Equal("hg38-reference 2024-01", result[0].DisplayLabel);
        }

        [Fact]
        public async Task ListDataAsync_DisplayLabel_FallsBackToDataNameOnly_WhenNoVersion()
        {
            const string json = """
                {
                    "data": [
                        {
                            "cas_hash": "d1",
                            "data_name": "hg38-reference",
                            "lifecycle_phase": "Active",
                            "registered_at": 0
                        }
                    ]
                }
                """;
            using var client = MakeClient(HttpStatusCode.OK, json);
            var result = await client.ListDataAsync();

            Assert.Single(result);
            Assert.Equal("hg38-reference", result[0].DisplayLabel);
        }

        [Fact]
        public async Task ListDataAsync_ServerError_ThrowsHttpRequestException()
        {
            using var client = MakeClient(HttpStatusCode.InternalServerError, "error");
            await Assert.ThrowsAsync<HttpRequestException>(
                () => client.ListDataAsync());
        }

        // ── Dispose ───────────────────────────────────────────────────────────

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            var client = new HttpCatalogClient("http://localhost:8080");
            client.Dispose();
            client.Dispose(); // 두 번 호출해도 예외 없음
        }

        // ── helper ────────────────────────────────────────────────────────────

        private static HttpCatalogClient MakeClient(HttpStatusCode status, string body)
            => new("http://localhost:8080", new FakeHttpMessageHandler(status, body));
    }
}
