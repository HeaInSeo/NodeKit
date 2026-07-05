using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Authoring.Recipes;

namespace NodeKit.Cli
{
    internal enum RecipeResolutionSource
    {
        // NodeVault proto not yet available — resolve step skipped.
        Unsupported,
        // Harbor already has the same tool+version image; single candidate returned.
        HarborCache,
        // Harbor had no match; external conda channel / BioContainers queried.
        ExternalSource,
        // Harbor had no match and the environment is air-gapped.
        NotFound,
    }

    internal sealed record BuildStringCandidate(
        string BuildString,
        string FullPin,
        string Channel);

    internal sealed record PackageResolution(
        string Name,
        string Version,
        IReadOnlyList<BuildStringCandidate> Candidates);

    internal sealed record ResolveRecipeResult(
        RecipeResolutionSource Source,
        IReadOnlyList<PackageResolution> Packages)
    {
        public static ResolveRecipeResult Unsupported() =>
            new(RecipeResolutionSource.Unsupported, System.Array.Empty<PackageResolution>());
    }

    // Seam for NodeVault ResolveRecipe RPC (병렬 트랙 D).
    // GrpcResolveRecipeClient is activated when NODEKIT_NODEVAULT_URL is set;
    // NullResolveRecipeClient is the fallback.
    internal interface IResolveRecipeClient
    {
        Task<ResolveRecipeResult> ResolveAsync(
            string toolName,
            string version,
            IReadOnlyList<string> packages,
            CancellationToken cancellationToken,
            RecipeBuildKind? buildKind = null,
            string? packageMirrorUri = null);
    }
}
