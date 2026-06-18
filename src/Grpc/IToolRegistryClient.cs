using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NodeKit.Grpc
{
    /// <summary>NodeVault ToolRegistryService 클라이언트 추상화.</summary>
    internal interface IToolRegistryClient
    {
        Task<IReadOnlyList<RegisteredTool>> ListToolsAsync(CancellationToken ct = default);
    }
}
