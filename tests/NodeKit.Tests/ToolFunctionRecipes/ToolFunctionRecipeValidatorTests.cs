using System.Collections.Generic;
using NodeKit.Authoring.ToolFunctionRecipes;
using NodeKit.Validation.ToolFunctionRecipes;
using Xunit;

namespace NodeKit.Tests.ToolFunctionRecipes
{
    public class ToolFunctionRecipeValidatorTests
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
        public void ValidRecipe_PassesWithNoViolations()
        {
            var result = ToolFunctionRecipeValidator.Validate(ValidRecipe());
            Assert.True(result.IsValid);
        }

        [Fact]
        public void MissingDigestReferences_FailsWithL1TFR001()
        {
            var recipe = ValidRecipe();
            recipe.ToolSpecDigest = string.Empty;
            recipe.BaseToolImageDigest = string.Empty;

            var result = ToolFunctionRecipeValidator.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Contains(result.Violations, v => v.RuleId == "L1-TFR-001" && v.Field == "ToolSpecDigest");
            Assert.Contains(result.Violations, v => v.RuleId == "L1-TFR-001" && v.Field == "BaseToolImageDigest");
        }

        [Theory]
        [InlineData("Samtools.Sort")]
        [InlineData("samtools sort")]
        [InlineData("1samtools")]
        public void InvalidFunctionIdFormat_FailsWithL1TFR002(string functionId)
        {
            var recipe = ValidRecipe();
            recipe.FunctionId = functionId;

            var result = ToolFunctionRecipeValidator.Validate(recipe);

            Assert.Contains(result.Violations, v => v.RuleId == "L1-TFR-002");
        }

        // FR-006/SC-002: raw shell 문자열 100% 차단 (quickstart 시나리오 2).
        [Theory]
        [InlineData("bash -c 'samtools sort'")]
        [InlineData("samtools;rm -rf /")]
        [InlineData("samtools|tee")]
        public void RawShellExecutable_FailsWithL1TFR003(string executable)
        {
            var recipe = ValidRecipe();
            recipe.Command.Executable = executable;

            var result = ToolFunctionRecipeValidator.Validate(recipe);

            Assert.Contains(result.Violations, v => v.RuleId == "L1-TFR-003");
        }

        [Fact]
        public void DuplicatePortNames_FailsWithL1TFR004()
        {
            var recipe = ValidRecipe();
            recipe.OutputPorts[0].Name = recipe.InputPorts[0].Name;

            var result = ToolFunctionRecipeValidator.Validate(recipe);

            Assert.Contains(result.Violations, v => v.RuleId == "L1-TFR-004");
        }

        [Fact]
        public void MemoryLimitBelowRequest_FailsWithL1TFR005()
        {
            var recipe = ValidRecipe();
            recipe.EnforcedResources.MemoryRequest = "1Gi";
            recipe.EnforcedResources.MemoryLimit = "256Mi";

            var result = ToolFunctionRecipeValidator.Validate(recipe);

            Assert.Contains(result.Violations, v => v.RuleId == "L1-TFR-005" && v.Field == "MemoryLimit");
        }

        [Fact]
        public void CpuLimitBelowRequest_FailsWithL1TFR005()
        {
            var recipe = ValidRecipe();
            recipe.EnforcedResources.CpuRequest = "2000m";
            recipe.EnforcedResources.CpuLimit = "500m";

            var result = ToolFunctionRecipeValidator.Validate(recipe);

            Assert.Contains(result.Violations, v => v.RuleId == "L1-TFR-005" && v.Field == "CpuLimit");
        }

        [Fact]
        public void MissingFixtureReference_FailsWithL1TFR006()
        {
            var recipe = ValidRecipe();
            recipe.FixtureReferences.Clear();

            var result = ToolFunctionRecipeValidator.Validate(recipe);

            Assert.Contains(result.Violations, v => v.RuleId == "L1-TFR-006" && v.Field == "FixtureReferences");
        }

        [Fact]
        public void MissingPorts_FailsWithL1TFR006()
        {
            var recipe = ValidRecipe();
            recipe.InputPorts.Clear();
            recipe.OutputPorts.Clear();

            var result = ToolFunctionRecipeValidator.Validate(recipe);

            Assert.Contains(result.Violations, v => v.RuleId == "L1-TFR-006" && v.Field == "InputPorts");
            Assert.Contains(result.Violations, v => v.RuleId == "L1-TFR-006" && v.Field == "OutputPorts");
        }
    }
}
