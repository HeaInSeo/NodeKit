using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
using NodeKit.Authoring.Recipes;
using NodeKit.Cli.Tests.Fakes;
using Xunit;
using ProtoBuildEvent = Nodevault.V1.BuildEvent;
using ProtoBuildEventKind = Nodevault.V1.BuildEventKind;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// P10.executable (S4-04-A–F): 실제 published package의 <c>nodekit</c>를 repository 밖 CWD에서 별도 process로
    /// 실행한다(<see cref="PublishedCliPackage"/>). 네트워크가 필요한 경우는 같은 테스트 process의 실제 loopback
    /// HTTP/2 fake(<see cref="LoopbackGrpcFixtureHost"/>)에 `--url` 또는 NODEKIT_NODEVAULT_URL로 붙는다. fake는 고정
    /// 응답만 돌려주므로 실제 NodeVault 빌드의 증거가 아니다. ToolFunction 등록 경로는
    /// ToolFunctionRegistryPublishedP10Tests.cs에 있다.
    /// </summary>
    public sealed class PublishedCliProcessTests : IDisposable
    {
        private const string FixtureBuildId = "build-fixture-p10-executable";

        private const string Digest =
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private const string ToolSpecDigest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        // fake ResolveToolSpec가 돌려주는 digest. Submit은 recipe 쪽 값이 아니라 이 Resolve 응답 digest를 보내야 한다.
        private const string ResolvedToolSpecDigest = "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

        private const string BaseToolImageDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        private readonly string _workDir = Path.Join(Path.GetTempPath(), "nodekit-p10-executable-" + Guid.NewGuid());

        public PublishedCliProcessTests() => Directory.CreateDirectory(_workDir);

        public void Dispose() => Directory.Delete(_workDir, recursive: true);

        // 최상위 사용법에만 있는 문장. subcommand --help가 최상위 사용법으로 빠지면 이 문장이 나온다.
        private const string TopLevelHelpMarker = "각 명령의 자세한 옵션은";

        // S4-04-A: 배포 binary의 최상위와 각 subcommand --help는 exit 0, usage를 stdout에, stderr는 비어 있다.
        // 각 행은 그 명령만의 사용법 줄을 요구하고, subcommand 행은 최상위 사용법이 아님을 확인한다. 최상위 목록은
        // 두 칸 들여쓰기로 명령을 나열하므로 "사용법: nodekit <명령>"은 해당 subcommand가 직접 낸 경우에만 나온다.
        [Theory]
        [InlineData(TopLevelHelpMarker, "--help")]
        [InlineData("사용법: nodekit validate <recipe.json>", "validate", "--help")]
        [InlineData("사용법: nodekit render <recipe.json>", "render", "--help")]
        [InlineData("사용법: nodekit submit <recipe.json>", "submit", "--help")]
        [InlineData("사용법: nodekit recipe create", "recipe", "create", "--help")]
        [InlineData("  nodekit receipt watch <receipt.json> [--format human|jsonl]", "receipt", "--help")]
        [InlineData("  nodekit function-recipe register-replay <registration-receipt.json>", "function-recipe", "--help")]
        [InlineData("사용법: nodekit function-recipe create", "function-recipe", "create", "--help")]
        [InlineData("사용법: nodekit function-recipe validate <path>", "function-recipe", "validate", "--help")]
        [InlineData("사용법: nodekit function-recipe render <path>", "function-recipe", "render", "--help")]
        [InlineData("사용법: nodekit function-recipe submit <path>", "function-recipe", "submit", "--help")]
        [InlineData("사용법: nodekit function-recipe register-replay", "function-recipe", "register-replay", "--help")]
        public void Help_ExitsZeroWithCommandSpecificUsage(string expectedUsage, params string[] args)
        {
            var package = PublishedCliPackage.Require();

            var result = package.Run(_workDir, args);

            Assert.True(result.Exit == 0, result.Stderr);
            Assert.Contains(expectedUsage, result.Stdout);
            Assert.Empty(result.Stderr);
            if (args[0] != "--help")
            {
                Assert.DoesNotContain(TopLevelHelpMarker, result.Stdout);
            }

            if (args is ["function-recipe", "--help"])
            {
                // function-recipe 묶음 사용법은 ToolSpec 명령을 나열하지 않는다.
                Assert.DoesNotContain("nodekit validate", result.Stdout);
                Assert.DoesNotContain("nodekit submit", result.Stdout);
            }
        }

        // S4-04-B/E: repository 밖 CWD에서 상대 경로로 create→validate→render. 저장된 Draft를 다시 읽어 Ready로
        // 검증하고, render 결과 파일에 같은 command/script가 남는다.
        [Fact]
        public void FunctionRecipe_CreateValidateRender_FromOutsideRepo()
        {
            var package = PublishedCliPackage.Require();
            AssertOutsideRepo(package.InstallDir);
            AssertOutsideRepo(_workDir);
            File.WriteAllText(Path.Join(_workDir, "sort.sh"), "#!/bin/sh\nsamtools sort \"$@\"\n");
            Directory.CreateDirectory(Path.Join(_workDir, "fixtures"));
            File.WriteAllText(Path.Join(_workDir, "fixtures", "small.bam"), "bam");

            var create = package.Run(_workDir, new[]
            {
                "function-recipe", "create", "samtools-sort.json",
                "--tool-spec-digest", ToolSpecDigest,
                "--base-tool-image-digest", BaseToolImageDigest,
                "--non-interactive",
                "--field", "FunctionId=samtools.sort",
                "--field", "Revision=v1",
                "--field", "ScriptPath=./sort.sh",
                "--field", "Command.Executable=samtools",
                "--field", "Command.Arguments=sort",
                "--field", "Command.Arguments=-@4",
                "--field", "InputPorts[0].Name=bam",
                "--field", "InputPorts[0].Required=true",
                "--field", "OutputPorts[0].Name=sortedBam",
                "--field", "FixtureReferences[0].LocalPath=./fixtures/small.bam",
                "--field", "EnforcedResources.CpuRequest=500m",
                "--field", "EnforcedResources.CpuLimit=2000m",
                "--field", "EnforcedResources.MemoryRequest=256Mi",
                "--field", "EnforcedResources.MemoryLimit=1Gi",
            });
            Assert.True(create.Exit == 0, create.Stderr);
            var recipePath = Path.Join(_workDir, "samtools-sort.json");
            Assert.Equal("Draft", ReadProperty(recipePath, "State"));

            var validate = package.Run(_workDir, new[] { "function-recipe", "validate", "samtools-sort.json" });
            Assert.True(validate.Exit == 0, validate.Stderr);
            Assert.Equal("Ready", ReadProperty(recipePath, "State"));

            var render = package.Run(_workDir, new[] { "function-recipe", "render", "samtools-sort.json", "--out", "rendered.json" });
            Assert.True(render.Exit == 0, render.Stderr);
            using var rendered = JsonDocument.Parse(File.ReadAllText(Path.Join(_workDir, "rendered.json")));
            var stage1 = rendered.RootElement.GetProperty("stage1");
            Assert.Equal(2, stage1.GetProperty("kind").GetInt32());
            Assert.Equal(BaseToolImageDigest, stage1.GetProperty("base_image_digest").GetString());
            Assert.Equal("./sort.sh", stage1.GetProperty("script").GetString());
            var stage2 = rendered.RootElement.GetProperty("stage2");
            var command = stage2.GetProperty("command");
            Assert.Equal("samtools", command.GetProperty("Executable").GetString());
            Assert.Equal(new[] { "sort", "-@4" }, command.GetProperty("Arguments").EnumerateArray().Select(a => a.GetString()));
            Assert.Equal("bam", stage2.GetProperty("inputPorts")[0].GetProperty("Name").GetString());
            Assert.Equal("sortedBam", stage2.GetProperty("outputPorts")[0].GetProperty("Name").GetString());
        }

        // S4-04-C: redirected `recipe create`에서 첫 필수 입력 전에 stdin이 끝나면 exit 130, 파일을 만들지 않고
        // machine stream에 ANSI가 없다(RecipeCreateInteractiveTests의 in-process 정책과 같은 값). NO_COLOR 없이
        // 색을 지원하는 TERM을 주므로 ANSI가 없는 이유는 stdin/stdout/stderr redirection에 대한 CLI의 판단뿐이다.
        [Fact]
        public void RecipeCreate_EofBeforeRequiredInput_Exit130_NoFile()
        {
            var package = PublishedCliPackage.Require();

            var result = package.Run(
                _workDir,
                new[] { "recipe", "create", "eof.json" },
                stdin: string.Empty,
                environment: new Dictionary<string, string> { ["TERM"] = "xterm-256color" },
                timeout: TimeSpan.FromSeconds(30));

            Assert.Equal(130, result.Exit);
            Assert.False(File.Exists(Path.Join(_workDir, "eof.json")));
            Assert.DoesNotContain('\u001b', result.Stdout);
            Assert.DoesNotContain('\u001b', result.Stderr);
        }

        // S4-04-C: 같은 정책을 `function-recipe create`에도 적용한다 — 첫 필수 입력(functionId) 전에 stdin이 끝나면
        // exit 130이고 Recipe 파일도 `.nodekit-lock`도 남기지 않는다.
        [Fact]
        public void FunctionRecipeCreate_EofBeforeRequiredInput_Exit130_NoFile()
        {
            var package = PublishedCliPackage.Require();

            var result = package.Run(
                _workDir,
                new[]
                {
                    "function-recipe", "create", "eof.json",
                    "--tool-spec-digest", ToolSpecDigest,
                    "--base-tool-image-digest", BaseToolImageDigest,
                },
                stdin: string.Empty,
                environment: new Dictionary<string, string> { ["TERM"] = "xterm-256color" },
                timeout: TimeSpan.FromSeconds(30));

            Assert.Equal(130, result.Exit);
            Assert.Empty(Directory.EnumerateFileSystemEntries(_workDir));
            Assert.DoesNotContain('\u001b', result.Stdout);
            Assert.DoesNotContain('\u001b', result.Stderr);
        }

        // S4-04-C 명시 취소: redirected `recipe create`의 첫 입력에 /cancel을 주면 exit 130, 파일을 만들지 않고
        // machine stream에 ANSI가 없다. 취소 뒤에 남은 "3"은 CI 사용법을 출력하고 끝나는 선택이므로, /cancel이
        // 무시되면 130이 아닌 종료와 사용법 출력으로 드러난다 — EOF가 아니라 명령이 종료시켰음을 보인다.
        [Fact]
        public void RecipeCreate_CancelCommand_Exit130_NoFile()
        {
            var package = PublishedCliPackage.Require();

            var result = package.Run(
                _workDir,
                new[] { "recipe", "create", "cancel.json" },
                stdin: "/cancel\n3\n",
                environment: new Dictionary<string, string> { ["TERM"] = "xterm-256color" },
                timeout: TimeSpan.FromSeconds(30));

            Assert.Equal(130, result.Exit);
            Assert.DoesNotContain("알 수 없는 선택입니다", result.Stdout);
            Assert.Empty(Directory.EnumerateFileSystemEntries(_workDir));
            Assert.DoesNotContain('\u001b', result.Stdout);
            Assert.DoesNotContain('\u001b', result.Stderr);
        }

        // S4-04-C 명시 취소: `function-recipe create`의 필수 입력(executable)에 /cancel을 주면 exit 130이고 Recipe
        // 파일도 `.nodekit-lock`도 남기지 않는다. stdin에는 그 뒤 나머지 필수 입력까지 모두 있으므로, /cancel을
        // 값으로 받아 버리면 Draft가 저장된다 — EOF가 아니라 명령이 종료시켰음을 보인다.
        [Fact]
        public void FunctionRecipeCreate_CancelCommand_Exit130_NoFile()
        {
            var package = PublishedCliPackage.Require();
            var stdin = string.Join("\n", new[]
            {
                "samtools.sort", "v1", "", "", "", "", "./sort.sh", // functionId … scriptPath
                "/cancel",                                         // executable
                "", "", "", "", "", "",                             // arguments … hard timeout
                "", "", "", "", "", "",                             // ports, fixtures, policies, parameters
                "500m", "2000m", "256Mi", "1Gi",                   // CPU/Memory request/limit
            }) + "\n";

            var result = package.Run(
                _workDir,
                new[]
                {
                    "function-recipe", "create", "cancel.json",
                    "--tool-spec-digest", ToolSpecDigest,
                    "--base-tool-image-digest", BaseToolImageDigest,
                },
                stdin: stdin,
                environment: new Dictionary<string, string> { ["TERM"] = "xterm-256color" },
                timeout: TimeSpan.FromSeconds(30));

            Assert.Equal(130, result.Exit);
            Assert.Empty(Directory.EnumerateFileSystemEntries(_workDir));
            Assert.DoesNotContain('\u001b', result.Stdout);
            Assert.DoesNotContain('\u001b', result.Stderr);
        }

        // S4-04-D: 알 수 없는 명령은 exit 2, 읽을 수 없는 JSON은 exit 2, 형식은 맞지만 의미가 틀린 recipe는 exit 1.
        [Fact]
        public void UnknownCommand_Exit2()
        {
            var result = PublishedCliPackage.Require().Run(_workDir, new[] { "frobnicate" });

            Assert.Equal(2, result.Exit);
            Assert.Contains("frobnicate", result.Stderr);
        }

        [Fact]
        public void MalformedRecipe_Exit2()
        {
            File.WriteAllText(Path.Join(_workDir, "broken.json"), "{ not json");

            var result = PublishedCliPackage.Require().Run(_workDir, new[] { "validate", "broken.json" });

            Assert.Equal(2, result.Exit);
            Assert.NotEmpty(result.Stderr);
        }

        [Fact]
        public void DomainInvalidRecipe_Exit1()
        {
            var recipe = ToolSpecRecipe();
            recipe.Packages = new List<string>();
            File.WriteAllText(Path.Join(_workDir, "invalid.json"), JsonSerializer.Serialize(recipe, RecipeCreateCommand.JsonOptions));

            var result = PublishedCliPackage.Require().Run(_workDir, new[] { "validate", "invalid.json" });

            Assert.Equal(1, result.Exit);
            Assert.NotEmpty(result.Stderr);
        }

        // S4-04-F (ToolSpec): published process가 --url의 loopback fake에 Resolve→Submit→Watch를 보내고 CWD 기준
        // receipt를 남긴다. Submit payload는 receipt에 저장한 request ID와 Resolve 응답 digest다. 별도 process의
        // `receipt watch`는 저장된 build ID로 Watch만 다시 보내고 새로 제출하지 않으며 receipt의 request ID를 바꾸지 않는다.
        // 각 실행의 transcript는 같은 package hash에 그 실행의 RPC 순서·요청 hash와 receipt hash를 묶는다.
        [Fact]
        public void ToolSpecSubmit_ThenSeparateProcessReceiptWatch_SameBuildId_NoResubmit()
        {
            var package = PublishedCliPackage.Require();
            using var host = NewHost();
            WriteToolSpecRecipe("recipe.json");

            var submit = package.Run(_workDir, new[] { "submit", "recipe.json", "--url", host.Url, "--format", "jsonl" }, host: host);

            Assert.True(submit.Exit == 0, submit.Stderr);
            Assert.Equal(new[] { "Resolve", "Submit", "Watch" }, host.Fake.CallOrder);
            AssertResolveRequest(Assert.Single(host.Fake.ResolveRequests));
            Assert.Equal(FixtureBuildId, Assert.Single(host.Fake.WatchRequests).BuildId);
            Assert.Equal("Succeeded", Completed(submit.Stdout).GetProperty("status").GetString());
            var receiptPath = Assert.Single(Directory.GetFiles(Path.Join(_workDir, ".nodekit", "receipts"), "*.json"));
            var stored = File.ReadAllText(receiptPath);
            Assert.Contains(host.Url, stored);
            Assert.Contains(FixtureBuildId, stored);
            var requestId = ReadProperty(receiptPath, "request_id");
            var submitRequest = Assert.Single(host.Fake.SubmitRequests);
            Assert.False(string.IsNullOrEmpty(requestId));
            Assert.Equal(requestId, submitRequest.RequestId);
            Assert.Equal(ResolvedToolSpecDigest, submitRequest.ToolSpecDigest);
            AssertTranscript(package, submit, host, new[] { "Resolve", "Submit", "Watch" }, receiptPath).Dispose();

            var watch = package.Run(_workDir, new[] { "receipt", "watch", receiptPath, "--format", "jsonl" }, host: host);

            Assert.True(watch.Exit == 0, watch.Stderr);
            Assert.Equal(new[] { "Resolve", "Submit", "Watch", "Watch" }, host.Fake.CallOrder);
            Assert.Single(host.Fake.SubmitRequests);
            Assert.All(host.Fake.WatchRequests, w => Assert.Equal(FixtureBuildId, w.BuildId));
            Assert.Equal("Succeeded", Completed(watch.Stdout).GetProperty("status").GetString());
            Assert.Equal(requestId, ReadProperty(receiptPath, "request_id"));
            AssertTranscript(package, watch, host, new[] { "Watch" }, receiptPath).Dispose();
        }

        // S4-04-F: --url 없이 NODEKIT_NODEVAULT_URL로 지정한 endpoint도 같은 경로·같은 payload로 가고, transcript에
        // 그 env override가 남는다.
        [Fact]
        public void ToolSpecSubmit_EndpointFromEnvironment_ReachesLoopbackFake()
        {
            var package = PublishedCliPackage.Require();
            using var host = NewHost();
            WriteToolSpecRecipe("recipe.json");

            var submit = package.Run(
                _workDir,
                new[] { "submit", "recipe.json", "--format", "jsonl" },
                environment: new Dictionary<string, string> { ["NODEKIT_NODEVAULT_URL"] = host.Url },
                host: host);

            Assert.True(submit.Exit == 0, submit.Stderr);
            Assert.Equal(new[] { "Resolve", "Submit", "Watch" }, host.Fake.CallOrder);
            AssertResolveRequest(Assert.Single(host.Fake.ResolveRequests));
            var receiptPath = Assert.Single(Directory.GetFiles(Path.Join(_workDir, ".nodekit", "receipts"), "*.json"));
            var submitRequest = Assert.Single(host.Fake.SubmitRequests);
            Assert.Equal(ReadProperty(receiptPath, "request_id"), submitRequest.RequestId);
            Assert.Equal(ResolvedToolSpecDigest, submitRequest.ToolSpecDigest);
            using var transcript = AssertTranscript(package, submit, host, new[] { "Resolve", "Submit", "Watch" }, receiptPath);
            Assert.Equal(host.Url, transcript.RootElement.GetProperty("env").GetProperty("NODEKIT_NODEVAULT_URL").GetString());
        }

        // listener를 닫은 주소로는 bounded 시간 안에 실패한다 — 실제 소켓을 탔다는 대조군이다.
        [Fact]
        public void ToolSpecSubmit_AfterListenerClosed_FailsWithinTimeout()
        {
            var package = PublishedCliPackage.Require();
            string url;
            using (var host = new LoopbackGrpcFixtureHost())
            {
                url = host.Url;
            }

            WriteToolSpecRecipe("recipe.json");

            var submit = package.Run(
                _workDir,
                new[] { "submit", "recipe.json", "--url", url, "--connect-timeout", "5" },
                timeout: TimeSpan.FromSeconds(30));

            Assert.NotEqual(0, submit.Exit);
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
                new() { Kind = ProtoBuildEventKind.Log, Status = "Running", BuildId = FixtureBuildId },
                new() { Kind = ProtoBuildEventKind.Log, Status = "Succeeded", BuildId = FixtureBuildId, ImageDigest = Digest },
            };
            return host;
        }

        // transcript record가 같은 package hash에 이 실행의 RPC 순서, fake가 받은 요청과 같은 bytes hash, Submit
        // payload, receipt 파일의 현재 hash를 담는지 확인한다. 실행 직후에 부르므로 receipt hash는 이 실행이 남긴 값이다.
        private static JsonDocument AssertTranscript(
            PublishedCliPackage package,
            PublishedCliResult result,
            LoopbackGrpcFixtureHost host,
            string[] expectedMethods,
            string receiptPath)
        {
            var transcript = JsonDocument.Parse(result.Transcript);
            var root = transcript.RootElement;
            Assert.Equal(package.ArchiveSha256, root.GetProperty("package_sha256").GetString());
            Assert.Equal(package.SourceSha, root.GetProperty("source_sha").GetString());
            Assert.Equal(result.Exit, root.GetProperty("exit").GetInt32());

            var rpc = root.GetProperty("rpc").EnumerateArray().ToList();
            Assert.Equal(expectedMethods, rpc.Select(r => r.GetProperty("method").GetString()));
            var recorded = host.Fake.CallsSince(host.Fake.CallCount - expectedMethods.Length);
            for (var i = 0; i < rpc.Count; i++)
            {
                Assert.Equal(recorded[i].Request.Descriptor.FullName, rpc[i].GetProperty("request_type").GetString());
                Assert.Equal(Sha256Hex(recorded[i].Request.ToByteArray()), rpc[i].GetProperty("request_sha256").GetString());
                if (rpc[i].GetProperty("method").GetString() == "Submit")
                {
                    using var request = JsonDocument.Parse(rpc[i].GetProperty("request").GetString()!);
                    Assert.Equal(ReadProperty(receiptPath, "request_id"), request.RootElement.GetProperty("requestId").GetString());
                    Assert.Equal(ResolvedToolSpecDigest, request.RootElement.GetProperty("toolSpecDigest").GetString());
                }
            }

            var receiptKey = ".nodekit/receipts/" + Path.GetFileName(receiptPath);
            Assert.Equal(Sha256Hex(File.ReadAllBytes(receiptPath)), root.GetProperty("files").GetProperty(receiptKey).GetString());
            return transcript;
        }

        // fake는 어떤 ToolSpecRequest든 받아 주므로, published process가 보낸 Resolve 요청 자체를 확인한다.
        // NodeVault buildRequestFromResolved가 encoding/json으로 읽는 raw_spec은 정확히 일곱 key(이 순서)이고
        // recipe 값과 일치해야 한다. dockerfile_content/environment_spec은 같은 source의 renderer 결과와 비교한다.
        private static void AssertResolveRequest(Nodevault.V1.ToolSpecRequest resolve)
        {
            var recipe = ToolSpecRecipe();
            Assert.Equal(recipe.ToolName, resolve.ToolName);
            Assert.Equal(recipe.Version, resolve.Version);

            using var rawSpec = JsonDocument.Parse(resolve.RawSpec);
            var root = rawSpec.RootElement;
            Assert.Equal(JsonValueKind.Object, root.ValueKind);
            Assert.Equal(
                new[] { "tool_name", "version", "kind", "image_uri", "dockerfile_content", "script", "environment_spec" },
                root.EnumerateObject().Select(p => p.Name));
            Assert.Equal(recipe.ToolName, root.GetProperty("tool_name").GetString());
            Assert.Equal(recipe.Version, root.GetProperty("version").GetString());
            Assert.Equal(1, root.GetProperty("kind").GetInt32());
            Assert.Equal(recipe.BaseImage, root.GetProperty("image_uri").GetString());
            Assert.Equal(recipe.Script, root.GetProperty("script").GetString());

            var definition = RecipeRenderer.Render(recipe);
            var dockerfile = root.GetProperty("dockerfile_content").GetString();
            Assert.Equal(definition.DockerfileContent, dockerfile);
            Assert.Contains("FROM " + recipe.BaseImage, dockerfile);
            Assert.Contains("bwa=0.7.17=h5bf99c6_8", dockerfile);
            Assert.Equal(definition.EnvironmentSpec, root.GetProperty("environment_spec").GetString());
        }

        private static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

        private static RecipeDocument ToolSpecRecipe() => new()
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

        private void WriteToolSpecRecipe(string name) =>
            File.WriteAllText(Path.Join(_workDir, name), JsonSerializer.Serialize(ToolSpecRecipe(), RecipeCreateCommand.JsonOptions));

        private static string? ReadProperty(string path, string name)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.GetProperty(name).GetString();
        }

        private static JsonElement Completed(string stdout) =>
            Assert.Single(
                stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line =>
                    {
                        using var document = JsonDocument.Parse(line);
                        return document.RootElement.Clone();
                    }),
                r => r.GetProperty("type").GetString() == "completed");

        // S4-04-E: 설치 경로와 CWD가 repository(NodeKit.sln이 있는 조상) 밖이다.
        private static void AssertOutsideRepo(string path)
        {
            for (var dir = new DirectoryInfo(path); dir is not null; dir = dir.Parent)
            {
                Assert.False(File.Exists(Path.Join(dir.FullName, "NodeKit.sln")), $"{path}가 repository 안에 있습니다.");
            }
        }
    }
}
