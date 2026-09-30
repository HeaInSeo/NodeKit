using System.Collections.Generic;
using System.Linq;
using NodeKit.Authoring.ToolFunctionRecipes;
using NodeKit.Validation;
using NodeKit.Validation.ToolFunctionRecipes;
using Xunit;

namespace NodeKit.Tests.ToolFunctionRecipes
{
    /// <summary>
    /// O-1 authoring binding bridge(whole-element {param|input|output.&lt;name&gt;})의
    /// 참조 무결성 검증. GR-BAL-NODEKIT-O1-ACCEPT matrix의 V1-V8(Ready)과
    /// I1-I11(Draft + O-1 위반)을 그대로 고정한다.
    /// </summary>
    public class ToolFunctionRecipeArgumentReferenceTests
    {
        private static readonly string[] _o1RuleIds = { "L1-TFR-008", "L1-TFR-009", "L1-TFR-010", "L1-TFR-011", "L1-TFR-012" };

        private static ToolFunctionRecipe Recipe(IEnumerable<string> arguments, params ParameterContract[] parameters) => new()
        {
            ToolSpecDigest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            BaseToolImageDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            FunctionId = "samtools.sort",
            Revision = "v1",
            ScriptPath = "./sort.sh",
            Command = new CommandContract { Executable = "samtools", Arguments = arguments.ToList() },
            InputPorts = new List<PortContract> { new() { Name = "reads", Direction = PortDirection.Input } },
            OutputPorts = new List<PortContract> { new() { Name = "bam", Direction = PortDirection.Output } },
            FixtureReferences = new List<FixtureReference> { new() { LocalPath = "./fixtures/small.bam" } },
            Parameters = parameters.ToList(),
            EnforcedResources = new ResourceContract
            {
                CpuRequest = "500m",
                CpuLimit = "2000m",
                MemoryRequest = "256Mi",
                MemoryLimit = "1Gi",
            },
        };

        private static ParameterContract Param(string name, ParameterType type = ParameterType.String, string mapping = "") =>
            new() { Name = name, Type = type, CliArgumentMapping = mapping };

        private static void AssertReady(ToolFunctionRecipe recipe)
        {
            var result = ToolFunctionRecipeValidationPipeline.Validate(recipe);

            Assert.True(result.IsValid, string.Join("; ", result.Violations));
            Assert.Equal(ToolFunctionRecipeState.Ready, recipe.State);
        }

        private static IReadOnlyList<ValidationViolation> AssertDraft(ToolFunctionRecipe recipe)
        {
            var result = ToolFunctionRecipeValidationPipeline.Validate(recipe);

            Assert.False(result.IsValid);
            Assert.Equal(ToolFunctionRecipeState.Draft, recipe.State);
            return result.Violations.Where(v => _o1RuleIds.Contains(v.RuleId)).ToList();
        }

        // ---- VALID → Ready ----

        [Fact]
        public void V1_ZeroArgumentsNoSubcommandZeroPorts_Ready()
        {
            var recipe = Recipe(System.Array.Empty<string>());
            recipe.InputPorts.Clear();
            recipe.OutputPorts.Clear();

            AssertReady(recipe);
        }

        [Fact]
        public void V2_LiteralOnlyArguments_Ready()
        {
            AssertReady(Recipe(new[] { "sort", "-@4" }));
        }

        [Fact]
        public void V3_SingleParamReference_Ready()
        {
            AssertReady(Recipe(new[] { "sort", "{param.threads}" }, Param("threads", ParameterType.Integer)));
        }

        [Fact]
        public void V4_RepeatedReference_Ready()
        {
            AssertReady(Recipe(new[] { "{param.t}", "x", "{param.t}" }, Param("t")));
        }

        [Fact]
        public void V5_GrammarEdgeName_Ready()
        {
            AssertReady(Recipe(new[] { "{param.a.b-c_1}" }, Param("a.b-c_1")));
        }

