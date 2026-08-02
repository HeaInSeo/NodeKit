using System;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Validation;

namespace NodeKit.Cli
{
    /// <summary>
    /// Returns a fixed stub digest. Activated by NODEKIT_BASE_IMAGE_STUB=1, or
    /// injected directly in tests.
    /// </summary>
    internal sealed class StubImageDigestResolver : IImageDigestResolver
    {
        internal const string StubDigest = KnownPlaceholderDigests.BaseImageStub;

        public static StubImageDigestResolver Instance { get; } = new();

        private StubImageDigestResolver()
        {
        }

        public static StubImageDigestResolver? TryCreate()
        {
            var env = Environment.GetEnvironmentVariable("NODEKIT_BASE_IMAGE_STUB");
            return env == "1" ? Instance : null;
        }

        public Task<ImageDigestResolutionResult> ResolveAsync(
            string imageUri,
            CancellationToken cancellationToken)
        {
            _ = imageUri;
            _ = cancellationToken;
            return Task.FromResult(ImageDigestResolutionResult.Resolved(StubDigest));
        }
    }
}
