using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using NodeKit.Cli;
using NodeKit.Grpc;
using Xunit;

namespace NodeKit.Cli.Tests
{
    public class SubmitCommandTests : IDisposable
    {
        private readonly string _workDir = Path.Combine(Path.GetTempPath(), "nodekit-submit-tests-" + Guid.NewGuid());

        public SubmitCommandTests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
        }

        private const string ValidRecipeJson = """
        {
            "BuildKind": "DockerfileFallback",
            "ToolName": "bwa",
            "Version": "0.7.17",
            "BaseImage": "registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "DockerfileContent": "FROM registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\nRUN echo ok\nUSER 1000\n",
            "Script": "bwa mem",
            "Inputs": [],
            "Outputs": []
        }
        """;

        private const string InvalidRecipeJson = """
        {
            "BuildKind": "SourceBuild",
            "ToolName": "bwa",
            "Version": "0.7.17",
            "BaseImage": "registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "SourceUri": "https://example.com/bwa-0.7.17.tar.gz",
            "SourceBuildCommands": [ "make" ],
            "Script": "bwa mem"
        }
        """;

        // 리뷰 지적: BuildKind가 없는 recipe.json은 RecipeValidationPipeline
        // .ValidateRecipe()에서 InvalidOperationException을 던진다(대화형
        // authoring 전용 내부 계약). SubmitCommand는 이 호출을 try/catch 밖에서
        // 하고 있어 외부 recipe.json에 buildKind가 없으면 스택트레이스와 함께
        // 죽었다.
        private const string MissingBuildKindRecipeJson = """
        {
            "ToolName": "bwa",
            "Version": "0.7.17",
            "BaseImage": "registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "DockerfileContent": "FROM registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\nRUN echo ok\nUSER 1000\n",
            "Script": "bwa mem",
            "Inputs": [],
            "Outputs": []
        }
        """;

        [Fact]
        public void Submit_RecipeMissingBuildKind_ReturnsTwoInsteadOfThrowing()
        {
            var recipePath = WriteFile("recipe.json", MissingBuildKindRecipeJson);
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(new[] { new BuildEvent { Kind = BuildEventKind.Succeeded } }));