        [Fact]
        public void V6_CaseSensitiveDistinctNames_Ready()
        {
            AssertReady(Recipe(new[] { "{param.t}", "{param.T}" }, Param("t"), Param("T")));
        }

        [Fact]
        public void V7_BooleanParamWithMappingReferenced_Ready()
        {
            AssertReady(Recipe(new[] { "{param.v}" }, Param("v", ParameterType.Boolean, "--verbose")));
        }

        [Fact]
        public void V8_ZeroPortEmptyExpectedResults_Ready()
        {
            var recipe = Recipe(new[] { "sort" });
            recipe.InputPorts.Clear();
            recipe.OutputPorts.Clear();

            AssertReady(recipe);
        }

        // ---- INVALID → Draft + O-1 violation ----

        [Fact]
        public void I1_UnresolvedParamReference_Draft()
        {
            var violation = Assert.Single(AssertDraft(Recipe(new[] { "sort", "{param.nope}" })));

            Assert.Equal("L1-TFR-009", violation.RuleId);
            Assert.Equal("Command.Arguments[1]", violation.Field);
            Assert.Contains("{param.nope}", violation.Message);
        }

        [Theory]
        [InlineData("--t={param.threads}")]
        [InlineData("x{param.threads}")]
        [InlineData("{param.threads}x")]
        public void I2_EmbeddedReference_DraftAndParamUnconsumed(string element)
        {
            var violations = AssertDraft(Recipe(new[] { element }, Param("threads")));

            Assert.Contains(violations, v => v.RuleId == "L1-TFR-008" && v.Field == "Command.Arguments[0]" && v.Message.Contains(element));
            Assert.Contains(violations, v => v.RuleId == "L1-TFR-012" && v.Field == "Parameters[0]" && v.Message.Contains("threads"));
            Assert.Equal(2, violations.Count);
        }

        [Fact]
        public void I2_EmbeddedReferenceAlongsideWholeElement_OnlyEmbeddedRejected()
        {
            var violation = Assert.Single(AssertDraft(Recipe(new[] { "--t={param.threads}", "{param.threads}" }, Param("threads"))));

            Assert.Equal("L1-TFR-008", violation.RuleId);
            Assert.Equal("Command.Arguments[0]", violation.Field);
        }

        [Fact]
        public void I3_EmptyTarget_Draft()
        {
            var violation = Assert.Single(AssertDraft(Recipe(new[] { "{param.}" })));

            Assert.Equal("L1-TFR-008", violation.RuleId);
            Assert.Equal("Command.Arguments[0]", violation.Field);
        }

        [Theory]
        [InlineData("{param.1bad}")]
        [InlineData("{param.a b}")]
        [InlineData("{param._x}")]
        [InlineData("{param.é}")]
        public void I4_BadReferenceNameGrammar_Draft(string element)
        {
            var violation = Assert.Single(AssertDraft(Recipe(new[] { element })));

            Assert.Equal("L1-TFR-008", violation.RuleId);
            Assert.Equal("Command.Arguments[0]", violation.Field);
        }

        [Theory]
        [InlineData("{foo.x}")]
        [InlineData("{params.x}")]
        [InlineData("{Param.x}")]
        public void I5_WrongNamespace_Draft(string element)
        {
            var violations = AssertDraft(Recipe(new[] { element }, Param("x")));

            Assert.Contains(violations, v => v.RuleId == "L1-TFR-008" && v.Field == "Command.Arguments[0]");
            Assert.Contains(violations, v => v.RuleId == "L1-TFR-012" && v.Field == "Parameters[0]");
        }

        [Fact]
        public void I6_ParamReferenceToInputPortName_Unresolved()
        {
            var violation = Assert.Single(AssertDraft(Recipe(new[] { "{param.reads}" })));

            Assert.Equal("L1-TFR-009", violation.RuleId);
            Assert.Equal("Command.Arguments[0]", violation.Field);
        }

