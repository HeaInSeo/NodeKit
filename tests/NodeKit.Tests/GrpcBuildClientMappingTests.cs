using System;
using System.Collections.Generic;
using System.Text.Json;
using NodeKit.Authoring;
using NodeKit.Grpc;
using Xunit;

namespace NodeKit.Tests
{
    /// <summary>
    /// BuildRequest → Nodevault.V1.BuildRequest proto 변환 단위 테스트.
    /// 네트워크 없이 GrpcBuildClient.ToProto/ToPortSpec을 직접 검증한다.
    /// </summary>
    public class GrpcBuildClientMappingTests
    {
        [Fact]
        public void ToProto_MapsScalarFields()
        {
            var toolDefinitionId = Guid.NewGuid();
            var request = new BuildRequest
            {
                RequestId = "req-1",
                ToolDefinitionId = toolDefinitionId,
                ToolName = "BWA-MEM2",
                Version = "2.2.1",
                ImageUri = "registry.example.com/bwa:2.2.1@sha256:abc",
                DockerfileContent = "FROM ubuntu:22.04",
                Script = "bwa mem ref.fa reads.fq",
                EnvironmentSpec = "name: env\n",
            };

            var proto = GrpcBuildClient.ToProto(request);

            Assert.Equal("req-1", proto.RequestId);
            Assert.Equal(toolDefinitionId.ToString(), proto.ToolDefinitionId);
            Assert.Equal("BWA-MEM2", proto.ToolName);
            Assert.Equal("2.2.1", proto.Version);
            Assert.Equal("registry.example.com/bwa:2.2.1@sha256:abc", proto.ImageUri);
            Assert.Equal("FROM ubuntu:22.04", proto.DockerfileContent);
            Assert.Equal("bwa mem ref.fa reads.fq", proto.Script);
            Assert.Equal("name: env\n", proto.EnvironmentSpec);
        }

        [Fact]
        public void ToProto_MapsDisplaySpec()
        {
            var request = new BuildRequest
            {
                DisplayLabel = "BWA-MEM 0.7.17",
                DisplayDescription = "Sequence alignment",
                DisplayCategory = "Alignment",
                DisplayTags = new List<string> { "alignment", "fastq" },
            };

            var proto = GrpcBuildClient.ToProto(request);

            Assert.Equal("BWA-MEM 0.7.17", proto.Display.Label);
            Assert.Equal("Sequence alignment", proto.Display.Description);
            Assert.Equal("Alignment", proto.Display.Category);
            Assert.Equal(new[] { "alignment", "fastq" }, proto.Display.Tags);
        }

        [Fact]
        public void ToProto_MapsInputsAndOutputsAsPortSpecs()
        {
            var request = new BuildRequest
            {
                Inputs = new List<ToolInput>
                {
                    new() { Name = "reads", Role = "sample-fastq", Format = "fastq", Shape = "pair", Required = false },
                },
                Outputs = new List<ToolOutput>
                {
                    new() { Name = "aligned", Role = "aligned-bam", Format = "bam", Shape = "single", Class = "secondary" },
                },
            };

            var proto = GrpcBuildClient.ToProto(request);

            Assert.Single(proto.Inputs);
            Assert.Equal("reads", proto.Inputs[0].Name);
            Assert.Equal("sample-fastq", proto.Inputs[0].Role);
            Assert.Equal("fastq", proto.Inputs[0].Format);
            Assert.Equal("pair", proto.Inputs[0].Shape);
            Assert.False(proto.Inputs[0].Required);

            Assert.Single(proto.Outputs);
            Assert.Equal("aligned", proto.Outputs[0].Name);
            Assert.Equal("aligned-bam", proto.Outputs[0].Role);
            Assert.Equal("bam", proto.Outputs[0].Format);
            Assert.Equal("single", proto.Outputs[0].Shape);
            Assert.Equal("secondary", proto.Outputs[0].Class);
        }

        [Fact]
        public void ToProto_WhenCommandPresent_SerializesAsJsonArray()
        {
            var request = new BuildRequest
            {
                Command = new List<string> { "/bin/sh", "-c", "/app/executor.sh" },
            };

            var proto = GrpcBuildClient.ToProto(request);

            Assert.Equal(
                request.Command,
                JsonSerializer.Deserialize<List<string>>(proto.Command));
        }

        [Fact]
        public void ToProto_WhenCommandEmpty_LeavesCommandFieldEmpty()
        {
            var request = new BuildRequest();

            var proto = GrpcBuildClient.ToProto(request);

            Assert.Equal(string.Empty, proto.Command);
        }
    }
}
