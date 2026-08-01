using System;
using System.Collections.Generic;

namespace NodeKit.Authoring.ToolFunctionRecipes
{
    /// <summary>
    /// NodeKit ToolFunctionRecipe authoring draft (data-model.md 루트 엔티티,
    /// spec.md Key Entities). RecipeDocument와 개념적으로 대응하지만 build-kind
    /// 선택이 없다는 점에서 구조가 달라 별도 모델로 취급한다. ToolSpecDigest/
    /// BaseToolImageDigest는 생성 후 불변인 read-only 참조다(FR-001/FR-002).
    ///
    /// 의도적으로 포함하지 않는 필드: nan 관련 필드(FR-004 — nan 결합은
    /// NodeVault 내부 구현), Observed/Recommended 자원 tier(FR-014 —
    /// ResourceContract 자체에 그 필드가 없음).
    /// </summary>
    internal sealed class ToolFunctionRecipe
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string SchemaVersion { get; set; } = "draft-1";

        public ToolFunctionRecipeState State { get; set; } = ToolFunctionRecipeState.Draft;

        public string ToolSpecDigest { get; set; } = string.Empty;

        public string BaseToolImageDigest { get; set; } = string.Empty;

        public string FunctionId { get; set; } = string.Empty;

        public string Revision { get; set; } = string.Empty;

        public string DisplayLabel { get; set; } = string.Empty;

        public string DisplayDescription { get; set; } = string.Empty;

        public string DisplayCategory { get; set; } = string.Empty;

        public List<string> DisplayTags { get; set; } = new();

        public string ScriptPath { get; set; } = string.Empty;

        public CommandContract Command { get; set; } = new();

        public List<PortContract> InputPorts { get; set; } = new();

        public List<PortContract> OutputPorts { get; set; } = new();

        public List<FixtureReference> FixtureReferences { get; set; } = new();

        public List<ExpectedResult> ExpectedResults { get; set; } = new();

        public List<IntermediateFilePolicyEntry> IntermediateFilePolicies { get; set; } = new();

        public List<ParameterContract> Parameters { get; set; } = new();

        public ResourceContract EnforcedResources { get; set; } = new();

        public ExecutionEnvironmentContract ExecutionEnvironment { get; set; } = new();

        public ValidationRequirements ValidationRequirements { get; set; } = new();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// RecipeDocument.Normalize()와 같은 이유 — System.Text.Json은 C#의
        /// non-nullable 어노테이션을 런타임에 강제하지 않으므로, 외부에서 작성된
        /// JSON에 "command": null 같은 값이 있으면 그대로 null로 역직렬화된다.
        /// 외부에서 읽은 ToolFunctionRecipe는 검증/렌더링 전에 반드시 이 메서드를
        /// 호출해야 한다.
        /// </summary>
        public void Normalize()
        {
            ToolSpecDigest ??= string.Empty;
            BaseToolImageDigest ??= string.Empty;
            FunctionId ??= string.Empty;
            Revision ??= string.Empty;
            DisplayLabel ??= string.Empty;
            DisplayDescription ??= string.Empty;
            DisplayCategory ??= string.Empty;
            DisplayTags ??= new List<string>();
            ScriptPath ??= string.Empty;
            Command ??= new CommandContract();
            Command.Normalize();
            InputPorts ??= new List<PortContract>();
            foreach (var port in InputPorts)
            {
                port.Normalize();
            }

            OutputPorts ??= new List<PortContract>();
            foreach (var port in OutputPorts)
            {
                port.Normalize();
            }

            FixtureReferences ??= new List<FixtureReference>();
            foreach (var fixture in FixtureReferences)
            {
                fixture.Normalize();
            }

            ExpectedResults ??= new List<ExpectedResult>();
            foreach (var expected in ExpectedResults)
            {
                expected.Normalize();
            }

            IntermediateFilePolicies ??= new List<IntermediateFilePolicyEntry>();
            foreach (var policy in IntermediateFilePolicies)
            {
                policy.Normalize();
            }

            Parameters ??= new List<ParameterContract>();
            foreach (var parameter in Parameters)
            {
                parameter.Normalize();
            }

            EnforcedResources ??= new ResourceContract();
            EnforcedResources.Normalize();
            ExecutionEnvironment ??= new ExecutionEnvironmentContract();
            ExecutionEnvironment.Normalize();
            ValidationRequirements ??= new ValidationRequirements();
            ValidationRequirements.Normalize();
        }
    }
}
