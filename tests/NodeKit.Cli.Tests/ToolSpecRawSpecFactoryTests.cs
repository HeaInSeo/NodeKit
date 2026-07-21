using NodeKit.Authoring;
using NodeKit.Grpc;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// Golden test for ToolSpecRawSpecFactory.Build's exact wire shape (Issue
    /// #73). GrpcToolSpecClientWireTests already round-trips raw_spec through a
    /// real in-process gRPC server, which catches a field key typo (rejected by
    /// NodeVault's DisallowUnknownFields()) — but not a field silently mapped to
    /// the wrong value, since the fake server doesn't assert on payload content.
    /// This test pins the exact serialized JSON byte-for-byte so an accidental
    /// field swap/rename/reorder is caught even without a live round trip.
    /// </summary>
    public class ToolSpecRawSpecFactoryTests
    {
        [Fact]
        public void Build_AllFieldsPopulated_MatchesExactWireShape()
        {
            var definition = new ToolDefinition
            {
                Name = "bwa-mem",
                Version = "0.7.17",
                ImageUri = "condaforge/miniforge3@sha256:abc123",
                DockerfileContent = "FROM condaforge/miniforge3\nRUN conda install bwa=0.7.17",
                Script = "bwa mem ref.fa reads.fq",
                EnvironmentSpec = "channels: bioconda",
            };

            var rawSpec = ToolSpecRawSpecFactory.Build(definition);

            Assert.Equal(
                "{\"tool_name\":\"bwa-mem\",\"version\":\"0.7.17\",\"kind\":1," +
                "\"image_uri\":\"condaforge/miniforge3@sha256:abc123\"," +
                "\"dockerfile_content\":\"FROM condaforge/miniforge3\\nRUN conda install bwa=0.7.17\"," +
                "\"script\":\"bwa mem ref.fa reads.fq\"," +
                "\"environment_spec\":\"channels: bioconda\"}",
                rawSpec);
        }

        [Fact]
        public void Build_EmptyOptionalFields_SerializesAsEmptyStrings_NotNull()
        {
            var definition = new ToolDefinition
            {
                Name = "samtools",
                Version = "1.17",
            };

            var rawSpec = ToolSpecRawSpecFactory.Build(definition);

            Assert.Equal(
                "{\"tool_name\":\"samtools\",\"version\":\"1.17\",\"kind\":1," +
                "\"image_uri\":\"\",\"dockerfile_content\":\"\",\"script\":\"\"," +
                "\"environment_spec\":\"\"}",
                rawSpec);
        }
    }
}
