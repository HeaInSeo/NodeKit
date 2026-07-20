using System;
using System.IO;
using Grpc.Core;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// Golden transcript tests for the interactive `nodekit recipe create`
    /// wizard — design doc Section 31.13. Drives CliApp.Run with a scripted
    /// stdin transcript and checks the resulting saved RecipeDocument JSON.
    /// </summary>
    public class RecipeCreateInteractiveTests : IDisposable
    {
        private const string ImageRefWithDigest =
            "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private const string DigestOnly =
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private readonly string _workDir = Path.Join(Path.GetTempPath(), "nodekit-recipe-interactive-tests-" + Guid.NewGuid());
        private readonly IDisposable _resolveClientOverride =
            ResolveRecipeClientTestOverride.Use(NullResolveRecipeClient.Instance);

        public RecipeCreateInteractiveTests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
            _resolveClientOverride.Dispose();
        }

        [Fact]
        public void BwaPackageHappyPath_SavesValidRecipe()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", // IsRestrictedNetwork
                "n", // HasInternalPackageMirror
                "n", // HasExistingContainerImage
                "y", // HasPackageInPublicChannels
                "n", // HasSourceArchiveAndChecksum
                "n", // HasExistingDockerfile
                "", // accept recommended method (package)
                "bioconda", // Channels item (채널 확정 단계)
                "", // complete Channels
                "0", // 기반 이미지: 직접 입력
                "bwa-mem", // ToolName
                "0.7.17", // ToolVersion
                "run.sh", // Script
                ImageRefWithDigest, // ImageRef
                "bwa=0.7.17=h5bf99c6_8", // Packages item
                "", // complete Packages
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"PackageEngine\": \"conda\"", json);
            Assert.Contains("\"BuildKind\": \"Conda\"", json);
            Assert.Contains("bwa-mem", json);
        }

        [Fact]
        public void BwaSourceStructuredHappyPath_CuratedProfiles_SavesValidRecipe()
        {
            // Adversarial review Major-1 follow-up (Issue #41): SourceStructured
            // is now wizard-reachable (previously --non-interactive only) and
            // is what the source-archive signal recommends instead of legacy
            // Source. This drives the full interactive field loop — including
            // the BuildProfile/RuntimeProfile Choice fields and their Optional
            // *ProfileImage siblings — end to end, not just unit-level pieces.
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", // IsRestrictedNetwork
                "n", // HasInternalPackageMirror
                "n", // HasExistingContainerImage
                "n", // HasPackageInPublicChannels
                "y", // HasSourceArchiveAndChecksum
                "n", // HasExistingDockerfile
                "", // accept recommended method (source-structured)
                "bwa-mem", // ToolName
                "0.7.17", // ToolVersion
                "run.sh", // Script
                "1", // BuildProfile: curated "generic"
                "", // BuildProfileImage — optional, skip (curated profile chosen)
                "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz", // SourceUri
                DigestOnly, // SourceChecksum
                "make install DESTDIR=/nodekit/output", "", // SourceBuildCommands + complete
                "", // BuildDependencies — recommended, leave empty
                "1", // RuntimeProfile: curated "minimal"
                "", // RuntimeProfileImage — optional, skip (curated profile chosen)
                "", // RuntimeDependencies — recommended, leave empty
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"SourceBuildStructured\"", json);
            Assert.Contains("\"BuildProfile\": \"generic\"", json);
            Assert.Contains("\"RuntimeProfile\": \"minimal\"", json);
            Assert.Contains("bwa-mem", json);
        }

        [Fact]
        public void PackageMethod_VersionOnlyPin_WarnsButStillSaves()
        {
            // §13 R19: confirming a version-only pin (no build string) during
            // interactive Package-method authoring should warn — non-blocking,
            // since L1 still allows it by default — that NodeVault's final
            // gate may reject it later.
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", // IsRestrictedNetwork
                "n", // HasInternalPackageMirror
                "n", // HasExistingContainerImage
                "y", // HasPackageInPublicChannels
                "n", // HasSourceArchiveAndChecksum
                "n", // HasExistingDockerfile
                "", // accept recommended method (package)
                "bioconda", // Channels item
                "", // complete Channels
                "0", // 기반 이미지: 직접 입력
                "bwa-mem", // ToolName
                "0.7.17", // ToolVersion
                "run.sh", // Script
                ImageRefWithDigest, // ImageRef
                "bwa=0.7.17", // Packages item — version-only, no build string
                "", // complete Packages
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            Assert.Contains("버전만 고정되어 있습니다", stdout.ToString());
        }

        [Fact]
        public void DockerfileWarningPath_RequiresAcceptanceAndSavesValidRecipe()
        {
            // Issue #20 (DockGuard DSF001 parity for dockerfile fallback) made
            // USER a final-validation requirement, which briefly made this
            // scenario unreachable interactively — Dockerfile syntax needs
            // each instruction on its own line, but PromptScalarField only
            // ever read a single line per field. Fixed by adding multi-line
            // support (PromptMultilineScalarField, blank line terminates,
            // same convention as StringList fields) for DockerfileContent
            // specifically. This transcript exercises that: two separate
            // lines (FROM, USER) for the one DockerfileContent prompt.
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", // IsRestrictedNetwork
                "n", // HasInternalPackageMirror
                "n", // HasExistingContainerImage
                "n", // HasPackageInPublicChannels
                "n", // HasSourceArchiveAndChecksum
                "y", // HasExistingDockerfile
                "", // accept recommended method (dockerfile)
                "y", // confirm dockerfile warning
                "bwa-mem", // ToolName
                "0.7.17", // ToolVersion
                "run.sh", // Script
                ImageRefWithDigest, // ImageRef
                "./Dockerfile", // DockerfilePath
                $"FROM {ImageRefWithDigest}", // DockerfileContent line 1
                "USER 1000", // DockerfileContent line 2
                "", // DockerfileContent: blank line ends multi-line input
                "reads", "1", "",
                "bam", "1", "",
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("강한 주의", stdout.ToString());
            Assert.Empty(stderr.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildContext\": \".\"", json);
            Assert.Contains("\"BuildKind\": \"DockerfileFallback\"", json);
            Assert.Contains("USER 1000", json);
        }

        [Fact]
        public void Dockerfile_NonInteractive_WithUserInstruction_SavesValidRecipe()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(
                new[]
                {
                    "recipe", "create", outPath,
                    "--non-interactive", "--method", "dockerfile", "--accept-dockerfile-warning",
                    "--field", "ToolName=bwa-mem",
                    "--field", "ToolVersion=0.7.17",
                    "--field", "Script=run.sh",
                    "--field", $"BaseImage={ImageRefWithDigest}",
                    "--field", $"DockerfileContent=FROM {ImageRefWithDigest}\nUSER 1000\n",
                    "--field", "DockerfilePath=./Dockerfile",
                },
                stdout,
                stderr);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"DockerfileFallback\"", json);
        }

        [Fact]
        public void DockerfileContent_StdinEndsMidMultilineInput_CancelsInsteadOfLooping()
        {
            // Same EOF-vs-blank-line bug class as #10/#11/#12: stdin running
            // out mid multi-line accumulation (no blank-line completion
            // signal ever arrives) must cancel immediately rather than
            // spinning forever re-prompting for a line that will never come.
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", "n", "n", "n", "n", "n", "y", "", "y",
                "bwa-mem", "0.7.17", "run.sh",
                ImageRefWithDigest,
                "./Dockerfile",
                $"FROM {ImageRefWithDigest}",
                "USER 1000",
                // stdin ends here — no blank line to terminate DockerfileContent
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(130, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("recipe 생성을 취소했습니다.", stdout.ToString());
        }

        [Fact]
        public void InteractiveAndNonInteractive_ProduceIdenticalRecipeDocument_ForSameLogicalAnswers()
        {
            var interactiveOutPath = Path.Join(_workDir, "interactive.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "n", "y", "n", "n", // Q&A -> recommend package
                "", // accept recommended method
                "bioconda", "", // 채널 확정 단계
                "0", // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh", ImageRefWithDigest,
                "bwa=0.7.17=h5bf99c6_8", "",
            };
            using var interactiveStdout = new StringWriter();
            using var interactiveStderr = new StringWriter();
            var interactiveExitCode = CliApp.Run(
                new[] { "recipe", "create", interactiveOutPath },
                new StringReader(string.Join("\n", transcript)),
                interactiveStdout,
                interactiveStderr);

            var nonInteractiveOutPath = Path.Join(_workDir, "non-interactive.json");
            using var nonInteractiveStdout = new StringWriter();
            using var nonInteractiveStderr = new StringWriter();
            var nonInteractiveExitCode = CliApp.Run(
                new[]
                {
                    "recipe", "create", nonInteractiveOutPath,
                    "--non-interactive", "--method", "package",
                    "--field", "ToolName=bwa-mem",
                    "--field", "ToolVersion=0.7.17",
                    "--field", "Script=run.sh",
                    "--field", $"BaseImage={ImageRefWithDigest}",
                    "--field", "Packages=bwa=0.7.17=h5bf99c6_8",
                    "--field", "Channels=bioconda",
                },
                nonInteractiveStdout,
                nonInteractiveStderr);

            Assert.Equal(0, interactiveExitCode);
            Assert.Equal(0, nonInteractiveExitCode);
            Assert.Equal(WithoutVolatileFields(File.ReadAllText(nonInteractiveOutPath)), WithoutVolatileFields(File.ReadAllText(interactiveOutPath)));
        }

        private static string WithoutVolatileFields(string recipeJson) =>
            System.Text.RegularExpressions.Regex.Replace(recipeJson, "\"(Id|CreatedAt)\": \"[^\"]+\",?\n", string.Empty);

        [Fact]
        public void DockerfileWarningPath_DeclinedCancelsWithoutSaving()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "n", "n", "n", "y", // Q&A -> recommend dockerfile
                "", // accept recommended method
                "n", // decline dockerfile warning
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void RestrictedNetworkGatePath_RecommendsMirror_SavesValidRecipe()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "y", // IsRestrictedNetwork
                "y", // HasInternalPackageMirror
                "n", // HasExistingContainerImage
                "n", // HasPackageInPublicChannels
                "n", // HasSourceArchiveAndChecksum
                "n", // HasExistingDockerfile
                "", // accept recommended method (mirror)
                "0", // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh",
                ImageRefWithDigest, // ImageRef
                "https://mirror.internal/conda-channel", // MirrorUri
                "bwa=0.7.17=h5bf99c6_8", "", // Packages item + complete
                "", // MirrorKind optional — skip
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"PackageMirror\"", json);
        }

        [Fact]
        public void RestrictedNetworkUnknownPath_WarnsOnPackageRecommendation_SavesValidRecipe()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "u", // IsRestrictedNetwork
                "n", // HasInternalPackageMirror
                "n", // HasExistingContainerImage
                "y", // HasPackageInPublicChannels
                "n", // HasSourceArchiveAndChecksum
                "n", // HasExistingDockerfile
                "", // accept recommended method (package)
                "bioconda", "", // 채널 확정 단계
                "0", // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh", ImageRefWithDigest,
                "bwa=0.7.17=h5bf99c6_8", "",
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("내부망인지 확실하지 않다고 답했습니다", stdout.ToString());
            Assert.Empty(stderr.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"Conda\"", json);
        }

        [Fact]
        public void UnknownHeavyAnswers_WithholdsRecommendation_RequiresExplicitMethodSelection()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", // IsRestrictedNetwork
                "n", // HasInternalPackageMirror
                "u", // HasExistingContainerImage
                "u", // HasPackageInPublicChannels
                "u", // HasSourceArchiveAndChecksum
                "u", // HasExistingDockerfile
                "2", // no recommendation — manually pick from fixed menu: [2] package
                "bioconda", "", // 채널 확정 단계
                "0", // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh", ImageRefWithDigest,
                "bwa=0.7.17=h5bf99c6_8", "",
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("추천 보류", stdout.ToString());
            Assert.Empty(stderr.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"Conda\"", json);
        }

        // Issue #10 회귀 테스트: 추천 보류 상태에서 수동 방식 선택 프롬프트에
        // 도달했는데 transcript가 거기서 끝나면(stdin EOF), 예전에는
        // MethodRecommendationPresenter.Present()의 while(true) 루프가 유효한
        // 선택을 영원히 못 받아 무한 재입력 루프에 빠졌다. 지금은 즉시 취소
        // 처리되어야 한다.
        [Fact]
        public void UnknownHeavyAnswers_StdinEndsAtManualMethodPrompt_CancelsInsteadOfLooping()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", // IsRestrictedNetwork
                "n", // HasInternalPackageMirror
                "u", // HasExistingContainerImage
                "u", // HasPackageInPublicChannels
                "u", // HasSourceArchiveAndChecksum
                "u", // HasExistingDockerfile
                // 여기서 transcript가 끝남 — 수동 방식 선택 프롬프트에서 stdin EOF
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(130, exitCode);
            Assert.False(File.Exists(outPath), "취소된 recipe는 저장되면 안 됩니다.");
        }

        // Issue #11 회귀 테스트: 방식은 선택됐지만 필수 채널 입력 단계에서
        // transcript가 끝나면(stdin EOF), PromptChannelEntry의 while(true) 루프가
        // "Channels requires at least one item" 실패를 영원히 반복했다.
        [Fact]
        public void PackageMethodSelected_StdinEndsAtChannelPrompt_CancelsInsteadOfLooping()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "u", "u", "u", "u", // 추천 보류로 유도
                "2", // 수동 선택: package
                // 여기서 transcript가 끝남 — 채널 입력 프롬프트에서 stdin EOF
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(130, exitCode);
            Assert.False(File.Exists(outPath), "취소된 recipe는 저장되면 안 됩니다.");
        }

        // Issue #11 회귀 테스트 (PromptStringListField 쪽): 채널까지는 입력했지만
        // 필수 리스트 필드인 Packages 입력 단계에서 transcript가 끝나면, 범용
        // PromptStringListField의 while(true) 루프도 PromptChannelEntry와 똑같은
        // 이유로 무한 재입력 루프에 빠졌다.
        [Fact]
        public void PackageMethodFields_StdinEndsAtPackagesPrompt_CancelsInsteadOfLooping()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "u", "u", "u", "u", // 추천 보류로 유도
                "2", // 수동 선택: package
                "bioconda", "", // 채널 확정 단계
                "0", // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh", ImageRefWithDigest,
                // 여기서 transcript가 끝남 — Packages 입력 프롬프트에서 stdin EOF
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(130, exitCode);
            Assert.False(File.Exists(outPath), "취소된 recipe는 저장되면 안 됩니다.");
        }

        // Issue #11 회귀 테스트 (PromptScalarField 쪽): 방식을 mirror로 고르고
        // 첫 필수 스칼라 필드(ToolName)에서 transcript가 끝나면(stdin EOF),
        // PromptScalarField의 while(true) 루프도 같은 이유로 무한 재입력
        // 루프에 빠졌다.
        [Fact]
        public void MirrorMethodSelected_StdinEndsAtToolNamePrompt_CancelsInsteadOfLooping()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "u", "u", "u", "u", // 추천 보류로 유도
                "3", // 수동 선택: mirror
                // 여기서 transcript가 끝남 — ToolName(필수 스칼라 필드) 입력에서 stdin EOF
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(130, exitCode);
            Assert.False(File.Exists(outPath), "취소된 recipe는 저장되면 안 됩니다.");
        }

        [Fact]
        public void ChangeMethodMidFieldEntry_PackageToSource_PreservesToolNameAndDiscardsPackageFields()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "n", "y", "n", "n", // Q&A -> recommend package
                "", // accept recommended method
                "bioconda", "", // 채널 확정 단계
                "0", // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh",
                "/change-method", // at the ImageRef prompt, switch away from package
                "4", // source
                "y", // confirm change
                ImageRefWithDigest, // ImageRef, now under source
                "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz", // SourceUri
                DigestOnly, // SourceChecksum
                "make", "make install", "", // SourceBuildCommands + complete
                "", // BuildDependencies — leave empty (Recommended, always complete), skipped
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("유지되는 필드: 도구 이름 (ToolName), 도구 버전 (ToolVersion)", stdout.ToString());
            Assert.Contains("버려지는 필드: 패키지 목록 (Packages), 채널 목록 (Channels), 패키지 엔진 (PackageEngine)", stdout.ToString());
            Assert.Empty(stderr.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"SourceBuild\"", json);
        }

        [Fact]
        public void ChangeMethodAfterCommonFieldsFilled_PackageToMirror_InvalidatedImageRefDoesNotBlockBuild()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "n", "y", "n", "n", // Q&A -> recommend package
                "", // accept recommended method
                "bioconda", "", // 채널 확정 단계 (Package 방식용, Mirror로 바뀌면 버려짐)
                "0", // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh", ImageRefWithDigest,
                "/change-method", // at the Packages prompt — ImageRef is preserved but invalidated
                "3", // mirror
                "y", // confirm change
                "https://mirror.internal/conda-channel", // MirrorUri, now under mirror
                "bwa=0.7.17=h5bf99c6_8", "", // Packages, fresh under mirror
                "", // MirrorKind optional — skip
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"PackageMirror\"", json);
        }

        [Fact]
        public void CrossFieldImageDigestViolation_TriggersEditRelatedFieldsRecovery_FixesAndSaves()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "y", "n", "n", "n", // Q&A -> recommend container
                "", // accept recommended method
                "bwa-mem", "0.7.17", "run.sh",
                "condaforge/miniforge3:24.3.0-0", // ImageRef
                "sha256:bad", // ImageDigest — malformed, passes authoring but fails final validation
                "", // Command optional list — skip
                "1", // recovery: the only action, editing ImageRef+ImageDigest together
                "condaforge/miniforge3:24.3.0-0", // re-enter ImageRef unchanged
                DigestOnly, // re-enter ImageDigest, corrected
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("이미지 digest 입력하기", stdout.ToString());
            Assert.Contains("Quay 또는 Harbor", stdout.ToString());
            Assert.Empty(stderr.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"BioContainer\"", json);
        }

        [Fact]
        public void HelpCommand_AtFieldPrompt_PrintsFieldHelpThenRetriesSameField()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "y", "n", "n", "n", // Q&A -> recommend container
                "", // accept recommended method
                "/help", // ToolName prompt: request help instead of answering
                "bwa-mem", // ToolName, asked again after help text
                "0.7.17", "run.sh",
                "condaforge/miniforge3:24.3.0-0", // ImageRef
                DigestOnly, // ImageDigest
                "", // Command optional list — skip
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());

            var stdoutText = stdout.ToString();
            Assert.Contains("도구 이름 — recipe에서 식별할 도구 이름입니다.", stdoutText);
            Assert.Contains("예시: bwa-mem", stdoutText);
            Assert.Contains("필수 항목입니다. 값이 없으면 최종 검증을 통과하지 못합니다.", stdoutText);

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"ToolName\": \"bwa-mem\"", json);
        }

        [Fact]
        public void ReviewCommand_AtFieldPrompt_ShowsSetAndUnsetFieldsThenRetriesSameField()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "y", "n", "n", "n", // Q&A -> recommend container
                "", // accept recommended method
                "bwa-mem", // ToolName
                "/review", // at ToolVersion prompt: review instead of answering
                "0.7.17", // ToolVersion, asked again after review
                "run.sh", // Script
                "condaforge/miniforge3:24.3.0-0", // ImageRef
                DigestOnly, // ImageDigest
                "", // Command optional list — skip
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());

            var stdoutText = stdout.ToString();
            Assert.Contains("도구 이름 (ToolName): bwa-mem", stdoutText);
            Assert.Contains("도구 버전 (ToolVersion): 아직 입력 안 함", stdoutText);

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"Version\": \"0.7.17\"", json);
        }

        [Fact]
        public void CancelCommand_AtModeSelector_ExitsWithCode130WithoutSaving()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "/cancel",
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(130, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Empty(stderr.ToString());
            Assert.Contains("recipe 생성을 취소했습니다.", stdout.ToString());
            Assert.Contains("파일은 저장되지 않았습니다.", stdout.ToString());
        }

        [Fact]
        public void CancelCommand_AtQuickSetupQuestion_ExitsWithCode130WithoutSaving()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "/cancel",
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(130, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Empty(stderr.ToString());
            Assert.Contains("recipe 생성을 취소했습니다.", stdout.ToString());
        }

        [Fact]
        public void BackCommand_AtGuidedCluePicker_ReturnsToModeSelector()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1", // 쉬운 안내 모드
                "/back", // return to mode selector
                "3", // CI usage, then exit 0
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Empty(stderr.ToString());
            var stdoutText = stdout.ToString();
            Assert.Contains("이전 화면으로 돌아갑니다.", stdoutText);
            Assert.Contains("스크립트/CI 모드", stdoutText);
        }

        [Fact]
        public void BackCommand_AtQuickSetupQuestion_ReturnsToModeSelector()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "/back", // return to mode selector
                "3", // CI usage, then exit 0
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Empty(stderr.ToString());
            var stdoutText = stdout.ToString();
            Assert.Contains("이전 화면으로 돌아갑니다.", stdoutText);
            Assert.Contains("스크립트/CI 모드", stdoutText);
        }

        [Fact]
        public void CancelCommand_DeclinedAtFieldPrompt_ContinuesAndSavesValidRecipe()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "y", "n", "n", "n", // Q&A -> recommend container
                "", // accept recommended method
                "bwa-mem", // ToolName
                "/cancel", // at ToolVersion prompt
                "2", // decline cancellation, continue
                "0.7.17", // ToolVersion, asked again after declining
                "run.sh",
                "condaforge/miniforge3:24.3.0-0",
                DigestOnly,
                "", // Command optional list — skip
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("[2] 계속 작성", stdout.ToString());
            Assert.Empty(stderr.ToString());
            Assert.True(File.Exists(outPath));
        }

        [Fact]
        public void BackCommand_AtFieldPrompt_RepromptsPreviousField()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "y", "n", "n", "n", // Q&A -> recommend container
                "", // accept recommended method
                "bwa-mem", // ToolName (first entry)
                "/back", // ToolVersion prompt → back to ToolName
                "bwa-mem2", // ToolName (re-entered)
                "0.7.17", // ToolVersion
                "bwa mem",
                "condaforge/miniforge3:24.3.0-0",
                DigestOnly,
                "", // Command optional list — skip
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());
            Assert.True(File.Exists(outPath));
            var stdoutText = stdout.ToString();
            Assert.DoesNotContain("/back은 현재 v1.0에서 초기 선택", stdoutText);
            Assert.Contains("[1 / ", stdoutText);
            var json = File.ReadAllText(outPath);
            Assert.Contains("\"ToolName\": \"bwa-mem2\"", json);
        }

        [Fact]
        public void BackCommand_AtFirstFieldPrompt_ReturnsToModeSelector()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "y", "n", "n", "n", // Q&A -> recommend container
                "", // accept recommended method
                "/back", // ToolName prompt (first field) → back to mode selector
                "2", // 빠른 설정 모드 (re-selected)
                "n", "n", "y", "n", "n", "n", // Q&A again
                "", // accept recommended method
                "bwa-mem",
                "0.7.17",
                "bwa mem",
                "condaforge/miniforge3:24.3.0-0",
                DigestOnly,
                "", // Command optional list — skip
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());
            Assert.True(File.Exists(outPath));
        }

        [Fact]
        public void CancelCommand_ConfirmedAtFieldPrompt_ExitsWithCode130WithoutSaving()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "y", "n", "n", "n", // Q&A -> recommend container
                "", // accept recommended method
                "bwa-mem", // ToolName
                "/cancel", // at ToolVersion prompt
                "1", // confirm cancellation
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(130, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("recipe 생성을 취소했습니다.", stdout.ToString());
            Assert.Contains("파일은 저장되지 않았습니다.", stdout.ToString());
        }

        [Fact]
        public void QuitCommand_ConfirmedAtFieldPrompt_ExitsWithCode130WithoutSaving()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "y", "n", "n", "n", // Q&A -> recommend container
                "", // accept recommended method
                "bwa-mem", // ToolName
                "/quit", // at ToolVersion prompt
                "1", // confirm cancellation
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(130, exitCode);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void ExitCommand_ConfirmedAtFieldPrompt_ExitsWithCode130WithoutSaving()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "y", "n", "n", "n", // Q&A -> recommend container
                "", // accept recommended method
                "bwa-mem", // ToolName
                "/exit", // at ToolVersion prompt
                "1", // confirm cancellation
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(130, exitCode);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void SimulatedCtrlC_AtFirstFieldPrompt_ExitsWithCode130WithoutSavingOrStackTrace()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "y", "n", "n", "n", // Q&A -> recommend container
                "", // accept recommended method
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var cancellation = new SequencedCancellationSource(checksBeforeCancellation: 0);
            var exitCode = RecipeCreateInteractiveRunner.Run(
                outPath,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                cancellation,
                resolveClient: NullResolveRecipeClient.Instance);

            Assert.Equal(130, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Empty(stderr.ToString());
            Assert.DoesNotContain("Exception", stdout.ToString());
            Assert.Contains("recipe 생성을 취소했습니다.", stdout.ToString());
            Assert.Contains("파일은 저장되지 않았습니다.", stdout.ToString());
        }

        [Fact]
        public void SimulatedCtrlC_AndCancelCommand_ProduceIdenticalExitCodeAndMessage()
        {
            var cancelOutPath = Path.Join(_workDir, "cancel.json");
            var cancelTranscript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "y", "n", "n", "n", // Q&A -> recommend container
                "", // accept recommended method
                "bwa-mem", // ToolName
                "/cancel", // at ToolVersion prompt
                "1", // confirm cancellation
            };

            using var cancelStdout = new StringWriter();
            using var cancelStderr = new StringWriter();
            var cancelExitCode = CliApp.Run(
                new[] { "recipe", "create", cancelOutPath },
                new StringReader(string.Join("\n", cancelTranscript)),
                cancelStdout,
                cancelStderr);

            var ctrlCOutPath = Path.Join(_workDir, "ctrlc.json");
            var ctrlCTranscript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "y", "n", "n", "n", // Q&A -> recommend container
                "", // accept recommended method
            };

            using var ctrlCStdout = new StringWriter();
            using var ctrlCStderr = new StringWriter();
            var ctrlCExitCode = RecipeCreateInteractiveRunner.Run(
                ctrlCOutPath,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", ctrlCTranscript)), ctrlCStdout),
                ctrlCStderr,
                new SequencedCancellationSource(checksBeforeCancellation: 0),
                resolveClient: NullResolveRecipeClient.Instance);

            Assert.Equal(cancelExitCode, ctrlCExitCode);
            Assert.False(File.Exists(cancelOutPath));
            Assert.False(File.Exists(ctrlCOutPath));

            var cancelMessageLines = cancelStdout.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n')[^2..];
            var ctrlCMessageLines = ctrlCStdout.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n')[^2..];
            Assert.Equal(cancelMessageLines, ctrlCMessageLines);
        }

        [Fact]
        public void ModeSelector_CiModeChoice_PrintsUsageAndExitsWithCode0WithoutQa()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "3", // CI 모드 사용법 보기
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(
                new[] { "recipe", "create", outPath },
                new StringReader(string.Join("\n", transcript)),
                stdout,
                stderr);

            Assert.Equal(0, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Empty(stderr.ToString());
            var stdoutText = stdout.ToString();
            Assert.Contains("스크립트/CI 모드", stdoutText);
            Assert.Contains("--non-interactive", stdoutText);
            Assert.DoesNotContain("Q1.", stdoutText);
        }

        [Fact]
        public void ModeSelector_GuidedBeginnerChoice_ShowsCluePickerAndRunsFlow()
        {
            // GuidedBeginner mode (Sprint R13) now runs BeginnerGuideFlow (Section 8.2).
            // Replaces the R11-era "FallsBackToQuickSetupFlowWithNotice" test.
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",  // 쉬운 안내 모드
                "2",  // clue: install command
                "conda install -c bioconda bwa=0.7.17=h5bf99c6_8 -y",
                "1",  // use understood values (Parsed result)
                "",   // 채널 확인: 파싱된 "bioconda" 그대로 사용 (Enter)
                "0",  // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh",
                ImageRefWithDigest,  // ImageRef (BaseImage for Package method)
                // Packages/Channels: pre-filled by BeginnerGuideFlow, skipped
                // PackageEngine: Defaulted, skipped
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(
                new[] { "recipe", "create", outPath },
                new StringReader(string.Join("\n", transcript)),
                stdout,
                stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());
            var stdoutText = stdout.ToString();
            Assert.Contains("쉬운 안내 모드", stdoutText);
            Assert.DoesNotContain("아직 준비 중", stdoutText);
            Assert.True(File.Exists(outPath));

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"PackageEngine\": \"conda\"", json);
            Assert.Contains("bwa-mem", json);
        }

        [Fact]
        public void RecommendationReject_ManualMethodSelection_SavesValidRecipe()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "n", "y", "n", "n", // Q&A -> recommend package
                "n", // reject recommendation
                "1", // manually pick [1] container
                "bwa-mem", "0.7.17", "run.sh",
                "condaforge/miniforge3:24.3.0-0", // ImageRef
                DigestOnly, // ImageDigest
                "", // Command optional list — skip
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(
                new[] { "recipe", "create", outPath },
                new StringReader(string.Join("\n", transcript)),
                stdout,
                stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());
            var stdoutText = stdout.ToString();
            Assert.Contains("추천 작성 방식:", stdoutText);
            Assert.Contains("다른 작성 방식을 선택하세요", stdoutText);

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"BioContainer\"", json);
        }

        [Fact]
        public void BackCommand_AtListFieldPrompt_ClearsStaleItemsAndRepromptsPreviousField()
        {
            // Regression: items typed before /back inside a list field must not
            // survive to the next pass of the same field. Without the fix,
            // "samtools=1.18" would appear alongside "bwa=0.7.17=h5bf99c6_8".
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "n", "y", "n", "n", // Q&A -> recommend package
                "", // accept recommended method
                "bioconda",           // Channels (채널 확정 단계)
                "",                   // complete Channels
                "0",                  // 기반 이미지: 직접 입력
                "bwa-mem", // ToolName
                "0.7.17",  // ToolVersion
                "run.sh",  // Script
                ImageRefWithDigest,   // ImageRef (first time)
                "samtools=1.18",      // Packages: partial entry before /back
                "/back",              // /back mid-list → clears in-progress items, back to ImageRef
                ImageRefWithDigest,   // ImageRef (re-entered)
                "bwa=0.7.17=h5bf99c6_8", // Packages: correct entry
                "",                   // complete Packages
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("bwa=0.7.17=h5bf99c6_8", json);
            Assert.DoesNotContain("samtools", json);
        }

        [Fact]
        public void BackCommand_DuringRecovery_ShowsNotSupportedMessageAndAllowsContinuation()
        {
            // Regression: /back inside a recovery re-edit field previously propagated
            // as an unhandled exception. Now it prints a message and re-shows the menu.
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "y", "n", "n", "n", // Q&A -> recommend container
                "", // accept recommended method
                "bwa-mem", "0.7.17", "bwa mem",
                "condaforge/miniforge3:24.3.0-0", // ImageRef
                "sha256:bad",   // ImageDigest — malformed, fails final validation
                "",             // Command optional list — skip
                "1",            // select recovery action (edit ImageRef + ImageDigest)
                "/back",        // /back during recovery → shows not-supported message, returns true
                // session still invalid → recovery menu shown again
                "1",            // select recovery action again
                "condaforge/miniforge3:24.3.0-0", // re-enter ImageRef
                DigestOnly,     // re-enter ImageDigest, corrected
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());
            var stdoutText = stdout.ToString();
            Assert.Contains("/back은 수정 단계에서 지원하지 않습니다", stdoutText);

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"BioContainer\"", json);
        }

        [Fact]
        public void RecoveryLoop_EmptySelection_ExitsWithCode1WithoutSaving()
        {
            // Regression guard: empty input at the recovery menu means "save 없이 종료".
            // RunRecoveryLoop returns false → main loop writes stderr message and returns 1.
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "y", "n", "n", "n", // Q&A -> recommend container
                "", // accept recommended method
                "bwa-mem", "0.7.17", "run.sh",
                "condaforge/miniforge3:24.3.0-0", // ImageRef
                "sha256:bad",   // ImageDigest — malformed, fails final validation
                "",             // Command — skip
                "",             // empty selection at recovery menu → return false
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("최종 검증을 통과하지 못해 저장하지 않습니다.", stderr.ToString());
        }

        [Fact]
        public void RecoveryLoop_InvalidSelection_PrintsMessageAndRecurses()
        {
            // Out-of-range or non-numeric selection prints "알 수 없는 선택입니다." and
            // re-shows the recovery menu. The user then picks a valid action.
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "y", "n", "n", "n", // Q&A -> recommend container
                "", // accept recommended method
                "bwa-mem", "0.7.17", "run.sh",
                "condaforge/miniforge3:24.3.0-0",
                "sha256:bad",
                "",
                "99", // invalid: out of range → "알 수 없는 선택입니다.", recurse
                "1",  // valid action
                "condaforge/miniforge3:24.3.0-0", DigestOnly, // fix the digest
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());
            Assert.Contains("알 수 없는 선택입니다.", stdout.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"BioContainer\"", json);
        }

        [Fact]
        public void RecoveryLoop_CancelCommand_ExitsWithCode130WithoutSaving()
        {
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "y", "n", "n", "n", // Q&A -> recommend container
                "", // accept recommended method
                "bwa-mem", "0.7.17", "run.sh",
                "condaforge/miniforge3:24.3.0-0",
                "sha256:bad",
                "",
                "/cancel", // at recovery menu → ThrowIfCancel → exit 130
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(130, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("recipe 생성을 취소했습니다.", stdout.ToString());
        }

        [Fact]
        public void ChangeMethod_BackDuringNumberInput_CancelsChangeAndRepromptsCurrentField()
        {
            // /back typed at the method-number prompt inside /change-method cancels the change
            // and re-prompts the same field (no method switch occurs).
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "n", "y", "n", "n", // Q&A -> recommend package
                "", // accept recommended method
                "bioconda", "",              // Channels (채널 확정 단계)
                "0", // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh",
                "/change-method", // at ImageRef prompt
                "/back",          // cancel the change → "method 변경을 취소하고..."
                ImageRefWithDigest,  // ImageRef re-prompted for the same Package method
                "bwa=0.7.17=h5bf99c6_8", "", // Packages
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());
            Assert.Contains("method 변경을 취소하고 현재 입력 단계로 돌아갑니다.", stdout.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"Conda\"", json);
        }

        [Fact]
        public void ChangeMethod_InvalidNumber_PrintsErrorAndRepromptsCurrentField()
        {
            // An unrecognized method number cancels the change and re-prompts the current field.
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "n", "y", "n", "n", // Q&A -> recommend package
                "", // accept recommended method
                "bioconda", "", // 채널 확정 단계
                "0", // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh",
                "/change-method",
                "99",  // invalid number → "알 수 없는 방법입니다. 변경을 취소합니다."
                ImageRefWithDigest,
                "bwa=0.7.17=h5bf99c6_8", "",
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());
            Assert.Contains("알 수 없는 방법입니다. 변경을 취소합니다.", stdout.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"Conda\"", json);
        }

        [Fact]
        public void ChangeMethod_ConfirmDeclined_DoesNotSwitchMethodAndRepromptsCurrentField()
        {
            // Typing N at the "계속할까요?" step cancels the method change in place.
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "n", "y", "n", "n", // Q&A -> recommend package
                "", // accept recommended method
                "bioconda", "", // 채널 확정 단계
                "0", // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh",
                "/change-method",
                "4",  // source
                "n",  // decline confirm → ChangeMethod(Cancel) → field re-prompted
                ImageRefWithDigest,
                "bwa=0.7.17=h5bf99c6_8", "",
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"Conda\"", json);
            Assert.DoesNotContain("\"BuildKind\": \"SourceBuild\"", json);
        }

        [Fact]
        public void StringListField_EmptyOnRequiredList_PrintsErrorAndRepromptsUntilFilled()
        {
            // CompleteListField throws when a Required list has no items yet.
            // PromptStringListField prints the exception message and continues collecting.
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "n", "y", "n", "n", // Q&A -> recommend package
                "", // accept recommended method
                "bioconda", "", // 채널 확정 단계
                "0", // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh", ImageRefWithDigest,
                "",                         // empty first entry on Packages → error message, re-prompt
                "bwa=0.7.17=h5bf99c6_8", "",  // correct entry + complete
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());
            Assert.Contains("requires at least one item before it can be completed", stdout.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("bwa=0.7.17=h5bf99c6_8", json);
        }

        [Fact]
        public void CancelCommand_DuringDockerfileWarningPrompt_ExitsWithCode130()
        {
            // /cancel typed at the Dockerfile warning confirmation bypasses the warning
            // and exits with 130 (same as any other /cancel).
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2", // 빠른 설정 모드
                "n", "n", "n", "n", "n", "y", // Q&A -> recommend dockerfile
                "",       // accept recommended method
                "/cancel", // at dockerfile warning prompt → ThrowIfCancel → exit 130
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(130, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("recipe 생성을 취소했습니다.", stdout.ToString());
        }

        [Fact]
        public void BackCommand_AtQandAMidQuestion_ReturnsToPreviousQuestion()
        {
            // /back at Q&A question N>0 decrements the index to N-1, re-prompting
            // the previous question. The first answer is replaced by the re-entered one.
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2",     // 빠른 설정 모드
                "y",     // Q1 (IsRestrictedNetwork): yes (will be corrected)
                "/back", // Q2: /back → goes back to Q1
                "n",     // Q1 (re-entered): no
                "n",     // Q2
                "n",     // Q3
                "y",     // Q4 (HasPackageInPublicChannels): yes → package recommendation
                "n",     // Q5
                "n",     // Q6
                "",      // accept package recommendation
                "bioconda", "", // 채널 확정 단계
                "0", // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh", ImageRefWithDigest,
                "bwa=0.7.17=h5bf99c6_8", "",
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)), stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"Conda\"", json);
        }

        [Fact]
        public void ResolveRecipe_ExternalSourceWithCandidates_AppliesSelectedFullPinAndSaves()
        {
            // Sprint R17: when resolver returns ExternalSource + multiple candidates,
            // PackageCandidatePresenter prompts the user; "1" picks the first candidate's
            // FullPin and that replaces the version-only pin in the saved document.
            var outPath = Path.Join(_workDir, "recipe.json");
            var resolveResult = new ResolveRecipeResult(
                RecipeResolutionSource.ExternalSource,
                new[]
                {
                    new PackageResolution("bwa", "0.7.17", new[]
                    {
                        new BuildStringCandidate("h5bf99c6_8", "bwa=0.7.17=h5bf99c6_8", "bioconda"),
                        new BuildStringCandidate("h6a6fa10_8", "bwa=0.7.17=h6a6fa10_8", "conda-forge"),
                    }),
                });
            var transcript = new[]
            {
                "2", "n", "n", "n", "y", "n", "n", "",
                "bioconda", "",    // 채널 확정 단계
                "0",              // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh", ImageRefWithDigest,
                "bwa=0.7.17", "",  // version-only pin
                "1",               // PackageCandidatePresenter: pick first candidate
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = RecipeCreateInteractiveRunner.Run(
                outPath,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                new SequencedCancellationSource(checksBeforeCancellation: 1000),
                resolveClient: new FixedResolveRecipeClient(resolveResult));

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());
            Assert.True(File.Exists(outPath));
            Assert.Contains("패키지 빌드 문자열 선택", stdout.ToString());
            var json = File.ReadAllText(outPath);
            Assert.Contains("bwa=0.7.17=h5bf99c6_8", json);
        }

        [Fact]
        public void ResolveRecipe_NotFound_PrintsWarningAndSavesOriginalPins()
        {
            // Sprint R17: when resolver returns NotFound with no candidates,
            // a ⚠ advisory is printed but the file is still saved with the
            // original version-only pins (no candidate picker is shown).
            var outPath = Path.Join(_workDir, "recipe.json");
            var resolveResult = new ResolveRecipeResult(
                RecipeResolutionSource.NotFound,
                Array.Empty<PackageResolution>());
            var transcript = new[]
            {
                "2", "n", "n", "n", "y", "n", "n", "",
                "bioconda", "", // 채널 확정 단계
                "0",              // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh", ImageRefWithDigest,
                "bwa=0.7.17", "",
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = RecipeCreateInteractiveRunner.Run(
                outPath,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                new SequencedCancellationSource(checksBeforeCancellation: 1000),
                resolveClient: new FixedResolveRecipeClient(resolveResult));

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            Assert.Contains("Harbor에 동일 tool+version 이미지가 없습니다.", stdout.ToString());
            var json = File.ReadAllText(outPath);
            Assert.Contains("bwa=0.7.17", json);
        }

        [Fact]
        public void ResolveRecipe_CalledWithBoundedTimeoutToken_NotCancellationTokenNone()
        {
            // Regression test (external review): the wizard is a synchronous/blocking
            // console loop, so a user has no way to /cancel while a network call
            // (ResolveRecipe here) is in flight -- the only escape is a bounded
            // timeout. Confirms RecipeCreateFlow no longer passes
            // CancellationToken.None (which would hang forever on a stalled network).
            var outPath = Path.Join(_workDir, "recipe.json");
            var resolveResult = new ResolveRecipeResult(
                RecipeResolutionSource.NotFound,
                Array.Empty<PackageResolution>());
            var transcript = new[]
            {
                "2", "n", "n", "n", "y", "n", "n", "",
                "bioconda", "",
                "0",
                "bwa-mem", "0.7.17", "run.sh", ImageRefWithDigest,
                "bwa=0.7.17", "",
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var client = new FixedResolveRecipeClient(resolveResult);
            var exitCode = RecipeCreateInteractiveRunner.Run(
                outPath,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                new SequencedCancellationSource(checksBeforeCancellation: 1000),
                resolveClient: client);

            Assert.Equal(0, exitCode);
            Assert.NotNull(client.CapturedCancellationToken);
            Assert.True(client.CapturedCancellationToken!.Value.CanBeCanceled);
        }

        [Fact]
        public void ManualBaseImageEntry_MicromambaImageWithCondaEngine_WarnsButStillSaves()
        {
            // Issue #15/#16 follow-up: step-4 candidate auto-detection only
            // covers the curated candidate list — typing a micromamba-style
            // image manually (via "0" direct entry) bypasses it entirely, so
            // this needs its own warning to avoid a silent 100%-fail combo.
            var outPath = Path.Join(_workDir, "recipe.json");
            const string micromambaImageWithDigest =
                "mambaorg/micromamba:1.5.8@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            var transcript = new[]
            {
                "2", "n", "n", "n", "y", "n", "n", "",
                "bioconda", "",   // 채널 확정 단계
                "0",              // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh", micromambaImageWithDigest,
                "bwa=0.7.17=h5bf99c6_8", "",
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = RecipeCreateInteractiveRunner.Run(
                outPath,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                new SequencedCancellationSource(checksBeforeCancellation: 1000),
                resolveClient: NullResolveRecipeClient.Instance);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            Assert.Contains("micromamba 전용 이미지로 보이는데 PackageEngine은 conda", stdout.ToString());
        }

        [Fact]
        public void PackageMirror_ResolveRecipeThrowsRpcException_PrintsWarningAndSavesInsteadOfCrashing()
        {
            // Issue #13 regression: Mirror 방식은 필드에 입력한 MirrorUri를
            // ResolveRecipe에 실제로 전달해야 하고(document.PackageMirrorUri),
            // ResolveRecipe가 RpcException을 던져도(예: NodeVault가
            // package_mirror_uri required로 거부) 프로세스 전체가 크래시하는 대신
            // 경고만 출력하고 저장까지 정상 완료되어야 한다.
            var outPath = Path.Join(_workDir, "recipe.json");
            var resolveClient = new ThrowingResolveRecipeClient(
                new RpcException(new Status(StatusCode.InvalidArgument,
                    "package_mirror_uri is required for PACKAGE_MIRROR variant")));

            var transcript = new[]
            {
                "2",                    // 빠른 설정 모드
                "n", "n", "u", "u", "u", "u", // 추천 질문 6개: 전부 모름 → 수동 선택으로
                "3",                    // 수동 method 선택: Mirror
                "0",                    // 기반 이미지: 직접 입력
                "mirrortool", "1.0", "mirrortool run", ImageRefWithDigest,
                "https://mirror.internal.example/conda", // MirrorUri
                "mirrortool=1.0", "",   // Packages, blank로 목록 종료
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = RecipeCreateInteractiveRunner.Run(
                outPath,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                new SequencedCancellationSource(checksBeforeCancellation: 1000),
                resolveClient: resolveClient);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            Assert.Contains("패키지 빌드 문자열을 조회하지 못했습니다", stdout.ToString());
            Assert.Equal("https://mirror.internal.example/conda", resolveClient.CapturedPackageMirrorUri);
        }

        [Fact]
        public void ResolveRecipe_CancelDuringCandidateSelection_ExitsWithCode130()
        {
            // Sprint R17: /cancel inside PackageCandidatePresenter throws
            // RecipeCreateCancelledException → caught by outer handler → exit 130.
            var outPath = Path.Join(_workDir, "recipe.json");
            var resolveResult = new ResolveRecipeResult(
                RecipeResolutionSource.ExternalSource,
                new[]
                {
                    new PackageResolution("bwa", "0.7.17", new[]
                    {
                        new BuildStringCandidate("h5bf99c6_8", "bwa=0.7.17=h5bf99c6_8", "bioconda"),
                        new BuildStringCandidate("h6a6fa10_8", "bwa=0.7.17=h6a6fa10_8", "conda-forge"),
                    }),
                });
            var transcript = new[]
            {
                "2", "n", "n", "n", "y", "n", "n", "",
                "bioconda", "", // 채널 확정 단계
                "0",         // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh", ImageRefWithDigest,
                "bwa=0.7.17", "",
                "/cancel",         // during PackageCandidatePresenter → exit 130
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = RecipeCreateInteractiveRunner.Run(
                outPath,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                new SequencedCancellationSource(checksBeforeCancellation: 1000),
                resolveClient: new FixedResolveRecipeClient(resolveResult));

            Assert.Equal(130, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("recipe 생성을 취소했습니다.", stdout.ToString());
        }

        private sealed class FixedResolveRecipeClient : IResolveRecipeClient
        {
            private readonly ResolveRecipeResult _result;
            internal FixedResolveRecipeClient(ResolveRecipeResult result) => _result = result;

            // Regression test seam: captures the token it was called with, so a test
            // can assert it's a real bounded timeout token, not CancellationToken.None.
            internal System.Threading.CancellationToken? CapturedCancellationToken { get; private set; }

            public System.Threading.Tasks.Task<ResolveRecipeResult> ResolveAsync(
                string toolName,
                string version,
                System.Collections.Generic.IReadOnlyList<string> packages,
                System.Threading.CancellationToken cancellationToken,
                NodeKit.Authoring.Recipes.RecipeBuildKind? buildKind = null,
                string? packageMirrorUri = null)
            {
                CapturedCancellationToken = cancellationToken;
                return System.Threading.Tasks.Task.FromResult(_result);
            }
        }

        // Issue #13 regression: captures the packageMirrorUri it was called with,
        // then throws to simulate NodeVault rejecting a Mirror-method resolve.
        private sealed class ThrowingResolveRecipeClient : IResolveRecipeClient
        {
            private readonly Exception _exception;
            internal ThrowingResolveRecipeClient(Exception exception) => _exception = exception;

            internal string? CapturedPackageMirrorUri { get; private set; }

            public System.Threading.Tasks.Task<ResolveRecipeResult> ResolveAsync(
                string toolName,
                string version,
                System.Collections.Generic.IReadOnlyList<string> packages,
                System.Threading.CancellationToken cancellationToken,
                NodeKit.Authoring.Recipes.RecipeBuildKind? buildKind = null,
                string? packageMirrorUri = null)
            {
                CapturedPackageMirrorUri = packageMirrorUri;
                throw _exception;
            }
        }

        /// <summary>
        /// Fake IRecipeCreateCancellationSource for design doc Section 18.5
        /// tests — simulates Ctrl+C without a real signal by returning false
        /// for a fixed number of checks, then true thereafter.
        /// </summary>
        private sealed class SequencedCancellationSource : IRecipeCreateCancellationSource
        {
            private int _checksRemaining;

            public SequencedCancellationSource(int checksBeforeCancellation)
            {
                _checksRemaining = checksBeforeCancellation;
            }

            public bool IsCancellationRequested
            {
                get
                {
                    if (_checksRemaining <= 0)
                    {
                        return true;
                    }

                    _checksRemaining--;
                    return false;
                }
            }
        }
    }
}
