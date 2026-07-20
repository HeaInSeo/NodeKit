using System.Text.Json;
using System.Text.Json.Serialization;
using NodeKit.Authoring;

namespace NodeKit.Grpc
{
    /// <summary>
    /// Builds the raw_spec JSON string GrpcToolSpecClient.ResolveAndBuildAsync
    /// sends to NodeVault's ResolveToolSpec. Shared by the CLI (SubmitCommand)
    /// and the Avalonia GUI (MainWindow) so both submit paths encode the same
    /// wire shape — a divergence here would silently break one of the two
    /// callers against a real NodeVault.
    /// </summary>
    internal static class ToolSpecRawSpecFactory
    {
        // raw_spec은 proto BuildRequest 필드명(snake_case) 기반 JSON이다.
        // NodeVault buildRequestFromResolved가 encoding/json(protojson이 아님)으로
        // 직접 파싱한다. inputs/outputs/display/command는 proto BuildRequest에서
        // 이미 reserved 처리되어 있어 여기 담아도 받을 필드가 없다 — NodeVault가
        // 스키마에서 뺀 것이므로 이 payload에 채워 넣을 대상이 아니다.
        //
        // "kind"는 생략하면 BuildKind_BUILD_KIND_UNSPECIFIED(0)가 되는데, NodeVault
        // 쪽은 UNSPECIFIED를 BUILD_KIND_TOOLSPEC과 동일하게 처리하고 있어(우연히)
        // 지금은 문제가 없다. 다만 그 동작에 기대는 대신 실제 의미(recipe 기반
        // base image + Dockerfile 빌드)를 명시한다 — encoding/json은 protojson이
        // 아니라 커스텀 (Un)MarshalJSON도 없으므로 열거형 이름이 아니라 정수값(1)을
        // 그대로 보낸다.
        private const int BuildKindToolSpec = 1;

        public static string Build(ToolDefinition definition)
        {
            var payload = new ToolSpecRawSpec(
                ToolName: definition.Name,
                Version: definition.Version,
                Kind: BuildKindToolSpec,
                ImageUri: definition.ImageUri,
                DockerfileContent: definition.DockerfileContent,
                Script: definition.Script,
                EnvironmentSpec: definition.EnvironmentSpec);
            return JsonSerializer.Serialize(payload);
        }

        // Field names/order are NodeVault's buildRequestFromResolved wire contract
        // (see the class doc comment) — a rename or removal here must fail to
        // compile every call site instead of silently changing what gets sent.
        internal sealed record ToolSpecRawSpec(
            [property: JsonPropertyName("tool_name")] string ToolName,
            [property: JsonPropertyName("version")] string Version,
            [property: JsonPropertyName("kind")] int Kind,
            [property: JsonPropertyName("image_uri")] string ImageUri,
            [property: JsonPropertyName("dockerfile_content")] string DockerfileContent,
            [property: JsonPropertyName("script")] string Script,
            [property: JsonPropertyName("environment_spec")] string EnvironmentSpec);
    }
}
