using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using NodeKit.Cli;
using NodeKit.Cli.Tests.Fakes;
using NodeKit.Grpc;
using Xunit;

namespace NodeKit.Cli.Tests
{
    public class SubmitCommandTests : IDisposable
    {
        private readonly string _workDir = Path.Join(Path.GetTempPath(), "nodekit-submit-tests-" + Guid.NewGuid());

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

        // 리뷰 지적: GrpcToolSpecClient(url!) 생성이 try/catch 밖에 있어서, 형식이
        // 잘못된 --url 하나로 CLI가 스택트레이스와 함께 죽었다(exit 134). 재현 시
        // 실제로 관찰된 예외가 UriFormatException("not-a-url")과
        // InvalidOperationException("ftp://..." — 지원 안 하는 scheme) 두 가지라
        // 둘 다 커버해야 한다. toolSpecClient를 주입하지 않아야(null) 실제
        // GrpcToolSpecClient 생성 경로를 태운다.
        [Theory]
        [InlineData("not-a-url")]
        [InlineData("ftp://example.com")]
        [InlineData("http://")]
        public void Submit_InvalidUrl_ReturnsTwoInsteadOfCrashing(string invalidUrl)
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--url", invalidUrl, "--connect-timeout", "1" },
                stdout,
                stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains(invalidUrl, stderr.ToString());
        }

        [Fact]
        public void Submit_RecipeMissingBuildKind_ReturnsTwoInsteadOfThrowing()
        {
            var recipePath = WriteFile("recipe.json", MissingBuildKindRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

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
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var previousUrl = Environment.GetEnvironmentVariable("NODEKIT_NODEVAULT_URL");
            Environment.SetEnvironmentVariable("NODEKIT_NODEVAULT_URL", null);
            try
            {
                var exitCode = SubmitCommand.Run(new[] { "submit", recipePath }, stdout, stderr);

                Assert.Equal(2, exitCode);
                Assert.Contains("NODEKIT_NODEVAULT_URL", stderr.ToString());
            }
            finally
            {
                Environment.SetEnvironmentVariable("NODEKIT_NODEVAULT_URL", previousUrl);
            }
        }

        [Fact]
        public void Submit_MissingArgs_ReturnsTwo()
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(new[] { "submit" }, stdout, stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("사용법", stderr.ToString());
        }

        [Fact]
        public void Submit_UrlOptionMissingValue_ReturnsTwoWithExplicitError()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--url" },
                stdout,
                stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("--url 옵션에는 값이 필요합니다", stderr.ToString());
        }

        [Fact]
        public void Submit_UrlOptionValueLooksLikeAnotherOption_ReturnsTwoWithExplicitError()
        {
            // Regression test: --url used to accept whatever the next token was,
            // even if it was itself another flag (e.g. "--url --strict-reproducible"
            // silently stored "--strict-reproducible" as the URL) -- that must be
            // treated the same as a missing value, not a valid (nonsensical) URL.
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--url", "--strict-reproducible" },
                stdout,
                stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("--url 옵션에는 값이 필요합니다", stderr.ToString());
        }

