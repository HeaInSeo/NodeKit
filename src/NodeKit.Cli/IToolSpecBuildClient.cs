using System.Collections.Generic;
using System.Threading;
using NodeKit.Grpc;

namespace NodeKit.Cli
{
    /// <summary>
    /// NodeVault 신규 경로: ResolveToolSpec → SubmitToolBuild → WatchToolBuild.
    /// </summary>
    internal interface IToolSpecBuildClient
    {
        IAsyncEnumerable<BuildEvent> ResolveAndBuildAsync(
            string toolName,
            string version,
            string rawSpec,
            CancellationToken cancellationToken = default);
    }
}
