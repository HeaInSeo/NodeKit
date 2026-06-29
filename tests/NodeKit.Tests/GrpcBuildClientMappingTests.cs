using System;
using System.Globalization;
using NodeKit.Grpc;
using Xunit;

namespace NodeKit.Tests
{
    /// <summary>
    /// BuildRequest → Nodevault.V1.BuildRequest proto 변환 단위 테스트.
    /// 네트워크 없이 GrpcBuildClient.ToProto/FromProto를 직접 검증한다.
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
            Assert.Equal(Nodevault.V1.BuildKind.Toolspec, proto.Kind);
        }

        [Theory]
        [InlineData((int)Nodevault.V1.BuildEventKind.Log, (int)BuildEventKind.Log)]
        [InlineData((int)Nodevault.V1.BuildEventKind.JobCreated, (int)BuildEventKind.JobCreated)]
        [InlineData((int)Nodevault.V1.BuildEventKind.JobRunning, (int)BuildEventKind.JobRunning)]
        [InlineData((int)Nodevault.V1.BuildEventKind.PushSucceeded, (int)BuildEventKind.RegistryPushSucceeded)]
        [InlineData((int)Nodevault.V1.BuildEventKind.DigestAcquired, (int)BuildEventKind.DigestAcquired)]
        [InlineData((int)Nodevault.V1.BuildEventKind.Succeeded, (int)BuildEventKind.Succeeded)]
        [InlineData((int)Nodevault.V1.BuildEventKind.Failed, (int)BuildEventKind.Failed)]
        public void MapKind_MapsEachKnownKind(int protoValue, int expectedValue)
        {
            var proto = (Nodevault.V1.BuildEventKind)protoValue;
            var expected = (BuildEventKind)expectedValue;
            Assert.Equal(expected, GrpcBuildClient.MapKind(proto));
        }

        [Fact]
        public void MapKind_UnknownKind_FallsBackToLog()
        {
            Assert.Equal(BuildEventKind.Log, GrpcBuildClient.MapKind((Nodevault.V1.BuildEventKind)(-1)));
        }

        [Fact]
        public void FromProto_MapsAllFields()
        {
            var timestamp = DateTimeOffset.Parse("2026-06-19T12:34:56Z", CultureInfo.InvariantCulture);
            var ev = new Nodevault.V1.BuildEvent
            {
                Kind = Nodevault.V1.BuildEventKind.DigestAcquired,
                Message = "digest acquired",
                Timestamp = timestamp.ToUnixTimeMilliseconds(),
                Digest = "sha256:abc123",
            };

            var mapped = GrpcBuildClient.FromProto(ev);

            Assert.Equal(BuildEventKind.DigestAcquired, mapped.Kind);
            Assert.Equal("digest acquired", mapped.Message);
            Assert.Equal(timestamp.UtcDateTime, mapped.Timestamp);
            Assert.Equal("sha256:abc123", mapped.Digest);
        }
    }
}
