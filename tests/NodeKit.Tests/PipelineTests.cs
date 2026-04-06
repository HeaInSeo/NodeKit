using System.Linq;
using Avalonia;
using Xunit;

namespace NodeKit.Tests
{
    public class PipelineTests
    {
        [Fact]
        public void AddNode_ShouldAddToPipeline()
        {
            var pipeline = new Pipeline();
            var def = SampleDefinitions.BwaMem2();

            pipeline.AddNode(def, new Point(0, 0));

            Assert.Single(pipeline.Nodes);
        }

        [Fact]
        public void TryConnect_MatchingPorts_ShouldSucceed()
        {
            var pipeline = new Pipeline();

            // Trimmomatic: out 2 → BWA: in 2
            var trim = pipeline.AddNode(SampleDefinitions.Trimmomatic(), new Point(0, 0));
            var bwa = pipeline.AddNode(SampleDefinitions.BwaMem2(), new Point(300, 0));

            var result = pipeline.TryConnect(trim.NodeId, bwa.NodeId, out var conn);

            Assert.True(result);
            Assert.NotNull(conn);
            Assert.Equal(2, conn!.PortCount);
            Assert.Single(pipeline.Connections);
        }

        [Fact]
        public void TryConnect_MismatchedPorts_ShouldFail()
        {
            var pipeline = new Pipeline();

            // BWA: out 1 → GATK: in 1 → 성공
            // BWA: out 1 → Trimmomatic: in 2 → 실패
            var bwa = pipeline.AddNode(SampleDefinitions.BwaMem2(), new Point(0, 0));
            var trim = pipeline.AddNode(SampleDefinitions.Trimmomatic(), new Point(300, 0));

            var result = pipeline.TryConnect(bwa.NodeId, trim.NodeId, out var conn);

            Assert.False(result);
            Assert.Null(conn);
            Assert.Empty(pipeline.Connections);
        }

        [Fact]
        public void TryConnect_BwaToGatk_ShouldSucceed()
        {
            var pipeline = new Pipeline();

            var bwa = pipeline.AddNode(SampleDefinitions.BwaMem2(), new Point(0, 0));
            var gatk = pipeline.AddNode(SampleDefinitions.GatkHc(), new Point(300, 0));

            var result = pipeline.TryConnect(bwa.NodeId, gatk.NodeId, out var conn);

            Assert.True(result);
            Assert.Equal(1, conn!.PortCount);
        }

        [Fact]
        public void NodeDefinition_InputOutputPorts_ShouldBeSeparated()
        {
            var def = SampleDefinitions.BwaMem2();

            Assert.Equal(2, def.InputPorts.Count());
            Assert.Equal(1, def.OutputPorts.Count());
        }
    }
}
