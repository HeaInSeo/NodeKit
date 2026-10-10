using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Authoring.Recipes;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// P01.support — S1-01-C01..C07. 동결된 cli-acceptance-contract.json 지원표 18셀을 셀마다 실제로 실행한다:
    /// 사용자 경로(Guided/Quick 입력 transcript 또는 --non-interactive 인자) → Recipe 저장 → 재읽기 →
    /// nodekit validate(전체 L1) → nodekit render가 기대 kind/pin/content를 보존하는지 본다.
    /// 외부 조회는 기반 이미지 "직접 입력"과 digest 포함 값으로 피하고, resolve seam은 호출을 세는 local fake로 고정한다.
    /// 실제 이미지 생성·서버 수용은 판정하지 않는다.
    /// </summary>
    public class SupportTableCellTests : IDisposable
    {
        private const string DigestHex = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private const string Digest = "sha256:" + DigestHex;

        // F-PIN: 문법상 유효한 고정 sha256 기반 이미지(존재 여부는 주장하지 않는다).
        private const string BaseImageWithDigest = "condaforge/miniforge3:24.3.0-0@" + Digest;

        // F-CONTAINER
        private const string ContainerImageRef = "quay.io/biocontainers/bwa:0.7.17--h7132678_9";

        // F-PACKAGE
        private const string PackagePin = "bwa=0.7.17=h5bf99c6_8";
        private const string InstallCommand = "conda install -c bioconda " + PackagePin + " -y";

        // F-MIRROR
        private const string MirrorUri = "https://mirror.internal/conda-channel";

        // F-SOURCE / F-STRUCTURED
        private const string SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz";

        // Quick 모드 Q&A — package를 추천받는 답(IsRestrictedNetwork..HasExistingDockerfile).
        private static readonly string[] _quickAnswersRecommendPackage = { "n", "n", "n", "y", "n", "n" };

        private readonly string _workDir = Path.Join(Path.GetTempPath(), "nodekit-support-table-tests-" + Guid.NewGuid());
        private readonly CountingResolveRecipeClient _resolveClient = new();
        private readonly IDisposable _resolveClientOverride;
        private readonly ITestOutputHelper _output;

        public SupportTableCellTests(ITestOutputHelper output)
        {
            _output = output;
            _resolveClientOverride = ResolveRecipeClientTestOverride.Use(_resolveClient);
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
            _resolveClientOverride.Dispose();
        }

        public static IEnumerable<object[]> FrozenCells() =>
            LoadContract().GetProperty("supportTable").GetProperty("cells").EnumerateArray()
                .Select(c => new object[]
                {
                    c.GetProperty("method").GetString()!,
                    c.GetProperty("mode").GetString()!,
                    c.GetProperty("status").GetString()!,
                });

        [Fact]
        public void EveryFrozenCell_HasExactlyOneDriver()
        {
            var frozen = FrozenCells().Select(c => $"{c[0]}/{c[1]}").OrderBy(k => k, StringComparer.Ordinal).ToArray();
            var drivers = _cellDrivers.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

            Assert.Equal(18, frozen.Length);
            Assert.Equal(frozen, drivers);
        }

        /// <summary>S1-01-C07: 17 REFERENCE 셀은 저장→재읽기→전체 L1→render, NO_DIRECT_ROUTE 1셀은 미지원·structured 안내.</summary>
        [Theory]
        [MemberData(nameof(FrozenCells))]
        public void SupportCell_UserPath_SavesReloadsValidatesAndRenders(string method, string mode, string status)
        {
            var reference = ReferenceFor(method);
            var outPath = Path.Join(_workDir, $"{method}-{mode}.json");
            var run = _cellDrivers[$"{method}/{mode}"](this, outPath);

            Assert.Equal(0, run.ExitCode);
            Assert.True(File.Exists(outPath), $"{method}/{mode}: Recipe 파일이 저장되지 않았습니다.");
            Assert.DoesNotContain("   at ", run.Stderr, StringComparison.Ordinal);

            // 재읽기: 저장된 BuildKind가 셀의 기대 kind와 같다.
            using var saved = JsonDocument.Parse(File.ReadAllText(outPath));
            var savedKind = saved.RootElement.GetProperty("BuildKind").GetString();

            if (status == "NO_DIRECT_ROUTE")
            {
                // legacy source에는 Guided 직접 경로가 없다: source 단서는 structured로 간다.
                var redirect = ReferenceFor("source-structured");
                Assert.Equal(redirect.Kind, savedKind);
                Assert.NotEqual(reference.Kind, savedKind);
                reference = redirect;
            }
            else
            {
                Assert.Equal("REFERENCE", status);
                Assert.Equal(reference.Kind, savedKind);
            }

            // 전체 L1: nodekit validate가 새 프로세스 경계처럼 파일만 다시 읽어 검증한다.
            var validate = RunCli(new[] { "validate", outPath }, TextReader.Null);
            Assert.Equal(0, validate.ExitCode);

            // render: 기대 pin/content가 build request에 남는다.
            var renderPath = Path.Join(_workDir, $"{method}-{mode}.build-request.json");
            var render = RunCli(new[] { "render", outPath, "--out", renderPath }, TextReader.Null);
            Assert.Equal(0, render.ExitCode);
            var rendered = File.ReadAllText(renderPath);
            foreach (var expected in reference.RenderedContent)
            {
                Assert.Contains(expected, rendered, StringComparison.Ordinal);
            }

            _output.WriteLine(
                $"cell={method}/{mode} status={status} exit(create/validate/render)={run.ExitCode}/{validate.ExitCode}/{render.ExitCode} " +
                $"kind={savedKind} recipeSha256={Sha256(outPath)} renderSha256={Sha256(renderPath)} " +
                $"resolveSeamCalls={_resolveClient.Calls} stderrLines={CountLines(run.Stderr)}");
        }

        /// <summary>S1-01-C01: 6 public 이름 × 3 모드와 내부 kind 연결은 code와 동결 계약이 같다.</summary>
        [Fact]
        public void PublicMethodNames_MatchFrozenContract_AndCode()
        {
            var methods = LoadContract().GetProperty("methods").EnumerateArray().ToArray();

            Assert.Equal(6, methods.Length);
            foreach (var m in methods)
            {
                var methodId = Enum.Parse<RecipeMethodId>(m.GetProperty("methodId").GetString()!);
                Assert.True(MethodRecommendationPresenter.TryParseMethodSelection(m.GetProperty("publicName").GetString()!, out var parsed));
                Assert.Equal(methodId, parsed);
                Assert.Equal(m.GetProperty("referenceKind").GetString(), ReferenceFor(m.GetProperty("publicName").GetString()!).Kind);
            }
        }

        /// <summary>S1-01-C01: 사용 가이드 지원표가 public/internal 이름, 직접 경로 없음, 서버 미확인을 함께 적는다.</summary>
        [Fact]
        public void UsageGuideSupportTable_NamesEveryMethod_AndDoesNotClaimServerAcceptance()
        {
            var guide = File.ReadAllText(Path.Join(RepoRoot(), "docs", "NODEKIT_CLI_USAGE.md"));
            var start = guide.IndexOf("### 2-1.5.", StringComparison.Ordinal);
            Assert.True(start >= 0, "지원표 절(2-1.5)이 없습니다.");
            var end = guide.IndexOf("\n### ", start + 1, StringComparison.Ordinal);
            var section = guide[start..end];

            foreach (var m in LoadContract().GetProperty("methods").EnumerateArray())
            {
                Assert.Contains($"| `{m.GetProperty("publicName").GetString()}` |", section, StringComparison.Ordinal);
                Assert.Contains($"`{m.GetProperty("referenceKind").GetString()}`", section, StringComparison.Ordinal);
            }

            Assert.Contains("직접 경로 없음", section, StringComparison.Ordinal);
            Assert.Contains("서버 수용은 이 표에서 확인하지 않는다", section, StringComparison.Ordinal);
            Assert.DoesNotContain("모두 성공", section, StringComparison.Ordinal);
        }

        /// <summary>S1-01-C02: 설치 명령은 파싱만 하고 실행하지 않으며 package/channel이 Conda Recipe에 남는다.</summary>
        [Fact]
        public void C02_GuidedInstallCommand_PreservesPackageAndChannel_AsConda()
        {
            var outPath = Path.Join(_workDir, "c02.json");
            var run = GuidedPackage(this, outPath);

            Assert.Equal(0, run.ExitCode);
            using var saved = JsonDocument.Parse(File.ReadAllText(outPath));
            var root = saved.RootElement;
            Assert.Equal("Conda", root.GetProperty("BuildKind").GetString());
            Assert.Equal(new[] { PackagePin }, root.GetProperty("Packages").EnumerateArray().Select(e => e.GetString()).ToArray());
            Assert.Equal(new[] { "bioconda" }, root.GetProperty("Channels").EnumerateArray().Select(e => e.GetString()).ToArray());

            // 설치 명령은 실행하지 않는다: 저장된 Recipe 어디에도 명령 원문이 남지 않고 pin 값만 남는다.
            Assert.DoesNotContain("conda install", File.ReadAllText(outPath), StringComparison.Ordinal);
            _output.WriteLine($"C02 resolveSeamCalls={_resolveClient.Calls} (local fake, Unsupported; external lookup 0)");
        }

        /// <summary>S1-01-C03: 추천(package)을 거절하고 고른 mirror로 저장하며, 선택한 방식과 이유를 출력한다.</summary>
        [Fact]
        public void C03_QuickRejectRecommendation_ManualMirror_SavesMirror_AndPrintsChoice()
        {
            var outPath = Path.Join(_workDir, "c03.json");
            var run = QuickMirror(this, outPath);

            Assert.Equal(0, run.ExitCode);
            Assert.Contains("추천 작성 방식: " + RecipeMethodCatalog.For(RecipeMethodId.Package).Label.Get("ko"), run.Stdout, StringComparison.Ordinal);
            Assert.Contains("선택한 작성 방식: " + RecipeMethodCatalog.For(RecipeMethodId.Mirror).Label.Get("ko"), run.Stdout, StringComparison.Ordinal);
            Assert.Contains("선택 이유: 추천(", run.Stdout, StringComparison.Ordinal);
            using var saved = JsonDocument.Parse(File.ReadAllText(outPath));
            Assert.Equal("PackageMirror", saved.RootElement.GetProperty("BuildKind").GetString());
            Assert.Contains(MirrorUri, File.ReadAllText(outPath), StringComparison.Ordinal);
        }

        /// <summary>S1-01-C04: 모드 메뉴 [3]은 --non-interactive 사용법만 보여 주고 exit 0, Q/A·파일 없음.</summary>
        [Fact]
        public void C04_ModeMenu3_PrintsNonInteractiveUsage_Exit0_NoQaNoFile()
        {
            var outPath = Path.Join(_workDir, "c04.json");
            var run = RunCli(new[] { "recipe", "create", outPath }, new StringReader("3"));

            Assert.Equal(0, run.ExitCode);
            Assert.Contains("--non-interactive", run.Stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("Q1.", run.Stdout, StringComparison.Ordinal);
            Assert.Empty(run.Stderr);
            Assert.False(File.Exists(outPath));
        }

        /// <summary>S1-01-C05: --non-interactive container는 stdin을 읽지 않고 BioContainer를 저장하며 full L1을 통과한다.</summary>
        [Fact]
        public void C05_NonInteractiveContainer_DoesNotReadStdin_SavesBioContainer_PassesL1()
        {
            var outPath = Path.Join(_workDir, "c05.json");
            var digestA = "sha256:" + new string('a', 64);
            var stdin = new ThrowingTextReader();
            var run = RunCli(
                new[]
                {
                    "recipe", "create", outPath, "--non-interactive", "--method", "container",
                    "--field", "ImageRef=condaforge/miniforge3:24.3.0-0",
                    "--field", $"ImageDigest={digestA}",
                    "--field", "ToolName=bwa-mem",
                    "--field", "ToolVersion=0.7.17",
                    "--field", "Script=run.sh",
                },
                stdin);

            Assert.Equal(0, run.ExitCode);
            Assert.Equal(0, stdin.Reads);
            using var saved = JsonDocument.Parse(File.ReadAllText(outPath));
            Assert.Equal("BioContainer", saved.RootElement.GetProperty("BuildKind").GetString());
            Assert.Equal(0, RunCli(new[] { "validate", outPath }, TextReader.Null).ExitCode);
        }

        /// <summary>S1-01-C06: 내부 kind 이름은 구체 오류·exit 2·파일 없음이며 public source-structured로 안내한다.</summary>
        [Fact]
        public void C06_InternalKindName_SourceBuildStructured_Exit2_NoFile_GuidesPublicName()
        {
            var outPath = Path.Join(_workDir, "c06.json");
            var run = RunCli(
                new[] { "recipe", "create", outPath, "--non-interactive", "--method", "source-build-structured" },
                new ThrowingTextReader());

            Assert.Equal(2, run.ExitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("내부 build kind 이름", run.Stderr, StringComparison.Ordinal);
            Assert.Contains("source-structured", run.Stderr, StringComparison.Ordinal);
        }

        // ── cell drivers ──────────────────────────────────────────────────────

        private static readonly IReadOnlyDictionary<string, Func<SupportTableCellTests, string, CliRun>> _cellDrivers =
            new Dictionary<string, Func<SupportTableCellTests, string, CliRun>>(StringComparer.Ordinal)
            {
                ["container/guided"] = (t, p) => t.Interactive(p, "1", "3", ContainerImageRef + "@" + Digest, "bwa-mem", "0.7.17", "run.sh", "", "reads", "1", "", "bam", "1", ""),
                ["container/quick"] = (t, p) => t.Interactive(p, Quick(reject: "1", "bwa-mem", "0.7.17", "run.sh", "condaforge/miniforge3:24.3.0-0", Digest)),
                ["container/non-interactive"] = (t, p) => t.NonInteractive(p, "container",
                    "ToolName=bwa-mem", "ToolVersion=0.7.17", "Script=run.sh", "ImageRef=" + ContainerImageRef, "ImageDigest=" + Digest),
                ["package/guided"] = GuidedPackage,
                ["package/quick"] = (t, p) => t.Interactive(p, Quick(reject: null, "bioconda", "", "0", "bwa-mem", "0.7.17", "run.sh", BaseImageWithDigest, PackagePin, "")),
                ["package/non-interactive"] = (t, p) => t.NonInteractive(p, "package",
                    "ToolName=bwa-mem", "ToolVersion=0.7.17", "Script=run.sh", "BaseImage=" + BaseImageWithDigest, "Packages=" + PackagePin, "Channels=bioconda"),
                ["mirror/guided"] = (t, p) => t.Interactive(p, "1", "6", MirrorUri, "0", "bwa-mem", "0.7.17", "run.sh", BaseImageWithDigest, PackagePin, "", "", "reads", "1", "", "bam", "1", ""),
                ["mirror/quick"] = QuickMirror,
                ["mirror/non-interactive"] = (t, p) => t.NonInteractive(p, "mirror",
                    "ToolName=bwa-mem", "ToolVersion=0.7.17", "Script=run.sh", "BaseImage=" + BaseImageWithDigest, "MirrorUri=" + MirrorUri, "Packages=" + PackagePin),
                ["source/guided"] = GuidedSource,
                ["source/quick"] = (t, p) => t.Interactive(p, Quick(reject: "4", "0", "bwa-mem", "0.7.17", "run.sh", BaseImageWithDigest, SourceUri, Digest, "make", "", "")),
                ["source/non-interactive"] = (t, p) => t.NonInteractive(p, "source",
                    "ToolName=bwa-mem", "ToolVersion=0.7.17", "Script=run.sh", "BaseImage=" + BaseImageWithDigest, "SourceUri=" + SourceUri, "SourceChecksum=" + Digest, "SourceBuildCommands=make"),
                ["source-structured/guided"] = GuidedSource,
                ["source-structured/quick"] = (t, p) => t.Interactive(p, "2", "n", "n", "n", "n", "y", "n", "",
                    "bwa-mem", "0.7.17", "run.sh", "1", "", SourceUri, Digest, "make install DESTDIR=/nodekit/output", "", "", "1", "", ""),
                ["source-structured/non-interactive"] = (t, p) => t.NonInteractive(p, "source-structured",
                    "ToolName=bwa-mem", "ToolVersion=0.7.17", "Script=run.sh", "BuildProfile=generic", "SourceUri=" + SourceUri, "SourceChecksum=" + Digest,
                    "SourceBuildCommands=make install DESTDIR=/nodekit/output", "RuntimeProfile=minimal"),
                ["dockerfile/guided"] = (t, p) => t.Interactive(p, "1", "5", t.WriteDockerfile(), "y", "bwa-mem", "0.7.17", "run.sh", BaseImageWithDigest, "reads", "1", "", "bam", "1", ""),
                ["dockerfile/quick"] = (t, p) => t.Interactive(p, "2", "n", "n", "n", "n", "n", "y", "", "y", "bwa-mem", "0.7.17", "run.sh", BaseImageWithDigest,
                    "", $"FROM {BaseImageWithDigest}", "USER 1000", "", "reads", "1", "", "bam", "1", ""),
                ["dockerfile/non-interactive"] = (t, p) => t.NonInteractive(p, "dockerfile",
                    "ToolName=bwa-mem", "ToolVersion=0.7.17", "Script=run.sh", "BaseImage=" + BaseImageWithDigest, "DockerfilePath=" + t.WriteDockerfile()),
            };

        private static CliRun GuidedPackage(SupportTableCellTests t, string outPath) =>
            t.Interactive(outPath, "1", "2", InstallCommand, "1", "", "0", "bwa-mem", "0.7.17", "run.sh", BaseImageWithDigest, "reads", "1", "", "bam", "1", "");

        private static CliRun GuidedSource(SupportTableCellTests t, string outPath) =>
            t.Interactive(outPath, "1", "4", SourceUri, Digest, "bwa-mem", "0.7.17", "run.sh", "", "make install DESTDIR=/nodekit/output", "", "", "", "", "reads", "1", "", "bam", "1", "");

        private static CliRun QuickMirror(SupportTableCellTests t, string outPath) =>
            t.Interactive(outPath, Quick(reject: "3", "0", "bwa-mem", "0.7.17", "run.sh", BaseImageWithDigest, MirrorUri, PackagePin, "", ""));

        // Quick 모드: package 추천 Q&A 뒤 reject가 null이면 추천을 수락하고, 아니면 거절 후 그 번호를 직접 고른다.
        private static string[] Quick(string? reject, params string[] rest)
        {
            var lines = new List<string> { "2" };
            lines.AddRange(_quickAnswersRecommendPackage);
            if (reject is null)
            {
                lines.Add(string.Empty);
            }
            else
            {
                lines.Add("n");
                lines.Add(reject);
            }

            lines.AddRange(rest);
            return lines.ToArray();
        }

        private static (string Kind, string[] RenderedContent) ReferenceFor(string publicName) => publicName switch
        {
            "container" => ("BioContainer", new[] { Digest }),
            "package" => ("Conda", new[] { PackagePin, "bioconda", Digest }),
            "mirror" => ("PackageMirror", new[] { PackagePin, MirrorUri, Digest }),
            "source" => ("SourceBuild", new[] { SourceUri, Digest }),
            // structured render는 checksum을 sha256sum -c 입력인 hex로 쓴다.
            "source-structured" => ("SourceBuildStructured", new[] { SourceUri, DigestHex, "/nodekit/output" }),
            "dockerfile" => ("DockerfileFallback", new[] { "USER 1000", Digest }),
            _ => throw new ArgumentOutOfRangeException(nameof(publicName), publicName, null),
        };

        private CliRun Interactive(string outPath, params string[] transcript) =>
            RunCli(new[] { "recipe", "create", outPath }, new StringReader(string.Join("\n", transcript)));

        private CliRun NonInteractive(string outPath, string method, params string[] fields)
        {
            var args = new List<string> { "recipe", "create", outPath, "--non-interactive", "--method", method };
            if (method == "dockerfile")
            {
                args.Add("--accept-dockerfile-warning");
            }

            foreach (var field in fields)
            {
                args.Add("--field");
                args.Add(field);
            }

            var stdin = new ThrowingTextReader();
            var run = RunCli(args.ToArray(), stdin);
            Assert.Equal(0, stdin.Reads);
            return run;
        }

        private string WriteDockerfile()
        {
            var path = Path.Join(_workDir, "Dockerfile");
            File.WriteAllText(path, $"FROM {BaseImageWithDigest}\nUSER 1000\n");
            return path;
        }

        private static CliRun RunCli(string[] args, TextReader stdin)
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(args, stdin, stdout, stderr);
            return new CliRun(exitCode, stdout.ToString(), stderr.ToString());
        }

        private static JsonElement LoadContract()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(
                Path.Join(RepoRoot(), "tests", "NodeKit.Cli.Tests", "Fixtures", "Contract", "cli-acceptance-contract.json")));
            return doc.RootElement.Clone();
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Join(dir.FullName, "NodeKit.sln")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new FileNotFoundException("repo root(NodeKit.sln)를 찾지 못했습니다.");
        }

        private static string Sha256(string path) =>
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

        private static int CountLines(string text) =>
            text.Length == 0 ? 0 : text.TrimEnd('\n').Split('\n').Length;

        private sealed record CliRun(int ExitCode, string Stdout, string Stderr);

        private sealed class ThrowingTextReader : TextReader
        {
            public int Reads { get; private set; }

            public override int Peek()
            {
                Reads++;
                throw new InvalidOperationException("--non-interactive 경로가 stdin을 읽었습니다.");
            }

            public override int Read()
            {
                Reads++;
                throw new InvalidOperationException("--non-interactive 경로가 stdin을 읽었습니다.");
            }

            public override string? ReadLine()
            {
                Reads++;
                throw new InvalidOperationException("--non-interactive 경로가 stdin을 읽었습니다.");
            }
        }

        private sealed class CountingResolveRecipeClient : IResolveRecipeClient
        {
            private int _calls;

            public int Calls => _calls;

            public Task<ResolveRecipeResult> ResolveAsync(
                string toolName,
                string version,
                IReadOnlyList<string> packages,
                CancellationToken cancellationToken,
                RecipeKind? buildKind = null,
                string? packageMirrorUri = null)
            {
                Interlocked.Increment(ref _calls);
                return Task.FromResult(ResolveRecipeResult.Unsupported());
            }
        }
    }
}
