using System;
using System.IO;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// Tests for U3 — step 4 base image selection + digest auto-resolution in
    /// RecipeCreateFlow. Uses StubImageDigestResolver injected via
    /// RecipeCreateInteractiveRunner.Run so no live HTTP calls are made.
    /// </summary>
    public class BaseImageSelectionStepTests : IDisposable
    {
        private static readonly IRecipeCreateCancellationSource NoCancellation =
            new FixedCancellationSource(false);

        private readonly string _workDir =
            Path.Combine(Path.GetTempPath(), "nodekit-base-image-tests-" + Guid.NewGuid());

        public BaseImageSelectionStepTests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
        }

        // ── Catalog ──────────────────────────────────────────────────────────────

        [Fact]
        public void BaseImageCatalog_Package_ReturnsTwoCandidates()
        {
            var candidates = BaseImageCatalog.CandidatesFor(NodeKit.Authoring.Recipes.RecipeMethodId.Package);
            Assert.Equal(2, candidates.Count);
            Assert.Contains(candidates, c => c.Reference.StartsWith("condaforge/miniforge3", StringComparison.Ordinal));
            Assert.Contains(candidates, c => c.Reference.StartsWith("mambaorg/micromamba", StringComparison.Ordinal));
        }

        [Fact]
        public void BaseImageCatalog_Container_ReturnsEmpty()
        {
            var candidates = BaseImageCatalog.CandidatesFor(NodeKit.Authoring.Recipes.RecipeMethodId.Container);
            Assert.Empty(candidates);
        }

        [Fact]
        public void BaseImageCatalog_Dockerfile_ReturnsEmpty()
        {
            var candidates = BaseImageCatalog.CandidatesFor(NodeKit.Authoring.Recipes.RecipeMethodId.Dockerfile);
            Assert.Empty(candidates);
        }

        // ── StubImageDigestResolver ───────────────────────────────────────────────

        [Fact]
        public async System.Threading.Tasks.Task StubResolver_ReturnsFixedDigest()
        {
            var resolver = StubImageDigestResolver.Instance;
            var result = await resolver.ResolveAsync("condaforge/miniforge3:24.3.0-0",
                System.Threading.CancellationToken.None);

            Assert.Equal(ImageDigestResolutionStatus.Resolved, result.Status);
            Assert.Equal(StubImageDigestResolver.StubDigest, result.Digest);
        }

        // ── Full flow: step 4 with StubResolver (Package method, QuickSetup) ────

        [Fact]
        public void Step4_PackageMethod_WithStubResolver_SetsImageRefAutoAndSkipsManualEntry()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");

            // Package method QuickSetup: user picks candidate [1] in step 4.
            // ImageRef is auto-set → RunFieldLoop does NOT prompt ImageRef.
            var transcript = new[]
            {
                "2",    // 빠른 설정 모드
                "n",    // IsRestrictedNetwork
                "n",    // HasInternalPackageMirror
                "n",    // HasExistingContainerImage
                "y",    // HasPackageInPublicChannels
                "n",    // HasSourceArchiveAndChecksum
                "n",    // HasExistingDockerfile
                "",     // accept recommended method (package)
                "bioconda", "", // 채널 확정 단계 (entry)
                "1",    // step 4: pick candidate [1] (condaforge/miniforge3)
                // RunFieldLoop: ImageRef already set — NOT prompted
                "bwa-mem",              // ToolName
                "0.7.17",               // ToolVersion
                "run.sh",               // Script
                "bwa=0.7.17=h5bf99c6_8", "", // Packages
                // PackageEngine: Defaulted → skipped
                // Port selection: null → "" → skipped
                // Save confirm: null → "" → saves
            };

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = RecipeCreateInteractiveRunner.Run(
                outPath,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                NoCancellation,
                imageDigestResolver: StubImageDigestResolver.Instance);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());
            Assert.True(File.Exists(outPath));

            var json = File.ReadAllText(outPath);
            // Auto-resolved digest from stub must appear in the saved recipe.
            Assert.Contains(StubImageDigestResolver.StubDigest, json);
            Assert.Contains("condaforge/miniforge3", json);
            Assert.Contains("bwa-mem", json);
        }

        [Fact]
        public void Step4_PackageMethod_WithStubResolver_PickSecondCandidate_SetsMicromamba()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");

            var transcript = new[]
            {
                "2",    // 빠른 설정 모드
                "n", "n", "n", "y", "n", "n",
                "",     // accept package
                "bioconda", "",
                "2",    // pick candidate [2] (micromamba)
                "bwa-mem", "0.7.17", "run.sh",
                "bwa=0.7.17=h5bf99c6_8", "",
            };

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = RecipeCreateInteractiveRunner.Run(
                outPath,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                NoCancellation,
                imageDigestResolver: StubImageDigestResolver.Instance);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));

            var json = File.ReadAllText(outPath);
            Assert.Contains("micromamba", json);
            Assert.Contains(StubImageDigestResolver.StubDigest, json);
        }

        [Fact]
        public void Step4_DirectInput_SkipsAutoResolve_ImageRefPromptedInFieldLoop()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            const string manualRef =
                "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

            var transcript = new[]
            {
                "2",    // 빠른 설정 모드
                "n", "n", "n", "y", "n", "n",
                "",     // accept package
                "bioconda", "",
                "0",    // step 4: 직접 입력 → ImageRef NOT set → RunFieldLoop prompts it
                "bwa-mem", "0.7.17", "run.sh",
                manualRef,  // ImageRef prompted by RunFieldLoop
                "bwa=0.7.17=h5bf99c6_8", "",
            };

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = RecipeCreateInteractiveRunner.Run(
                outPath,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                NoCancellation,
                imageDigestResolver: StubImageDigestResolver.Instance);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));

            var json = File.ReadAllText(outPath);
            // Manual ref (not stub digest) is in the file.
            Assert.Contains("0123456789abcdef", json);
            Assert.DoesNotContain(StubImageDigestResolver.StubDigest, json);
        }

        [Fact]
        public void Step4_PublicResolver_DirectInput_ImageRefPromptedInFieldLoop()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            const string manualRef =
                "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

            // publicResolver is used (open-net). User selects '0' (직접 입력) → step 4 returns, ImageRef prompted in field loop.
            var transcript = new[]
            {
                "2",    // 빠른 설정 모드
                "n", "n", "n", "y", "n", "n",
                "",     // accept package
                "bioconda", "",
                "0", // 기반 이미지: 직접 입력 → ImageRef still in field loop
                // Step 4 direct input → RunFieldLoop includes ImageRef
                "bwa-mem", "0.7.17", "run.sh",
                manualRef,
                "bwa=0.7.17=h5bf99c6_8", "",
            };

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = RecipeCreateInteractiveRunner.Run(
                outPath,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                NoCancellation,
                imageDigestResolver: null);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));

            var json = File.ReadAllText(outPath);
            Assert.Contains("0123456789abcdef", json);
        }

        [Fact]
        public void Step4_ContainerMethod_StepSkippedBecauseNoCandidates_ImageRefPromptedManually()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            // Container method: ImageRef (tag only) and ImageDigest are separate fields.
            const string imageRef = "condaforge/miniforge3:24.3.0-0";
            const string digest =
                "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

            // Container method: catalog returns empty → step 4 skipped even with resolver.
            var transcript = new[]
            {
                "2",    // 빠른 설정 모드
                "n",    // IsRestrictedNetwork
                "n",    // HasInternalPackageMirror
                "y",    // HasExistingContainerImage
                "n",    // HasPackageInPublicChannels
                "n",    // HasSourceArchiveAndChecksum
                "n",    // HasExistingDockerfile
                "",     // accept recommended method (container)
                // No step 3 (channels only for Package), no step 4 (empty catalog)
                "bwa-mem",  // ToolName
                "0.7.17",   // ToolVersion
                "run.sh",   // Script
                imageRef,   // ImageRef (tag only — Container keeps tag and digest separate)
                digest,     // ImageDigest (separate Required field)
                "",         // Command (optional, skip)
            };

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = RecipeCreateInteractiveRunner.Run(
                outPath,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                NoCancellation,
                imageDigestResolver: StubImageDigestResolver.Instance);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
        }

        [Fact]
        public void Step4_FailedResolution_WarnsUser_AndPromptRetryOrDirect()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            const string manualRef =
                "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

            // Resolver that always fails.
            var failingResolver = new FixedResultResolver(
                ImageDigestResolutionResult.NetworkUnavailable("테스트 네트워크 오류"));

            var transcript = new[]
            {
                "2",    // 빠른 설정 모드
                "n", "n", "n", "y", "n", "n",
                "",     // accept package
                "bioconda", "",
                "1",    // pick candidate [1] → fails
                "0",    // retry: direct input → step 4 exits
                "bwa-mem", "0.7.17", "run.sh",
                manualRef,  // ImageRef now prompted in RunFieldLoop
                "bwa=0.7.17=h5bf99c6_8", "",
            };

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = RecipeCreateInteractiveRunner.Run(
                outPath,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                NoCancellation,
                imageDigestResolver: failingResolver);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            Assert.Contains("digest 조회 실패", stdout.ToString());
        }

        // ── BeginnerGuide + step 4 ────────────────────────────────────────────────

        [Fact]
        public void Step4_BeginnerGuide_InstallCommand_WithStubResolver_SetsImageRefAuto()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");

            var transcript = new[]
            {
                "1",    // GuidedBeginner
                "2",    // 설치 명령
                "conda install -c bioconda bwa=0.7.17=h5bf99c6_8 -y",
                "1",    // use understood values
                "",     // 채널 확인: 파싱된 "bioconda" 그대로 사용
                "1",    // step 4: pick candidate [1]
                // RunFieldLoop: ImageRef auto-set → NOT prompted
                "bwa-mem",  // ToolName
                "0.7.17",   // ToolVersion
                "run.sh",   // Script
                // Packages: pre-filled → skipped
                // Channels: pre-filled → skipped
                // PackageEngine: Defaulted → skipped
                // Port + save: null → "" → saved
            };

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = RecipeCreateInteractiveRunner.Run(
                outPath,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                NoCancellation,
                imageDigestResolver: StubImageDigestResolver.Instance);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());
            Assert.True(File.Exists(outPath));

            var json = File.ReadAllText(outPath);
            Assert.Contains(StubImageDigestResolver.StubDigest, json);
            Assert.Contains("condaforge/miniforge3", json);
            Assert.Contains("bwa=0.7.17=h5bf99c6_8", json);
            Assert.Contains("bioconda", json);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private sealed class FixedCancellationSource : IRecipeCreateCancellationSource
        {
            private readonly bool _cancelled;

            public FixedCancellationSource(bool cancelled) => _cancelled = cancelled;

            public bool IsCancellationRequested => _cancelled;
        }

        private sealed class FixedResultResolver : IImageDigestResolver
        {
            private readonly ImageDigestResolutionResult _result;

            public FixedResultResolver(ImageDigestResolutionResult result) => _result = result;

            public System.Threading.Tasks.Task<ImageDigestResolutionResult> ResolveAsync(
                string imageUri, System.Threading.CancellationToken cancellationToken)
            {
                _ = imageUri;
                _ = cancellationToken;
                return System.Threading.Tasks.Task.FromResult(_result);
            }
        }
    }
}
