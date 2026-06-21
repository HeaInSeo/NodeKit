using System;
using System.Collections.Generic;
using NodeKit.Authoring;
using NodeKit.Grpc;
using Xunit;

namespace NodeKit.Tests
{
    public class DataRegisterRequestFactoryTests
    {
        [Fact]
        public void FromDataDefinition_MapsAllScalarFields()
        {
            var id = Guid.NewGuid();
            var def = new DataDefinition
            {
                Id = id,
                Name = "hg38-reference",
                Version = "2024-01",
                Description = "Human GRCh38 reference genome",
                Format = "FASTA",
                SourceUri = "https://ftp.ncbi.nlm.nih.gov/genomes/hg38.fa",
                Checksum = "abc123",
            };

            var req = DataRegisterRequestFactory.FromDataDefinition(def);

            Assert.Equal(id, req.DataDefinitionId);
            Assert.Equal("hg38-reference", req.DataName);
            Assert.Equal("2024-01", req.Version);
            Assert.Equal("Human GRCh38 reference genome", req.Description);
            Assert.Equal("FASTA", req.Format);
            Assert.Equal("https://ftp.ncbi.nlm.nih.gov/genomes/hg38.fa", req.SourceUri);
            Assert.Equal("abc123", req.Checksum);
        }

        [Fact]
        public void FromDataDefinition_MapsDisplayMetadata()
        {
            var def = new DataDefinition
            {
                DisplayLabel = "Human GRCh38 Reference",
                DisplayDescription = "Reference genome for alignment",
                DisplayCategory = "Reference Genome",
                DisplayTags = new List<string> { "genome", "reference" },
            };

            var req = DataRegisterRequestFactory.FromDataDefinition(def);

            Assert.Equal("Human GRCh38 Reference", req.DisplayLabel);
            Assert.Equal("Reference genome for alignment", req.DisplayDescription);
            Assert.Equal("Reference Genome", req.DisplayCategory);
            Assert.Equal(def.DisplayTags, req.DisplayTags);
        }

        [Fact]
        public void FromDataDefinition_RequestIdIsNonEmpty()
        {
            var req = DataRegisterRequestFactory.FromDataDefinition(new DataDefinition());

            Assert.False(string.IsNullOrEmpty(req.RequestId));
        }

        [Fact]
        public void FromDataDefinition_MutatingDefinitionTagsAfterward_DoesNotAffectRequest()
        {
            var def = new DataDefinition
            {
                DisplayTags = new List<string> { "genome" },
            };

            var req = DataRegisterRequestFactory.FromDataDefinition(def);

            def.DisplayTags.Add("reference");

            Assert.Single(req.DisplayTags);
        }
    }
}
