using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NodeKit.Cli.Tests.Fakes;
using Xunit;
using ProtoBuildEvent = Nodevault.V1.BuildEvent;
using ProtoBuildEventKind = Nodevault.V1.BuildEventKind;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// P10.portability (S4-04-G): 작성 환경 A의 published CLI가 만든 Recipe를 독립 clean 설치 B에서 다시 쓴다.
    /// A와 B는 같은 archive를 따로 푼 두 설치이며 설치 경로·HOME/XDG/TMPDIR·작업 디렉터리·loopback endpoint가 모두
    /// 다르다. B로는 Recipe 파일과 Recipe 디렉터리 기준 동반 파일만 같은 상대 배치로 복사하고, B를 실행하기 전에 A의
    /// 설치·HOME·작업 디렉터리를 통째로 지우고 A endpoint를 닫는다. 그래서 B가 A의 경로·journal·endpoint에 기대면
    /// 실패한다. 한계: 같은 host·같은 OS 사용자에서 도는 두 설치이며 별도 기계/OS 이미지 증거가 아니다. fake 응답은
    /// 실제 NodeVault 빌드 증거가 아니다(portable-authoring-transfer.json).
    /// </summary>
    public sealed class PublishedCliPortabilityTests : IDisposable
    {
        private const string DigestHex = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private const string Digest = "sha256:" + DigestHex;
        private const string BaseImageWithDigest = "condaforge/miniforge3:24.3.0-0@" + Digest;
        private const string ContainerImageRef = "quay.io/biocontainers/bwa:0.7.17--h7132678_9";
        private const string PackagePin = "bwa=0.7.17=h5bf99c6_8";
        private const string MirrorUri = "https://mirror.internal/conda-channel";
        private const string SourceUri = "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz";
        private const string SourceChecksum = "sha256:fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";
        // typed 등록의 C1 형식(접두 없는 소문자 hex 64자리). preflight가 fresh submit을 상류 gate까지 통과시킨다.
        private const string ToolSpecDigest = "c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1";
        private const string BaseToolImageDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private const string ResolvedToolSpecDigest = "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        private const string FixtureDigest = "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
        private const string FixtureBuildId = "build-fixture-p10-portability";

        // image 안의 경로. B의 host에는 없으며 host 경로로 해석되면 안 된다.
        private const string ImageExecutable = "/usr/local/bin/sort-tool";
        private const string ImageWorkingDirectory = "/work";

        private const string ScriptBytes = "#!/bin/sh\nset -eu\nsort-tool --threads \"$1\" \"$2\" > \"$3\"\n";
        private const string FixtureBytes = "@r1\nACGT\n+\nIIII\n";

        // 공개 ToolSpec 방법 6개(cli-acceptance-contract.json methods)의 --non-interactive 입력. dockerfile은 A의
        // Dockerfile 경로를 받아 내용을 Recipe에 고정한다.
        private static readonly IReadOnlyDictionary<string, string[]> _toolSpecMethods = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["container"] = new[] { "ImageRef=" + ContainerImageRef, "ImageDigest=" + Digest },
            ["package"] = new[] { "BaseImage=" + BaseImageWithDigest, "Packages=" + PackagePin, "Channels=bioconda" },
            ["mirror"] = new[] { "BaseImage=" + BaseImageWithDigest, "MirrorUri=" + MirrorUri, "Packages=" + PackagePin },
            ["source"] = new[] { "BaseImage=" + BaseImageWithDigest, "SourceUri=" + SourceUri, "SourceChecksum=" + SourceChecksum, "SourceBuildCommands=make" },
            ["source-structured"] = new[]
            {
                "BuildProfile=generic", "SourceUri=" + SourceUri, "SourceChecksum=" + SourceChecksum,
                "SourceBuildCommands=make install DESTDIR=/nodekit/output", "RuntimeProfile=minimal",
            },
            ["dockerfile"] = new[] { "BaseImage=" + BaseImageWithDigest },
        };

        private readonly Install _a;
        private readonly Install _b;

        public PublishedCliPortabilityTests()
        {
            // B는 다른 깊이의 설치 경로·사용자 경로·Recipe 위치·CWD를 쓴다.
            var id = Guid.NewGuid().ToString("N");
            _a = new Install(
                Path.Join(Path.GetTempPath(), "nodekit-p10-port-a-" + id),
                install: "install",
                home: Path.Join("home", "author-a"),
                recipes: Path.Join("work", "project"),
                cwd: Path.Join("work", "project"));
            _b = new Install(
                Path.Join(Path.GetTempPath(), "nodekit-p10-port-b-" + id),
                install: Path.Join("opt", "tools", "nodekit"),
                home: Path.Join("home", "user-b"),
                recipes: Path.Join("data", "imported", "recipes"),
                cwd: Path.Join("elsewhere", "cwd"));
        }

        public void Dispose()
        {
            _a.Delete();
            _b.Delete();
        }

        // S4-04-G (ToolSpec): A가 공개 방법 6개로 만든 Recipe를 A에서 validate/render/submit한 뒤 Recipe 파일만 B로
        // 옮긴다. B는 CWD 밖의 상대 경로로 같은 파일을 읽어 같은 render 결과와 같은 Resolve raw_spec을 만들고, 자기
        // endpoint로 새 request ID를 써서 fresh 제출하며, 그 receipt의 source snapshot digest는 A receipt의 것과 같다.
        // dockerfile Recipe는 A의 Dockerfile이 사라져도 저장된 내용을 쓴다. B의 어떤 파일(디코드한 snapshot 내용 포함)도
        // dockerfile Recipe의 DockerfilePath 한 값 말고는 A의 경로나 endpoint를 담지 않는다.
        [Fact]
        public void ToolSpecRecipes_FromA_ReusedOnCleanB_SameMeaning_FreshSubmitToOwnEndpoint()
        {
            _a.Prepare();
            var dockerfile = Path.Join(_a.Recipes, "Dockerfile");
            File.WriteAllText(dockerfile, $"FROM {BaseImageWithDigest}\nUSER 1000\n");

            var authored = new Dictionary<string, (byte[] Recipe, string Rendered, string RawSpec, string RequestId, string SourceSha)>(StringComparer.Ordinal);

            // B의 endpoint를 A보다 먼저 bind해 A가 닫힐 때까지 둘 다 열어 둔다. A를 닫은 뒤 B를 bind하면 OS가 A가 막
            // 놓은 ephemeral port를 B에 다시 줄 수 있고, 그러면 B receipt의 정상 URL이 A endpoint 참조로 잡힌다.
            using var hostB = NewHost();
            string urlA;
            using (var hostA = NewHost())
            {
                urlA = hostA.Url;
                foreach (var (method, fields) in _toolSpecMethods)
                {
                    var name = method + ".json";
                    var args = new List<string>
                    {
                        "recipe", "create", name, "--non-interactive", "--method", method,
                        "--field", "ToolName=bwa-mem", "--field", "ToolVersion=0.7.17", "--field", "Script=run.sh",
                    };
                    if (method == "dockerfile")
                    {
                        args.AddRange(new[] { "--accept-dockerfile-warning", "--field", "DockerfilePath=" + dockerfile });
                    }

                    foreach (var field in fields)
                    {
                        args.Add("--field");
                        args.Add(field);
                    }

                    ExpectExit(0, _a.Run(_a.Cwd, args));
                    ExpectExit(0, _a.Run(_a.Cwd, new[] { "validate", name }));
                    var renderOut = Path.Join(_a.Root, "render-" + method + ".json");
                    ExpectExit(0, _a.Run(_a.Cwd, new[] { "render", name, "--out", renderOut }));
                    var submit = _a.Run(_a.Cwd, new[] { "submit", name, "--url", hostA.Url, "--format", "jsonl" }, host: hostA);
                    ExpectExit(0, submit);
                    var requestIdA = hostA.Fake.SubmitRequests.Last().RequestId;
                    authored[method] = (
                        File.ReadAllBytes(Path.Join(_a.Recipes, name)),
                        WithoutPerRunIds(File.ReadAllText(renderOut)),
                        hostA.Fake.ResolveRequests.Last().RawSpec,
                        requestIdA,
                        SnapshotSha(Path.Join(_a.Recipes, ".nodekit", "receipts", requestIdA + ".json")));
                }

                Assert.Equal(_toolSpecMethods.Count, hostA.Fake.SubmitRequests.Count);
            }

            // Recipe 파일만 옮긴다. A의 Dockerfile·.nodekit 기록·설치·HOME은 옮기지 않고 지운다.
            _b.Prepare();
            foreach (var (method, item) in authored)
            {
                File.WriteAllBytes(Path.Join(_b.Recipes, method + ".json"), item.Recipe);
            }

            _a.Delete();
            Assert.False(Directory.Exists(_a.Root));

            Assert.NotEqual(urlA, hostB.Url);
            foreach (var (method, item) in authored)
            {
                var recipe = Path.GetRelativePath(_b.Cwd, Path.Join(_b.Recipes, method + ".json"));
                Assert.StartsWith("..", recipe, StringComparison.Ordinal);

                ExpectExit(0, _b.Run(_b.Cwd, new[] { "validate", recipe }));
                var renderOut = Path.Join(_b.Root, "render-" + method + ".json");
                ExpectExit(0, _b.Run(_b.Cwd, new[] { "render", recipe, "--out", renderOut }));
                Assert.Equal(item.Rendered, WithoutPerRunIds(File.ReadAllText(renderOut)));

                var callsBefore = hostB.Fake.CallOrder.Count;
                var submit = _b.Run(_b.Cwd, new[] { "submit", recipe, "--url", hostB.Url, "--format", "jsonl" }, host: hostB, recordDirectory: _b.Recipes);
                ExpectExit(0, submit);
                Assert.Equal(new[] { "Resolve", "Submit", "Watch" }, hostB.Fake.CallOrder.Skip(callsBefore));
                var resolve = hostB.Fake.ResolveRequests.Last();
                Assert.Equal("bwa-mem", resolve.ToolName);
                Assert.Equal("0.7.17", resolve.Version);
                Assert.Equal(item.RawSpec, resolve.RawSpec);
                var submitted = hostB.Fake.SubmitRequests.Last();
                Assert.Equal(ResolvedToolSpecDigest, submitted.ToolSpecDigest);
                Assert.NotEqual(item.RequestId, submitted.RequestId);

                // fresh receipt는 B의 Recipe 옆에 생기고 B의 endpoint와 새 request ID를 담는다.
                var receiptPath = Path.Join(_b.Recipes, ".nodekit", "receipts", submitted.RequestId + ".json");
                var receipt = File.ReadAllText(receiptPath);
                Assert.Contains(hostB.Url, receipt, StringComparison.Ordinal);

                // 같은 Recipe bytes는 설치·HOME·CWD·endpoint가 달라도 같은 source snapshot을 만든다(sourceBinding
                // excludedFromKey). A receipt의 digest는 A를 지우기 전에 기록해 두었다.
                Assert.Equal(item.SourceSha, SnapshotSha(receiptPath));
                using var transcript = JsonDocument.Parse(submit.Transcript);
                Assert.Equal(_b.Package.InstallDir, transcript.RootElement.GetProperty("install_dir").GetString());
                Assert.True(transcript.RootElement.GetProperty("files").TryGetProperty(".nodekit/receipts/" + submitted.RequestId + ".json", out _));
            }

            using (var dockerRaw = JsonDocument.Parse(authored["dockerfile"].RawSpec))
            {
                var content = dockerRaw.RootElement.GetProperty("dockerfile_content").GetString();
                Assert.Contains("USER 1000", content, StringComparison.Ordinal);
            }

            Assert.Equal(_toolSpecMethods.Count, Directory.GetFiles(Path.Join(_b.Recipes, ".nodekit", "receipts"), "*.json").Length);

            // 관측: dockerfile Recipe는 고정한 내용 옆에 A의 작성 입력 DockerfilePath(절대 경로)도 그대로 담는다. B는
            // 그 파일 없이(A를 지운 뒤) 위 validate/render/submit을 통과했으므로 이 값을 읽지 않는다. B의 source snapshot은
            // 그 Recipe bytes를 그대로(base64로) 담으므로 같은 값이 snapshot의 dockerfile.json entry에도 한 번 있다. 이
            // 한 값 말고는 B의 Recipe·기록(디코드한 snapshot 내용 포함)·render 결과 어디에도 A의 경로나 endpoint가 없다.
            using (var dockerRecipe = JsonDocument.Parse(authored["dockerfile"].Recipe))
            {
                Assert.Equal(dockerfile, Property(dockerRecipe.RootElement, "DockerfilePath").GetString());
            }

            AssertNoReferenceTo(_b.Root, _a.Root, urlA, allowed: ("dockerfile.json", dockerfile));
        }

        // S4-04-G (source binding): B에서 Recipe를 고쳐 다시 제출하면 새 source snapshot이 생기고, 앞 제출의 receipt와
        // 그 receipt가 가리키는 source/resolved snapshot bytes는 그대로 남는다(과거 기록을 현재 내용에 다시 묶지 않는다).
        [Fact]
        public void ToolSpecRecipe_EditedOnB_KeepsPastSnapshots_NewSubmitBindsNewSource()
        {
            _a.Prepare();
            ExpectExit(0, _a.Run(_a.Cwd, CreateToolSpecArgs("package.json", "package")));
            _b.Prepare();
            var recipeB = Path.Join(_b.Recipes, "package.json");
            File.Copy(Path.Join(_a.Recipes, "package.json"), recipeB);
            _a.Delete();

            using var host = NewHost();
            ExpectExit(0, _b.Run(_b.Cwd, new[] { "submit", recipeB, "--url", host.Url, "--format", "jsonl" }, host: host, recordDirectory: _b.Recipes));
            var firstId = host.Fake.SubmitRequests.Single().RequestId;
            var firstReceiptPath = Path.Join(_b.Recipes, ".nodekit", "receipts", firstId + ".json");
            var firstReceipt = File.ReadAllBytes(firstReceiptPath);
            var firstSource = SnapshotSha(firstReceiptPath);
            var snapshotsBefore = SnapshotFiles(_b.Recipes);
            Assert.Contains(Path.Join("source", firstSource + ".json"), snapshotsBefore.Keys);

            var edited = JsonNode.Parse(File.ReadAllText(recipeB))!.AsObject();
            var versionKey = edited.Select(p => p.Key).Single(k => string.Equals(k, "Version", StringComparison.OrdinalIgnoreCase));
            edited[versionKey] = "0.7.18";
            File.WriteAllText(recipeB, edited.ToJsonString());

            ExpectExit(0, _b.Run(_b.Cwd, new[] { "submit", recipeB, "--url", host.Url, "--format", "jsonl" }, host: host, recordDirectory: _b.Recipes));
            Assert.Equal(2, host.Fake.SubmitRequests.Count);
            Assert.Equal("0.7.18", host.Fake.ResolveRequests[1].Version);
            var secondReceiptPath = Path.Join(_b.Recipes, ".nodekit", "receipts", host.Fake.SubmitRequests[1].RequestId + ".json");
            Assert.NotEqual(firstSource, SnapshotSha(secondReceiptPath));

            Assert.Equal(firstReceipt, File.ReadAllBytes(firstReceiptPath));
            var snapshotsAfter = SnapshotFiles(_b.Recipes);
            foreach (var (path, bytes) in snapshotsBefore)
            {
                Assert.True(snapshotsAfter.TryGetValue(path, out var now) && now.SequenceEqual(bytes), path + " changed");
            }

            Assert.Contains(Path.Join("source", SnapshotSha(secondReceiptPath) + ".json"), snapshotsAfter.Keys);
        }

        // S4-04-G (ToolFunctionRecipe): A가 상대 script·fixture를 쓰는 Recipe를 만들고 검증·render한다. Recipe와 동반
        // 파일만 같은 상대 배치로 B에 옮기고 A를 지운다. B는 다른 CWD에서 Recipe 디렉터리 기준으로 동반 파일을 읽어
        // 같은 render 결과를 만들고, image 내부 경로·인자 순서·port·parameter·digest-only fixture 선언을 바꾸지 않으며
        // 파일을 새로 만들지 않는다. fresh submit은 B의 script로 preflight를 통과한 뒤 닫힌 상류 gate에서 RPC 없이 끝난다.
        [Fact]
        public void FunctionRecipe_WithRelativeCompanions_FromA_ReusedOnCleanB()
        {
            var (recipeB, renderedA) = TransferFunctionRecipe();
            var filesBefore = RelativeFiles(_b.Recipes);
            Assert.Equal(new[] { "fixtures/reads.fastq", "function-recipe.json", "scripts/sort.sh" }, filesBefore);
            var recipe = Path.GetRelativePath(_b.Cwd, recipeB);

            ExpectExit(0, _b.Run(_b.Cwd, new[] { "function-recipe", "validate", recipe }));
            var renderOut = Path.Join(_b.Root, "rendered.json");
            ExpectExit(0, _b.Run(_b.Cwd, new[] { "function-recipe", "render", recipe, "--out", renderOut }));
            Assert.Equal(renderedA, File.ReadAllText(renderOut));

            using (var rendered = JsonDocument.Parse(File.ReadAllText(renderOut)))
            {
                var stage1 = rendered.RootElement.GetProperty("stage1");
                Assert.Equal(BaseToolImageDigest, stage1.GetProperty("base_image_digest").GetString());
                Assert.Equal("./scripts/sort.sh", stage1.GetProperty("script").GetString());
                var stage2 = rendered.RootElement.GetProperty("stage2");
                var command = stage2.GetProperty("command");
                Assert.Equal(ImageExecutable, command.GetProperty("Executable").GetString());
                Assert.Equal(ImageWorkingDirectory, command.GetProperty("WorkingDirectory").GetString());
                Assert.Equal(new[] { "--threads", "{param.threads}", "--in" }, command.GetProperty("Arguments").EnumerateArray().Select(a => a.GetString()));
                Assert.Equal("reads", stage2.GetProperty("inputPorts")[0].GetProperty("Name").GetString());
                Assert.Equal("/work/in/*.fastq", stage2.GetProperty("inputPorts")[0].GetProperty("PathOrGlob").GetString());
                Assert.Equal("sorted", stage2.GetProperty("outputPorts")[0].GetProperty("Name").GetString());
            }

            Assert.False(File.Exists(ImageExecutable));

            using (var saved = JsonDocument.Parse(File.ReadAllText(recipeB)))
            {
                var root = saved.RootElement;
                Assert.Equal("./scripts/sort.sh", Property(root, "ScriptPath").GetString());
                var fixtures = Property(root, "FixtureReferences").EnumerateArray().ToList();
                Assert.Equal("./fixtures/reads.fastq", Property(fixtures[0], "LocalPath").GetString());
                Assert.Equal(FixtureDigest, Property(fixtures[1], "ContentDigest").GetString());
                Assert.Equal(string.Empty, Property(fixtures[1], "LocalPath").GetString());
                var parameter = Property(root, "Parameters")[0];
                Assert.Equal("threads", Property(parameter, "Name").GetString());
                Assert.Equal("4", Property(parameter, "DefaultValue").GetString());
            }

            using var host = NewHost();
            var submit = _b.Run(_b.Cwd, new[] { "function-recipe", "submit", recipe, "--url", host.Url }, host: host, recordDirectory: _b.Recipes);
            Assert.Equal(1, submit.Exit);
            Assert.Contains(ToolFunctionRecipeSubmitCommand.UpstreamGateClosedCode, submit.Stderr, StringComparison.Ordinal);
            Assert.Empty(host.Fake.CallOrder);
            Assert.Empty(host.Registry.RegisterRequests);

            // digest-only fixture로 파일을 만들지 않았고 동반 파일 bytes는 그대로다.
            // .nodekit 기록과 Recipe 저장 writer의 lock 파일(AtomicFileWriter) 말고는 새 파일이 없다.
            Assert.Equal(
                filesBefore,
                RelativeFiles(_b.Recipes).Where(f => !f.StartsWith(".nodekit/", StringComparison.Ordinal) && !f.EndsWith(".nodekit-lock", StringComparison.Ordinal)));
            Assert.Equal(ScriptBytes, File.ReadAllText(Path.Join(_b.Recipes, "scripts", "sort.sh")));
            Assert.Equal(FixtureBytes, File.ReadAllText(Path.Join(_b.Recipes, "fixtures", "reads.fastq")));
            AssertNoReferenceTo(_b.Root, _a.Root, null);
        }

        // S4-04-G 변형(B에서): 동반 script 누락은 2·RPC0, 공백 script는 1·RPC0, A의 절대 script 경로는 rewrite 없이
        // 2·RPC0, 지원하지 않는 SchemaVersion은 2·RPC0이다. A 경로나 CWD로 fallback하지 않으며 Recipe를 고쳐 쓰지 않는다.
        [Fact]
        public void FunctionRecipe_TransferVariantsOnB_ExitCodes_NoRpc_NoFallback()
        {
            var (recipeB, _) = TransferFunctionRecipe();
            var scriptB = Path.Join(_b.Recipes, "scripts", "sort.sh");
            var original = File.ReadAllBytes(recipeB);
            using var host = NewHost();

            // CWD에 같은 이름의 script가 있어도 Recipe 디렉터리 기준으로만 읽는다.
            Directory.CreateDirectory(Path.Join(_b.Cwd, "scripts"));
            File.WriteAllText(Path.Join(_b.Cwd, "scripts", "sort.sh"), ScriptBytes);
            File.Delete(scriptB);
            AssertRefused(recipeB, 2, "SCRIPT_READ_FAILED", host);

            File.WriteAllText(scriptB, " \n\t\n");
            AssertRefused(recipeB, 1, "BLANK_SCRIPT", host);
            File.WriteAllText(scriptB, ScriptBytes);

            var absolute = JsonNode.Parse(original)!.AsObject();
            absolute[absolute.Select(p => p.Key).Single(k => string.Equals(k, "ScriptPath", StringComparison.OrdinalIgnoreCase))] =
                Path.Join(_a.Recipes, "scripts", "sort.sh");
            File.WriteAllText(recipeB, absolute.ToJsonString());
            var absoluteBytes = File.ReadAllBytes(recipeB);
            AssertRefused(recipeB, 2, "ABSOLUTE_SCRIPT_PATH", host);
            Assert.Equal(absoluteBytes, File.ReadAllBytes(recipeB));

            var schema = JsonNode.Parse(original)!.AsObject();
            schema[schema.Select(p => p.Key).Single(k => string.Equals(k, "SchemaVersion", StringComparison.OrdinalIgnoreCase))] = "draft-2";
            File.WriteAllText(recipeB, schema.ToJsonString());
            var schemaBytes = File.ReadAllBytes(recipeB);
            Assert.Equal(2, _b.Run(_b.Cwd, new[] { "function-recipe", "validate", recipeB }).Exit);
            AssertRefused(recipeB, 2, "SchemaVersion", host);
            Assert.Equal(schemaBytes, File.ReadAllBytes(recipeB));

            Assert.Empty(host.Fake.CallOrder);
            Assert.Empty(host.Registry.RegisterRequests);
        }

        // S4-04-G 변형: A에서 만든 ToolSpec Recipe의 SchemaVersion이 지원 값이 아니면 B의 validate/submit은 2·RPC0이다.
        [Fact]
        public void ToolSpecRecipe_UnsupportedSchemaOnB_Exit2_NoRpc()
        {
            _a.Prepare();
            ExpectExit(0, _a.Run(_a.Cwd, CreateToolSpecArgs("package.json", "package")));
            _b.Prepare();
            var recipeB = Path.Join(_b.Recipes, "package.json");
            var node = JsonNode.Parse(File.ReadAllText(Path.Join(_a.Recipes, "package.json")))!.AsObject();
            _a.Delete();
            node[node.Select(p => p.Key).Single(k => string.Equals(k, "SchemaVersion", StringComparison.OrdinalIgnoreCase))] = "draft-2";
            File.WriteAllText(recipeB, node.ToJsonString());
            using var host = NewHost();

            var validate = _b.Run(_b.Cwd, new[] { "validate", recipeB });
            var submit = _b.Run(_b.Cwd, new[] { "submit", recipeB, "--url", host.Url }, host: host, recordDirectory: _b.Recipes);

            Assert.Equal(2, validate.Exit);
            Assert.Equal(2, submit.Exit);
            Assert.Contains("SchemaVersion", submit.Stderr, StringComparison.Ordinal);
            Assert.Contains("draft-2", submit.Stderr, StringComparison.Ordinal);
            Assert.Empty(host.Fake.CallOrder);
            Assert.False(Directory.Exists(Path.Join(_b.Recipes, ".nodekit", "receipts")));
        }

        private void AssertRefused(string recipeB, int exit, string code, LoopbackGrpcFixtureHost host)
        {
            var renderOut = Path.Join(_b.Root, "refused-render.json");
            var render = _b.Run(_b.Cwd, new[] { "function-recipe", "render", recipeB, "--out", renderOut });
            Assert.True(render.Exit == exit, $"render exit {render.Exit}, want {exit}: {render.Stderr}");
            Assert.Contains(code, render.Stderr, StringComparison.Ordinal);
            Assert.False(File.Exists(renderOut));

            var submit = _b.Run(_b.Cwd, new[] { "function-recipe", "submit", recipeB, "--url", host.Url }, host: host, recordDirectory: _b.Recipes);
            Assert.True(submit.Exit == exit, $"submit exit {submit.Exit}, want {exit}: {submit.Stderr}");
            Assert.Contains(code, submit.Stderr, StringComparison.Ordinal);
            Assert.DoesNotContain(ToolFunctionRecipeSubmitCommand.UpstreamGateClosedCode, submit.Stderr, StringComparison.Ordinal);
        }

        // A에서 function Recipe를 만들고 validate/render한 뒤 Recipe·scripts/sort.sh·fixtures/reads.fastq만 B로 옮기고
        // A를 지운다. B의 Recipe 경로와 A의 render 결과를 돌려준다.
        private (string RecipeB, string RenderedA) TransferFunctionRecipe()
        {
            _a.Prepare();
            Directory.CreateDirectory(Path.Join(_a.Recipes, "scripts"));
            Directory.CreateDirectory(Path.Join(_a.Recipes, "fixtures"));
            File.WriteAllText(Path.Join(_a.Recipes, "scripts", "sort.sh"), ScriptBytes);
            File.WriteAllText(Path.Join(_a.Recipes, "fixtures", "reads.fastq"), FixtureBytes);

            ExpectExit(0, _a.Run(_a.Cwd, new[]
            {
                "function-recipe", "create", "function-recipe.json",
                "--tool-spec-digest", ToolSpecDigest,
                "--base-tool-image-digest", BaseToolImageDigest,
                "--non-interactive",
                "--field", "FunctionId=sort-tool.sort",
                "--field", "Revision=v1",
                "--field", "ScriptPath=./scripts/sort.sh",
                "--field", "Command.Executable=" + ImageExecutable,
                "--field", "Command.WorkingDirectory=" + ImageWorkingDirectory,
                "--field", "Command.Arguments=--threads",
                "--field", "Command.Arguments={param.threads}",
                "--field", "Command.Arguments=--in",
                "--field", "InputPorts[0].Name=reads",
                "--field", "InputPorts[0].Required=true",
                "--field", "InputPorts[0].PathOrGlob=/work/in/*.fastq",
                "--field", "OutputPorts[0].Name=sorted",
                "--field", "OutputPorts[0].PathOrGlob=/work/out/sorted.fastq",
                "--field", "Parameters[0].Name=threads",
                "--field", "Parameters[0].Type=Integer",
                "--field", "Parameters[0].DefaultValue=4",
                "--field", "FixtureReferences[0].LocalPath=./fixtures/reads.fastq",
                "--field", "FixtureReferences[1].ContentDigest=" + FixtureDigest,
                "--field", "EnforcedResources.CpuRequest=500m",
                "--field", "EnforcedResources.CpuLimit=2000m",
                "--field", "EnforcedResources.MemoryRequest=256Mi",
                "--field", "EnforcedResources.MemoryLimit=1Gi",
            }));
            ExpectExit(0, _a.Run(_a.Cwd, new[] { "function-recipe", "validate", "function-recipe.json" }));
            var renderOut = Path.Join(_a.Root, "rendered.json");
            ExpectExit(0, _a.Run(_a.Cwd, new[] { "function-recipe", "render", "function-recipe.json", "--out", renderOut }));
            var renderedA = File.ReadAllText(renderOut);

            _b.Prepare();
            foreach (var relative in new[] { "function-recipe.json", Path.Join("scripts", "sort.sh"), Path.Join("fixtures", "reads.fastq") })
            {
                var target = Path.Join(_b.Recipes, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Join(_a.Recipes, relative), target);
            }

            _a.Delete();
            Assert.False(Directory.Exists(_a.Root));
            return (Path.Join(_b.Recipes, "function-recipe.json"), renderedA);
        }

        private static string[] CreateToolSpecArgs(string name, string method)
        {
            var args = new List<string>
            {
                "recipe", "create", name, "--non-interactive", "--method", method,
                "--field", "ToolName=bwa-mem", "--field", "ToolVersion=0.7.17", "--field", "Script=run.sh",
            };
            foreach (var field in _toolSpecMethods[method])
            {
                args.Add("--field");
                args.Add(field);
            }

            return args.ToArray();
        }

        private static LoopbackGrpcFixtureHost NewHost()
        {
            var host = new LoopbackGrpcFixtureHost();
            host.Fake.OnResolveToolSpec = request => new Nodevault.V1.ResolvedToolSpecResponse
            {
                ToolSpecDigest = ResolvedToolSpecDigest,
                ToolName = request.ToolName,
                Version = request.Version,
            };
            host.Fake.OnSubmitToolBuild = _ => new Nodevault.V1.SubmitToolBuildResponse { BuildId = FixtureBuildId, Status = "Requested" };
            host.Fake.WatchEvents = new List<ProtoBuildEvent>
            {
                new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = FixtureBuildId, ImageDigest = Digest },
            };
            return host;
        }

        // ToolSpec render 미리보기는 실행마다 새로 만드는 값(RequestId, ToolDefinitionId, 정의의 Id/CreatedAt)을 담는다.
        // 같은 설치에서 두 번 render해도 달라지는 이 값만 빼고 나머지 내용을 비교한다.
        private static readonly string[] _perRunKeys = { "RequestId", "ToolDefinitionId", "Id", "CreatedAt" };

        private static string WithoutPerRunIds(string rendered)
        {
            var node = JsonNode.Parse(rendered)!;
            Assert.True(node.AsObject().ContainsKey("RequestId"), "render 결과에 RequestId가 없습니다.");
            Strip(node);
            return node.ToJsonString();

            static void Strip(JsonNode? current)
            {
                switch (current)
                {
                    case JsonObject obj:
                        foreach (var key in _perRunKeys)
                        {
                            obj.Remove(key);
                        }

                        foreach (var (_, child) in obj)
                        {
                            Strip(child);
                        }

                        break;
                    case JsonArray array:
                        foreach (var child in array)
                        {
                            Strip(child);
                        }

                        break;
                }
            }
        }

        // A 참조 검사 자체의 회귀: snapshot의 content_base64 안에 숨은 A 경로도 잡고, 허용한 DockerfilePath는 실제 source
        // snapshot artifact의 같은 logical_path entry에서 한 번만 통과시킨다. published package 없이 도는 검사다.
        [Fact]
        public void AssertNoReferenceTo_DecodesSnapshotPayloads_AndScopesTheAllowance()
        {
            var root = Path.Join(Path.GetTempPath(), "nodekit-p10-port-check-" + Guid.NewGuid().ToString("N"));
            var recipes = Path.Join(root, "recipes");
            var snapshots = Path.Join(recipes, ".nodekit", "snapshots", "source");
            var aRoot = Path.Join(Path.GetTempPath(), "nodekit-p10-port-a-" + Guid.NewGuid().ToString("N"));
            const string aUrl = "http://127.0.0.1:1/";
            var aPath = Path.Join(aRoot, "work", "project", "Dockerfile");
            var allowed = ("dockerfile.json", aPath);
            var recipe = JsonSerializer.Serialize(new { DockerfilePath = aPath, DockerfileContent = "FROM x" });
            try
            {
                Directory.CreateDirectory(snapshots);
                File.WriteAllText(Path.Join(recipes, "dockerfile.json"), recipe);

                static object Entry(string logicalPath, string content) =>
                    new { logical_path = logicalPath, content_base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content)) };

                static byte[] SnapshotBytes(params (string LogicalPath, string Content)[] files) =>
                    JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        schema_version = "nodekit.source-snapshot.v1",
                        files = files.Select(f => Entry(f.LogicalPath, f.Content)),
                    });

                // 실제 store처럼 content-addressed 이름으로 하나만 둔다.
                string WriteSnapshot(params (string LogicalPath, string Content)[] files)
                {
                    foreach (var old in Directory.GetFiles(snapshots))
                    {
                        File.Delete(old);
                    }

                    var bytes = SnapshotBytes(files);
                    var path = Path.Join(snapshots, Convert.ToHexStringLower(SHA256.HashData(bytes)) + ".json");
                    File.WriteAllBytes(path, bytes);
                    return path;
                }

                void Check() => AssertNoReferenceTo(root, Path.Join(root, "install"), recipes, aRoot, aUrl, allowed);

                // 허용한 값이 Recipe와 snapshot의 같은 entry에 한 번씩 있으면 통과한다.
                WriteSnapshot(("dockerfile.json", recipe));
                Check();

                // encoded 내용 안의 A 참조는 놓치지 않는다.
                WriteSnapshot(("dockerfile.json", recipe), ("scripts/run.sh", "cat " + Path.Join(aRoot, "x")));
                Assert.ThrowsAny<Xunit.Sdk.XunitException>(Check);
                WriteSnapshot(("dockerfile.json", recipe.Replace("FROM x", "FROM x " + aUrl, StringComparison.Ordinal)));
                Assert.ThrowsAny<Xunit.Sdk.XunitException>(Check);

                // 허용은 같은 logical_path에서 정확히 그 값 한 번뿐이다.
                WriteSnapshot(("other.json", recipe));
                Assert.ThrowsAny<Xunit.Sdk.XunitException>(Check);
                WriteSnapshot(("dockerfile.json", recipe.Replace("FROM x", "FROM x " + aPath, StringComparison.Ordinal)));
                Assert.ThrowsAny<Xunit.Sdk.XunitException>(Check);

                // 허용은 실제 source snapshot artifact의 최상위 files entry에만 있다(Codex r4321429907). 같은 모양의
                // dockerfile.json entry를 담은 다른 B 기록 JSON은 A 경로를 남기면 잡는다.
                var snapshot = WriteSnapshot(("dockerfile.json", recipe));
                Check();
                var receipts = Path.Join(recipes, ".nodekit", "receipts");
                Directory.CreateDirectory(receipts);
                var other = Path.Join(receipts, "r.json");
                File.WriteAllText(other, JsonSerializer.Serialize(new { note = Entry("dockerfile.json", recipe) }));
                Assert.ThrowsAny<Xunit.Sdk.XunitException>(Check);
                File.WriteAllBytes(other, SnapshotBytes(("dockerfile.json", recipe)));
                Assert.ThrowsAny<Xunit.Sdk.XunitException>(Check);
                File.Delete(other);

                // snapshots/source 안이라도 이름이 자기 bytes의 SHA-256이 아니면 실제 artifact가 아니다.
                File.Move(snapshot, Path.Join(snapshots, new string('0', 64) + ".json"));
                Assert.ThrowsAny<Xunit.Sdk.XunitException>(Check);

                // 실제 snapshot 안이라도 최상위 files 배열 밖에 둔 entry에는 허용이 없다.
                var nested = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    schema_version = "nodekit.source-snapshot.v1",
                    files = new[] { Entry("dockerfile.json", recipe) },
                    extra = new { files = new[] { Entry("dockerfile.json", recipe) } },
                });
                foreach (var old in Directory.GetFiles(snapshots))
                {
                    File.Delete(old);
                }

                File.WriteAllBytes(Path.Join(snapshots, Convert.ToHexStringLower(SHA256.HashData(nested)) + ".json"), nested);
                Assert.ThrowsAny<Xunit.Sdk.XunitException>(Check);

                // schema_version이 source snapshot이 아니면 content-addressed 이름이어도 허용이 없다.
                var resolvedShape = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    schema_version = "nodekit.resolved-snapshot.v1",
                    files = new[] { Entry("dockerfile.json", recipe) },
                });
                foreach (var old in Directory.GetFiles(snapshots))
                {
                    File.Delete(old);
                }

                File.WriteAllBytes(Path.Join(snapshots, Convert.ToHexStringLower(SHA256.HashData(resolvedShape)) + ".json"), resolvedShape);
                Assert.ThrowsAny<Xunit.Sdk.XunitException>(Check);

                // 평문 파일의 A 참조도 그대로 잡는다.
                WriteSnapshot(("dockerfile.json", recipe));
                Check();
                File.WriteAllText(Path.Join(root, "render.json"), "{\"u\":\"" + aUrl + "\"}");
                Assert.ThrowsAny<Xunit.Sdk.XunitException>(Check);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        private static void ExpectExit(int exit, PublishedCliResult result) =>
            Assert.True(result.Exit == exit, $"exit {result.Exit}, want {exit}\nstdout: {result.Stdout}\nstderr: {result.Stderr}");

        private static JsonElement Property(JsonElement element, string name) =>
            element.EnumerateObject().Single(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

        private static string SnapshotSha(string receiptPath)
        {
            using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
            return receipt.RootElement.GetProperty("source_snapshot_sha256").GetString()!;
        }

        private static Dictionary<string, byte[]> SnapshotFiles(string recipes)
        {
            var root = Path.Join(recipes, ".nodekit", "snapshots");
            return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetRelativePath(root, f), File.ReadAllBytes, StringComparer.Ordinal);
        }

        private static List<string> RelativeFiles(string directory) =>
            Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(directory, f).Replace(Path.DirectorySeparatorChar, '/'))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

        private void AssertNoReferenceTo(string root, string aRoot, string? aUrl, (string LogicalPath, string Value)? allowed = null) =>
            AssertNoReferenceTo(root, _b.Package.InstallDir, _b.Recipes, aRoot, aUrl, allowed);

        // B 아래의 어떤 파일(기록·render 결과 포함)도 A의 경로나 A endpoint를 담지 않는다. skipDirectory(설치 binary)는
        // 제외한다. JSON 기록 안의 content_base64(source snapshot이 담은 Recipe·동반 파일 exact bytes)는 디코드해 같은
        // 검사를 한다. allowed는 Recipe 기준 상대 이름 하나와 그 파일에서 정확히 한 번 나와도 되는 A 값이다. 그 Recipe
        // 파일과 snapshot 안의 같은 logical_path entry에만 적용되며, 그 밖의 곳이나 같은 파일의 다른 곳에는 A 경로가 없어야 한다.
        private static void AssertNoReferenceTo(
            string root,
            string skipDirectory,
            string recipes,
            string aRoot,
            string? aUrl,
            (string LogicalPath, string Value)? allowed)
        {
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                if (file.StartsWith(skipDirectory, StringComparison.Ordinal))
                {
                    continue;
                }

                var text = File.ReadAllText(file);
                var allowedHere = allowed is { } recipe && file == Path.Join(recipes, recipe.LogicalPath) ? recipe.Value : null;
                AssertNoReference(file, text, aRoot, aUrl, allowedHere);
                var sourceSnapshot = IsSourceSnapshot(file, recipes);
                foreach (var (logicalPath, content, snapshotEntry) in EncodedContents(text))
                {
                    var allowedEntry = allowed is { } entry && sourceSnapshot && snapshotEntry && logicalPath == entry.LogicalPath ? entry.Value : null;
                    AssertNoReference(file + "!" + logicalPath, content, aRoot, aUrl, allowedEntry);
                }
            }
        }

        private static void AssertNoReference(string label, string text, string aRoot, string? aUrl, string? allowedValue)
        {
            if (allowedValue is not null)
            {
                var quoted = JsonSerializer.Serialize(allowedValue);
                Assert.True(text.Split(quoted).Length - 1 == 1, label + ": 허용한 A 값이 정확히 한 번 나와야 한다");
                text = text.Replace(quoted, "\"\"", StringComparison.Ordinal);
            }

            Assert.False(text.Contains(aRoot, StringComparison.Ordinal), label + ": A 경로를 담는다");
            Assert.False(aUrl is not null && text.Contains(aUrl, StringComparison.Ordinal), label + ": A endpoint를 담는다");
        }

        // 실제 source snapshot artifact인가: Recipe 기록 root의 snapshots/source 아래, 파일 이름이 자기 bytes의 SHA-256인
        // content-addressed 파일이고 최상위 schema_version이 source snapshot이다. 다른 B 기록은 같은 모양의 entry를 담아도 아니다.
        private static bool IsSourceSnapshot(string file, string recipes)
        {
            if (Path.GetDirectoryName(file) != Path.Join(recipes, ".nodekit", "snapshots", "source")
                || Path.GetFileName(file) != Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))) + ".json")
            {
                return false;
            }

            try
            {
                return JsonNode.Parse(File.ReadAllText(file)) is JsonObject root
                    && root["schema_version"] is JsonValue schema
                    && schema.TryGetValue<string>(out var version)
                    && version == "nodekit.source-snapshot.v1";
            }
            catch (JsonException)
            {
                return false;
            }
        }

        // JSON 문서 어디든 content_base64가 있으면 디코드한 text를 (logical_path, text, 최상위 files 배열의 entry인가)로
        // 돌려준다. JSON이 아니면 없다.
        private static List<(string LogicalPath, string Content, bool SnapshotEntry)> EncodedContents(string text)
        {
            var found = new List<(string, string, bool)>();
            JsonNode? node;
            try
            {
                node = JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                return found;
            }

            var snapshotFiles = node is JsonObject root && root["files"] is JsonArray files ? files : null;
            Walk(node);
            return found;

            void Walk(JsonNode? current)
            {
                switch (current)
                {
                    case JsonObject obj:
                        if (obj["content_base64"] is JsonValue encoded)
                        {
                            var logicalPath = obj["logical_path"]?.GetValue<string>() ?? "content_base64";
                            var snapshotEntry = snapshotFiles is not null && obj.Parent == snapshotFiles;
                            found.Add((logicalPath, Encoding.UTF8.GetString(Convert.FromBase64String(encoded.GetValue<string>())), snapshotEntry));
                        }

                        foreach (var (_, child) in obj)
                        {
                            Walk(child);
                        }

                        break;
                    case JsonArray array:
                        foreach (var child in array)
                        {
                            Walk(child);
                        }

                        break;
                }
            }
        }

        /// <summary>
        /// 독립 설치 하나: 자기 archive 추출본·HOME/XDG/TMPDIR·Recipe 디렉터리·CWD를 root 아래에 둔다.
        /// </summary>
        private sealed class Install
        {
            private readonly string _install;
            private PublishedCliPackage? _package;

            public Install(string root, string install, string home, string recipes, string cwd)
            {
                Root = root;
                _install = Path.Join(root, install);
                Home = Path.Join(root, home);
                Recipes = Path.Join(root, recipes);
                Cwd = Path.Join(root, cwd);
            }

            public string Root { get; }

            public string Home { get; }

            public string Recipes { get; }

            public string Cwd { get; }

            public PublishedCliPackage Package => _package ?? throw new InvalidOperationException("Prepare first");

            public void Prepare()
            {
                _package = PublishedCliPackage.InstallInto(_install);
                Directory.CreateDirectory(Home);
                Directory.CreateDirectory(Path.Join(Home, "tmp"));
                Directory.CreateDirectory(Recipes);
                Directory.CreateDirectory(Cwd);
            }

            public PublishedCliResult Run(
                string cwd,
                IEnumerable<string> args,
                LoopbackGrpcFixtureHost? host = null,
                string? recordDirectory = null) =>
                Package.Run(
                    cwd,
                    args,
                    environment: new Dictionary<string, string>
                    {
                        ["HOME"] = Home,
                        ["XDG_CONFIG_HOME"] = Path.Join(Home, ".config"),
                        ["XDG_DATA_HOME"] = Path.Join(Home, ".local", "share"),
                        ["XDG_CACHE_HOME"] = Path.Join(Home, ".cache"),
                        ["TMPDIR"] = Path.Join(Home, "tmp"),
                    },
                    host: host,
                    recordDirectory: recordDirectory ?? cwd);

            public void Delete()
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
        }
    }
}
