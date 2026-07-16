using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    public class MappedHarborImageDigestResolverTests : IDisposable
    {
        private readonly string? _originalMap = Environment.GetEnvironmentVariable("NODEKIT_HARBOR_IMAGE_MAP");

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("NODEKIT_HARBOR_IMAGE_MAP", _originalMap);
        }

        [Fact]
        public async Task ResolveAsync_MappedCandidate_RequestsExpectedManifestPath()
        {
            Environment.SetEnvironmentVariable("NODEKIT_HARBOR_IMAGE_MAP", "docker.io=harbor.lab.local/dockerhub-proxy");

            const string expectedDigest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            var fakeResponse = new HttpResponseMessage(HttpStatusCode.OK);
            fakeResponse.Headers.Add("Docker-Content-Digest", expectedDigest);

            var handler = new RecordingHandler(fakeResponse);
            using var inner = HarborImageDigestResolver.CreateForTest("https://harbor.lab.local", handler);
            var resolver = new MappedHarborImageDigestResolver(inner);

            var result = await resolver.ResolveAsync("condaforge/miniforge3:24.3.0-0", CancellationToken.None);

            Assert.Equal(ImageDigestResolutionStatus.Resolved, result.Status);
            Assert.Equal(expectedDigest, result.Digest);
            Assert.NotNull(handler.LastRequestUri);
            Assert.EndsWith(
                "/v2/dockerhub-proxy/condaforge/miniforge3/manifests/24.3.0-0",
                handler.LastRequestUri!.AbsoluteUri,
                StringComparison.Ordinal);
        }

        [Fact]
        public async Task ResolveAsync_Resolved_SetsResolvedReferenceToMappedPath()
        {
            Environment.SetEnvironmentVariable("NODEKIT_HARBOR_IMAGE_MAP", "docker.io=harbor.lab.local/dockerhub-proxy");

            const string expectedDigest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            var fakeResponse = new HttpResponseMessage(HttpStatusCode.OK);
            fakeResponse.Headers.Add("Docker-Content-Digest", expectedDigest);

            using var inner = HarborImageDigestResolver.CreateForTest("https://harbor.lab.local", new RecordingHandler(fakeResponse));
            var resolver = new MappedHarborImageDigestResolver(inner);

            var result = await resolver.ResolveAsync("condaforge/miniforge3:24.3.0-0", CancellationToken.None);

            Assert.Equal("harbor.lab.local/dockerhub-proxy/condaforge/miniforge3:24.3.0-0", result.ResolvedReference);
        }

        [Fact]
        public async Task ResolveAsync_NoMappingConfigured_ReturnsUnsupportedWithoutHttpCall()
        {
            Environment.SetEnvironmentVariable("NODEKIT_HARBOR_IMAGE_MAP", null);

            using var inner = HarborImageDigestResolver.CreateForTest("https://harbor.lab.local", new NoCallHandler());
            var resolver = new MappedHarborImageDigestResolver(inner);

            var result = await resolver.ResolveAsync("condaforge/miniforge3:24.3.0-0", CancellationToken.None);

            Assert.Equal(ImageDigestResolutionStatus.Unsupported, result.Status);
            Assert.Contains("NODEKIT_HARBOR_IMAGE_MAP", result.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void HasAnyMapping_ReflectsEnvironmentVariable()
        {
            using var inner = HarborImageDigestResolver.CreateForTest("https://harbor.lab.local", new NoCallHandler());
            var resolver = new MappedHarborImageDigestResolver(inner);

            Environment.SetEnvironmentVariable("NODEKIT_HARBOR_IMAGE_MAP", null);
            Assert.False(resolver.HasAnyMapping);

            Environment.SetEnvironmentVariable("NODEKIT_HARBOR_IMAGE_MAP", "docker.io=harbor.lab.local/dockerhub-proxy");
            Assert.True(resolver.HasAnyMapping);
        }

        [Fact]
        public async Task ResolveAsync_ExplicitConcreteHarborReference_PassesThroughUnchanged()
        {
            // Issue #49 requirement 6: a reference the user already typed as a full
            // Harbor path (e.g. via [0] 직접 입력) must not need a mapping entry —
            // it's already concrete, so it goes straight to HarborImageDigestResolver.
            Environment.SetEnvironmentVariable("NODEKIT_HARBOR_IMAGE_MAP", null);

            const string expectedDigest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            var fakeResponse = new HttpResponseMessage(HttpStatusCode.OK);
            fakeResponse.Headers.Add("Docker-Content-Digest", expectedDigest);

            using var inner = HarborImageDigestResolver.CreateForTest("https://harbor.lab.local", new RecordingHandler(fakeResponse));
            var resolver = new MappedHarborImageDigestResolver(inner);

            const string concreteRef = "harbor.lab.local/library/samtools-quicksetup-open:latest";
            var result = await resolver.ResolveAsync(concreteRef, CancellationToken.None);

            Assert.Equal(ImageDigestResolutionStatus.Resolved, result.Status);
            Assert.Equal(concreteRef, result.ResolvedReference);
        }

        [Fact]
        public async Task ResolveAsync_HostMismatch_ReturnsUnsupported()
        {
            // Issue #49 requirement 7: an explicit reference whose host doesn't
            // match the configured Harbor must still be rejected exactly like
            // HarborImageDigestResolver already does on its own — the wrapper
            // must not change this behavior.
            Environment.SetEnvironmentVariable("NODEKIT_HARBOR_IMAGE_MAP", "quay.io=harbor.lab.local/quay-proxy");

            using var inner = HarborImageDigestResolver.CreateForTest("https://harbor.lab.local", new NoCallHandler());
            var resolver = new MappedHarborImageDigestResolver(inner);

            var result = await resolver.ResolveAsync("ghcr.io/example/tool:1.0", CancellationToken.None);

            Assert.Equal(ImageDigestResolutionStatus.Unsupported, result.Status);
        }

        private sealed class NoCallHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("HTTP 요청이 발생해서는 안 됩니다.");
            }
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly HttpResponseMessage _response;

            public RecordingHandler(HttpResponseMessage response)
            {
                _response = response;
            }

            public Uri? LastRequestUri { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequestUri = request.RequestUri;
                return Task.FromResult(_response);
            }
        }
    }
}
