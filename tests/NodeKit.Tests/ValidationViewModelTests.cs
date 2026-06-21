using NodeKit.Authoring;
using NodeKit.Policy;
using NodeKit.UI.ViewModels;
using NodeKit.Validation;
using Xunit;

namespace NodeKit.Tests
{
    public class ValidationViewModelTests
    {
        [Fact]
        public void Validate_WhenDefinitionPasses_EnablesSubmission()
        {
            var sut = CreateSut(PolicyResult.Pass);

            sut.Validate(ValidDefinition());

            Assert.True(sut.CanSubmitBuild);
            Assert.True(sut.HasValidatedDefinition);
            Assert.True(sut.IsValidationPassVisible);
            Assert.False(sut.IsValidationResultVisible);
            Assert.Empty(sut.Violations);
        }

        [Fact]
        public void Validate_WhenDefinitionFails_DisablesSubmissionAndExposesViolations()
        {
            var sut = CreateSut(PolicyResult.Pass);

            sut.Validate(new ToolDefinition());

            Assert.False(sut.CanSubmitBuild);
            Assert.False(sut.HasValidatedDefinition);
            Assert.False(sut.IsValidationPassVisible);
            Assert.True(sut.IsValidationResultVisible);
            Assert.Contains(sut.Violations, v => v.RuleId == "L1-REQ-001");
        }

        [Fact]
        public void Validate_WhenPolicyCheckerMissing_AddsPolicyUnavailableViolation()
        {
            var sut = CreateSut(policyResult: null, withPolicyChecker: false);

            sut.Validate(ValidDefinition());

            Assert.False(sut.CanSubmitBuild);
            Assert.Contains(sut.Violations, v => v.RuleId == "POLICY-UNAVAIL");
        }

        [Fact]
        public void MarkDefinitionChanged_InvalidatesPreviouslyValidDefinition()
        {
            var sut = CreateSut(PolicyResult.Pass);
            sut.Validate(ValidDefinition());

            sut.MarkDefinitionChanged();

            Assert.False(sut.CanSubmitBuild);
            Assert.False(sut.HasValidatedDefinition);
            Assert.False(sut.IsValidationPassVisible);
        }

        private static ValidationViewModel CreateSut(PolicyResult? policyResult, bool withPolicyChecker = true) =>
            new(
                new RequiredFieldsValidator(),
                new ImageUriValidator(),
                new DockerfileStructureValidator(),
                new PackageVersionValidator(),
                new ValidatedDefinitionState(),
                withPolicyChecker ? new StubPolicyChecker(policyResult ?? PolicyResult.Pass) : null);

        private static ToolDefinition ValidDefinition() =>
            new()
            {
                Name = "bwa",
                Version = "0.7.17",
                ImageUri = "registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                DockerfileContent = "FROM ubuntu:22.04@sha256:abc123def456\nRUN echo ok\n",
                Script = "bwa mem",
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

        private sealed class StubPolicyChecker : IPolicyChecker
        {
            private readonly PolicyResult _result;

            public StubPolicyChecker(PolicyResult result)
            {
                _result = result;
            }

            public PolicyResult Check(string dockerfileContent) => _result;
        }
    }
}
