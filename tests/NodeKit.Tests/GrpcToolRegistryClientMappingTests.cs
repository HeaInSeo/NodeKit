using NodeKit.Grpc;
using Nodevault.V1;
using Xunit;

namespace NodeKit.Tests
{
    /// <summary>
    /// GrpcToolRegistryClient.ToRegisteredTool의 legacy display(14) label/category 표시 의미 회귀.
    /// display는 NodeVault 912b23c에서 [deprecated]이므로, 테스트는 deprecated 속성을 직접 쓰지 않고
    /// proto JSON으로 메시지를 만들어 억제 범위를 제품 코드의 두 call-site로 유지한다.
    /// </summary>
    public sealed class GrpcToolRegistryClientMappingTests
    {
        private static RegisteredToolDefinition Parse(string json)
            => RegisteredToolDefinition.Parser.ParseJson(json);

        [Fact]
        public void Display_LabelAndCategory_AreUsed()
        {
            var tool = GrpcToolRegistryClient.ToRegisteredTool(Parse(
                "{\"toolName\":\"bwa\",\"version\":\"0.7.17\",\"display\":{\"label\":\"BWA MEM\",\"category\":\"Alignment\"}}"));

            Assert.Equal("BWA MEM", tool.DisplayLabel);
            Assert.Equal("Alignment", tool.DisplayCategory);
        }

        [Fact]
        public void Display_EmptyLabel_FallsBackToNameAndVersion()
        {
            var tool = GrpcToolRegistryClient.ToRegisteredTool(Parse(
                "{\"toolName\":\"bwa\",\"version\":\"0.7.17\",\"display\":{\"label\":\"\",\"category\":\"Alignment\"}}"));

            Assert.Equal("bwa 0.7.17", tool.DisplayLabel);
            Assert.Equal("Alignment", tool.DisplayCategory);
        }

        [Fact]
        public void NoDisplay_FallsBackToNameAndVersion_AndEmptyCategory()
        {
            var tool = GrpcToolRegistryClient.ToRegisteredTool(Parse(
                "{\"toolName\":\"bwa\",\"version\":\"0.7.17\"}"));

            Assert.Equal("bwa 0.7.17", tool.DisplayLabel);
            Assert.Equal(string.Empty, tool.DisplayCategory);
        }

        [Fact]
        public void NoDisplay_NoVersion_FallsBackToName()
        {
            var tool = GrpcToolRegistryClient.ToRegisteredTool(Parse("{\"toolName\":\"bwa\"}"));

            Assert.Equal("bwa", tool.DisplayLabel);
            Assert.Equal(string.Empty, tool.DisplayCategory);
        }
    }
}
