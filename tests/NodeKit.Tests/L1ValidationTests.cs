using NodeKit.Authoring;
using NodeKit.Validation;
using Xunit;

namespace NodeKit.Tests
{
    public class ImageUriValidatorTests
    {
        private readonly ImageUriValidator _sut = new();

        [Fact]
        public void Pass_WhenDigestAndTagPresent()
        {
            var def = Def($"registry.example.com/bwa-mem2:2.2.1@sha256:{ValidDigest}");
            Assert.True(_sut.Validate(def).IsValid);
        }

        [Fact]
        public void Fail_WhenLatestTag()
        {
            var def = Def("ubuntu:latest@sha256:abc");
            var result = _sut.Validate(def);
            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-IMG-002");
        }

        [Fact]
        public void Fail_WhenLatestTagImplicit()
        {
            var def = Def("ubuntu@sha256:abc");
            var result = _sut.Validate(def);
            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-IMG-003");
        }

        [Fact]
        public void Fail_WhenNoDigest()
        {
            var def = Def("registry.example.com/bwa-mem2:2.2.1");
            var result = _sut.Validate(def);
            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-IMG-004");
        }

        [Fact]
        public void Fail_WhenEmpty()
        {
            var def = Def(string.Empty);
            var result = _sut.Validate(def);
            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-IMG-001");
        }

        [Fact]
        public void Fail_WhenRegistryPortExistsButTagMissing()
        {
            var def = Def("registry.example.com:5000/bwa-mem2@sha256:abc123def456");
            var result = _sut.Validate(def);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-IMG-003");
        }

        [Fact]
        public void Pass_WhenRegistryPortAndTagBothExist()
        {
            var def = Def($"registry.example.com:5000/bwa-mem2:2.2.1@sha256:{ValidDigest}");
            Assert.True(_sut.Validate(def).IsValid);
        }

        [Fact]
        public void Fail_WhenDigestIsTooShort()
        {
            var def = Def("registry.example.com/bwa-mem2:2.2.1@sha256:abc123def456");
            var result = _sut.Validate(def);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-IMG-005");
        }

        [Fact]
        public void Fail_WhenDigestContainsNonHexCharacters()
        {
            var def = Def($"registry.example.com/bwa-mem2:2.2.1@sha256:{ValidDigest[..63]}g");
            var result = _sut.Validate(def);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-IMG-005");
        }

        [Fact]
        public void Pass_WhenMultistageDockerfileFirstFromMatchesImageUriAndAllStagesArePinned()
        {
            var imageUri = $"ubuntu:22.04@sha256:{ValidDigest}";
            var def = new ToolDefinition
            {
                ImageUri = imageUri,
                DockerfileContent =
                    $"FROM {imageUri} AS builder\n" +
                    "RUN echo build\n" +
                    $"FROM debian:12@sha256:{ValidDigest}\n" +
                    "COPY --from=builder /x /x\n",
            };

            var result = _sut.Validate(def);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Fail_WhenFirstFromDoesNotMatchImageUri()
        {
            var imageUri = $"ubuntu:22.04@sha256:{ValidDigest}";
            var def = new ToolDefinition
            {
                ImageUri = imageUri,
                DockerfileContent = $"FROM alpine:3.20@sha256:{ValidDigest}\nRUN echo ok\n",
            };

            var result = _sut.Validate(def);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-IMG-006");
        }

        private const string ValidDigest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private static ToolDefinition Def(string imageUri) =>
            new() { ImageUri = imageUri };
    }

    public class PackageVersionValidatorTests
    {
        private readonly PackageVersionValidator _sut = new();

