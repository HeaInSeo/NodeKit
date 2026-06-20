using NodeKit.Authoring;

namespace NodeKit.Grpc
{
    /// <summary>
    /// ToolDefinition → BuildRequest 변환 팩토리.
    /// L1 검증 통과 후 NodeVault gRPC 전송 직전에 호출된다.
    /// </summary>
    internal static class BuildRequestFactory
    {
        internal static BuildRequest FromToolDefinition(ToolDefinition def)
        {
            return new BuildRequest
            {
                ToolDefinitionId = def.Id,
                ToolName = def.Name,
                Version = def.Version,
                ImageUri = def.ImageUri,
                DockerfileContent = def.DockerfileContent,
                Script = def.Script,
                EnvironmentSpec = def.EnvironmentSpec,
                Inputs = new(def.Inputs),
                Outputs = new(def.Outputs),
                Command = new(def.Command),
                DisplayLabel = def.DisplayLabel,
                DisplayDescription = def.DisplayDescription,
                DisplayCategory = def.DisplayCategory,
                DisplayTags = new(def.DisplayTags),
            };
        }
    }
}
