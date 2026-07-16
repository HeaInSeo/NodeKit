using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    public class HarborImageReferenceMapperTests
    {
        [Fact]
        public void TryMapToHarbor_NoMapConfigured_ReturnsNull()
        {
            var result = HarborImageReferenceMapper.TryMapToHarbor("condaforge/miniforge3:24.3.0-0", rawMap: null);

            Assert.Null(result);
        }

        [Fact]
        public void TryMapToHarbor_HostLessReference_MapsUsingDockerIoOrigin()
        {
            var result = HarborImageReferenceMapper.TryMapToHarbor(
                "condaforge/miniforge3:24.3.0-0",
                "docker.io=harbor.lab.local/dockerhub-proxy");

            Assert.Equal("harbor.lab.local/dockerhub-proxy/condaforge/miniforge3:24.3.0-0", result);
        }

        [Fact]
        public void TryMapToHarbor_ExplicitHostOrigin_MapsUsingThatOrigin()
        {
            var result = HarborImageReferenceMapper.TryMapToHarbor(
                "quay.io/biocontainers/bwa:0.7.17",
                "quay.io=harbor.lab.local/quay-proxy");

            Assert.Equal("harbor.lab.local/quay-proxy/biocontainers/bwa:0.7.17", result);
        }

        [Fact]
        public void TryMapToHarbor_OriginNotInMap_ReturnsNull()
        {
            var result = HarborImageReferenceMapper.TryMapToHarbor(
                "quay.io/biocontainers/bwa:0.7.17",
                "docker.io=harbor.lab.local/dockerhub-proxy");

            Assert.Null(result);
        }

        [Fact]
        public void TryMapToHarbor_TrailingSlashOnPrefix_IsTrimmed()
        {
            var result = HarborImageReferenceMapper.TryMapToHarbor(
                "condaforge/miniforge3:24.3.0-0",
                "docker.io=harbor.lab.local/dockerhub-proxy/");

            Assert.Equal("harbor.lab.local/dockerhub-proxy/condaforge/miniforge3:24.3.0-0", result);
        }

        [Fact]
        public void TryMapToHarbor_MultipleEntries_PicksMatchingOrigin()
        {
            var result = HarborImageReferenceMapper.TryMapToHarbor(
                "mambaorg/micromamba:1.5.8",
                "docker.io=harbor.lab.local/dockerhub-proxy,quay.io=harbor.lab.local/quay-proxy");

            Assert.Equal("harbor.lab.local/dockerhub-proxy/mambaorg/micromamba:1.5.8", result);
        }

        [Fact]
        public void HasAnyMapping_NoMapConfigured_ReturnsFalse()
        {
            Assert.False(HarborImageReferenceMapper.HasAnyMapping(rawMap: null));
        }

        [Fact]
        public void HasAnyMapping_MapConfigured_ReturnsTrue()
        {
            Assert.True(HarborImageReferenceMapper.HasAnyMapping("docker.io=harbor.lab.local/dockerhub-proxy"));
        }
    }
}