            Assert.Equal(2, exitCode);
            Assert.Contains("buildKind", stderr.ToString());
        }

        // ── 공통 검증 ────────────────────────────────────────────────────────────

        [Fact]
        public void Submit_MissingUrl_ReturnsTwo()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(new[] { "submit", recipePath }, stdout, stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("NODEKIT_NODEVAULT_URL", stderr.ToString());
        }

        [Fact]
        public void Submit_MissingArgs_ReturnsTwo()
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(new[] { "submit" }, stdout, stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("사용법", stderr.ToString());
        }

        [Fact]
        public void Submit_MissingRecipeFile_ReturnsTwo()
        {
            var missingPath = Path.Combine(_workDir, "nonexistent.json");
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", missingPath },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(new[] { new BuildEvent { Kind = BuildEventKind.Succeeded } }));

            Assert.Equal(2, exitCode);
            Assert.Contains("읽을 수 없습니다", stderr.ToString());
        }

        [Fact]
        public void Submit_InvalidJson_ReturnsTwo()
        {
            var recipePath = WriteFile("bad.json", "{ not valid json }");
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(new[] { new BuildEvent { Kind = BuildEventKind.Succeeded } }));

            Assert.Equal(2, exitCode);
            Assert.Contains("파싱에 실패", stderr.ToString());
        }

        [Fact]
        public void Submit_L1ValidationFailure_ReturnsOne()
        {
            var recipePath = WriteFile("recipe.json", InvalidRecipeJson);
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(new[] { new BuildEvent { Kind = BuildEventKind.Succeeded } }));

            Assert.Equal(1, exitCode);
            Assert.Contains("L1-SRC-001", stderr.ToString());
        }

        // ── 신규 경로 (IToolSpecBuildClient 주입) ─────────────────────────────

        [Fact]
        public void Submit_BuildSucceeded_ReturnsZero()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.Log, Message = "spec 해결 완료" },
                new BuildEvent { Kind = BuildEventKind.JobCreated, Message = "빌드 제출됨", BuildId = "build-123" },
                new BuildEvent { Kind = BuildEventKind.Succeeded, Message = "완료" },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(0, exitCode);
            Assert.Contains("[성공]", stdout.ToString());
        }

        // R18 (docs/NODEKIT_CLI_FIRST_SPRINT_PLAN.md §13): live testing against a
        // real NodeVault found that WatchToolBuild does not reliably send a
        // DigestAcquired event, so the digest silently never appeared anywhere in
        // the CLI output. This doesn't fix NodeVault (out of scope), but it stops
        // NodeKit from staying silent about it.

        [Fact]
        public void Submit_BuildSucceeded_WithoutDigestAcquired_PrintsFallbackNotice()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.JobCreated, Message = "빌드 제출됨", BuildId = "build-123" },
                new BuildEvent { Kind = BuildEventKind.Succeeded, Message = "완료" },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(0, exitCode);
            Assert.Contains("digest가 서버에서 제공되지 않았습니다", stdout.ToString());
            Assert.Contains("build-123", stdout.ToString());
        }

        [Fact]
        public void Submit_BuildSucceeded_WithDigestAcquired_DoesNotPrintFallbackNotice()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.JobCreated, Message = "빌드 제출됨", BuildId = "build-123" },
                new BuildEvent { Kind = BuildEventKind.DigestAcquired, Digest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" },
                new BuildEvent { Kind = BuildEventKind.Succeeded, Message = "완료" },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(0, exitCode);
            Assert.DoesNotContain("제공되지 않았습니다", stdout.ToString());
        }

        [Fact]
        public void Submit_StreamEndsWithoutTerminalEvent_DoesNotReturnZero()
        {
            // Hidden-failure-mode check (CLAUDE.md §11 "gRPC 실패가 조용히
            // 사라지는 경우"): if WatchToolBuild's stream ends (server
            // restart, network blip) without ever sending a Succeeded/Failed/
            // Interrupted status, the outcome was never actually observed —
            // this must not be reported as success (exit 0).
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.Log, Message = "spec 해결 완료" },
                new BuildEvent { Kind = BuildEventKind.JobCreated, Message = "빌드 제출됨", BuildId = "build-999" },
                new BuildEvent { Kind = BuildEventKind.JobRunning, Message = "실행 중" },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.NotEqual(0, exitCode);
        }

        [Fact]
        public void Submit_BuildFailed_ReturnsOne()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.Log, Message = "spec 해결 완료" },
                new BuildEvent { Kind = BuildEventKind.Failed, Message = "빌드 실패: 이미지 없음" },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(1, exitCode);
            Assert.Contains("빌드 실패", stderr.ToString());
        }

        [Fact]
        public void Submit_OperationCanceled_CallsCancelBuildAndReturns130()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var client = new CancellingToolSpecClient("build-cancel-1", new OperationCanceledException());

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                toolSpecClient: client);

            Assert.Equal(130, exitCode);
            Assert.Contains("취소되었습니다", stderr.ToString());
            Assert.Equal(new[] { "build-cancel-1" }, client.CancelledBuildIds);
        }

        [Fact]
        public void Submit_RpcCancelled_CallsCancelBuildAndReturns130()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var client = new CancellingToolSpecClient(
                "build-cancel-2",
                new RpcException(new Status(StatusCode.Cancelled, "stream cancelled")));

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                toolSpecClient: client);

            Assert.Equal(130, exitCode);
            Assert.Contains("취소되었습니다", stderr.ToString());
            Assert.Equal(new[] { "build-cancel-2" }, client.CancelledBuildIds);
        }

        [Fact]
        public void Submit_RawSpecContainsProtoFieldNames()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            string? capturedRawSpec = null;

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                toolSpecClient: new CapturingToolSpecClient(rawSpec =>
                {
                    capturedRawSpec = rawSpec;
                    return new[] { new BuildEvent { Kind = BuildEventKind.Succeeded } };
                }));

            Assert.Equal(0, exitCode);
            Assert.NotNull(capturedRawSpec);

            // NodeVault buildRequestFromResolved은 proto BuildRequest JSON 태그(snake_case)를 기대한다.
            var doc = JsonDocument.Parse(capturedRawSpec!);
            Assert.True(doc.RootElement.TryGetProperty("dockerfile_content", out _),
                "raw_spec에 dockerfile_content 필드가 있어야 합니다.");
            Assert.True(doc.RootElement.TryGetProperty("image_uri", out _),
                "raw_spec에 image_uri 필드가 있어야 합니다.");
            Assert.True(doc.RootElement.TryGetProperty("tool_name", out _),
                "raw_spec에 tool_name 필드가 있어야 합니다.");
        }

        [Fact]
        public void Submit_RawSpecContainsExplicitToolSpecKind()
        {
            // NodeVault의 encoding/json 기반 파서는 BuildKind를 열거형 이름이 아니라
            // 정수값으로 받는다(protojson이 아니므로) — BUILD_KIND_TOOLSPEC == 1.
            // 생략하면 BUILD_KIND_UNSPECIFIED(0)가 되는데, 지금은 NodeVault가
            // 우연히 이를 TOOLSPEC과 동일하게 처리해 통과할 뿐이라 명시하는 게 안전하다.
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            string? capturedRawSpec = null;

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                toolSpecClient: new CapturingToolSpecClient(rawSpec =>
                {
                    capturedRawSpec = rawSpec;
                    return new[] { new BuildEvent { Kind = BuildEventKind.Succeeded } };
                }));

            Assert.Equal(0, exitCode);
            var doc = JsonDocument.Parse(capturedRawSpec!);
            Assert.True(doc.RootElement.TryGetProperty("kind", out var kind), "raw_spec에 kind 필드가 있어야 합니다.");
            Assert.Equal(1, kind.GetInt32());
        }

        // ── 헬퍼 ──────────────────────────────────────────────────────────────

        private string WriteFile(string name, string content)
        {
            var path = Path.Combine(_workDir, name);
            File.WriteAllText(path, content);
            return path;
        }

        private sealed class StubToolSpecClient : IToolSpecBuildClient
        {
            private readonly BuildEvent[] _events;

            public StubToolSpecClient(BuildEvent[] events)
            {
                _events = events;
            }

            public List<string> CancelledBuildIds { get; } = new();

#pragma warning disable CS1998, IDE0060
            public async IAsyncEnumerable<BuildEvent> ResolveAndBuildAsync(
                string toolName,
                string version,
                string rawSpec,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                foreach (var ev in _events)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return ev;
                }
            }

            public Task CancelBuildAsync(string buildId, CancellationToken cancellationToken = default)
            {
                CancelledBuildIds.Add(buildId);
                return Task.CompletedTask;
            }
