using System;
using System.Collections.Generic;
using NodeKit.Authoring;
using NodeKit.Grpc;
using Xunit;

namespace NodeKit.Tests
{
    public class BuildRequestFactoryTests
    {
        [Fact]
        public void FromToolDefinition_MapsAllScalarFields()
        {
            var id = Guid.NewGuid();
            var def = new ToolDefinition
            {
                Id = id,
                Name = "BWA-MEM2",
                ImageUri = "registry.example.com/bwa:2.2.1@sha256:abc",
                DockerfileContent = "FROM ubuntu:22.04 AS builder",
                Script = "bwa mem ref.fa reads.fq > out.sam",
                EnvironmentSpec = string.Empty,
                Inputs = new List<ToolInput> { new() { Name = "reads.fq" } },
                Outputs = new List<ToolOutput> { new() { Name = "out.sam" } },
            };

            var req = BuildRequestFactory.FromToolDefinition(def);

            Assert.Equal(id, req.ToolDefinitionId);
            Assert.Equal("BWA-MEM2", req.ToolName);
            Assert.Equal("registry.example.com/bwa:2.2.1@sha256:abc", req.ImageUri);
            Assert.Equal("FROM ubuntu:22.04 AS builder", req.DockerfileContent);
            Assert.Equal("bwa mem ref.fa reads.fq > out.sam", req.Script);
        }

        [Fact]
        public void FromToolDefinition_MapsInputOutputNames()
        {
            var def = new ToolDefinition
            {
                ImageUri = "reg/img:1.0@sha256:abc",
                Inputs = new List<ToolInput>
                {
                    new() { Name = "input.fastq" },
                    new() { Name = "ref.fa" },
                },
                Outputs = new List<ToolOutput>
                {
                    new() { Name = "out.bam" },
                },
            };

            var req = BuildRequestFactory.FromToolDefinition(def);

            Assert.Equal(new[] { "input.fastq", "ref.fa" }, req.InputNames);
            Assert.Equal(new[] { "out.bam" }, req.OutputNames);
        }

        [Fact]
        public void FromToolDefinition_RequestIdIsNonEmpty()
        {
            var def = new ToolDefinition { ImageUri = "reg/img:1.0@sha256:abc" };

            var req = BuildRequestFactory.FromToolDefinition(def);

            Assert.False(string.IsNullOrEmpty(req.RequestId));
        }

        [Fact]
        public void FromToolDefinition_EmptyIoLists_MapToEmpty()
        {
            var def = new ToolDefinition { ImageUri = "reg/img:1.0@sha256:abc" };

            var req = BuildRequestFactory.FromToolDefinition(def);

            Assert.Empty(req.InputNames);
            Assert.Empty(req.OutputNames);
        }
    }
}
