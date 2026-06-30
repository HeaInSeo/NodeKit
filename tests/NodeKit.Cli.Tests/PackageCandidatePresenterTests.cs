using System.Collections.Generic;
using System.IO;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    public class PackageCandidatePresenterTests
    {
        private static readonly IRecipeCreateCancellationSource NeverCancel =
            new FixedCancellationSource(false);

        // ── Present ──────────────────────────────────────────────────────────

        [Fact]
        public void Present_SingleCandidate_AutoSelectsWithoutPrompt()
        {
            var packages = new[]
            {
                new PackageResolution("bwa", "0.7.17", new[]
                {
                    new BuildStringCandidate("h5bf99c6_8", "bwa=0.7.17=h5bf99c6_8", "bioconda"),
                }),
            };

            var stdin = new StringReader(""); // no input needed
            var stdout = new StringWriter();

            var selections = PackageCandidatePresenter.Present(packages, new PlainTextRecipeConsole(stdin, stdout), NeverCancel);

            Assert.NotNull(selections);
            Assert.Equal("bwa=0.7.17=h5bf99c6_8", selections!["bwa"]);
            Assert.DoesNotContain("선택하세요", stdout.ToString());
        }

        [Fact]
        public void Present_MultipleCandidates_PromptsUser_PicksSecond()
        {
            var packages = new[]
            {
                new PackageResolution("bwa", "0.7.17", new[]
                {
                    new BuildStringCandidate("h5bf99c6_8", "bwa=0.7.17=h5bf99c6_8", "bioconda"),
                    new BuildStringCandidate("h7132678_8", "bwa=0.7.17=h7132678_8", "conda-forge"),
                }),
            };

            var stdin = new StringReader("2\n");
            var stdout = new StringWriter();

            var selections = PackageCandidatePresenter.Present(packages, new PlainTextRecipeConsole(stdin, stdout), NeverCancel);

            Assert.NotNull(selections);
            Assert.Equal("bwa=0.7.17=h7132678_8", selections!["bwa"]);
        }

        [Fact]
        public void Present_MultipleCandidates_EnterSelectsFirst()
        {
            var packages = new[]
            {
                new PackageResolution("bwa", "0.7.17", new[]
                {
                    new BuildStringCandidate("h5bf99c6_8", "bwa=0.7.17=h5bf99c6_8", "bioconda"),
                    new BuildStringCandidate("h7132678_8", "bwa=0.7.17=h7132678_8", "conda-forge"),
                }),
            };

            var stdin = new StringReader("\n");
            var stdout = new StringWriter();

            var selections = PackageCandidatePresenter.Present(packages, new PlainTextRecipeConsole(stdin, stdout), NeverCancel);

            Assert.NotNull(selections);
            Assert.Equal("bwa=0.7.17=h5bf99c6_8", selections!["bwa"]);
        }

        [Fact]
        public void Present_CancelCommand_ThrowsCancelledException()
        {
            var packages = new[]
            {
                new PackageResolution("bwa", "0.7.17", new[]
                {
                    new BuildStringCandidate("h5bf99c6_8", "bwa=0.7.17=h5bf99c6_8", "bioconda"),
                    new BuildStringCandidate("h7132678_8", "bwa=0.7.17=h7132678_8", "bioconda"),
                }),
            };

            var stdin = new StringReader("/cancel\n");
            var stdout = new StringWriter();

            Assert.Throws<RecipeCreateCancelledException>(() =>
                PackageCandidatePresenter.Present(packages, new PlainTextRecipeConsole(stdin, stdout), NeverCancel));
        }

        [Fact]
        public void Present_InvalidThenValid_RepromptsAndAccepts()
        {
            var packages = new[]
            {
                new PackageResolution("bwa", "0.7.17", new[]
                {
                    new BuildStringCandidate("h5bf99c6_8", "bwa=0.7.17=h5bf99c6_8", "bioconda"),
                    new BuildStringCandidate("h7132678_8", "bwa=0.7.17=h7132678_8", "bioconda"),
                }),
            };

            var stdin = new StringReader("9\n1\n"); // 9 is out of range; then valid
            var stdout = new StringWriter();

            var selections = PackageCandidatePresenter.Present(packages, new PlainTextRecipeConsole(stdin, stdout), NeverCancel);

            Assert.NotNull(selections);
            Assert.Equal("bwa=0.7.17=h5bf99c6_8", selections!["bwa"]);
            Assert.Contains("1부터", stdout.ToString());
        }

        // ── ApplySelections ──────────────────────────────────────────────────

        [Fact]
        public void ApplySelections_ReplacesVersionOnlyPinWithFullPin()
        {
            var packages = new List<string> { "bwa=0.7.17", "samtools=1.19" };
            var selections = new Dictionary<string, string>
            {
                ["bwa"] = "bwa=0.7.17=h5bf99c6_8",
            };

            var result = PackageCandidatePresenter.ApplySelections(packages, selections);

            Assert.Equal("bwa=0.7.17=h5bf99c6_8", result[0]);
            Assert.Equal("samtools=1.19", result[1]);
        }

        [Fact]
        public void ApplySelections_EmptySelections_ReturnsOriginal()
        {
            var packages = new List<string> { "bwa=0.7.17" };
            var result = PackageCandidatePresenter.ApplySelections(packages, new Dictionary<string, string>());

            Assert.Equal("bwa=0.7.17", result[0]);
        }

        [Fact]
        public void ApplySelections_AlreadyFullPin_GetsReplaced()
        {
            var packages = new List<string> { "bwa=0.7.17=old_build" };
            var selections = new Dictionary<string, string>
            {
                ["bwa"] = "bwa=0.7.17=h5bf99c6_8",
            };

            var result = PackageCandidatePresenter.ApplySelections(packages, selections);

            Assert.Equal("bwa=0.7.17=h5bf99c6_8", result[0]);
        }

        // ── NullResolveRecipeClient ──────────────────────────────────────────

        [Fact]
        public async System.Threading.Tasks.Task NullResolveRecipeClient_ReturnsUnsupported()
        {
            var client = NullResolveRecipeClient.Instance;
            var result = await client.ResolveAsync("bwa", "0.7.17", new[] { "bwa=0.7.17" },
                System.Threading.CancellationToken.None);

            Assert.Equal(RecipeResolutionSource.Unsupported, result.Source);
            Assert.Empty(result.Packages);
        }

        private sealed class FixedCancellationSource : IRecipeCreateCancellationSource
        {
            private readonly bool _cancelled;

            public FixedCancellationSource(bool cancelled) => _cancelled = cancelled;

            public bool IsCancellationRequested => _cancelled;
        }
    }
}
