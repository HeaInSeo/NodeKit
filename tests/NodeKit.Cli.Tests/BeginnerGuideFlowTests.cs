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

        private static readonly NoOpCancellationSource _noCancellation = new();

        private readonly IDisposable _resolveClientOverride =
            ResolveRecipeClientTestOverride.Use(NullResolveRecipeClient.Instance);

        public BeginnerGuideFlowTests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
            _resolveClientOverride.Dispose();
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
                "",    // 채널 확인: 파싱된 "bioconda" 그대로 사용 (Enter)
                "0",  // 기반 이미지: 직접 입력
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
                "bioconda", "",              // Channels (채널 확정 단계, RunFieldLoop 이전)
                "0",                        // 기반 이미지: 직접 입력
                // RunFieldLoop (all Package fields must be filled):
                "bwa-mem", "0.7.17", "run.sh",
                BaseImageWithDigest,         // ImageRef
                "bwa=0.7.17=h5bf99c6_8", "", // Packages
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
                "1",   // use understood values (channels missing → 채널 확정 단계에서 입력)
                "bioconda", "",  // Channels: NOT pre-filled, 채널 확정 단계에서 입력
                "0",            // 기반 이미지: 직접 입력
                // RunFieldLoop:
                "bwa-mem", "0.7.17", "run.sh",
                BaseImageWithDigest,
                // Packages: pre-filled
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
        public void SourceClue_WithChecksum_SavesValidSourceStructuredRecipe()
        {
            // Adversarial review Major-1 follow-up (Issue #43): the guided
            // beginner source-clue path used to select legacy
            // RecipeBuildKind.SourceBuild — rejected almost unconditionally
            // by NodeVault's Sprint 9 policy (#41). It now selects
            // SourceStructured with the curated generic/minimal profiles
            // pre-filled (BeginnerGuideFlow.SelectSourceStructured), so
            // there's no separate build/runtime-environment question for a
            // beginner to answer, and — since SourceStructured has no
            // BaseImage field — the base-image-selection step no longer
            // appears at all for this path.
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",  // GuidedBeginner
                "4",  // source
                "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                // RunFieldLoop (SourceUri/SourceChecksum/BuildProfile/RuntimeProfile
                // pre-filled — NextField() skips them; no BaseImage field at all):
                "bwa-mem", "0.7.17", "run.sh",
                "",                           // BuildProfileImage (advanced-only, optional) — skip
                "make", "make install", "",   // SourceBuildCommands
                "",                           // BuildDependencies (Recommended → skipped)
                "",                           // RuntimeProfileImage (advanced-only, optional) — skip
                "",                           // RuntimeDependencies (Recommended → skipped)
                "reads", "1", "",
                "bam", "1", "",
            };

            var exitCode = RunCli(outPath, transcript, out _, out _);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"SourceBuildStructured\"", json);
            Assert.Contains("\"BuildProfile\": \"generic\"", json);
            Assert.Contains("\"RuntimeProfile\": \"minimal\"", json);
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
            using var stdout = new StringWriter();
            var stdin = new StringReader(string.Join("\n", new[]
            {
                "3",               // container image
                ContainerImageRef, // no digest
                "",                // accept resolved digest
            }));

            var method = BeginnerGuideFlow.Run(
                session,
                new PlainTextRecipeConsole(stdin, stdout),
                _noCancellation,
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
            using var stdout = new StringWriter();
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
                _noCancellation,
                NullImageDigestResolver.Instance);

            Assert.Equal(RecipeMethodId.Container, method);
            Assert.Contains("현재 환경에서는 자동 조회를 사용할 수 없습니다", stdout.ToString());
            Assert.Contains("ImageDigest:", stdout.ToString());
        }

        [Fact]
        public void ContainerImageFlow_WhenResolverFails_PrintsHumanReadableReason()
        {
            var session = new RecipeAuthoringSession();
            using var stdout = new StringWriter();
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
                _noCancellation,
                new FakeDigestResolver(ImageDigestResolutionResult.NotFound()));

            Assert.Equal(RecipeMethodId.Container, method);
            Assert.Contains("이미지를 찾을 수 없습니다", stdout.ToString());
        }

        [Fact]
        public void ContainerImageFlow_WhenUserRejectsResolvedDigest_AsksManualDigest()
        {
            var session = new RecipeAuthoringSession();
            using var stdout = new StringWriter();
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
                _noCancellation,
                new FakeDigestResolver(ImageDigestResolutionResult.Resolved(Digest)));

            Assert.Equal(RecipeMethodId.Container, method);
            Assert.Contains("직접 digest를 입력합니다", stdout.ToString());
            Assert.Contains("ImageDigest:", stdout.ToString());
        }

        // ── Dockerfile clue ───────────────────────────────────────────────────

        [Fact]
        public void DockerfileClue_AcceptWarning_SavesValidRecipe()
        {
            // Issue #20 (DockGuard DSF001 parity for dockerfile fallback) made
            // USER a final-validation requirement, which briefly made this
            // scenario unreachable interactively — Dockerfile syntax needs
            // each instruction on its own line, but PromptScalarField only
            // ever read a single line per field. Fixed by adding multi-line
            // support (PromptMultilineScalarField) for DockerfileContent
            // specifically. This transcript exercises that: two separate
            // lines (FROM, USER) for the one DockerfileContent prompt.
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
                $"FROM {BaseImageWithDigest}", // DockerfileContent line 1
                "USER 1000",                   // DockerfileContent line 2
                "",                            // DockerfileContent: blank line ends multi-line input
                // BuildContext (Defaulted → skipped)
                "reads", "1", "",
                "bam", "1", "",
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            Assert.Contains("Dockerfile fallback", stdout);
            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"DockerfileFallback\"", json);
            Assert.Contains("USER 1000", json);
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
                "0",                             // 기반 이미지: 직접 입력
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

        // ── Issue #12 regression: stdin EOF must cancel, not loop forever ──────
        // Same bug class as #10/#11: a retry loop that only checks the trimmed
        // value can't tell true stdin EOF from a genuine blank Enter, so it
        // retries forever once there is no more input to satisfy a required
        // prompt. Confirmed empirically pre-fix (one probe produced 3.5M lines
        // in 5 seconds). Fixed via ReadTrimmedLineOrNull, which escalates to
        // RecipeCreateCancelledException only on a true null read.

        [Fact]
        public void CluePicker_StdinEndsWithoutChoice_CancelsInsteadOfLooping()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[] { "1" }; // GuidedBeginner, then EOF at the clue picker itself

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(130, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("recipe 생성을 취소했습니다.", stdout);
        }

        [Fact]
        public void ToolNameFlow_StdinEndsAtNamePrompt_CancelsInsteadOfLooping()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[] { "1", "1" }; // GuidedBeginner, clue: tool name, then EOF

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(130, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("recipe 생성을 취소했습니다.", stdout);
        }

        [Fact]
        public void InstallCommandFlow_StdinEndsAtFailedParseChoice_CancelsInsteadOfLooping()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[] { "1", "2", "notarealcommand" }; // EOF at the failed-parse choice prompt

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(130, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("recipe 생성을 취소했습니다.", stdout);
        }

        [Fact]
        public void SourceFlow_StdinEndsAtChecksumPrompt_CancelsInsteadOfLooping()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[] { "1", "4", "https://example.org/tool.tar.gz" }; // EOF at SourceChecksum prompt

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(130, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("recipe 생성을 취소했습니다.", stdout);
        }

        [Fact]
        public void ContainerFlow_StdinEndsAtSeparateDigestPrompt_CancelsInsteadOfLooping()
        {
            // The trickiest site: an ad-hoc read embedded inside the outer
            // while(true) (not its own loop) after the user picks "직접 입력"
            // (2) for a missing digest. Confirmed pre-fix this reached the
            // digest prompt cleanly but then hung forever once stdin ran dry.
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[] { "1", "3", ContainerImageRef, "2" }; // EOF right at "ImageDigest:"

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(130, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("recipe 생성을 취소했습니다.", stdout);
        }

        [Fact]
        public void MirrorFlow_StdinEndsAtUriPrompt_CancelsInsteadOfLooping()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[] { "1", "6" }; // GuidedBeginner, clue: internal mirror, then EOF

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(130, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("recipe 생성을 취소했습니다.", stdout);
        }

        [Fact]
        public void DockerfileFlow_StdinEndsAtPathPrompt_CancelsInsteadOfLooping()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[] { "1", "5" }; // GuidedBeginner, clue: Dockerfile, then EOF

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(130, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("recipe 생성을 취소했습니다.", stdout);
        }

        [Fact]
        public void NoClueFlow_StdinEndsWithoutChoice_CancelsInsteadOfLooping()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[] { "1", "7" }; // GuidedBeginner, 잘 모르겠다, then EOF at NoClue's choice

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(130, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("recipe 생성을 취소했습니다.", stdout);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static int RunCli(string outPath, string[] transcript, out string stdout, out string stderr)
        {
            using var stdoutWriter = new StringWriter();
            using var stderrWriter = new StringWriter();
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
