using System.Threading;
using System.Threading.Tasks;

namespace NodeKit.Cli
{
    internal enum ImageDigestResolutionStatus
    {
        Resolved,
        NotFound,
        AuthenticationRequired,
        NetworkUnavailable,
        InvalidReference,
        Unsupported,
    }

    internal sealed record ImageDigestResolutionResult(
        ImageDigestResolutionStatus Status,
        string? Digest,
        string? Message,
        // Set when the resolver resolved a *different* reference than the one it
        // was asked about (e.g. MappedHarborImageDigestResolver rewriting a
        // host-less public reference onto a concrete Harbor pull path). Null
        // means "use the reference the caller originally passed in".
        string? ResolvedReference = null)
    {
        public static ImageDigestResolutionResult Resolved(string digest) =>
            new(ImageDigestResolutionStatus.Resolved, digest, null);

        public static ImageDigestResolutionResult NotFound(string? message = null) =>
            new(ImageDigestResolutionStatus.NotFound, null, message);

        public static ImageDigestResolutionResult AuthenticationRequired(string? message = null) =>
            new(ImageDigestResolutionStatus.AuthenticationRequired, null, message);

        public static ImageDigestResolutionResult NetworkUnavailable(string? message = null) =>
            new(ImageDigestResolutionStatus.NetworkUnavailable, null, message);

        public static ImageDigestResolutionResult InvalidReference(string? message = null) =>
            new(ImageDigestResolutionStatus.InvalidReference, null, message);

        public static ImageDigestResolutionResult Unsupported(string? message = null) =>
            new(ImageDigestResolutionStatus.Unsupported, null, message);
    }

    internal interface IImageDigestResolver
    {
        Task<ImageDigestResolutionResult> ResolveAsync(
            string imageUri,
            CancellationToken cancellationToken);
    }
}
