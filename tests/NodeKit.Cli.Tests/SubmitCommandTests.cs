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
            "DockerfileContent": "FROM registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\nRUN echo ok\n",
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
