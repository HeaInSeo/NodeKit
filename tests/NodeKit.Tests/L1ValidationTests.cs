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
            Assert.False(_sut.Validate(def).IsValid);
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

        private static ToolDefinition DefWithSpec(string spec) =>
            new() { ImageUri = "reg/img:1.0@sha256:abc", EnvironmentSpec = spec };
    }
}
