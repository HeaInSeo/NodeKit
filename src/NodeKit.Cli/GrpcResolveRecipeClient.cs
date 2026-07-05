using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using NodeKit.Authoring.Recipes;
using Nodevault.V1;

namespace NodeKit.Cli
{
    // gRPC implementation of IResolveRecipeClient using NodeVault BuildService.ResolveRecipe.
    // Activated when NODEKIT_NODEVAULT_URL is set (e.g. "http://100.123.80.48:50051").
    internal sealed class GrpcResolveRecipeClient : IResolveRecipeClient, IDisposable
    {
        private readonly GrpcChannel? _channel;
        private readonly BuildService.BuildServiceClient _client;
        private bool _disposed;

        private GrpcResolveRecipeClient(string address)
        {
            _channel = GrpcChannel.ForAddress(address);
            _client = new BuildService.BuildServiceClient(_channel);
        }

        // 테스트 전용: in-process fake 서버(TestServer)가 만든 채널을 그대로 쓴다.
        // 이 인스턴스는 채널을 소유하지 않으므로 Dispose()에서 닫지 않는다.
        internal GrpcResolveRecipeClient(ChannelBase channel)
        {
            _channel = null;
            _client = new BuildService.BuildServiceClient(channel);
        }

        public static GrpcResolveRecipeClient? TryCreate()
        {
            var url = Environment.GetEnvironmentVariable("NODEKIT_NODEVAULT_URL");
            return !string.IsNullOrWhiteSpace(url) ? new GrpcResolveRecipeClient(url) : null;
        }

        public async Task<ResolveRecipeResult> ResolveAsync(
            string toolName,
            string version,
            IReadOnlyList<string> packages,
            CancellationToken cancellationToken,
            RecipeBuildKind? buildKind = null,
            string? packageMirrorUri = null)
        {
            var request = new ResolveRecipeRequest
            {
                ToolName = toolName,
                Version = version,
                Variant = MapVariant(buildKind),
                PackageMirrorUri = packageMirrorUri ?? string.Empty,
            };

            foreach (var pkg in packages)
            {
                request.Packages.Add(ParsePackageSpec(pkg));
            }

            var response = await _client.ResolveRecipeAsync(request, cancellationToken: cancellationToken);
            return MapResponse(response);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _channel?.Dispose();
            _disposed = true;
        }

        private static PackageSpec ParsePackageSpec(string pin)
        {
            var eq = pin.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                return new PackageSpec { Name = pin.Trim() };
            }

            var name = pin[..eq].Trim();
            var rest = pin[(eq + 1)..];
            var eq2 = rest.IndexOf('=', StringComparison.Ordinal);
            var ver = eq2 < 0 ? rest.Trim() : rest[..eq2].Trim();
            return new PackageSpec { Name = name, Version = ver };
        }

        private static RecipeVariant MapVariant(RecipeBuildKind? buildKind) => buildKind switch
        {
            RecipeBuildKind.Micromamba => RecipeVariant.Micromamba,
            RecipeBuildKind.PackageMirror => RecipeVariant.PackageMirror,
            RecipeBuildKind.BioContainer => RecipeVariant.Biocontainer,
            _ => RecipeVariant.Conda,
        };

        private static ResolveRecipeResult MapResponse(ResolveRecipeResponse response)
        {
            var source = response.ResolutionSource switch
            {
                "harbor_cache" => RecipeResolutionSource.HarborCache,
                "external_source" => RecipeResolutionSource.ExternalSource,
                "not_found" => RecipeResolutionSource.NotFound,
                _ => RecipeResolutionSource.Unsupported,
            };

            var packages = new List<PackageResolution>();
            foreach (var pkg in response.Packages)
            {
                var candidates = new List<BuildStringCandidate>();
                foreach (var c in pkg.Candidates)
                {
                    candidates.Add(new BuildStringCandidate(c.BuildString, c.FullPin, c.Channel));
                }

                packages.Add(new PackageResolution(pkg.Name, pkg.Version, candidates));
            }

            return new ResolveRecipeResult(source, packages);
        }
    }
}
