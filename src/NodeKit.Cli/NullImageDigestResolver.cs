using System.Threading;
using System.Threading.Tasks;

namespace NodeKit.Cli
{
    internal sealed class NullImageDigestResolver : IImageDigestResolver
    {
        public static NullImageDigestResolver Instance { get; } = new();

        private NullImageDigestResolver()
        {
        }

        public Task<ImageDigestResolutionResult> ResolveAsync(
            string imageUri,
            CancellationToken cancellationToken)
        {
            _ = imageUri;
            _ = cancellationToken;
            return Task.FromResult(ImageDigestResolutionResult.Unsupported());
        }
    }
}