#pragma warning restore CS1998, IDE0060
        }

        private sealed class CapturingToolSpecClient : IToolSpecBuildClient
        {
            private readonly Func<string, BuildEvent[]> _onResolve;

            public CapturingToolSpecClient(Func<string, BuildEvent[]> onResolve)
            {
                _onResolve = onResolve;
            }

#pragma warning disable CS1998, IDE0060
            public async IAsyncEnumerable<BuildEvent> ResolveAndBuildAsync(
                string toolName,
                string version,
                string rawSpec,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                foreach (var ev in _onResolve(rawSpec))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return ev;
                }
            }

            public Task CancelBuildAsync(string buildId, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
#pragma warning restore CS1998, IDE0060
        }

        // Console.CancelKeyPress는 테스트에서 직접 발생시킬 수 없으므로, 이미
        // 취소된 상황에서 실제로 관측되는 예외(OperationCanceledException 또는
        // RpcException(StatusCode.Cancelled))를 build_id 수신 이후 던져서
        // SubmitCommand의 취소 처리 경로(CancelBuildAsync 호출 + exit 130)를 검증한다.
        private sealed class CancellingToolSpecClient : IToolSpecBuildClient
        {
            private readonly string _buildId;
            private readonly Exception _cancellationException;

            public CancellingToolSpecClient(string buildId, Exception cancellationException)
            {
                _buildId = buildId;
                _cancellationException = cancellationException;
            }

            public List<string> CancelledBuildIds { get; } = new();

#pragma warning disable CS1998
            public async IAsyncEnumerable<BuildEvent> ResolveAndBuildAsync(
                string toolName,
                string version,
                string rawSpec,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield return new BuildEvent { Kind = BuildEventKind.JobCreated, BuildId = _buildId };
                throw _cancellationException;
            }
#pragma warning restore CS1998

            public Task CancelBuildAsync(string buildId, CancellationToken cancellationToken = default)
            {
                CancelledBuildIds.Add(buildId);
                return Task.CompletedTask;
            }
        }
    }
}
