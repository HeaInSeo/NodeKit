using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Authoring.Recipes;

namespace NodeKit.Cli
{
    // UX 테스트용 stub. NODEKIT_RESOLVE_RECIPE_STUB=1 환경변수로 활성화.
    // 실제 NodeVault 호출 없이 PackageCandidatePresenter UX 전체를 검증할 수 있다.
    // 각 패키지에 대해 bioconda 1개 + conda-forge 1개 후보를 반환한다.
    internal sealed class StubResolveRecipeClient : IResolveRecipeClient
    {
        public static StubResolveRecipeClient? TryCreate()
        {
            var val = Environment.GetEnvironmentVariable("NODEKIT_RESOLVE_RECIPE_STUB");
            return val is "1" or "true" ? new StubResolveRecipeClient() : null;
        }

        public Task<ResolveRecipeResult> ResolveAsync(
            string toolName,
            string version,
            IReadOnlyList<string> packages,
            CancellationToken cancellationToken,
            RecipeBuildKind? buildKind = null)
        {
            var resolutions = new List<PackageResolution>();
            foreach (var pkg in packages)
            {
                var (name, ver) = ParseNameVersion(pkg);
                if (name is null)
                {
                    continue;
                }

                var usedVersion = ver ?? version;
                var fakeHash1 = $"h{Math.Abs(name.GetHashCode(StringComparison.Ordinal) ^ 0x5A5A):x8}_8";
                var fakeHash2 = $"h{Math.Abs(name.GetHashCode(StringComparison.Ordinal) ^ 0xA5A5):x8}_8";

                resolutions.Add(new PackageResolution(name, usedVersion, new[]
                {
                    new BuildStringCandidate(fakeHash1, $"{name}={usedVersion}={fakeHash1}", "bioconda"),
                    new BuildStringCandidate(fakeHash2, $"{name}={usedVersion}={fakeHash2}", "conda-forge"),
                }));
            }

            var result = resolutions.Count > 0
                ? new ResolveRecipeResult(RecipeResolutionSource.ExternalSource, resolutions)
                : ResolveRecipeResult.Unsupported();

            return Task.FromResult(result);
        }

        private static (string? Name, string? Version) ParseNameVersion(string pkg)
        {
            if (string.IsNullOrWhiteSpace(pkg))
            {
                return (null, null);
            }

            var eq = pkg.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                return (pkg.Trim(), null);
            }

            var name = pkg[..eq].Trim();
            var rest = pkg[(eq + 1)..];
            var eq2 = rest.IndexOf('=', StringComparison.Ordinal);
            var ver = eq2 < 0 ? rest.Trim() : rest[..eq2].Trim();
            return (name, ver);
        }
    }
}
