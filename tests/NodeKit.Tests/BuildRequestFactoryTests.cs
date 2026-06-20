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
            Assert.Equal(string.Empty, req.EnvironmentSpec);
        }

        [Fact]
        public void FromToolDefinition_MapsEnvironmentSpec()
        {
            var def = new ToolDefinition
            {
                ImageUri = "reg/img:1.0@sha256:abc",
                EnvironmentSpec = "name: test\ndependencies:\n  - bwa=0.7.17=h5bf99c6_8\n",
            };

            var req = BuildRequestFactory.FromToolDefinition(def);

            Assert.Equal(def.EnvironmentSpec, req.EnvironmentSpec);
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

            Assert.Equal(2, req.Inputs.Count);
            Assert.Equal("input.fastq", req.Inputs[0].Name);
            Assert.Equal("ref.fa", req.Inputs[1].Name);
            Assert.Single(req.Outputs);
            Assert.Equal("out.bam", req.Outputs[0].Name);
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

            Assert.Empty(req.Inputs);
            Assert.Empty(req.Outputs);
        }

        [Fact]
        public void FromToolDefinition_MapsVersion()
        {
            var def = new ToolDefinition { ImageUri = "reg/img:1.0@sha256:abc", Version = "2.2.1" };

            var req = BuildRequestFactory.FromToolDefinition(def);

            Assert.Equal("2.2.1", req.Version);
        }

        [Fact]
        public void FromToolDefinition_MapsCommand()
        {
            var def = new ToolDefinition
            {
                ImageUri = "reg/img:1.0@sha256:abc",
                Command = new List<string> { "/bin/sh", "-c", "/app/executor.sh" },
            };

            var req = BuildRequestFactory.FromToolDefinition(def);

            Assert.Equal(def.Command, req.Command);
        }

        [Fact]
        public void FromToolDefinition_MapsDisplayMetadata()
        {
            var def = new ToolDefinition
            {
                ImageUri = "reg/img:1.0@sha256:abc",
                DisplayLabel = "BWA-MEM 0.7.17",
                DisplayDescription = "Sequence alignment",
                DisplayCategory = "Alignment",
                DisplayTags = new List<string> { "alignment", "fastq" },
            };

            var req = BuildRequestFactory.FromToolDefinition(def);

            Assert.Equal("BWA-MEM 0.7.17", req.DisplayLabel);
            Assert.Equal("Sequence alignment", req.DisplayDescription);
            Assert.Equal("Alignment", req.DisplayCategory);
            Assert.Equal(def.DisplayTags, req.DisplayTags);
        }

        [Fact]
        public void FromToolDefinition_MapsFullInputOutputContract()
        {
            var def = new ToolDefinition
            {
                ImageUri = "reg/img:1.0@sha256:abc",
                Inputs =
                {
                    new ToolInput { Name = "reads", Role = "sample-fastq", Format = "fastq", Shape = "pair", Required = false },
                },
                Outputs =
                {
                    new ToolOutput { Name = "aligned", Role = "aligned-bam", Format = "bam", Shape = "single", Class = "secondary" },
                },
            };

            var req = BuildRequestFactory.FromToolDefinition(def);

            var input = Assert.Single(req.Inputs);
            Assert.Equal("sample-fastq", input.Role);
            Assert.Equal("fastq", input.Format);
            Assert.Equal("pair", input.Shape);
            Assert.False(input.Required);

            var output = Assert.Single(req.Outputs);
            Assert.Equal("aligned-bam", output.Role);
            Assert.Equal("bam", output.Format);
            Assert.Equal("single", output.Shape);
            Assert.Equal("secondary", output.Class);
        }
    }
}
