using System.Collections.Generic;
using NodeKit.Authoring.ToolFunctionRecipes;
using NodeKit.Validation.ToolFunctionRecipes;
using Xunit;

namespace NodeKit.Tests.ToolFunctionRecipes
{
    /// <summary>
    /// /speckit-analyze가 찾은 커버리지 공백(quickstart.md 시나리오 1의 두
    /// 번째 명령 — validate 성공 시 State가 실제로 Draft→Ready로 전이되는지)을
    /// 메우는 테스트. 파일 I/O 왕복은 CLI 계층(CliApp.RunFunctionRecipeValidate)의
    /// 책임이라 여기서는 순수 in-memory 상태 전이만 검증한다.
    /// </summary>
    public class ToolFunctionRecipeValidationPipelineTests
    {
        private static ToolFunctionRecipe ValidRecipe() => new()
        {
            ToolSpecDigest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            BaseToolImageDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            FunctionId = "samtools.sort",
            Revision = "v1",
            ScriptPath = "./sort.sh",
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
        public void ValidationPasses_TransitionsStateDraftToReady()
        {
            var recipe = ValidRecipe();
            Assert.Equal(ToolFunctionRecipeState.Draft, recipe.State);

            var result = ToolFunctionRecipeValidationPipeline.Validate(recipe);

            Assert.True(result.IsValid);
            Assert.Equal(ToolFunctionRecipeState.Ready, recipe.State);
        }

        [Fact]
        public void ValidationFails_StateStaysDraftAndViolationsReported()
        {
            var recipe = ValidRecipe();
            recipe.FunctionId = string.Empty;

            var result = ToolFunctionRecipeValidationPipeline.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Equal(ToolFunctionRecipeState.Draft, recipe.State);
            Assert.NotEmpty(result.Violations);
        }
    }
}
