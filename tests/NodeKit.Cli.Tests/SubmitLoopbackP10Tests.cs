using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NodeKit.Authoring.Recipes;
using NodeKit.Cli.Tests.Fakes;
using NodeKit.Grpc;
using Xunit;
using ProtoBuildEvent = Nodevault.V1.BuildEvent;
using ProtoBuildEventKind = Nodevault.V1.BuildEventKind;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// P10.listener: 주입 client 없이 `CliApp.Run(["submit", recipe, "--url", url])`이 실제 loopback HTTP/2 listener에
    /// Resolve→Submit→Watch를 보내고 기본 receipt를 남긴다. listener를 닫으면 같은 주소는 실패해야 한다 — 소켓을
    /// 실제로 탔다는 증거다. fake 응답이므로 NodeVault 빌드 증거가 아니다.
    /// </summary>
    public sealed class SubmitLoopbackP10Tests : IDisposable
    {
        private const string FixtureBuildId = "build-fixture-p10-listener";

        private const string Digest =
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private readonly string _workDir = Path.Join(Path.GetTempPath(), "nodekit-p10-listener-" + Guid.NewGuid());

        private readonly IDisposable _resolveClientOverride =
            ResolveRecipeClientTestOverride.Use(NullResolveRecipeClient.Instance);

        public SubmitLoopbackP10Tests() => Directory.CreateDirectory(_workDir);

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
            _resolveClientOverride.Dispose();
        }

        [Fact]
        public void CliSubmitWithUrl_GoesOverLoopbackSocket_AndWritesDefaultReceipt()
        {
            using var host = new LoopbackGrpcFixtureHost();
            host.Fake.OnSubmitToolBuild = _ => new Nodevault.V1.SubmitToolBuildResponse { BuildId = FixtureBuildId, Status = "Requested" };
            host.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
                new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = FixtureBuildId, ImageDigest = Digest },
            };
            var url = new Uri(host.Url);
            Assert.Equal("127.0.0.1", url.Host);
            Assert.True(url.Port > 0);

            var (exit, stdout, stderr) = Run("submit", WriteRecipe(), "--url", host.Url);

            Assert.True(exit == 0, stderr);
            Assert.Equal(new[] { "Resolve", "Submit", "Watch" }, host.Fake.CallOrder);
            Assert.Equal("bwa-mem", Assert.Single(host.Fake.ResolveRequests).ToolName);
            Assert.Equal(FixtureBuildId, Assert.Single(host.Fake.WatchRequests).BuildId);
            Assert.Contains(host.Url, stdout);
            var receipt = Assert.Single(Directory.GetFiles(Path.Join(_workDir, ".nodekit", "receipts"), "*.json"));
            var stored = File.ReadAllText(receipt);
            Assert.Contains(host.Url, stored);
            Assert.Contains(FixtureBuildId, stored);
        }

        [Fact]
        public void CliSubmitWithUrl_AfterListenerClosed_FailsWithoutHanging()
        {
            string url;
            using (var host = new LoopbackGrpcFixtureHost())
            {
                url = host.Url;
            }

            var (exit, _, _) = Run("submit", WriteRecipe(), "--url", url, "--connect-timeout", "5");

            Assert.NotEqual(0, exit);
        }

        private static (int Exit, string Stdout, string Stderr) Run(params string[] args)
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exit = CliApp.Run(args, stdout, stderr);
            return (exit, stdout.ToString(), stderr.ToString());
        }

        private string WriteRecipe()
        {
            var recipe = new RecipeDocument
            {
                BuildKind = RecipeKind.Conda,
                ToolName = "bwa-mem",
                Version = "0.7.17",
                Script = "run.sh",
                BaseImage = "condaforge/miniforge3:24.3.0-0@" + Digest,
                PackageEngine = "conda",
                Channels = new List<string> { "bioconda" },
                Packages = new List<string> { "bwa=0.7.17=h5bf99c6_8" },
            };
            var path = Path.Join(_workDir, "recipe.json");
            File.WriteAllText(path, JsonSerializer.Serialize(recipe, RecipeCreateCommand.JsonOptions));
            return path;
        }
    }
}
