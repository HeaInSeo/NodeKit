using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    public class PublicRegistryImageDigestResolverTests
    {
        [Fact]
        public async Task ResolveAsync_WhenBareOfficialImageName_RequestsLibraryNamespace()
        {
            const string expectedDigest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            var handler = new CapturingHandler(expectedDigest);

            using var resolver = new PublicRegistryImageDigestResolver(handler);
            var result = await resolver.ResolveAsync("alpine:3.20", CancellationToken.None);

            Assert.Equal(ImageDigestResolutionStatus.Resolved, result.Status);
            Assert.Equal(expectedDigest, result.Digest);
            Assert.Contains("repository:library/alpine:pull", handler.TokenRequestUri);
            Assert.Contains("/v2/library/alpine/manifests/3.20", handler.ManifestRequestUri);
        }

        [Fact]
        public async Task ResolveAsync_WhenRepositoryAlreadyHasNamespace_DoesNotPrependLibrary()
        {
            const string expectedDigest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            var handler = new CapturingHandler(expectedDigest);

            using var resolver = new PublicRegistryImageDigestResolver(handler);
            var result = await resolver.ResolveAsync("bioconda/samtools:1.17", CancellationToken.None);

            Assert.Equal(ImageDigestResolutionStatus.Resolved, result.Status);
            Assert.Contains("/v2/bioconda/samtools/manifests/1.17", handler.ManifestRequestUri);
        }

        [Fact]
        public async Task ResolveAsync_WhenQuayIoBareRepository_DoesNotPrependLibrary()
        {
            var fakeResponse = new HttpResponseMessage(HttpStatusCode.OK);
            fakeResponse.Headers.Add(
                "Docker-Content-Digest",
                "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");

            using var resolver = new PublicRegistryImageDigestResolver(new FixedResponseHandler(fakeResponse));
            var result = await resolver.ResolveAsync("quay.io/biocontainers/bwa:0.7.17", CancellationToken.None);

            Assert.Equal(ImageDigestResolutionStatus.Resolved, result.Status);
        }

        private sealed class FixedResponseHandler : HttpMessageHandler
        {
            private readonly HttpResponseMessage _response;

            public FixedResponseHandler(HttpResponseMessage response)
            {
                _response = response;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(_response);
            }
        }

        // Docker Hub 흐름은 토큰 요청 -> 매니페스트 요청 순서로 2번 호출된다.
        // 실제로 요청된 URI를 캡처해서 library/ 네임스페이스 보정이 적용됐는지 검증한다.
        private sealed class CapturingHandler : HttpMessageHandler
        {
            private readonly string _digest;

            public CapturingHandler(string digest)
            {
                _digest = digest;
            }

            public string? TokenRequestUri { get; private set; }

            public string? ManifestRequestUri { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var uri = request.RequestUri!.ToString();

                // Both responses below are handed to the caller (HttpClient) via
                // HttpMessageHandler.SendAsync's contract, which disposes them after
                // reading — disposing here first would break the returned object.
                if (uri.Contains("auth.docker.io", System.StringComparison.Ordinal))
                {
                    TokenRequestUri = uri;
                    var tokenResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"token\":\"fake-token\"}"),
                    };
                    return Task.FromResult(tokenResponse);
                }

                ManifestRequestUri = uri;
                var manifestResponse = new HttpResponseMessage(HttpStatusCode.OK);
                manifestResponse.Headers.Add("Docker-Content-Digest", _digest);
                return Task.FromResult(manifestResponse);
            }
        }
    }
}
