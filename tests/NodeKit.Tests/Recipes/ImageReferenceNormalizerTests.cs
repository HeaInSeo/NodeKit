using System.Linq;
using NodeKit.Authoring;
using NodeKit.Authoring.Recipes;
using NodeKit.Validation.Recipes;
using Xunit;

namespace NodeKit.Tests.Recipes
{
    public class ImageReferenceNormalizerTests
    {
        private const string Ref = "quay.io/biocontainers/bwa:0.7.17--h7132678_9";
        private const string Digest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private const string CanonicalUri = Ref + "@" + Digest;

        // ── Normalized cases ─────────────────────────────────────────────────

        [Fact]
        public void EmbeddedDigest_NoSeparate_Normalized()
        {
            var r = ImageReferenceNormalizer.Normalize(CanonicalUri, null);

            Assert.Equal(ImageReferenceNormalizeStatus.Normalized, r.Status);
            Assert.Equal(Ref, r.RepositoryAndTag);
            Assert.Equal(Digest, r.Digest);
            Assert.Equal(CanonicalUri, r.CanonicalUri);
        }

        [Fact]
        public void NoEmbedded_SeparateDigest_Normalized()
        {
            var r = ImageReferenceNormalizer.Normalize(Ref, Digest);

            Assert.Equal(ImageReferenceNormalizeStatus.Normalized, r.Status);
            Assert.Equal(Ref, r.RepositoryAndTag);
            Assert.Equal(Digest, r.Digest);
            Assert.Equal(CanonicalUri, r.CanonicalUri);
        }

        [Fact]
        public void EmbeddedAndSeparate_Same_Normalized()
        {
            var r = ImageReferenceNormalizer.Normalize(CanonicalUri, Digest);

            Assert.Equal(ImageReferenceNormalizeStatus.Normalized, r.Status);
            Assert.Equal(Digest, r.Digest);
            Assert.Equal(CanonicalUri, r.CanonicalUri);
        }

        [Fact]
        public void SeparateDigestWithoutPrefix_PrefixAdded_Normalized()
        {
            const string rawHex = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            var r = ImageReferenceNormalizer.Normalize(Ref, rawHex);

            Assert.Equal(ImageReferenceNormalizeStatus.Normalized, r.Status);
            Assert.Equal("sha256:" + rawHex, r.Digest);
            Assert.Equal(CanonicalUri, r.CanonicalUri);
        }

        [Fact]
        public void CanonicalUri_HasCorrectFormat()
        {
            var r = ImageReferenceNormalizer.Normalize(Ref, Digest);

            Assert.Equal($"{Ref}@{Digest}", r.CanonicalUri);
        }

        // ── MissingDigest cases ──────────────────────────────────────────────

        [Fact]
        public void NoEmbedded_NoSeparate_MissingDigest()
        {
            var r = ImageReferenceNormalizer.Normalize(Ref, null);

            Assert.Equal(ImageReferenceNormalizeStatus.MissingDigest, r.Status);
            Assert.Equal(Ref, r.RepositoryAndTag);
            Assert.Null(r.Digest);
            Assert.Null(r.CanonicalUri);
        }

        [Fact]
        public void NoEmbedded_WhitespaceSeparate_MissingDigest()
        {
            var r = ImageReferenceNormalizer.Normalize(Ref, "   ");

            Assert.Equal(ImageReferenceNormalizeStatus.MissingDigest, r.Status);
        }

        [Fact]
        public void NoEmbedded_EmptySeparate_MissingDigest()
        {
            var r = ImageReferenceNormalizer.Normalize(Ref, string.Empty);

            Assert.Equal(ImageReferenceNormalizeStatus.MissingDigest, r.Status);
        }

        // ── DigestConflict cases ──────────────────────────────────────────────

        [Fact]
        public void EmbeddedAndSeparate_Different_DigestConflict()
        {
            const string otherDigest = "sha256:ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
            var r = ImageReferenceNormalizer.Normalize(CanonicalUri, otherDigest);

            Assert.Equal(ImageReferenceNormalizeStatus.DigestConflict, r.Status);
            Assert.Equal(Ref, r.RepositoryAndTag);
            Assert.Equal(Digest, r.EmbeddedDigest);
            Assert.Equal(otherDigest, r.SeparateDigest);
            Assert.Null(r.Digest);
            Assert.Null(r.CanonicalUri);
        }

        [Fact]
        public void DigestConflict_NullDigestAndCanonicalUri()
        {
            var r = ImageReferenceNormalizer.Normalize(
                CanonicalUri,
                "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

            Assert.Null(r.Digest);
            Assert.Null(r.CanonicalUri);
        }

        // ── RepositoryAndTag extraction ───────────────────────────────────────

        [Fact]
        public void EmbeddedDigest_RepositoryAndTagStripped()
        {
            var r = ImageReferenceNormalizer.Normalize(CanonicalUri, null);

            Assert.DoesNotContain("@sha256:", r.RepositoryAndTag);
        }

        [Fact]
        public void NoDigest_RepositoryAndTagIsImageRef()
        {
            var r = ImageReferenceNormalizer.Normalize(Ref, null);

            Assert.Equal(Ref, r.RepositoryAndTag);
        }

        // ── Integration: session Build() + L1 validator ───────────────────────
        // Proves RecipeValidator/L1 only see the canonical digest-pinned URI,
        // never the raw split ImageRef. (See Done criteria for Sprint R13.)

        [Fact]
        public void Session_SplitImageRefDigest_BuildProducesCanonicalBioContainerUri()
        {
            var doc = BuildCompleteContainerDocument(Ref, Digest);

            Assert.Equal($"{Ref}@{Digest}", doc.BioContainerImageUri);
            Assert.DoesNotContain("@sha256:", doc.BaseImage ?? string.Empty);
        }

        [Fact]
        public void Session_SplitImageRefDigest_L1ValidationPasses_NoDigestViolation()
        {
            var doc = BuildCompleteContainerDocument(Ref, Digest);
            var result = RecipeValidationPipeline.ValidateRecipe(doc);

            Assert.True(result.IsValid,
                $"L1 validation failed: {string.Join(", ", result.Violations.Select(v => v.RuleId + " " + v.Message))}");
        }

        private static RecipeDocument BuildCompleteContainerDocument(string imageRef, string imageDigest)
        {
            var session = new RecipeAuthoringSession();
            session.SelectMethod(RecipeMethodId.Container);
            session.SetField("ToolName", "bwa-mem");
            session.SetField("ToolVersion", "0.7.17");
            session.SetField("Script", "run.sh");
            session.SetField("ImageRef", imageRef);
            session.SetField("ImageDigest", imageDigest);
            session.CompleteListField("Command");
            var doc = session.Build();
            doc.BuildKind = RecipeKindResolver.Resolve(RecipeMethodId.Container, doc);
            return doc;
        }
    }
}
