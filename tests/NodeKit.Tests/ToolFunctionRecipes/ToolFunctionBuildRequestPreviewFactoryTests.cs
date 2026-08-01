using System.Collections.Generic;
using System.Text.Json;
using NodeKit.Authoring.ToolFunctionRecipes;
using NodeKit.Grpc;
using Xunit;

namespace NodeKit.Tests.ToolFunctionRecipes
{
    public class ToolFunctionBuildRequestPreviewFactoryTests
    {
        private static ToolFunctionRecipe ReadyRecipe() => new()
        {
            ToolSpecDigest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            BaseToolImageDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            FunctionId = "samtools.sort",
            Revision = "v1",
            ScriptPath = "./sort.sh",
            State = ToolFunctionRecipeState.Ready,
            Command = new CommandContract { Executable = "samtools", Arguments = new List<string> { "sort" } },
            InputPorts = new List<PortContract> { new() { Name = "bam", Direction = PortDirection.Input } },
            OutputPorts = new List<PortContract> { new() { Name = "sortedBam", Direction = PortDirection.Output } },
            FixtureReferences = new List<FixtureReference> { new() { LocalPath = "./fixtures/small.bam" } },
            EnforcedResources = new ResourceContract
            {
                CpuRequest = "500m",
                CpuLimit = "2000m",
                MemoryRequest = "256Mi",
                MemoryLimit = "1Gi",
            },
        };

        [Fact]
        public void Build_SeparatesStage1AndStage2TopLevelGroups()
        {
            var json = ToolFunctionBuildRequestPreviewFactory.Build(ReadyRecipe());

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.True(root.TryGetProperty("stage1", out var stage1));
            Assert.True(root.TryGetProperty("stage2", out var stage2));

            Assert.Equal(2, stage1.GetProperty("kind").GetInt32());
            Assert.Equal("sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", stage1.GetProperty("base_image_digest").GetString());
            Assert.Equal("./sort.sh", stage1.GetProperty("script").GetString());

            Assert.True(stage2.TryGetProperty("command", out _));
            Assert.True(stage2.TryGetProperty("inputPorts", out _));
            Assert.True(stage2.TryGetProperty("outputPorts", out _));
            Assert.True(stage2.TryGetProperty("parameters", out _));
            Assert.True(stage2.TryGetProperty("fixtureReferences", out _));
            Assert.True(stage2.TryGetProperty("expectedResults", out _));
            Assert.True(stage2.TryGetProperty("intermediateFilePolicies", out _));
            Assert.True(stage2.TryGetProperty("enforcedResources", out _));
            Assert.True(stage2.TryGetProperty("executionEnvironment", out _));
            Assert.True(stage2.TryGetProperty("validationRequirements", out _));
        }
    }
}
