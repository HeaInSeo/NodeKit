using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace NodeKit.Tests
{
    public class CommercialGuardrailTests
    {
        private static readonly string[] _forbiddenProductionApiTerms =
        {
            "ToolSpecRequest",
            "SubmitToolBuild",
            "WatchToolBuild",
            "CancelToolBuild",
            "ResolveToolSpecAsync",
        };

        private static readonly string[] _forbiddenKubernetesDependencyTerms =
        {
            "KubernetesClient",
            "KubernetesClient.Models",
            "k8s",
            "kubernetes-client",
            "Microsoft.Rest.ClientRuntime",
        };

        [Fact]
        public void PackageReferences_DoNotUseFloatingVersions()
        {
            var offenders = RepoFiles("*.csproj")
                .SelectMany(path => XDocument.Load(path)
                    .Descendants("PackageReference")
                    .Select(reference => new
                    {
                        Path = Path.GetRelativePath(RepoRoot, path),
                        Include = reference.Attribute("Include")?.Value ?? string.Empty,
                        Version = reference.Attribute("Version")?.Value ?? reference.Element("Version")?.Value ?? string.Empty,
                    }))
                .Where(reference => string.IsNullOrWhiteSpace(reference.Version) || reference.Version.Contains('*', StringComparison.Ordinal))
                .Select(reference => $"{reference.Path}: {reference.Include} {reference.Version}")
                .ToArray();

            Assert.True(
                offenders.Length == 0,
                "PackageReference versions must be explicit for repeatable commercial builds. Offenders: "
                + string.Join(", ", offenders));
        }

        [Fact]
        public void ProductionSource_DoesNotUseBlockedNodeVaultNewPathApis()
        {
            var offenders = SourceFiles()
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Generated", StringComparison.Ordinal))
                .SelectMany(path =>
                {
                    var text = File.ReadAllText(path);
                    return _forbiddenProductionApiTerms
                        .Where(term => text.Contains(term, StringComparison.Ordinal))
                        .Select(term => $"{Path.GetRelativePath(RepoRoot, path)}: {term}");
                })
                .ToArray();

            Assert.True(
                offenders.Length == 0,
                "NodeKit must stay on legacy BuildRequest/BuildAndRegister until the NodeVault migration gate opens. Offenders: "
                + string.Join(", ", offenders));
        }

        [Fact]
        public void ProjectFiles_DoNotAddDirectKubernetesClientDependencies()
        {
            var offenders = RepoFiles("*.csproj")
                .SelectMany(path =>
                {
                    var text = File.ReadAllText(path);
                    return _forbiddenKubernetesDependencyTerms
                        .Where(term => text.Contains(term, StringComparison.OrdinalIgnoreCase))
                        .Select(term => $"{Path.GetRelativePath(RepoRoot, path)}: {term}");
                })
                .ToArray();

            Assert.True(
                offenders.Length == 0,
                "NodeKit is a gRPC/REST client and must not add direct Kubernetes client dependencies. Offenders: "
                + string.Join(", ", offenders));
        }

        [Fact]
        public void VerifyWorkflow_UsesLockedRestoreAndPackageAudit()
        {
            var workflow = ReadRepoFile(".github", "workflows", "verify.yml");

            Assert.Contains("dotnet restore NodeKit.sln --locked-mode", workflow, StringComparison.Ordinal);
            Assert.Contains("./scripts/ci-audit-packages.sh", workflow, StringComparison.Ordinal);
            Assert.Contains("./scripts/ci-check-coverage.sh", workflow, StringComparison.Ordinal);
            Assert.Contains("/p:TreatWarningsAsErrors=true", workflow, StringComparison.Ordinal);
            Assert.Contains("/p:EnforceCodeStyleInBuild=true", workflow, StringComparison.Ordinal);
        }

        [Fact]
        public void SecurityWorkflows_ArePresent()
        {
            Assert.True(File.Exists(RepoPath(".github", "workflows", "dependency-review.yml")));
            Assert.True(File.Exists(RepoPath(".github", "workflows", "codeql.yml")));
        }

        private static string ReadRepoFile(params string[] paths) =>
            File.ReadAllText(RepoPath(paths));

        private static string RepoPath(params string[] paths) =>
            Path.Combine(new[] { RepoRoot }.Concat(paths).ToArray());

        private static string[] RepoFiles(string pattern) =>
            Directory.GetFiles(RepoRoot, pattern, SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .ToArray();

        private static string[] SourceFiles() =>
            Directory.GetFiles(Path.Combine(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories);

        private static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NodeKit.sln")))
                {
                    dir = dir.Parent;
                }

                return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate NodeKit.sln from test output directory.");
            }
        }
    }
}
