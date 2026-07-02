using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
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
        public void Submit_MissingRecipeFile_ReturnsTwo()
        {
            var missingPath = Path.Combine(_workDir, "nonexistent.json");
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", missingPath },
                stdout,
                stderr,
                new StubBuildClient(BuildEventKind.Succeeded));

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
                new StubBuildClient(BuildEventKind.Succeeded));

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
                new StubBuildClient(BuildEventKind.Succeeded));

            Assert.Equal(1, exitCode);
            Assert.Contains("L1-SRC-001", stderr.ToString());
        }

        [Fact]
        public void Submit_BuildSucceeded_ReturnsZero()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.JobCreated, Message = "빌드 시작됨" },
                new BuildEvent { Kind = BuildEventKind.Succeeded, Message = "완료" },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                new StubBuildClient(events));

            Assert.Equal(0, exitCode);
            Assert.Contains("[빌드 시작]", stdout.ToString());
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
                new BuildEvent { Kind = BuildEventKind.Log, Message = "빌드 로그" },
                new BuildEvent { Kind = BuildEventKind.Failed, Message = "패키지 설치 실패" },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                new StubBuildClient(events));

            Assert.Equal(1, exitCode);
            Assert.Contains("빌드 실패", stderr.ToString());
        }

        [Fact]
        public void Submit_DigestAcquiredEvent_PrintsDigest()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.DigestAcquired, Digest = "sha256:abcd1234" },
                new BuildEvent { Kind = BuildEventKind.Succeeded },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                new StubBuildClient(events));

            Assert.Equal(0, exitCode);
            Assert.Contains("sha256:abcd1234", stdout.ToString());
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

        private string WriteFile(string name, string content)
        {
            var path = Path.Combine(_workDir, name);
            File.WriteAllText(path, content);
            return path;
        }

        private sealed class StubBuildClient : IBuildClient
        {
            private readonly BuildEvent[] _events;

            public StubBuildClient(BuildEventKind terminalKind)
            {
                _events = new[] { new BuildEvent { Kind = terminalKind } };
            }

            public StubBuildClient(BuildEvent[] events)
            {
                _events = events;
            }

#pragma warning disable CS1998 // async method lacks await
            public async IAsyncEnumerable<BuildEvent> BuildAndRegisterAsync(
                BuildRequest request,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                foreach (var ev in _events)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return ev;
                }
            }
#pragma warning restore CS1998
        }
    }
}
