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
            var def = Def("registry.example.com/bwa-mem2:2.2.1@sha256:abc123def456");
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
            var def = Def("registry.example.com:5000/bwa-mem2:2.2.1@sha256:abc123def456");
            Assert.True(_sut.Validate(def).IsValid);
        }

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
        public void Fail_WhenCondaVersionOnly()
        {
            var def = DefWithSpec(@"
name: myenv
dependencies:
  - bwa=0.7.17
");
            var result = _sut.Validate(def);
            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-PKG-002");
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
            Assert.Contains(result.Violations, v => v.RuleId == "L1-REQ-004");
            Assert.Contains(result.Violations, v => v.RuleId == "L1-REQ-005");
        }

        [Fact]
        public void Fail_WhenIoNamesAreDuplicated()
        {
            var definition = new ToolDefinition
            {
                Name = "BWA",
                DockerfileContent = "FROM ubuntu:22.04",
                Script = "echo hi",
                Inputs = { new ToolInput { Name = "reads.fq" }, new ToolInput { Name = "reads.fq" } },
                Outputs = { new ToolOutput { Name = "out.bam" }, new ToolOutput { Name = "out.bam" } },
            };

            var result = _sut.Validate(definition);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-REQ-007");
            Assert.Contains(result.Violations, v => v.RuleId == "L1-REQ-009");
        }

        [Fact]
        public void Fail_WhenIoNamesAreEmpty()
        {
            var definition = new ToolDefinition
            {
                Name = "BWA",
                DockerfileContent = "FROM ubuntu:22.04",
                Script = "echo hi",
                Inputs = { new ToolInput { Name = "  " } },
                Outputs = { new ToolOutput { Name = string.Empty } },
            };

            var result = _sut.Validate(definition);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-REQ-006");
            Assert.Contains(result.Violations, v => v.RuleId == "L1-REQ-008");
        }

        [Fact]
        public void Fail_WhenIoContractFieldsAreMissingOrInvalid()
        {
            var definition = new ToolDefinition
            {
                Name = "BWA",
                Version = "0.7.17",
                DockerfileContent = "FROM ubuntu:22.04",
                Script = "echo hi",
                Inputs =
                {
                    new ToolInput
                    {
                        Name = "reads",
                        Shape = "triple",
                    },
                },
                Outputs =
                {
                    new ToolOutput
                    {
                        Name = "aligned",
                        Shape = "many",
                        Class = "artifact",
                    },
                },
            };

            var result = _sut.Validate(definition);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-REQ-011");
            Assert.Contains(result.Violations, v => v.RuleId == "L1-REQ-012");
            Assert.Contains(result.Violations, v => v.RuleId == "L1-REQ-013");
            Assert.Contains(result.Violations, v => v.RuleId == "L1-REQ-014");
            Assert.Contains(result.Violations, v => v.RuleId == "L1-REQ-015");
            Assert.Contains(result.Violations, v => v.RuleId == "L1-REQ-016");
            Assert.Contains(result.Violations, v => v.RuleId == "L1-REQ-017");
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
                Inputs =
                {
                    new ToolInput
                    {
                        Name = "reads",
                        Role = "sample-fastq",
                        Format = "fastq",
                        Shape = "pair",
                    },
                },
                Outputs =
                {
                    new ToolOutput
                    {
                        Name = "aligned",
                        Role = "aligned-bam",
                        Format = "bam",
                        Shape = "single",
                        Class = "primary",
                    },
                },
            };
    }

    public class DockerfileStructureValidatorTests
    {
        private readonly DockerfileStructureValidator _sut = new();

        [Fact]
        public void Pass_WhenDockerfileHasFromAndValidCopy()
        {
            var definition = Def("FROM ubuntu:22.04\nCOPY app/ /app/\nRUN echo ok\n");

            Assert.True(_sut.Validate(definition).IsValid);
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
    }
}
