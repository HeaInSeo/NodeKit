using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NodeKit.Cli
{
    internal sealed class NullResolveRecipeClient : IResolveRecipeClient
    {
        public static NullResolveRecipeClient Instance { get; } = new();

        private NullResolveRecipeClient()
        {
        }

        public Task<ResolveRecipeResult> ResolveAsync(
            string toolName,
            string version,
            IReadOnlyList<string> packages,
            CancellationToken cancellationToken)
        {
            _ = toolName;
            _ = version;
            _ = packages;
            _ = cancellationToken;
            return Task.FromResult(ResolveRecipeResult.Unsupported());
        }
    }
}
