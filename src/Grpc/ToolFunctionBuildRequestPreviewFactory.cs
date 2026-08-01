using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using NodeKit.Authoring.ToolFunctionRecipes;

namespace NodeKit.Grpc
{
    /// <summary>
    /// Ready 상태 ToolFunctionRecipe를 NodeVault ToolFunctionBuildRequest와 같은
    /// 구조의 canonical JSON으로 렌더링한다(FR-019). 실제 gRPC 전송 코드는
    /// 포함하지 않는다(FR-020) — nodekit function-recipe render는 로컬 파일
    /// 출력만 한다.
    ///
    /// Stage-1 그룹(kind/base_image_digest/script)은 실제 proto
    /// BuildRequest(kind=BUILD_KIND_TOOLFUNCTIONSPEC)와 매핑되는 필드다.
    /// Stage-2 그룹은 아직 실제 wire 메시지가 없는 미리보기 전용 필드다
    /// (data-model.md Renderer 섹션, research.md §8) — 두 그룹을 최상위
    /// 키로 시각적으로 분리해, 오늘 실제로 보낼 수 있는 부분과 아직 아닌
    /// 부분을 사용자가 혼동하지 않게 한다.
    /// </summary>
    internal static class ToolFunctionBuildRequestPreviewFactory
    {
        // protos/nodevault/v1/nodevault.proto의 BuildRequest.kind ==
        // BUILD_KIND_TOOLFUNCTIONSPEC 값 — 별도 ToolFunctionBuildRequest 메시지는
        // proto에 없고 기존 BuildRequest를 재사용한다(FR-019, spec.md 의존성
        // 섹션).
        private const int BuildKindToolFunctionSpec = 2;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            Converters = { new JsonStringEnumConverter() },
        };

        public static string Build(ToolFunctionRecipe recipe)
        {
            var payload = new ToolFunctionBuildRequestPreview(
                Stage1: new Stage1Group(BuildKindToolFunctionSpec, recipe.BaseToolImageDigest, recipe.ScriptPath),
                Stage2: new Stage2Group(
                    recipe.Command,
                    recipe.InputPorts,
                    recipe.OutputPorts,
                    recipe.Parameters,
                    recipe.FixtureReferences,
                    recipe.ExpectedResults,
                    recipe.IntermediateFilePolicies,
                    recipe.EnforcedResources,
                    recipe.ExecutionEnvironment,
                    recipe.ValidationRequirements));

            return JsonSerializer.Serialize(payload, _jsonOptions);
        }

        internal sealed record ToolFunctionBuildRequestPreview(
            [property: JsonPropertyName("stage1")] Stage1Group Stage1,
            [property: JsonPropertyName("stage2")] Stage2Group Stage2);

        internal sealed record Stage1Group(
            [property: JsonPropertyName("kind")] int Kind,
            [property: JsonPropertyName("base_image_digest")] string BaseImageDigest,
            [property: JsonPropertyName("script")] string Script);

        internal sealed record Stage2Group(
            [property: JsonPropertyName("command")] CommandContract Command,
            [property: JsonPropertyName("inputPorts")] List<PortContract> InputPorts,
            [property: JsonPropertyName("outputPorts")] List<PortContract> OutputPorts,
            [property: JsonPropertyName("parameters")] List<ParameterContract> Parameters,
            [property: JsonPropertyName("fixtureReferences")] List<FixtureReference> FixtureReferences,
            [property: JsonPropertyName("expectedResults")] List<ExpectedResult> ExpectedResults,
            [property: JsonPropertyName("intermediateFilePolicies")] List<IntermediateFilePolicyEntry> IntermediateFilePolicies,
            [property: JsonPropertyName("enforcedResources")] ResourceContract EnforcedResources,
            [property: JsonPropertyName("executionEnvironment")] ExecutionEnvironmentContract ExecutionEnvironment,
            [property: JsonPropertyName("validationRequirements")] ValidationRequirements ValidationRequirements);
    }
}