        [Fact]
        public void Pass_WhenCondaFullyPinned()
        {
            var def = DefWithSpec(@"
name: myenv
dependencies:
  - bwa=0.7.17=h5bf99c6_8
  - samtools=1.17=h00cdaf9_0
");
            Assert.True(_sut.Validate(def).IsValid);
        }

        [Fact]
        public void Pass_WhenCondaVersionOnly()
        {
            // build string 결정은 NodeVault ResolveToolSpec 담당 — L1은 =version까지만 요구.
            // PLATFORM_MASTER_DESIGN.md §4.9
            var def = DefWithSpec(@"
name: myenv
dependencies:
  - bwa=0.7.17
");
            Assert.True(_sut.Validate(def).IsValid);
        }

        [Fact]
        public void Fail_WhenCondaNoVersion()
        {
            var def = DefWithSpec(@"
name: myenv
dependencies:
  - bwa
");
            var result = _sut.Validate(def);
            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-PKG-001");
        }

        [Fact]
        public void Pass_WhenPipFullyPinned()
        {
            var def = DefWithSpec("numpy==1.26.4\nscipy==1.12.0\n");
            Assert.True(_sut.Validate(def).IsValid);
        }

        [Fact]
        public void Fail_WhenPipUnpinned()
        {
            var def = DefWithSpec("numpy\n");
            var result = _sut.Validate(def);
            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-PKG-003");
        }

        [Fact]
        public void Pass_WhenEmptySpec()
        {
            var def = DefWithSpec(string.Empty);
            Assert.True(_sut.Validate(def).IsValid);
        }

        [Fact]
        public void Fail_WhenDockerfileCondaInstallIsUnpinned()
        {
            var def = new ToolDefinition
            {
                ImageUri = "reg/img:1.0@sha256:abc",
                DockerfileContent = "FROM ubuntu:22.04\nRUN micromamba install -y bwa samtools\n",
            };

            var result = _sut.Validate(def);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-PKG-001" && v.Field == "DockerfileContent");
        }

        [Fact]
        public void Pass_WhenDockerfileCondaInstallIsFullyPinned()
        {
            var def = new ToolDefinition
            {
                ImageUri = "reg/img:1.0@sha256:abc",
                DockerfileContent = "FROM ubuntu:22.04\nRUN micromamba install -y bwa=0.7.17=h5bf99c6_8 samtools=1.17=h00cdaf9_0\n",
            };

            Assert.True(_sut.Validate(def).IsValid);
        }

        [Fact]
        public void Fail_WhenDockerfilePipInstallIsUnpinned()
        {
            // Gap found while checking whether IPolicyChecker/WasmPolicyChecker being
            // GUI-only (not wired into the CLI) left any coverage hole versus
            // DockGuard's actual policy rules: DockGuard's DGF002 requires pip
            // installs to be version-pinned too, but this validator only recognized
            // conda/micromamba — "RUN pip install numpy" passed L1 with no warning
            // at all. Confirmed live: `nodekit validate` returned OK/exit 0.
            var def = new ToolDefinition
            {
                ImageUri = "reg/img:1.0@sha256:abc",
                DockerfileContent = "FROM ubuntu:22.04\nRUN pip install numpy\n",
            };

            var result = _sut.Validate(def);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-PKG-003" && v.Field == "DockerfileContent");
        }

        [Fact]
        public void Pass_WhenDockerfilePipInstallIsFullyPinned()
        {
            var def = new ToolDefinition
            {
                ImageUri = "reg/img:1.0@sha256:abc",
                DockerfileContent = "FROM ubuntu:22.04\nRUN pip install numpy==1.26.4 pandas==2.2.1\n",
            };

            Assert.True(_sut.Validate(def).IsValid);
        }

        // Review finding: only an exact "pip"/"pip3" first token was recognized,
        // so the equally common "python -m pip install ..." and absolute-path
        // "/usr/bin/pip install ..." forms bypassed pinning checks entirely.

        [Fact]
        public void Fail_WhenDockerfileUsesPythonModulePipInstall_Unpinned()
        {
            var def = new ToolDefinition
            {
                ImageUri = "reg/img:1.0@sha256:abc",
                DockerfileContent = "FROM ubuntu:22.04\nRUN python -m pip install numpy\n",
            };

            var result = _sut.Validate(def);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-PKG-003" && v.Field == "DockerfileContent");
        }

        [Fact]
        public void Fail_WhenDockerfileUsesAbsolutePathPipInstall_Unpinned()
        {
            var def = new ToolDefinition
            {
                ImageUri = "reg/img:1.0@sha256:abc",
                DockerfileContent = "FROM ubuntu:22.04\nRUN /usr/bin/pip install numpy\n",
            };

            var result = _sut.Validate(def);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-PKG-003" && v.Field == "DockerfileContent");
        }

        [Fact]
        public void Pass_WhenDockerfileUsesPythonModulePipInstall_FullyPinned()
        {
            var def = new ToolDefinition
            {
                ImageUri = "reg/img:1.0@sha256:abc",
                DockerfileContent = "FROM ubuntu:22.04\nRUN python3 -m pip install numpy==1.26.4\n",
            };

            Assert.True(_sut.Validate(def).IsValid);
        }

        [Fact]
        public void Fail_WhenDockerfilePip3EditableInstall_IsBlocked()
        {
            var def = new ToolDefinition
            {
                ImageUri = "reg/img:1.0@sha256:abc",
                DockerfileContent = "FROM ubuntu:22.04\nRUN pip3 install -e git+https://example.com/repo.git\n",
            };

            var result = _sut.Validate(def);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-PKG-004" && v.Field == "DockerfileContent");
        }

        [Fact]
        public void Fail_WhenDockerfilePipInstallUsesRequirementsFileFlag()
        {
            // -r/--requirement points at a file NodeKit never sees the content of,
            // so it cannot verify version pinning inside it — same reasoning as
            // -e/--editable, so it is blocked rather than silently skipped.
            var def = new ToolDefinition
            {
                ImageUri = "reg/img:1.0@sha256:abc",
                DockerfileContent = "FROM ubuntu:22.04\nRUN pip install -r requirements.txt\n",
            };

            var result = _sut.Validate(def);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-PKG-005");
        }

        [Fact]
        public void Pass_WhenDockerfileCondaInstallUsesChannelFlag()
        {
            // Regression test: -c bioconda was previously extracted as a package name,
            // causing a false positive L1-PKG-001 violation for the channel argument.
            var def = new ToolDefinition
            {
                ImageUri = "reg/img:1.0@sha256:abc",
                DockerfileContent = "FROM ubuntu:22.04\nRUN conda install -c bioconda bwa=0.7.17=h5bf99c6_8 -y\n",
            };

            Assert.True(_sut.Validate(def).IsValid);
        }

        [Fact]
        public void Pass_WhenDockerfileMicromambaInstallUsesMultipleChannelFlags()
        {
            var def = new ToolDefinition
            {
                ImageUri = "reg/img:1.0@sha256:abc",
                DockerfileContent = "FROM ubuntu:22.04\nRUN micromamba install -c conda-forge -c bioconda bwa=0.7.17=h5bf99c6_8 samtools=1.18=h50ea8bc_1 -y\n",
            };

            Assert.True(_sut.Validate(def).IsValid);
        }

        [Fact]
        public void Fail_WhenDockerfileCondaInstallWithChannelFlagHasUnpinnedPackage()
        {
            // Channel name must not be flagged; only the unpinned package name must be.
            var def = new ToolDefinition
            {
                ImageUri = "reg/img:1.0@sha256:abc",
                DockerfileContent = "FROM ubuntu:22.04\nRUN conda install -c bioconda bwa -y\n",
            };

            var result = _sut.Validate(def);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-PKG-001" && v.Message.Contains("bwa", System.StringComparison.Ordinal));
            Assert.DoesNotContain(result.Violations, v => v.Message.Contains("bioconda", System.StringComparison.Ordinal));
        }

        [Fact]
        public void Fail_WhenCondaPipSubsectionContainsUnpinnedPackage()
        {
            var def = DefWithSpec(@"
name: myenv
dependencies:
  - python=3.11=h123
  - pip
  - pip:
    - requests==2.31.0
    - numpy
");

            var result = _sut.Validate(def);

            Assert.False(result.IsValid);
            Assert.DoesNotContain(result.Violations, v => v.Message.Contains("'pip'", System.StringComparison.Ordinal));
            Assert.Contains(result.Violations, v => v.RuleId == "L1-PKG-003" && v.Message.Contains("numpy", System.StringComparison.Ordinal));
        }

        [Fact]
        public void Fail_WhenPipSpecHasDependenciesWordInComment()
        {
            var def = DefWithSpec("# our dependencies: numpy, scipy\nnumpy\n");

            var result = _sut.Validate(def);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-PKG-003" && v.Message.Contains("numpy", System.StringComparison.Ordinal));
        }

        [Fact]
        public void Fail_WhenPipSpecHasEditableInstall()
        {
            var def = DefWithSpec("numpy==1.26.4\n-e git+https://github.com/example/foo.git@main#egg=foo\n");

            var result = _sut.Validate(def);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-PKG-004" && v.Message.Contains("git+", System.StringComparison.Ordinal));
        }

        [Fact]
        public void Fail_WhenPipSpecHasLongFormEditableInstall()
        {
            var def = DefWithSpec("numpy==1.26.4\n--editable=./local/foo\n");

            var result = _sut.Validate(def);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-PKG-004");
        }

        [Fact]
        public void Pass_WhenPipSpecHasOrdinaryDashOption()
        {
            var def = DefWithSpec("numpy==1.26.4\n--no-cache-dir\n");

            Assert.True(_sut.Validate(def).IsValid);
        }

        [Fact]
        public void Fail_WhenPipSpecReferencesAnotherRequirementsFile()
        {
            // -r/--requirement points at a file NodeKit never sees the content of,
            // so it cannot verify version pinning inside it.
            var def = DefWithSpec("numpy==1.26.4\n-r other-requirements.txt\n");

            var result = _sut.Validate(def);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-PKG-005");
        }

        [Fact]
        public void Fail_WhenCondaPackageHasEmptyVersionSegment()
        {
            var def = DefWithSpec(@"
name: myenv
dependencies:
  - bwa==0.7.17
");

            var result = _sut.Validate(def);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-PKG-001");
        }

        [Fact]
        public void Pass_WhenCondaVersionWithEmptyBuildSegment()
        {
            // build string은 L1 요구사항이 아니므로 빈 build segment도 통과.
            // PLATFORM_MASTER_DESIGN.md §4.9
            var def = DefWithSpec(@"
name: myenv
dependencies:
  - bwa=0.7.17=
");
            Assert.True(_sut.Validate(def).IsValid);
        }

        private static ToolDefinition DefWithSpec(string spec) =>
            new() { ImageUri = "reg/img:1.0@sha256:abc", EnvironmentSpec = spec };
    }

    public class RequiredFieldsValidatorTests
    {
        private readonly RequiredFieldsValidator _sut = new();

        [Fact]
        public void Fail_WhenRequiredFieldsAreMissing()
        {
            var result = _sut.Validate(new ToolDefinition());

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-REQ-001");
            Assert.Contains(result.Violations, v => v.RuleId == "L1-REQ-010");
            Assert.Contains(result.Violations, v => v.RuleId == "L1-REQ-002");
            Assert.Contains(result.Violations, v => v.RuleId == "L1-REQ-003");
        }

        [Fact]
        public void Fail_WhenCommandContainsEmptyItem()
        {
            var definition = ValidDefinition();
            definition.Command.Add("/bin/sh");
            definition.Command.Add(string.Empty);

            var result = _sut.Validate(definition);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-REQ-018");
        }

        [Fact]
        public void Pass_WhenRequiredToolContractIsComplete()
        {
            var result = _sut.Validate(ValidDefinition());

            Assert.True(result.IsValid);
        }

        private static ToolDefinition ValidDefinition() =>
            new()
            {
                Name = "BWA",
                Version = "0.7.17",
                DockerfileContent = "FROM ubuntu:22.04",
                Script = "echo hi",
            };
    }

    public class DockerfileStructureValidatorTests
    {
        private const string ValidDigest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private const string ValidDigest2 = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

        private readonly DockerfileStructureValidator _sut = new();

        [Fact]
        public void Pass_WhenDockerfileHasFromAndValidCopy()
        {
            var definition = Def($"FROM ubuntu:22.04@sha256:{ValidDigest}\nCOPY app/ /app/\nRUN echo ok\n");

            Assert.True(_sut.Validate(definition).IsValid);
        }

        [Fact]
        public void Fail_WhenFromBaseImageHasLatestTag()
        {
            var result = _sut.Validate(Def("FROM ubuntu:latest\nRUN echo ok\n"));

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-DOCKER-008");
        }

        [Fact]
        public void Fail_WhenFromBaseImageHasNoDigest()
        {
            var result = _sut.Validate(Def("FROM ubuntu:22.04\nRUN echo ok\n"));

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-DOCKER-009");
        }

        [Fact]
        public void Pass_WhenFromBaseImageIsFullyPinned()
        {
            var result = _sut.Validate(Def($"FROM ubuntu:22.04@sha256:{ValidDigest}\nRUN echo ok\n"));

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Fail_WhenFromBaseImageDigestIsNotValidHex()
        {
            var result = _sut.Validate(Def("FROM ubuntu:22.04@sha256:not-a-real-digest\nRUN echo ok\n"));

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-DOCKER-011");
        }

        [Fact]
        public void Fail_WhenSecondStageFromDigestIsNotValidHex()
        {
            // The gap this closes: only the first FROM's digest format was
            // strictly checked (via ImageUriValidator); later stages only got
            // a "contains @sha256:" substring check, so a malformed digest in
            // a second stage silently passed.
            var result = _sut.Validate(Def(
                $"FROM ubuntu:22.04@sha256:{ValidDigest} AS builder\n" +
                "RUN echo build\n" +
                "FROM debian:12@sha256:not-a-real-digest\n" +
                "COPY --from=builder /x /x\n"));

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-DOCKER-011");
        }

        [Fact]
        public void Fail_WhenDockerfileHasNoExecutableInstructions()
        {
            var result = _sut.Validate(Def("# just a comment\n"));

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-DOCKER-001");
        }

        [Fact]
        public void Fail_WhenFirstInstructionIsNotFrom()
        {
            var result = _sut.Validate(Def("RUN echo before-from\nFROM ubuntu:22.04\n"));

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-DOCKER-002");
        }

        [Fact]
        public void Fail_WhenFromHasNoImage()
        {
            var result = _sut.Validate(Def("FROM\nRUN echo ok\n"));

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-DOCKER-003");
        }

        [Fact]
        public void Fail_WhenDockerfileHasDanglingLineContinuation()
        {
            var result = _sut.Validate(Def("FROM ubuntu:22.04\nRUN echo ok \\\n"));

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-DOCKER-004");
        }

        [Fact]
        public void Fail_WhenCopyHasNoDestination()
        {
            var result = _sut.Validate(Def("FROM ubuntu:22.04\nCOPY app/\n"));

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-DOCKER-005");
        }

        [Fact]
        public void Fail_WhenCopySourceEscapesBuildContext()
        {
            var result = _sut.Validate(Def("FROM ubuntu:22.04\nCOPY ../secret /app/secret\n"));

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-DOCKER-006");
        }

        [Fact]
        public void Fail_WhenAddUsesRemoteSource()
        {
            var result = _sut.Validate(Def("FROM ubuntu:22.04\nADD https://example.com/tool.tar.gz /tmp/tool.tar.gz\n"));

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-DOCKER-007");
        }

        [Fact]
        public void Fail_WhenAddUsesRemoteSourceInJsonArrayForm()
        {
            var result = _sut.Validate(Def(
                "FROM ubuntu:22.04\nADD [\"https://example.com/tool.tar.gz\", \"/tmp/tool.tar.gz\"]\n"));

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-DOCKER-007");
        }

        [Fact]
        public void Fail_WhenCopySourceContainsVariableReference()
        {
            var result = _sut.Validate(Def("FROM ubuntu:22.04\nCOPY $APP_DIR/app /app/\n"));

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-DOCKER-010");
        }

        [Fact]
        public void Fail_WhenCopySourceContainsBracedVariableReference()
        {
            var result = _sut.Validate(Def("FROM ubuntu:22.04\nCOPY ${APP_DIR}/app /app/\n"));

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-DOCKER-010");
        }

        [Fact]
        public void Pass_WhenMultistageDockerfileEveryFromIsPinned()
        {
            var result = _sut.Validate(Def(
                $"FROM ubuntu:22.04@sha256:{ValidDigest} AS builder\n" +
                "RUN echo build\n" +
                $"FROM debian:12@sha256:{ValidDigest2}\n" +
                "COPY --from=builder /x /x\n"));

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Fail_WhenSecondStageFromHasLatestTag()
        {
            var result = _sut.Validate(Def(
                $"FROM ubuntu:22.04@sha256:{ValidDigest} AS builder\n" +
                "RUN echo build\n" +
                "FROM alpine:latest\n" +
                "COPY --from=builder /x /x\n"));

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-DOCKER-008");
        }

        [Fact]
        public void Fail_WhenSecondStageFromHasNoDigest()
        {
            var result = _sut.Validate(Def(
                $"FROM ubuntu:22.04@sha256:{ValidDigest} AS builder\n" +
                "RUN echo build\n" +
                "FROM alpine:3.20\n" +
                "COPY --from=builder /x /x\n"));

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-DOCKER-009");
        }

        [Fact]
        public void Fail_WhenBuilderStageFromHasLatestTag_NoBuilderStageException()
        {
            var result = _sut.Validate(Def(
                "FROM golang:latest AS builder\n" +
                "RUN go build -o app\n" +
                $"FROM debian:12@sha256:{ValidDigest}\n" +
                "COPY --from=builder /src/app /usr/local/bin/app\n"));

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-DOCKER-008");
        }

        [Fact]
        public void Pass_WhenHeredocBodyLineStartsWithCopyKeyword()
        {
            // heredoc 본문은 Dockerfile 명령이 아니므로, 우연히 COPY로 시작하는 줄이 있어도
            // L1-DOCKER-006으로 오탐(false positive)되면 안 된다.
            var dockerfile = $"FROM ubuntu:22.04@sha256:{ValidDigest}\n" +
                "RUN <<EOF\n" +
                "COPY ../secret /app/secret\n" +
                "EOF\n";

            var result = _sut.Validate(Def(dockerfile));

            Assert.True(result.IsValid);
        }

        private static ToolDefinition Def(string dockerfile) =>
            new() { DockerfileContent = dockerfile };
    }

    public class ValidatedDefinitionStateTests
    {
        [Fact]
        public void Matches_ReturnsFalse_AfterInvalidation()
        {
            var state = new ValidatedDefinitionState();
            var definition = new ToolDefinition
            {
                Name = "BWA",
                ImageUri = "reg/img:1.0@sha256:abc",
                DockerfileContent = "FROM ubuntu:22.04",
                Script = "echo hi",
                Inputs = { new ToolInput { Name = "reads.fq" } },
                Outputs = { new ToolOutput { Name = "out.bam" } },
            };

            state.MarkValidated(definition);
            state.Invalidate();

            Assert.False(state.Matches(definition));
        }

        [Fact]
        public void Matches_ReturnsFalse_WhenDefinitionChangedAfterValidation()
        {
            var state = new ValidatedDefinitionState();
            var validated = new ToolDefinition
            {
                Name = "BWA",
                ImageUri = "reg/img:1.0@sha256:abc",
                DockerfileContent = "FROM ubuntu:22.04",
                Script = "echo hi",
                Inputs = { new ToolInput { Name = "reads.fq" } },
                Outputs = { new ToolOutput { Name = "out.bam" } },
            };

            state.MarkValidated(validated);

            var changed = new ToolDefinition
            {
                Name = validated.Name,
                ImageUri = "reg/img:2.0@sha256:def",
                DockerfileContent = validated.DockerfileContent,
                Script = validated.Script,
                Inputs = { new ToolInput { Name = "reads.fq" } },
                Outputs = { new ToolOutput { Name = "out.bam" } },
            };

            Assert.False(state.Matches(changed));
        }

        [Fact]
        public void Matches_ReturnsFalse_WhenVersionChangedAfterValidation()
        {
            var state = new ValidatedDefinitionState();
            var definition = Baseline();

            state.MarkValidated(definition);
            definition.Version = "2.0.0";

            Assert.False(state.Matches(definition));
        }

        [Fact]
        public void Matches_ReturnsFalse_WhenCommandChangedAfterValidation()
        {
            var state = new ValidatedDefinitionState();
            var definition = Baseline();

            state.MarkValidated(definition);
            definition.Command.Add("--verbose");

            Assert.False(state.Matches(definition));
        }

        [Fact]
        public void Matches_ReturnsFalse_WhenInputRoleChangedAfterValidation()
        {
            var state = new ValidatedDefinitionState();
            var definition = Baseline();

            state.MarkValidated(definition);
            definition.Inputs[0].Role = "different-role";

            Assert.False(state.Matches(definition));
        }

        [Fact]
        public void Matches_ReturnsFalse_WhenOutputClassChangedAfterValidation()
        {
            var state = new ValidatedDefinitionState();
            var definition = Baseline();

            state.MarkValidated(definition);
            definition.Outputs[0].Class = "secondary";

            Assert.False(state.Matches(definition));
        }

        [Fact]
        public void Matches_ReturnsFalse_WhenDisplayLabelChangedAfterValidation()
        {
            var state = new ValidatedDefinitionState();
            var definition = Baseline();

            state.MarkValidated(definition);
            definition.DisplayLabel = "New Label";

            Assert.False(state.Matches(definition));
        }

        private static ToolDefinition Baseline() =>
            new()
            {
                Name = "BWA",
                Version = "1.0.0",
                ImageUri = "reg/img:1.0@sha256:abc",
                DockerfileContent = "FROM ubuntu:22.04@sha256:abc123def456",
                Script = "echo hi",
                Command = { "/bin/sh", "-c", "run.sh" },
                Inputs = { new ToolInput { Name = "reads.fq", Role = "sample-fastq" } },
                Outputs = { new ToolOutput { Name = "out.bam", Class = "primary" } },
                DisplayLabel = "BWA-MEM",
            };
    }
}
