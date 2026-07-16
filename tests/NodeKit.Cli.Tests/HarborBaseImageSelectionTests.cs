using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Authoring.Recipes;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// Regression coverage for Issue #49: BaseImageCatalog's host-less public
    /// candidates couldn't be resolved through HarborImageDigestResolver at
    /// all (closed-net "base image 자동 조회" always failed). Exercises the
    /// fix end-to-end through RecipeCreateInteractiveRunner with a fake Harbor
    /// HTTP layer (HarborImageDigestResolver.CreateForTest), so no live
    /// network is used.
    /// </summary>
    public class HarborBaseImageSelectionTests : IDisposable
    {
        private static readonly IRecipeCreateCancellationSource _noCancellation =
            new FixedCancellationSource(false);

        private const string ExpectedDigest =
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private readonly string _workDir =
            Path.Join(Path.GetTempPath(), "nodekit-harbor-base-image-tests-" + Guid.NewGuid());

        private readonly string? _originalMap = Environment.GetEnvironmentVariable("NODEKIT_HARBOR_IMAGE_MAP");

        public HarborBaseImageSelectionTests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
            Environment.SetEnvironmentVariable("NODEKIT_HARBOR_IMAGE_MAP", _originalMap);
        }

        [Fact]
        public void Step4_PackageMethod_MappedHarborResolver_SavesConcreteHarborReference_NotPublicOne()
        {
            Environment.SetEnvironmentVariable("NODEKIT_HARBOR_IMAGE_MAP", "docker.io=harbor.lab.local/dockerhub-proxy");

            var outPath = Path.Join(_workDir, "recipe.json");
            using var innerResolver = HarborImageDigestResolver.CreateForTest(
                "https://harbor.lab.local", new FixedDigestHandler(ExpectedDigest));
            var resolver = new MappedHarborImageDigestResolver(innerResolver);

            var transcript = new[]
            {
                "2",    // 빠른 설정 모드
                "y",    // IsRestrictedNetwork — 폐쇄망
                "n",    // HasInternalPackageMirror
                "n",    // HasExistingContainerImage
                "y",    // HasPackageInPublicChannels
                "n",    // HasSourceArchiveAndChecksum
                "n",    // HasExistingDockerfile
                "2",    // 폐쇄망은 package를 자동 추천하지 않으므로 수동 선택
                "bioconda", "",
                "1",    // step 4: candidate [1] condaforge/miniforge3
                "bwa-mem", "0.7.17", "run.sh",
                "bwa=0.7.17=h5bf99c6_8", "",
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = RecipeCreateInteractiveRunner.Run(
                outPath,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                _noCancellation,
                resolveClient: NullResolveRecipeClient.Instance,
                imageDigestResolver: resolver);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());
            Assert.True(File.Exists(outPath));

            var json = File.ReadAllText(outPath);
            Assert.Contains(
                $"harbor.lab.local/dockerhub-proxy/condaforge/miniforge3:24.3.0-0@{ExpectedDigest}",
                json,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                $"\"BaseImage\": \"condaforge/miniforge3:24.3.0-0@{ExpectedDigest}\"",
                json,
                StringComparison.Ordinal);
            Assert.Contains("\"PackageEngine\": \"conda\"", json, StringComparison.Ordinal);
        }

        [Fact]
        public void Step4_MicromambaCandidate_MappedHarborResolver_SetsMicromambaEngine()
        {
            Environment.SetEnvironmentVariable("NODEKIT_HARBOR_IMAGE_MAP", "docker.io=harbor.lab.local/dockerhub-proxy");

            var outPath = Path.Join(_workDir, "recipe.json");
            using var innerResolver = HarborImageDigestResolver.CreateForTest(
                "https://harbor.lab.local", new FixedDigestHandler(ExpectedDigest));
            var resolver = new MappedHarborImageDigestResolver(innerResolver);

            var transcript = new[]
            {
                "2", "y", "n", "n", "y", "n", "n",
                "2",
                "bioconda", "",
                "2",    // step 4: candidate [2] mambaorg/micromamba
                "bwa-mem", "0.7.17", "run.sh",
                "bwa=0.7.17=h5bf99c6_8", "",
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = RecipeCreateInteractiveRunner.Run(
                outPath,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                _noCancellation,
                resolveClient: NullResolveRecipeClient.Instance,
                imageDigestResolver: resolver);

            Assert.Equal(0, exitCode);
            var json = File.ReadAllText(outPath);
            Assert.Contains(
                $"harbor.lab.local/dockerhub-proxy/mambaorg/micromamba:1.5.8@{ExpectedDigest}",
                json,
                StringComparison.Ordinal);
            Assert.Contains("\"PackageEngine\": \"micromamba\"", json, StringComparison.Ordinal);
        }

        [Fact]
        public void Step4_HarborConfiguredWithoutMapping_DoesNotClaimAutoResolve_AndMakesNoHttpCall()
        {
            Environment.SetEnvironmentVariable("NODEKIT_HARBOR_IMAGE_MAP", null);

            var outPath = Path.Join(_workDir, "recipe.json");
            using var innerResolver = HarborImageDigestResolver.CreateForTest(
                "https://harbor.lab.local", new NoCallHandler());
            var resolver = new MappedHarborImageDigestResolver(innerResolver);

            var transcript = new[]
            {
                "2", "y", "n", "n", "y", "n", "n",
                "2",
                "bioconda", "",
                "0",    // 자동 조회가 불가능하니 직접 입력으로
                "/cancel",
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            RecipeCreateInteractiveRunner.Run(
                outPath,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                _noCancellation,
                resolveClient: NullResolveRecipeClient.Instance,
                imageDigestResolver: resolver);

            var output = stdout.ToString();
            Assert.DoesNotContain("Digest는 자동으로 조회합니다", output, StringComparison.Ordinal);
            Assert.Contains("NODEKIT_HARBOR_IMAGE_MAP", output, StringComparison.Ordinal);
            // NoCallHandler would have thrown (surfacing as a NetworkUnavailable
            // result / unhandled exception) if ResolveAsync ever reached HTTP.
        }

        [Fact]
        public void Step4_ManualEntry_HarborTagReference_AutoResolvesDigest()
        {
            Environment.SetEnvironmentVariable("NODEKIT_HARBOR_IMAGE_MAP", null);

            var outPath = Path.Join(_workDir, "recipe.json");
            using var innerResolver = HarborImageDigestResolver.CreateForTest(
                "https://harbor.lab.local", new FixedDigestHandler(ExpectedDigest));
            var resolver = new MappedHarborImageDigestResolver(innerResolver);

            var transcript = new[]
            {
                "2", "y", "n", "n", "y", "n", "n",
                "2",
                "bioconda", "",
                "0",    // base image: 직접 입력
                "bwa-mem", "0.7.17", "run.sh",
                "harbor.lab.local/library/bwa-mem:1.0", // BaseImage: no digest — auto-resolve attempt
                "",     // confirm resolved digest [Y/n] — Enter = Y
                "bwa=0.7.17=h5bf99c6_8", "",
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = RecipeCreateInteractiveRunner.Run(
                outPath,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                _noCancellation,
                resolveClient: NullResolveRecipeClient.Instance,
                imageDigestResolver: resolver);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            var json = File.ReadAllText(outPath);
            Assert.Contains($"harbor.lab.local/library/bwa-mem:1.0@{ExpectedDigest}", json, StringComparison.Ordinal);
        }

        [Fact]
        public void GuidedBeginnerMode_ContainerClue_HarborReference_ResolvesSameDigestAsQuickSetup()
        {
            // Issue #49 requirement 4: both modes must produce the same result
            // for the same underlying resolver — both now route through the
            // shared ImageDigestAutoResolveHelper, so this proves parity for
            // BeginnerGuideFlow's own auto-resolve path (independent of
            // RecipeCreateInteractiveRunner's mode selection).
            using var innerResolver = HarborImageDigestResolver.CreateForTest(
                "https://harbor.lab.local", new FixedDigestHandler(ExpectedDigest));
            var resolver = new MappedHarborImageDigestResolver(innerResolver);

            var transcript = new[]
            {
                "3",    // 컨테이너 이미지가 있다
                "harbor.lab.local/library/bwa-mem:1.0", // no digest
                "",     // confirm resolved digest [Y/n] — Enter = Y
            };

            var session = new RecipeAuthoringSession();
            var console = new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), new StringWriter());

            var method = BeginnerGuideFlow.Run(session, console, _noCancellation, resolver);

            Assert.Equal(RecipeMethodId.Container, method);
            var imageRefValue = session.Snapshot().Values
                .FirstOrDefault(v => v.FieldName == "ImageRef")?.DisplayValue;
            var digestValue = session.Snapshot().Values
                .FirstOrDefault(v => v.FieldName == "ImageDigest")?.DisplayValue;

            Assert.Equal("harbor.lab.local/library/bwa-mem:1.0", imageRefValue);
            Assert.Equal(ExpectedDigest, digestValue);
        }

        private sealed class FixedCancellationSource : IRecipeCreateCancellationSource
        {
            private readonly bool _cancelled;

            public FixedCancellationSource(bool cancelled) => _cancelled = cancelled;

            public bool IsCancellationRequested => _cancelled;
        }

        private sealed class NoCallHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("HTTP 요청이 발생해서는 안 됩니다.");
            }
        }

        private sealed class FixedDigestHandler : HttpMessageHandler
        {
            private readonly string _digest;

            public FixedDigestHandler(string digest)
            {
                _digest = digest;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                // HttpMessageHandler.SendAsync's contract hands ownership of the returned
                // response to the caller (HttpClient), which disposes it after reading —
                // disposing it here before returning would break the very object being handed back.
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Headers.Add("Docker-Content-Digest", _digest);
                return Task.FromResult(response);
            }
        }
    }
}
