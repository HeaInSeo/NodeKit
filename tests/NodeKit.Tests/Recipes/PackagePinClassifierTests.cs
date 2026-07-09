using NodeKit.Validation.Recipes;
using Xunit;

namespace NodeKit.Tests.Recipes
{
    public class PackagePinClassifierTests
    {
        [Fact]
        public void Classify_FullPin_ReturnsFullPin()
        {
            Assert.Equal(PackagePinStatus.FullPin, PackagePinClassifier.Classify("bwa=0.7.17=h5bf99c6_8"));
        }

        [Fact]
        public void Classify_VersionOnly_ReturnsVersionOnly()
        {
            Assert.Equal(PackagePinStatus.VersionOnly, PackagePinClassifier.Classify("bwa=0.7.17"));
        }

        [Fact]
        public void Classify_NoEquals_ReturnsMalformed()
        {
            Assert.Equal(PackagePinStatus.Malformed, PackagePinClassifier.Classify("bwa"));
        }

        [Fact]
        public void Classify_Empty_ReturnsMalformed()
        {
            Assert.Equal(PackagePinStatus.Malformed, PackagePinClassifier.Classify(""));
        }

        [Fact]
        public void Classify_TooManyEquals_ReturnsMalformed()
        {
            Assert.Equal(PackagePinStatus.Malformed, PackagePinClassifier.Classify("bwa=0.7.17=h5bf99c6_8=extra"));
        }
    }
}
