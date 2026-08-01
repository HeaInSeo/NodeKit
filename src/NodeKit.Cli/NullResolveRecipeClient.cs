using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Authoring.Recipes;

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
            CancellationToken cancellationToken,
            RecipeKind? buildKind = null,
            string? packageMirrorUri = null)
        {
            _ = toolName;
            _ = version;
            _ = packages;
            _ = cancellationToken;
            _ = buildKind;
            _ = packageMirrorUri;
            return Task.FromResult(ResolveRecipeResult.Unsupported());
        }
    }
}
