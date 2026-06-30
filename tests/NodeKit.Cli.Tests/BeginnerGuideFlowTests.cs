using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Authoring.Recipes;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// Transcript tests for BeginnerGuideFlow (Sections 8.2–14 of the UX v0.9.2 doc).
    /// Drives CliApp.Run with mode "1" (GuidedBeginner) and verifies per-clue
    /// sub-flow behavior, pre-population, and the safe-exit path.
    /// </summary>
    public class BeginnerGuideFlowTests : IDisposable
    {
        private const string ContainerImageRef =
            "quay.io/biocontainers/bwa:0.7.17--h7132678_9";

        private const string Digest =
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private const string ContainerWithDigest = ContainerImageRef + "@" + Digest;

        private const string BaseImageWithDigest =
            "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private readonly string _workDir = Path.Combine(
            Path.GetTempPath(), "nodekit-beginner-guide-tests-" + Guid.NewGuid());

        private static readonly NoOpCancellationSource NoCancellation = new();

        public BeginnerGuideFlowTests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
        }

        // ── Safe-exit paths (아무것도 모름) ─────────────────────────────────────

        [Fact]
        public void CancelCommand_AtGuidedCluePicker_ExitsWithCode130WithoutFile()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",       // GuidedBeginner
                "/cancel", // clue picker
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out var stderr);

            Assert.Equal(130, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Empty(stderr);
            Assert.Contains("recipe 생성을 취소했습니다.", stdout);
            Assert.Contains("파일은 저장되지 않았습니다.", stdout);
        }

        [Fact]
        public void NoClue_ExitsWithCode0WithoutFile()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",  // GuidedBeginner
                "7",  // 잘 모르겠다 → NoClue flow
                "6",  // 저장하지 않고 종료 → return null
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(0, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("단서가 부족합니다", stdout);
            Assert.Contains("현재 NodeKit CLI는 외부 검색이나 NodeVault 조회를 하지 않습니다.", stdout);
            Assert.DoesNotContain("v0.9.2", stdout);
        }

        [Fact]
        public void ToolName_ThenNoClue_ExitsWithCode0WithoutFile()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",    // GuidedBeginner
                "1",    // clue: 도구 이름만 알고 있다
                "bwa",  // 도구 이름
                "6",    // 아무것도 모른다 → NoClue flow
                "6",    // 저장하지 않고 종료
            };

            var exitCode = RunCli(outPath, transcript, out _, out _);

            Assert.Equal(0, exitCode);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void NoClue_InvalidInputThenExit_RepromptsAndExits()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",    // GuidedBeginner
                "7",    // 잘 모르겠다 → NoClue flow
                "9",    // invalid — re-prompt
                "6",    // 저장하지 않고 종료
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(0, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("1–6 중에서 선택", stdout);
        }

        [Fact]
        public void RunToolNameFlow_PrintsBiocondaAndBioContainersUrls()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",    // GuidedBeginner
                "1",    // clue: 도구 이름만 알고 있다
                "bwa",  // 도구 이름
                "6",    // 아무것도 모른다 → NoClue flow
                "6",    // 저장하지 않고 종료
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(0, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("https://anaconda.org/bioconda/bwa", stdout);
            Assert.Contains("https://quay.io/repository/biocontainers/bwa?tab=tags", stdout);
        }

        [Fact]
        public void RunToolNameFlow_EmptyToolName_AsksAgain()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",    // GuidedBeginner
                "1",    // clue: 도구 이름만 알고 있다
                "",     // empty → ask again
                "bwa",
                "6",    // 아무것도 모른다 → NoClue flow
                "6",    // 저장하지 않고 종료
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(0, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("도구 이름을 입력해 주세요.", stdout);
            Assert.Contains("https://anaconda.org/bioconda/bwa", stdout);
        }

        [Fact]
        public void RunNoClueFlow_CanRouteToToolNameLookup()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",    // GuidedBeginner
                "7",    // 잘 모르겠다 → NoClue flow
                "1",    // tool-name lookup
                "bwa",
                "6",    // 아무것도 모른다 → NoClue flow
                "6",    // 저장하지 않고 종료
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(0, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("https://anaconda.org/bioconda/bwa", stdout);
            Assert.Contains("https://quay.io/repository/biocontainers/bwa?tab=tags", stdout);
        }

        // ── Install command clue ────────────────────────────────────────────────

        [Fact]
        public void InstallCommand_ParsedCommand_PrePopulatesAndSavesRecipe()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",   // GuidedBeginner
                "2",   // 설치 명령
                "conda install -c bioconda bwa=0.7.17=h5bf99c6_8 -y",
                "1",   // use understood values
                // RunFieldLoop:
                "bwa-mem", "0.7.17", "run.sh",
                BaseImageWithDigest,  // ImageRef (BaseImage for Package method)
                // Packages: pre-filled & completed → skipped
                // Channels: pre-filled & completed → skipped
                // PackageEngine: Defaulted → skipped
                "reads", "1", "",
                "bam", "1", "",
            };

            var exitCode = RunCli(outPath, transcript, out _, out var stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);
            Assert.True(File.Exists(outPath));

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"PackageEngine\": \"conda\"", json);
            Assert.Contains("bwa=0.7.17=h5bf99c6_8", json);
            Assert.Contains("bioconda", json);
        }

        [Fact]
        public void InstallCommand_FailedCommand_CanSwitchToManualPackage()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",            // GuidedBeginner
                "2",            // 설치 명령
                "pip install bwa==0.7.17",  // Failed
                "1",            // package 방식으로 계속 → no pre-population
                // RunFieldLoop (all Package fields must be filled):
                "bwa-mem", "0.7.17", "run.sh",
                BaseImageWithDigest,         // ImageRef
                "bwa=0.7.17=h5bf99c6_8", "", // Packages
                "bioconda", "",              // Channels
                "",                          // PackageEngine (Defaulted — skipped)
                "reads", "1", "",
                "bam", "1", "",
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            Assert.Contains("자동으로 이해하지 못했습니다", stdout);
        }

        [Fact]
        public void InstallCommand_PartiallyParsed_ShowsMissingFields()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",   // GuidedBeginner
                "2",   // 설치 명령
                "conda install bwa=0.7.17=h5bf99c6_8",  // PartiallyParsed: no channel
                "1",   // use understood values (channels missing → RunFieldLoop asks)
                // RunFieldLoop (Channels missing → will be asked):
                "bwa-mem", "0.7.17", "run.sh",
                BaseImageWithDigest,
                // Packages: pre-filled
                "bioconda", "",  // Channels: NOT pre-filled, must be entered
                // PackageEngine: Defaulted → skipped
                "reads", "1", "",
                "bam", "1", "",
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            Assert.Contains("추가로 필요한 값", stdout);
            Assert.Contains("Channels", stdout);
        }

        [Fact]
        public void InstallCommand_SwitchMethod_GoesBackToCluePicker()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",    // GuidedBeginner
                "2",    // 설치 명령
                "pip install bwa",  // Failed
                "3",    // 다른 작성 방식 → back to clue picker
                // Clue picker again:
                "3",    // container
                ContainerWithDigest,
                "bwa-mem", "0.7.17", "run.sh",
                "",     // Command (optional)
                "reads", "1", "",
                "bam", "1", "",
            };

            var exitCode = RunCli(outPath, transcript, out _, out _);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"BioContainer\"", json);
        }

        // ── Container image clue ──────────────────────────────────────────────

        [Fact]
        public void ContainerClue_WithEmbeddedDigest_SavesValidRecipe()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",                // GuidedBeginner
                "3",                // container image
                ContainerWithDigest, // embedded digest → Normalized
                // RunFieldLoop (ImageRef + ImageDigest pre-filled):
                "bwa-mem", "0.7.17", "run.sh",
                "",                 // Command (optional)
                "reads", "1", "",
                "bam", "1", "",
            };

            var exitCode = RunCli(outPath, transcript, out _, out var stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);
            Assert.True(File.Exists(outPath));

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"BioContainer\"", json);
            Assert.Contains(ContainerImageRef, json);
        }

        [Fact]
        public void ContainerClue_NoDigest_ThenReenter_SavesValidRecipe()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",                    // GuidedBeginner
                "3",                    // container image
                ContainerImageRef,      // no digest → MissingDigest
                "1",                    // re-enter with digest
                ContainerWithDigest,    // now has digest → Normalized
                // RunFieldLoop:
                "bwa-mem", "0.7.17", "run.sh",
                "",                     // Command (optional)
                "reads", "1", "",
                "bam", "1", "",
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            Assert.Contains("digest가 없습니다", stdout);

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"BioContainer\"", json);
        }

        [Fact]
        public void ContainerClue_NoDigest_ThenSeparateDigest_SavesValidRecipe()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",                // GuidedBeginner
                "3",                // container image
                ContainerImageRef,  // no digest
                "2",                // input ImageDigest separately
                Digest,             // separate digest
                // RunFieldLoop:
                "bwa-mem", "0.7.17", "run.sh",
                "",                 // Command
                "reads", "1", "",
                "bam", "1", "",
            };

            var exitCode = RunCli(outPath, transcript, out _, out _);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"BioContainer\"", json);
        }

        [Fact]
        public void ContainerClue_MissingDigest_SwitchMethod_GoesBackToCluePicker()
        {
            // Verifies that [3] 다른 작성 방식 from MissingDigest screen loops back to clue picker.
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",                // GuidedBeginner
                "3",                // container image
                ContainerImageRef,  // no digest → MissingDigest
                "3",                // 다른 작성 방식 → back to clue picker
                "7",                // 잘 모르겠다 → NoClue
                "6",                // exit
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(0, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("digest가 없습니다", stdout);
        }

        // ── Source clue ───────────────────────────────────────────────────────

        [Fact]
        public void SourceClue_WithChecksum_SavesValidRecipe()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",  // GuidedBeginner
                "4",  // source
                "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                // RunFieldLoop (SourceUri + SourceChecksum pre-filled):
                "bwa-mem", "0.7.17", "run.sh",
                BaseImageWithDigest,  // ImageRef (BaseImage for Source method)
                // SourceUri: pre-filled
                // SourceChecksum: pre-filled
                "make", "make install", "",  // SourceBuildCommands
                "",                          // BuildDependencies (Recommended → skipped)
                "reads", "1", "",
                "bam", "1", "",
            };

            var exitCode = RunCli(outPath, transcript, out _, out _);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"SourceBuild\"", json);
        }

        [Fact]
        public void SourceFlow_MissingChecksum_PrintsCurlSha256sumGuidance()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var sourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz";
            var transcript = new[]
            {
                "1",       // GuidedBeginner
                "4",       // source
                sourceUri,
                "",        // missing checksum
                "1",       // show guidance
                "4",       // 저장하지 않고 종료
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(0, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains($"curl -fsSL \"{sourceUri}\" | sha256sum", stdout);
            Assert.DoesNotContain("draft", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("나중에 추가", stdout);
        }

        [Fact]
        public void ContainerImageFlow_WhenResolverReturnsDigest_AsksToUseDigest()
        {
            var session = new RecipeAuthoringSession();
            var stdout = new StringWriter();
            var stdin = new StringReader(string.Join("\n", new[]
            {
                "3",               // container image
                ContainerImageRef, // no digest
                "",                // accept resolved digest
            }));

            var method = BeginnerGuideFlow.Run(
                session,
                new PlainTextRecipeConsole(stdin, stdout),
                NoCancellation,
                new FakeDigestResolver(ImageDigestResolutionResult.Resolved(Digest)));

            Assert.Equal(RecipeMethodId.Container, method);
            Assert.Contains("이 digest를 사용할까요", stdout.ToString());
            var snapshot = session.Snapshot();
            Assert.Contains(snapshot.Values, v => v.FieldName == "ImageDigest" && v.DisplayValue == Digest);
        }

        [Fact]
        public void ContainerImageFlow_WhenResolverUnsupported_FallsBackToManualDigestInput()
        {
            var session = new RecipeAuthoringSession();
            var stdout = new StringWriter();
            var stdin = new StringReader(string.Join("\n", new[]
            {
                "3",               // container image
                ContainerImageRef, // no digest
                "2",               // manual digest
                Digest,
            }));

            var method = BeginnerGuideFlow.Run(
                session,
                new PlainTextRecipeConsole(stdin, stdout),
                NoCancellation,
                NullImageDigestResolver.Instance);

            Assert.Equal(RecipeMethodId.Container, method);
            Assert.Contains("현재 환경에서는 자동 조회를 사용할 수 없습니다", stdout.ToString());
            Assert.Contains("ImageDigest:", stdout.ToString());
        }

        [Fact]
        public void ContainerImageFlow_WhenResolverFails_PrintsHumanReadableReason()
        {
            var session = new RecipeAuthoringSession();
            var stdout = new StringWriter();
            var stdin = new StringReader(string.Join("\n", new[]
            {
                "3",               // container image
                ContainerImageRef, // no digest
                "2",               // manual digest
                Digest,
            }));

            var method = BeginnerGuideFlow.Run(
                session,
                new PlainTextRecipeConsole(stdin, stdout),
                NoCancellation,
                new FakeDigestResolver(ImageDigestResolutionResult.NotFound()));

            Assert.Equal(RecipeMethodId.Container, method);
            Assert.Contains("이미지를 찾을 수 없습니다", stdout.ToString());
        }

        [Fact]
        public void ContainerImageFlow_WhenUserRejectsResolvedDigest_AsksManualDigest()
        {
            var session = new RecipeAuthoringSession();
            var stdout = new StringWriter();
            var stdin = new StringReader(string.Join("\n", new[]
            {
                "3",               // container image
                ContainerImageRef, // no digest
                "n",               // reject resolved digest
                "2",               // manual digest
                Digest,
            }));

            var method = BeginnerGuideFlow.Run(
                session,
                new PlainTextRecipeConsole(stdin, stdout),
                NoCancellation,
                new FakeDigestResolver(ImageDigestResolutionResult.Resolved(Digest)));

            Assert.Equal(RecipeMethodId.Container, method);
            Assert.Contains("직접 digest를 입력합니다", stdout.ToString());
            Assert.Contains("ImageDigest:", stdout.ToString());
        }

        // ── Dockerfile clue ───────────────────────────────────────────────────

        [Fact]
        public void DockerfileClue_AcceptWarning_SavesValidRecipe()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",           // GuidedBeginner
                "5",           // Dockerfile
                "./Dockerfile",
                "y",           // confirm warning
                // RunFieldLoop (DockerfilePath pre-filled by BeginnerGuideFlow):
                "bwa-mem", "0.7.17", "run.sh",
                BaseImageWithDigest,           // ImageRef (BaseImage for Dockerfile method)
                // DockerfilePath: pre-filled → skipped
                $"FROM {BaseImageWithDigest}", // DockerfileContent (Required — still asked)
                "",                            // BuildContext (Defaulted → skipped)
                "reads", "1", "",
                "bam", "1", "",
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            Assert.Contains("Dockerfile fallback", stdout);
            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"DockerfileFallback\"", json);
        }

        [Fact]
        public void DockerfileClue_RejectWarning_GoesBackToCluePicker()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",           // GuidedBeginner
                "5",           // Dockerfile
                "./Dockerfile",
                "N",           // reject warning → back to clue picker
                "7",           // 잘 모르겠다 → NoClue flow
                "6",           // exit
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(0, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("Dockerfile fallback", stdout);
        }

        // ── Mirror clue ───────────────────────────────────────────────────────

        [Fact]
        public void MirrorClue_SavesValidRecipe()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",  // GuidedBeginner
                "6",  // internal mirror
                "https://mirror.internal/conda",
                // RunFieldLoop (MirrorUri pre-filled):
                "bwa-mem", "0.7.17", "run.sh",
                BaseImageWithDigest,            // ImageRef (BaseImage for Mirror method)
                // MirrorUri: pre-filled
                "bwa=0.7.17=h5bf99c6_8", "",   // Packages
                "",                              // MirrorKind (Optional → skip)
                "reads", "1", "",
                "bam", "1", "",
            };

            var exitCode = RunCli(outPath, transcript, out _, out _);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            var json = File.ReadAllText(outPath);
            Assert.Contains("mirror.internal", json);
        }

        // ── Clue picker validation ────────────────────────────────────────────

        [Fact]
        public void InvalidClueChoice_RepromptsUntilValid()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",    // GuidedBeginner
                "8",    // invalid → re-prompt
                "abc",  // invalid → re-prompt
                "7",    // 잘 모르겠다
                "6",    // exit
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(0, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("1–7 중에서 선택", stdout);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static int RunCli(string outPath, string[] transcript, out string stdout, out string stderr)
        {
            var stdoutWriter = new StringWriter();
            var stderrWriter = new StringWriter();
            var exitCode = CliApp.Run(
                new[] { "recipe", "create", outPath },
                new StringReader(string.Join("\n", transcript)),
                stdoutWriter,
                stderrWriter);
            stdout = stdoutWriter.ToString();
            stderr = stderrWriter.ToString();
            return exitCode;
        }

        private sealed class NoOpCancellationSource : IRecipeCreateCancellationSource
        {
            public bool IsCancellationRequested => false;
        }

        private sealed class FakeDigestResolver : IImageDigestResolver
        {
            private readonly ImageDigestResolutionResult _result;

            public FakeDigestResolver(ImageDigestResolutionResult result)
            {
                _result = result;
            }

            public Task<ImageDigestResolutionResult> ResolveAsync(
                string imageUri,
                CancellationToken cancellationToken)
            {
                _ = imageUri;
                _ = cancellationToken;
                return Task.FromResult(_result);
            }
        }
    }
}