        [Fact]
        public void I6_InputReferenceToParameterName_Unresolved()
        {
            var violations = AssertDraft(Recipe(new[] { "{input.threads}", "{param.threads}" }, Param("threads")));

            var violation = Assert.Single(violations);
            Assert.Equal("L1-TFR-009", violation.RuleId);
            Assert.Equal("Command.Arguments[0]", violation.Field);
        }

        [Theory]
        [InlineData("{input.reads}")]
        [InlineData("{output.bam}")]
        public void I7_DirectPortReference_NotRunnable(string element)
        {
            var violation = Assert.Single(AssertDraft(Recipe(new[] { element })));

            Assert.Equal("L1-TFR-010", violation.RuleId);
            Assert.Equal("Command.Arguments[0]", violation.Field);
            Assert.Contains(element, violation.Message);
        }

        [Fact]
        public void I7_DraftKeepsDirectPortReferenceUnchanged()
        {
            var recipe = Recipe(new[] { "{input.reads}" });

            AssertDraft(recipe);

            Assert.Equal(new[] { "{input.reads}" }, recipe.Command.Arguments);
        }

        [Fact]
        public void I8_ParamWithOnlyCliMapping_Unconsumed()
        {
            var violation = Assert.Single(AssertDraft(Recipe(new[] { "sort" }, Param("threads", ParameterType.Integer, "-@"))));

            Assert.Equal("L1-TFR-012", violation.RuleId);
            Assert.Equal("Parameters[0]", violation.Field);
            Assert.Contains("threads", violation.Message);
        }

        [Fact]
        public void I8_ParamReferencedOnlyFromEnvironment_Unconsumed()
        {
            var recipe = Recipe(new[] { "sort" }, Param("threads"));
            recipe.Command.Environment.Add(new EnvironmentEntry { Name = "THREADS", Source = "{param.threads}" });

            var violation = Assert.Single(AssertDraft(recipe));

            Assert.Equal("L1-TFR-012", violation.RuleId);
            Assert.Equal("Parameters[0]", violation.Field);
        }

        [Fact]
        public void I8_RequiredDefaultedBooleanParams_Unconsumed()
        {
            var required = Param("a");
            required.Required = true;
            var defaulted = Param("b");
            defaulted.DefaultValue = "4";
            var boolean = Param("c", ParameterType.Boolean, "--verbose");

            var violations = AssertDraft(Recipe(new[] { "sort" }, required, defaulted, boolean));

            Assert.Equal(new[] { "Parameters[0]", "Parameters[1]", "Parameters[2]" }, violations.Select(v => v.Field));
            Assert.All(violations, v => Assert.Equal("L1-TFR-012", v.RuleId));
        }

        [Fact]
        public void I9_DuplicateParamName_Draft()
        {
            var violation = Assert.Single(AssertDraft(Recipe(new[] { "{param.t}" }, Param("t"), Param("t"))));

            Assert.Equal("L1-TFR-011", violation.RuleId);
            Assert.Equal("Parameters[1].Name", violation.Field);
            Assert.Contains("'t'", violation.Message);
        }

        [Theory]
        [InlineData("1bad")]
        [InlineData("a b")]
        [InlineData("")]
        public void I10_UnreferenceableParamName_Draft(string name)
        {
            var violation = Assert.Single(AssertDraft(Recipe(new[] { "sort" }, Param(name))));

            Assert.Equal("L1-TFR-011", violation.RuleId);
            Assert.Equal("Parameters[0].Name", violation.Field);
        }

        [Fact]
        public void I11_RepeatedBadReference_OneViolationPerSite()
        {
            var violations = AssertDraft(Recipe(new[] { "{param.nope}", "x", "{param.nope}" }));

            Assert.Equal(2, violations.Count);
            Assert.All(violations, v => Assert.Equal("L1-TFR-009", v.RuleId));
            Assert.Equal(new[] { "Command.Arguments[0]", "Command.Arguments[2]" }, violations.Select(v => v.Field));
        }
    }
}
