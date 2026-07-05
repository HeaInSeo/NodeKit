using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Cli.Tests.Fakes;
using Nodevault.V1;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// GrpcResolveRecipeClient를 in-process fake gRPC 서버에 실제로 붙여서 검증.
    /// GrpcToolSpecClientWireTests와 동일한 목적 — seoy/NodeVault 없이 매번 자동 실행.
    /// </summary>
    public class GrpcResolveRecipeClientWireTests
    {
        [Fact]
        public async Task ResolveAsync_HarborCache_MapsToHarborCacheSource()
        {
            using var server = new GrpcTestServer();
            server.Fake.OnResolveRecipe = _ => new ResolveRecipeResponse
            {
                ResolutionSource = "harbor_cache",
                Packages =
                {
                    new Nodevault.V1.PackageResolution
                    {
                        Name = "samtools",
                        Version = "1.17",
                        Candidates =
                        {
                            new Nodevault.V1.BuildStringCandidate
                            {
                                BuildString = "h00cdaf9_0",
                                FullPin = "samtools=1.17=h00cdaf9_0",
                                Channel = "bioconda",
                            },
                        },
                    },
                },
            };
            using var client = new GrpcResolveRecipeClient(server.Channel);

            var result = await client.ResolveAsync(
                "samtools", "1.17", new List<string> { "samtools=1.17" }, CancellationToken.None);

            Assert.Equal(RecipeResolutionSource.HarborCache, result.Source);
            Assert.Single(result.Packages);
            Assert.Single(result.Packages[0].Candidates);
        }

        [Fact]
        public async Task ResolveAsync_ExternalSource_MapsToExternalSourceSource()
        {
            using var server = new GrpcTestServer();
            server.Fake.OnResolveRecipe = _ => new ResolveRecipeResponse { ResolutionSource = "external_source" };
            using var client = new GrpcResolveRecipeClient(server.Channel);

            var result = await client.ResolveAsync(
                "samtools", "1.17", new List<string> { "samtools=1.17" }, CancellationToken.None);

            Assert.Equal(RecipeResolutionSource.ExternalSource, result.Source);
        }

        [Fact]
        public async Task ResolveAsync_NotFound_MapsToNotFoundSource()
        {
            using var server = new GrpcTestServer();
            server.Fake.OnResolveRecipe = _ => new ResolveRecipeResponse { ResolutionSource = "not_found" };
            using var client = new GrpcResolveRecipeClient(server.Channel);

            var result = await client.ResolveAsync(
                "samtools", "1.17", new List<string> { "samtools=1.17" }, CancellationToken.None);

            Assert.Equal(RecipeResolutionSource.NotFound, result.Source);
        }
    }
}
