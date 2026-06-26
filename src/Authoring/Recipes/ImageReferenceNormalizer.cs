using System;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Combines an ImageRef (may or may not embed @sha256:) with an optional
    /// separate ImageDigest into a canonical "repo:tag@sha256:..." URI.
    /// Detects missing digest and conflicting digest cases.
    /// See docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_V0.9.2.md Sections 10.3–10.5.
    /// Called just before RecipeDocument construction (Section 10.4), not inside
    /// prompt-layer string handling.
    /// </summary>
    internal static class ImageReferenceNormalizer
    {
        private const string DigestSeparator = "@sha256:";
        private const string DigestPrefix = "sha256:";

        public static ImageReferenceNormalizeResult Normalize(string imageRef, string? imageDigest)
        {
            var atIdx = imageRef.IndexOf(DigestSeparator, StringComparison.Ordinal);
            string repositoryAndTag;
            string? embeddedDigest;

            if (atIdx >= 0)
            {
                repositoryAndTag = imageRef[..atIdx];
                embeddedDigest = DigestPrefix + imageRef[(atIdx + DigestSeparator.Length)..];
            }
            else
            {
                repositoryAndTag = imageRef;
                embeddedDigest = null;
            }

            var separateDigest = NormalizeDigest(imageDigest);

            if (embeddedDigest is null && separateDigest is null)
            {
                return new ImageReferenceNormalizeResult(
                    ImageReferenceNormalizeStatus.MissingDigest,
                    repositoryAndTag,
                    Digest: null,
                    CanonicalUri: null,
                    EmbeddedDigest: null,
                    SeparateDigest: null);
            }

            if (embeddedDigest is not null && separateDigest is not null
                && !string.Equals(embeddedDigest, separateDigest, StringComparison.Ordinal))
            {
                return new ImageReferenceNormalizeResult(
                    ImageReferenceNormalizeStatus.DigestConflict,
                    repositoryAndTag,
                    Digest: null,
                    CanonicalUri: null,
                    EmbeddedDigest: embeddedDigest,
                    SeparateDigest: separateDigest);
            }

            var digest = embeddedDigest ?? separateDigest!;
            return new ImageReferenceNormalizeResult(
                ImageReferenceNormalizeStatus.Normalized,
                repositoryAndTag,
                Digest: digest,
                CanonicalUri: $"{repositoryAndTag}@{digest}",
                EmbeddedDigest: null,
                SeparateDigest: null);
        }

        private static string? NormalizeDigest(string? digest)
        {
            if (string.IsNullOrWhiteSpace(digest))
            {
                return null;
            }

            return digest.StartsWith(DigestPrefix, StringComparison.Ordinal)
                ? digest
                : DigestPrefix + digest;
        }
    }
}
