using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    public class HarborImageDigestResolverTests
    {
        [Fact]
        public void TryCreate_WhenNoEnvVar_ReturnsNull()
        {
            var original = Environment.GetEnvironmentVariable("NODEKIT_HARBOR_URL");
            try
            {
                Environment.SetEnvironmentVariable("NODEKIT_HARBOR_URL", null);
                Assert.Null(HarborImageDigestResolver.TryCreate());
            }
            finally
            {
                Environment.SetEnvironmentVariable("NODEKIT_HARBOR_URL", original);
            }
        }

        [Fact]
        public void TryCreate_WhenEnvVarSet_ReturnsInstance()
        {
            var original = Environment.GetEnvironmentVariable("NODEKIT_HARBOR_URL");
            try
            {
                Environment.SetEnvironmentVariable("NODEKIT_HARBOR_URL", "https://harbor.lab.local");
                using var resolver = HarborImageDigestResolver.TryCreate();
                Assert.NotNull(resolver);
            }
            finally
            {
                Environment.SetEnvironmentVariable("NODEKIT_HARBOR_URL", original);
            }
        }

        [Fact]
        public async Task ResolveAsync_WhenImageHostDoesNotMatchHarbor_ReturnsUnsupported()
        {
            using var resolver = HarborImageDigestResolver.CreateForTest(
                "https://harbor.lab.local",
                new NoCallHandler());

            var result = await resolver.ResolveAsync(
                "quay.io/biocontainers/bwa:0.7.17",
                CancellationToken.None);

            Assert.Equal(ImageDigestResolutionStatus.Unsupported, result.Status);
        }

        [Fact]
        public async Task ResolveAsync_WhenImageAlreadyHasDigest_ReturnsInvalidReference()
        {
            using var resolver = HarborImageDigestResolver.CreateForTest(
                "https://harbor.lab.local",
                new NoCallHandler());

            var result = await resolver.ResolveAsync(
                "harbor.lab.local/proj/bwa:0.7.17@sha256:abc123",
                CancellationToken.None);

            Assert.Equal(ImageDigestResolutionStatus.InvalidReference, result.Status);
        }

        [Fact]
        public async Task ResolveAsync_WhenImageHasNoRegistryHost_ReturnsInvalidReference()
        {
            using var resolver = HarborImageDigestResolver.CreateForTest(
                "https://harbor.lab.local",
                new NoCallHandler());

            var result = await resolver.ResolveAsync(
                "ubuntu:22.04",
                CancellationToken.None);

            Assert.Equal(ImageDigestResolutionStatus.InvalidReference, result.Status);
        }

        [Fact]
        public async Task ResolveAsync_WhenHarborReturnsDigestHeader_ReturnsResolved()
        {
            const string expectedDigest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

            var fakeResponse = new HttpResponseMessage(HttpStatusCode.OK);
            fakeResponse.Headers.Add("Docker-Content-Digest", expectedDigest);

            using var resolver = HarborImageDigestResolver.CreateForTest(
                "https://harbor.lab.local",
                new FixedResponseHandler(fakeResponse));

            var result = await resolver.ResolveAsync(
                "harbor.lab.local/bioinformatics/bwa:0.7.17",
                CancellationToken.None);

            Assert.Equal(ImageDigestResolutionStatus.Resolved, result.Status);
            Assert.Equal(expectedDigest, result.Digest);
        }

        [Fact]
        public async Task ResolveAsync_WhenHarborReturns404_ReturnsNotFound()
        {
            using var resolver = HarborImageDigestResolver.CreateForTest(
                "https://harbor.lab.local",
                new FixedResponseHandler(new HttpResponseMessage(HttpStatusCode.NotFound)));

            var result = await resolver.ResolveAsync(
                "harbor.lab.local/bioinformatics/nonexistent:1.0",
                CancellationToken.None);

            Assert.Equal(ImageDigestResolutionStatus.NotFound, result.Status);
        }

        [Fact]
        public async Task ResolveAsync_WhenHarborReturns401_ReturnsAuthenticationRequired()
        {
            using var resolver = HarborImageDigestResolver.CreateForTest(
                "https://harbor.lab.local",
                new FixedResponseHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized)));

            var result = await resolver.ResolveAsync(
                "harbor.lab.local/private/tool:1.0",
                CancellationToken.None);

            Assert.Equal(ImageDigestResolutionStatus.AuthenticationRequired, result.Status);
        }

        [Fact]
        public async Task ResolveAsync_WhenNetworkFails_ReturnsNetworkUnavailable()
        {
            using var resolver = HarborImageDigestResolver.CreateForTest(
                "https://harbor.lab.local",
                new ThrowingHandler());

            var result = await resolver.ResolveAsync(
                "harbor.lab.local/bioinformatics/bwa:0.7.17",
                CancellationToken.None);

            Assert.Equal(ImageDigestResolutionStatus.NetworkUnavailable, result.Status);
        }

        private sealed class NoCallHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("HTTP 요청이 발생해서는 안 됩니다.");
            }
        }

        private sealed class FixedResponseHandler : HttpMessageHandler
        {
            private readonly HttpResponseMessage _response;

            public FixedResponseHandler(HttpResponseMessage response)
            {
                _response = response;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(_response);
            }
        }

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                throw new HttpRequestException("연결 실패");
            }
        }
    }
}