        [Fact]
        public void Submit_UrlOptionDuplicated_ReturnsTwoWithExplicitError()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--url", "http://a", "--url", "http://b" },
                stdout,
                stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("--url 옵션이 여러 번 지정되었습니다", stderr.ToString());
        }

        [Fact]
        public void Submit_UnknownOption_ReturnsTwoWithExplicitError()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--url", "http://a", "--typo-flag" },
                stdout,
                stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("알 수 없는 옵션입니다: --typo-flag", stderr.ToString());
        }

        [Fact]
        public void Submit_ConnectTimeoutOptionMissingValue_ReturnsTwoWithExplicitError()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--connect-timeout" },
                stdout,
                stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("--connect-timeout 옵션에는 값이 필요합니다", stderr.ToString());
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("0")]
        [InlineData("-5")]
        public void Submit_ConnectTimeoutOptionInvalidValue_ReturnsTwoWithExplicitError(string value)
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--connect-timeout", value },
                stdout,
                stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("--connect-timeout 값이 올바르지 않습니다", stderr.ToString());
        }

        [Fact]
        public void Submit_ConnectTimeoutOptionDuplicated_ReturnsTwoWithExplicitError()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--connect-timeout", "5", "--connect-timeout", "10" },
                stdout,
                stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("--connect-timeout 옵션이 여러 번 지정되었습니다", stderr.ToString());
        }

        [Fact]
        public void Submit_WatchTimeoutOptionMissingValue_ReturnsTwoWithExplicitError()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--watch-timeout" },
                stdout,
                stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("--watch-timeout 옵션에는 값이 필요합니다", stderr.ToString());
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("2x")]
        [InlineData("0h")]
        [InlineData("-5m")]
        [InlineData("120")]
        public void Submit_WatchTimeoutOptionInvalidValue_ReturnsTwoWithExplicitError(string value)
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--watch-timeout", value },
                stdout,
                stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("--watch-timeout 값이 올바르지 않습니다", stderr.ToString());
        }

        [Theory]
        [InlineData("2h")]
        [InlineData("90m")]
        [InlineData("120s")]
        [InlineData("1.5h")]
        public void Submit_WatchTimeoutOptionValidDurationFormats_AreAccepted(string value)
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--watch-timeout", value },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(new[] { new BuildEvent { Kind = BuildEventKind.Succeeded } }));

            Assert.Equal(0, exitCode);
        }

        [Fact]
        public void Submit_WatchTimeoutOptionDuplicated_ReturnsTwoWithExplicitError()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--watch-timeout", "1h", "--watch-timeout", "2h" },
                stdout,
                stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("--watch-timeout 옵션이 여러 번 지정되었습니다", stderr.ToString());
        }

        [Fact]
        public void Submit_MissingRecipeFile_ReturnsTwo()
        {
            var missingPath = Path.Join(_workDir, "nonexistent.json");
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

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
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

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
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(new[] { new BuildEvent { Kind = BuildEventKind.Succeeded } }));

            Assert.Equal(1, exitCode);
            Assert.Contains("L1-SRC-001", stderr.ToString());
        }

        // §13 R19: --strict-reproducible blocks version-only conda pins before
        // submit, instead of letting NodeVault's final gate reject them later.

        private const string VersionOnlyPinRecipeJson = """
        {
            "BuildKind": "Conda",
            "ToolName": "bwa",
            "Version": "0.7.17",
            "BaseImage": "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "Packages": [ "bwa=0.7.17" ],
            "Channels": [ "bioconda" ],
            "Script": "bwa mem"
        }
        """;

        [Fact]
        public void Submit_VersionOnlyPin_WithStrictFlag_ReturnsOne()
        {
            var recipePath = WriteFile("recipe.json", VersionOnlyPinRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--strict-reproducible" },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(new[] { new BuildEvent { Kind = BuildEventKind.Succeeded } }));

            Assert.Equal(1, exitCode);
            Assert.Contains("L1-RCP-016", stderr.ToString());
        }

        // ── 신규 경로 (IToolSpecBuildClient 주입) ─────────────────────────────

        [Fact]
        public void Submit_BuildSucceeded_ReturnsZero()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
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
        //
        // Superseded by NodeVault Sprint 7 P1a (commit 03f5025, 2026-07-13):
        // WatchToolBuild's events now carry ImageDigest/ImageRef directly (see
        // GrpcToolSpecClient.MapWatchEvent) instead of ever emitting
        // DigestAcquired/Digest — that pair is legacy-BuildAndRegister-only and
        // WatchToolBuild's Kind is always Log, so the two tests below still
        // exercise a real code path (the safety-net branch for an unexpected
        // NodeVault version or regression) even though it's no longer the
        // common case. Adversarial review Major-1 follow-up, Issue #41 item 4.

        [Fact]
        public void Submit_BuildSucceeded_WithoutDigestAcquired_PrintsFallbackNotice()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
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
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
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
        public void Submit_BuildSucceeded_WithImageDigestFromWatchToolBuild_PrintsDigestSummary()
        {
            // The actual shape WatchToolBuild sends today: Kind is always Log
            // (buildStateEvent in NodeVault's submit_tool_build.go), digest
            // travels via ImageDigest/ImageRef, never via DigestAcquired/Digest.
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.JobCreated, Message = "빌드 제출됨", BuildId = "build-123" },
                new BuildEvent
                {
                    Kind = BuildEventKind.Log,
                    Status = "Running",
                    ImageRef = "registry.internal/library/bwa-mem:0.7.17",
                    ImageDigest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                },
                new BuildEvent { Kind = BuildEventKind.Succeeded, Message = "완료" },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(0, exitCode);
            Assert.Contains("registry.internal/library/bwa-mem:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", stdout.ToString());
            Assert.DoesNotContain("제공되지 않았습니다", stdout.ToString());
        }

        [Fact]
        public void Submit_BuildSucceeded_WithDegradedIntegrityHealth_PrintsWarningButStillReturnsZero()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.JobCreated, Message = "빌드 제출됨", BuildId = "build-123" },
                new BuildEvent
                {
                    Kind = BuildEventKind.Log,
                    ImageDigest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                    IntegrityHealth = "Partial",
                },
                new BuildEvent { Kind = BuildEventKind.Succeeded, Message = "완료" },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            // 빌드 자체는 성공했으므로 exit code는 0 유지 — 기존 스크립트의
            // 성공 판정을 깨지 않기 위한 의도적 선택. 다만 무결성 상태
            // degraded는 눈에 띄게 경고해야 한다. stderr로 나가야 한다 —
            // stdout은 digest 등 실제 결과값 전용(파이프/자동화가 stdout만
            // 파싱해도 진단성 경고에 오염되지 않도록).
            Assert.Equal(0, exitCode);
            Assert.Contains("경고: 무결성 상태가 Partial입니다", stderr.ToString());
            Assert.DoesNotContain("경고: 무결성 상태", stdout.ToString());
        }

        [Fact]
        public void Submit_BuildSucceeded_WithHealthyIntegrityHealth_DoesNotPrintWarning()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.JobCreated, Message = "빌드 제출됨", BuildId = "build-123" },
                new BuildEvent
                {
                    Kind = BuildEventKind.Log,
                    ImageDigest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                    IntegrityHealth = "Healthy",
                },
                new BuildEvent { Kind = BuildEventKind.Succeeded, Message = "완료" },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(0, exitCode);
            Assert.DoesNotContain("경고: 무결성 상태", stdout.ToString());
            Assert.DoesNotContain("경고: 무결성 상태", stderr.ToString());
        }

        [Fact]
        public void Submit_ConnectTimeoutFires_BeforeAnyEvent_ReturnsDistinctTimeoutExitCode()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var client = new HangingBeforeAnyEventToolSpecClient();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--connect-timeout", "1" },
                stdout,
                stderr,
                toolSpecClient: client);

            // 130(사용자 Ctrl-C)과 구분되는 별도 exit code — 타임아웃은
            // 사용자가 취소한 게 아니라 서버/네트워크가 응답하지 않은 것.
            Assert.Equal(124, exitCode);
            Assert.Contains("타임아웃되었습니다 (--connect-timeout)", stderr.ToString());
        }

        [Fact]
        public void Submit_ConnectTimeout_DoesNotApplyAfterBuildIdReceived()
        {
            // connect-timeout(1초)보다 오래 걸리는 WatchToolBuild 단계가 있어도
            // 실제 빌드는 정상적으로 오래 걸릴 수 있으므로, buildId를 받은
            // 뒤에는 이 타이머가 더 이상 적용되지 않아야 한다.
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var client = new SlowWatchToolSpecClient();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--connect-timeout", "1" },
                stdout,
                stderr,
                toolSpecClient: client);

            Assert.Equal(0, exitCode);
        }

        [Fact]
        public void Submit_WatchTimeoutFires_AfterBuildIdReceived_ReturnsDistinctExitCodeAndDoesNotCancelServerBuild()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var client = new HangingDuringWatchToolSpecClient();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--watch-timeout", "1s" },
                stdout,
                stderr,
                toolSpecClient: client);

            // 124(--connect-timeout)/130(Ctrl-C)과 구분되는 별도 exit code.
            Assert.Equal(125, exitCode);
            Assert.Contains("타임아웃되었습니다 (--watch-timeout)", stderr.ToString());
            Assert.Contains("build-hanging-watch", stderr.ToString());

            // Issue #71 결정: watch-timeout은 로컬 관찰만 끝낸다 — 서버 쪽 빌드는
            // 여전히 진행 중일 수 있으므로 취소 요청을 보내지 않는다.
            Assert.Empty(client.CancelledBuildIds);
        }

        [Fact]
        public void Submit_WatchTimeout_DoesNotFireIfBuildCompletesFirst()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.JobCreated, BuildId = "build-fast" },
                new BuildEvent { Kind = BuildEventKind.Succeeded, Message = "완료" },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--watch-timeout", "1h" },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(0, exitCode);
        }

        [Fact]
        public void Submit_ConnectTimeoutFires_AgainstRealGrpcToolSpecClient_ReturnsDistinctTimeoutExitCode()
        {
            // End-to-end regression test (external review): the two tests above
            // ("fires" / "does not apply after buildId") both use a hand-written fake
            // IToolSpecBuildClient that throws OperationCanceledException directly from
            // the async-enumerable -- that shape does NOT match the real
            // GrpcToolSpecClient, whose Resolve/Submit steps used to swallow every
            // exception (including cancellation) into a plain Failed BuildEvent. This
            // test drives SubmitCommand.Run through the real GrpcToolSpecClient wired
            // to an in-process fake gRPC server (same infra as GrpcToolSpecClientWireTests)
            // so a regression in that swallowing behavior fails here even if the plain
            // fake-based tests above still (misleadingly) pass.
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            using var server = new GrpcTestServer();
            server.Fake.HangOnResolveToolSpec = true;
            using var client = new GrpcToolSpecClient(server.Channel);

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--connect-timeout", "1" },
                stdout,
                stderr,
                toolSpecClient: client);

            Assert.Equal(124, exitCode);
            Assert.Contains("타임아웃되었습니다 (--connect-timeout)", stderr.ToString());
        }

        [Fact]
        public void Submit_WatchTimeoutFires_AgainstRealGrpcToolSpecClient_ReturnsDistinctTimeoutExitCode()
        {
            // End-to-end regression coverage (same rationale as the --connect-timeout
            // end-to-end test above): drives SubmitCommand.Run through the real
            // GrpcToolSpecClient wired to an in-process fake gRPC server whose
            // WatchToolBuild sends one event then hangs (honoring the server-side
            // context.CancellationToken, same infra as GrpcToolSpecClientWireTests).
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            using var server = new GrpcTestServer();
            server.Fake.WatchEvents = new List<Nodevault.V1.BuildEvent>
            {
                new() { Kind = Nodevault.V1.BuildEventKind.Log, Status = "Building", BuildId = "build-real-watch-hang" },
            };
            server.Fake.HangAfterEvents = true;
            using var client = new GrpcToolSpecClient(server.Channel);

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--watch-timeout", "1s" },
                stdout,
                stderr,
                toolSpecClient: client);

            Assert.Equal(125, exitCode);
            Assert.Contains("타임아웃되었습니다 (--watch-timeout)", stderr.ToString());
            Assert.Contains("build-real-watch-hang", stderr.ToString());
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
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
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
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
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
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var client = new CancellingToolSpecClient("build-cancel-1", new OperationCanceledException());

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                toolSpecClient: client);

            Assert.Equal(130, exitCode);
            Assert.Contains("취소되었습니다", stderr.ToString());
            Assert.Equal(new[] { "build-cancel-1" }, client.CancelledBuildIds);

            // Regression: CancelBuildAsync must not be called with CancellationToken.None
            // (which would let a best-effort cancel notification hang forever if the
            // server/network is unresponsive) — it must carry its own bounded timeout.
            Assert.True(client.CancelledTokens[0].CanBeCanceled);
        }

        [Fact]
        public void Submit_RpcCancelled_CallsCancelBuildAndReturns130()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
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
            Assert.True(client.CancelledTokens[0].CanBeCanceled);
        }

        [Fact]
        public void Submit_ServerCancelRpcHangs_StillReturnsWithinOwnTimeout()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var client = new HangingCancelToolSpecClient("build-hang-1", new OperationCanceledException());

            var stopwatch = Stopwatch.StartNew();
            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                toolSpecClient: client);
            stopwatch.Stop();

            Assert.Equal(130, exitCode);
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(10),
                $"CancelServerBuildBestEffort should be bounded by its own timeout even when the " +
                $"server never responds, took {stopwatch.Elapsed}");
        }

        [Fact]
        public void Submit_RawSpecContainsProtoFieldNames()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
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
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
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
            var path = Path.Join(_workDir, name);
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

            public List<CancellationToken> CancelledTokens { get; } = new();

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
                CancelledTokens.Add(cancellationToken);
                return Task.CompletedTask;
            }
        }

        // Simulates a server/network that never responds to the cancel RPC — CancelBuildAsync
        // only ever completes (with a cancellation exception) when the token it was given
        // fires. If SubmitCommand ever regresses to passing CancellationToken.None again,
        // this task never completes and the owning test hangs instead of finishing quickly.
        private sealed class HangingCancelToolSpecClient : IToolSpecBuildClient
        {
            private readonly string _buildId;
            private readonly Exception _cancellationException;

            public HangingCancelToolSpecClient(string buildId, Exception cancellationException)
            {
                _buildId = buildId;
                _cancellationException = cancellationException;
            }

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

            public Task CancelBuildAsync(string buildId, CancellationToken cancellationToken = default) =>
                Task.Delay(Timeout.Infinite, cancellationToken);
        }

        // Simulates ResolveToolSpec/SubmitToolBuild hanging before any event is ever
        // yielded (no buildId reaches SubmitCommand) -- proves --connect-timeout can
        // still get the CLI out even though there is nothing to observe yet.
        private sealed class HangingBeforeAnyEventToolSpecClient : IToolSpecBuildClient
        {
#pragma warning disable CS1998
            public async IAsyncEnumerable<BuildEvent> ResolveAndBuildAsync(
                string toolName,
                string version,
                string rawSpec,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                yield break;
            }
#pragma warning restore CS1998

            public Task CancelBuildAsync(string buildId, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
        }

        // Yields JobCreated immediately, then simulates a long-running real build by
        // delaying well past a short --connect-timeout before the terminal Succeeded
        // event -- proves the connect-timeout is disarmed once a buildId exists.
        private sealed class SlowWatchToolSpecClient : IToolSpecBuildClient
        {
            public async IAsyncEnumerable<BuildEvent> ResolveAndBuildAsync(
                string toolName,
                string version,
                string rawSpec,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield return new BuildEvent { Kind = BuildEventKind.JobCreated, BuildId = "build-slow-watch" };
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                yield return new BuildEvent { Kind = BuildEventKind.Succeeded, Message = "완료" };
            }

            public Task CancelBuildAsync(string buildId, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
        }

        // Yields JobCreated then hangs forever without ever completing -- proves
        // --watch-timeout can get the CLI out of a stuck WatchToolBuild stream, and
        // tracks whether CancelBuildAsync was called (it must NOT be -- Issue #71
        // decision: the server-side build may still be legitimately running).
        private sealed class HangingDuringWatchToolSpecClient : IToolSpecBuildClient
        {
            public List<string> CancelledBuildIds { get; } = new();

            public async IAsyncEnumerable<BuildEvent> ResolveAndBuildAsync(
                string toolName,
                string version,
                string rawSpec,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield return new BuildEvent { Kind = BuildEventKind.JobCreated, BuildId = "build-hanging-watch" };
                await Task.Delay(Timeout.Infinite, cancellationToken);
                yield break;
            }

            public Task CancelBuildAsync(string buildId, CancellationToken cancellationToken = default)
            {
                CancelledBuildIds.Add(buildId);
                return Task.CompletedTask;
            }
        }
    }
}
