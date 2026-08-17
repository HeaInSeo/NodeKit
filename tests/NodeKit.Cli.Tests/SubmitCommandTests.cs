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

        [Theory]
        [InlineData("--help")]
        [InlineData("-h")]
        public void Submit_HelpFlag_ReturnsZeroWithUsage(string helpFlag)
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(new[] { "submit", helpFlag }, stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("사용법: nodekit submit", stdout.ToString());
            Assert.Empty(stderr.ToString());
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
        // 리뷰 지적(High): double.TryParse(NumberStyles.Float)는 "NaN"/"Infinity"를
        // 유효한 값으로 파싱하고, "1e400" 같은 오버플로 지수도 조용히 +∞로
        // 만든다 -- value <= 0 검사로는 못 걸러내서 TimeSpan.FromHours/Minutes/
        // Seconds가 그대로 ArgumentException/OverflowException을 던져 CLI가
        // try/catch 밖에서 죽었다(옵션 파싱은 recipe 로드보다도 먼저 실행됨).
        // "1e10h"/"999999999h"는 유한하지만 TimeSpan 표현 범위(~10,675,199일)를
        // 넘어 같은 OverflowException 경로를 탄다.
        [InlineData("NaNh")]
        [InlineData("Infinitym")]
        [InlineData("1e400h")]
        [InlineData("1e309s")]
        [InlineData("1e10h")]
        [InlineData("999999999h")]
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
            // 리뷰 지적: lastImageDigest가 legacy ev.Digest(DigestAcquired)에서
            // 채워지지 않아서, fallback 문구는 안 뜨지만("제공되지 않았습니다"
            // 없음, digestReceived=true라서) 정작 "이미지 digest: ..." 요약
            // 줄도 안 떴었다 -- 둘 다 확인한다.
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
            Assert.Contains("이미지 digest: sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", stdout.ToString());
        }

        [Fact]
        public void Submit_FormatJsonl_LegacyDigestAcquired_PopulatesImageDigestInCompletedRecord()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.JobCreated, BuildId = "build-legacy-digest" },
                new BuildEvent { Kind = BuildEventKind.DigestAcquired, Digest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" },
                new BuildEvent { Kind = BuildEventKind.Succeeded },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(0, exitCode);
            var lines = SplitNonEmptyLines(stdout.ToString());
            var completed = ParseRecord(lines[^1]);
            Assert.Equal("completed", completed.GetProperty("type").GetString());
            Assert.Equal(
                "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                completed.GetProperty("image_digest").GetString());
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

        // 외부 리뷰: PrintEvent가 모든 이벤트를 무조건 stdout에 찍어서, Failed
        // 이벤트가 오면 stdout("[실패] ...")과 stderr("빌드 실패: ...") 양쪽에
        // 같은 메시지가 중복으로 나갔다 — stdout은 결과값 전용, 진단은 stderr
        // 전용이라는 이 코드베이스의 기존 원칙(IntegrityHealth 경고와 동일)에
        // 어긋났다. 자동화 스크립트가 stdout만 파싱해도 실패 문구에 오염될 수
        // 있었다.
        [Fact]
        public void Submit_BuildFailed_MessageAppearsOnlyOnStderr_NotDuplicatedOnStdout()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.Log, Message = "spec 해결 완료" },
                new BuildEvent { Kind = BuildEventKind.Failed, Message = "NodeVault에 연결할 수 없습니다." },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(1, exitCode);
            Assert.Contains("NodeVault에 연결할 수 없습니다.", stderr.ToString());
            Assert.DoesNotContain("NodeVault에 연결할 수 없습니다.", stdout.ToString());
        }

        // 외부 리뷰: 첫 RPC(ResolveToolSpec) 호출 전에 "빌드를 제출합니다"라고
        // 찍어서, 연결 실패나 ResolveToolSpec 실패로 실제로는 아무것도
        // 제출되지 않았어도 사용자가 제출된 것으로 오해할 수 있었다. 문구를
        // "빌드 요청을 시작합니다"로 완화했다 — 실제 제출 확인은 서버가
        // JobCreated 이벤트를 보내야만 나오는 "[빌드 시작]" 로그가 담당한다.
        [Fact]
        public void Submit_AnnouncementWording_DoesNotClaimSubmissionBeforeAnyRpc()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(new[] { new BuildEvent { Kind = BuildEventKind.Succeeded } }));

            Assert.Equal(0, exitCode);
            Assert.Contains("빌드 요청을 시작합니다", stdout.ToString());
            Assert.DoesNotContain("빌드를 제출합니다", stdout.ToString());
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

        // ── Issue #82: --format jsonl ────────────────────────────────────────

        [Fact]
        public void Submit_FormatJsonlUnknownValue_ReturnsTwoWithExplicitError()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "bogus" },
                stdout,
                stderr);

            Assert.Equal(2, exitCode);
            Assert.Contains("--format", stderr.ToString());
        }

        [Fact]
        public void Submit_FormatJsonl_BuildSucceeded_EmitsSubmittedStateAndCompletedRecords()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.JobCreated, BuildId = "build-jsonl-1", Status = "Building" },
                new BuildEvent { Kind = BuildEventKind.Log, BuildId = "build-jsonl-1", Status = "Pushing" },
                new BuildEvent
                {
                    Kind = BuildEventKind.Succeeded,
                    BuildId = "build-jsonl-1",
                    ImageRef = "registry.example.com/bwa:0.7.17",
                    ImageDigest = "sha256:abc123",
                    IntegrityHealth = "Healthy",
                },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(0, exitCode);
            var lines = SplitNonEmptyLines(stdout.ToString());
            Assert.Equal(3, lines.Count);

            var submitted = ParseRecord(lines[0]);
            Assert.Equal("nodekit.submit.v1", submitted.GetProperty("schema_version").GetString());
            Assert.Equal("submitted", submitted.GetProperty("type").GetString());
            Assert.Equal("build-jsonl-1", submitted.GetProperty("build_id").GetString());
            Assert.False(submitted.TryGetProperty("status", out _), "submitted 레코드에는 status가 없어야 합니다.");

            var state = ParseRecord(lines[1]);
            Assert.Equal("state", state.GetProperty("type").GetString());
            Assert.Equal("Pushing", state.GetProperty("state").GetString());

            var completed = ParseRecord(lines[2]);
            Assert.Equal("completed", completed.GetProperty("type").GetString());
            Assert.Equal("Succeeded", completed.GetProperty("status").GetString());
            Assert.Equal("build-jsonl-1", completed.GetProperty("build_id").GetString());
            Assert.Equal("sha256:abc123", completed.GetProperty("image_digest").GetString());
            Assert.False(completed.TryGetProperty("error_code", out _), "성공 시 error_code가 없어야 합니다.");

            Assert.Empty(stderr.ToString());
        }

        [Fact]
        public void Submit_FormatJsonl_ProgressEventWithoutBuildId_OmitsBuildIdFieldEntirely()
        {
            // 리뷰 지적(High): ResolveToolSpec 성공 직후 뜨는 흔한 Log 이벤트("spec
            // 해결 완료...")처럼 build ID가 아직 없는 진행 이벤트가 오면
            // ProgressState가 build_id를 빈 문자열("")로 채워서 직렬화했었다 --
            // "optional 필드는 아예 생략한다"는 계약과 어긋난다. 이 테스트는 그
            // 정확한 입력 모양(빈 BuildId를 가진 Log 이벤트가 먼저 옴)을 재현해서
            // 첫 번째 state 레코드에 build_id 자체가 없는지 확인한다.
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.Log, Message = "spec 해결 완료 (digest: 8f3a1c2d...)" },
                new BuildEvent { Kind = BuildEventKind.JobCreated, BuildId = "build-jsonl-progress" },
                new BuildEvent { Kind = BuildEventKind.Succeeded, BuildId = "build-jsonl-progress" },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(0, exitCode);
            var lines = SplitNonEmptyLines(stdout.ToString());
            var firstState = ParseRecord(lines[0]);
            Assert.Equal("state", firstState.GetProperty("type").GetString());
            Assert.False(firstState.TryGetProperty("build_id", out _),
                "build ID를 아직 모를 때는 build_id 필드 자체가 없어야 합니다(빈 문자열 아님).");

            var submitted = ParseRecord(lines[1]);
            Assert.Equal("submitted", submitted.GetProperty("type").GetString());
            Assert.Equal("build-jsonl-progress", submitted.GetProperty("build_id").GetString());
        }

        [Fact]
        public void Submit_FormatJsonl_DegradedIntegrityHealth_FieldPresentButNoStderrWarning()
        {
            // human 모드는 degraded IntegrityHealth를 stderr 경고로도 알리지만,
            // jsonl 모드는 completed 레코드의 integrity_health 필드에 이미
            // 구조화되어 있으므로 별도 stderr 경고를 내지 않는다(SubmitCommand.cs
            // 주석 참조) -- 데이터가 실제로 필드에 담기는지, stderr가 비어있는지
            // 둘 다 확인한다.
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.JobCreated, BuildId = "build-jsonl-health" },
                new BuildEvent
                {
                    Kind = BuildEventKind.Succeeded,
                    BuildId = "build-jsonl-health",
                    ImageDigest = "sha256:abc123",
                    IntegrityHealth = "Partial",
                },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(0, exitCode);
            var lines = SplitNonEmptyLines(stdout.ToString());
            var completed = ParseRecord(lines[^1]);
            Assert.Equal("Partial", completed.GetProperty("integrity_health").GetString());
            Assert.Empty(stderr.ToString());
        }

        [Fact]
        public void Submit_FormatJsonl_FirstBuildIdEventIsTerminal_SkipsSubmittedRecordButKeepsBuildId()
        {
            // 리뷰 지적: build ID를 처음 받는 이벤트가 곧바로 terminal(Succeeded/
            // Failed)이면 "submitted" 레코드 자체가 생략된다(loop이 Succeeded/
            // Failed를 submitted/state 분기에서 명시적으로 제외하기 때문) --
            // build_id는 completed 레코드에 정상적으로 담기므로 데이터 손실은
            // 아니지만, "submitted 다음에 completed" 순서가 항상 보장되지는
            // 않는다는 걸 문서화하는 회귀 테스트.
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.Succeeded, BuildId = "build-instant" },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(0, exitCode);
            var lines = SplitNonEmptyLines(stdout.ToString());
            var only = ParseRecord(Assert.Single(lines));
            Assert.Equal("completed", only.GetProperty("type").GetString());
            Assert.Equal("build-instant", only.GetProperty("build_id").GetString());
        }

        [Fact]
        public void Submit_FormatJsonl_WatchTerminalFailed_EmitsBuildFailedWithConfirmedRemoteState()
        {
            // Watch에서 실제 Failed 수신 -> BUILD_FAILED (외부 리뷰 follow-up:
            // build ID가 이미 있는 상태에서 온 Failed만 WatchToolBuild가 실제로
            // terminal 실패를 관측한 것이므로 remote_build_state를 "failed"로
            // 확정할 수 있다).
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.JobCreated, BuildId = "build-jsonl-2" },
                new BuildEvent { Kind = BuildEventKind.Failed, BuildId = "build-jsonl-2", Message = "이미지 없음" },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(1, exitCode);
            var lines = SplitNonEmptyLines(stdout.ToString());
            var completed = ParseRecord(lines[^1]);
            Assert.Equal("completed", completed.GetProperty("type").GetString());
            Assert.Equal("Failed", completed.GetProperty("status").GetString());
            Assert.Equal("BUILD_FAILED", completed.GetProperty("error_code").GetString());
            Assert.Equal("watch", completed.GetProperty("phase").GetString());
            Assert.Equal("failed", completed.GetProperty("remote_build_state").GetString());
            Assert.Equal("이미지 없음", completed.GetProperty("message").GetString());
            Assert.Equal("build-jsonl-2", completed.GetProperty("build_id").GetString());
        }

        [Fact]
        public void Submit_FormatJsonl_ResolveConnectFailure_EmitsPreWatchFailedWithUnknownRemoteState()
        {
            // Resolve 연결 실패 -> PRE_WATCH_FAILED. GrpcToolSpecClient converts a
            // ResolveToolSpec-stage connection failure into a single Failed event
            // with no BuildId -- SubmitCommand never learns a build ID, so it
            // cannot claim the remote build didn't happen (SubmitToolBuild is
            // never even reached in this scenario, but the CLI-observable shape
            // is identical to a lost SubmitToolBuild response either way).
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.Failed, Message = "NodeVault에 연결할 수 없습니다. 주소와 네트워크 상태를 확인하세요." },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(1, exitCode);
            var lines = SplitNonEmptyLines(stdout.ToString());
            var completed = ParseRecord(Assert.Single(lines));
            Assert.Equal("completed", completed.GetProperty("type").GetString());
            Assert.Equal("Failed", completed.GetProperty("status").GetString());
            Assert.Equal("PRE_WATCH_FAILED", completed.GetProperty("error_code").GetString());
            Assert.Equal("pre_watch", completed.GetProperty("phase").GetString());
            Assert.Equal("unknown", completed.GetProperty("remote_build_state").GetString());
            Assert.False(completed.TryGetProperty("build_id", out _),
                "pre-watch 실패는 build ID를 받기 전이므로 build_id가 없어야 합니다.");

            var message = completed.GetProperty("message").GetString();
            Assert.DoesNotContain("생성되지 않았", message);
            Assert.DoesNotContain("시작되지 않았", message);
        }

        [Fact]
        public void Submit_FormatJsonl_SubmitRpcFailure_EmitsPreWatchFailedWithUnknownRemoteState()
        {
            // Submit RPC 실패 -> PRE_WATCH_FAILED. ResolveToolSpec succeeded first
            // (a Log event, still no BuildId), then SubmitToolBuild itself failed --
            // still no BuildId was ever received, so this must be indistinguishable
            // from the resolve-failure case at the SubmitCommand level: same
            // error_code/phase/remote_build_state.
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.Log, Message = "spec 해결 완료 (digest: 8f3a1c2d...)" },
                new BuildEvent { Kind = BuildEventKind.Failed, Message = "연결된 NodeVault가 이 요청을 지원하지 않습니다: ..." },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(1, exitCode);
            var lines = SplitNonEmptyLines(stdout.ToString());
            var completed = ParseRecord(lines[^1]);
            Assert.Equal("completed", completed.GetProperty("type").GetString());
            Assert.Equal("PRE_WATCH_FAILED", completed.GetProperty("error_code").GetString());
            Assert.Equal("pre_watch", completed.GetProperty("phase").GetString());
            Assert.Equal("unknown", completed.GetProperty("remote_build_state").GetString());
            Assert.False(completed.TryGetProperty("build_id", out _));
        }

        [Fact]
        public void Submit_FormatJsonl_PreWatchAndWatchFailures_HumanOutputAndExitCodeUnchanged()
        {
            // human 출력과 기존 exit code는 변경되지 않음(둘 다 exit 1, 동일한
            // "빌드 실패: {message}" stderr 문구) -- phase/remote_build_state
            // distinction is jsonl-only.
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var preWatchStdout = new StringWriter();
            using var preWatchStderr = new StringWriter();
            using var watchStdout = new StringWriter();
            using var watchStderr = new StringWriter();

            var preWatchExit = SubmitCommand.Run(
                new[] { "submit", recipePath },
                preWatchStdout,
                preWatchStderr,
                toolSpecClient: new StubToolSpecClient(new[]
                {
                    new BuildEvent { Kind = BuildEventKind.Failed, Message = "연결 실패" },
                }));
            var watchExit = SubmitCommand.Run(
                new[] { "submit", recipePath },
                watchStdout,
                watchStderr,
                toolSpecClient: new StubToolSpecClient(new[]
                {
                    new BuildEvent { Kind = BuildEventKind.JobCreated, BuildId = "build-x" },
                    new BuildEvent { Kind = BuildEventKind.Failed, BuildId = "build-x", Message = "빌드 실패함" },
                }));

            Assert.Equal(1, preWatchExit);
            Assert.Equal(1, watchExit);
            Assert.Equal("빌드 실패: 연결 실패", preWatchStderr.ToString().Trim());
            Assert.Contains("빌드 실패: 빌드 실패함", watchStderr.ToString());
            Assert.DoesNotContain('{', preWatchStdout.ToString());
        }

        [Fact]
        public void Submit_FormatJsonl_StreamEndsWithoutTerminalEvent_EmitsCompletedRecordWithStreamEndedErrorCode()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.JobCreated, BuildId = "build-jsonl-3" },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(1, exitCode);
            var lines = SplitNonEmptyLines(stdout.ToString());
            var completed = ParseRecord(lines[^1]);
            Assert.Equal("completed", completed.GetProperty("type").GetString());
            Assert.Equal("Failed", completed.GetProperty("status").GetString());
            Assert.Equal("STREAM_ENDED_WITHOUT_RESULT", completed.GetProperty("error_code").GetString());
        }

        [Fact]
        public void Submit_FormatJsonl_ConnectTimeoutFires_EmitsCompletedRecordWithoutBuildId()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var client = new HangingBeforeAnyEventToolSpecClient();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--connect-timeout", "1", "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: client);

            Assert.Equal(124, exitCode);
            var lines = SplitNonEmptyLines(stdout.ToString());
            var completed = ParseRecord(Assert.Single(lines));
            Assert.Equal("completed", completed.GetProperty("type").GetString());
            Assert.Equal("Failed", completed.GetProperty("status").GetString());
            Assert.Equal("CONNECT_TIMEOUT", completed.GetProperty("error_code").GetString());
            Assert.False(completed.TryGetProperty("build_id", out _),
                "connect-timeout은 build ID를 받기 전에만 발동하므로 build_id가 없어야 합니다.");
            Assert.Empty(stderr.ToString());
        }

        [Fact]
        public void Submit_FormatJsonl_WatchTimeoutFires_EmitsCompletedRecordWithBuildId()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var client = new HangingDuringWatchToolSpecClient();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--watch-timeout", "1s", "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: client);

            Assert.Equal(125, exitCode);
            var lines = SplitNonEmptyLines(stdout.ToString());
            var completed = ParseRecord(lines[^1]);
            Assert.Equal("completed", completed.GetProperty("type").GetString());
            Assert.Equal("Failed", completed.GetProperty("status").GetString());
            Assert.Equal("WATCH_TIMEOUT", completed.GetProperty("error_code").GetString());
            Assert.Equal("build-hanging-watch", completed.GetProperty("build_id").GetString());
            Assert.Empty(client.CancelledBuildIds);
        }

        [Fact]
        public void Submit_FormatJsonl_Cancelled_EmitsCompletedRecordWithUserCancelledErrorCode()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var client = new CancellingToolSpecClient("build-jsonl-cancel", new OperationCanceledException());

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: client);

            Assert.Equal(130, exitCode);
            // CancellingToolSpecClient yields a JobCreated (build_id-bearing) event
            // before throwing, so a "submitted" record precedes the "completed" one.
            var lines = SplitNonEmptyLines(stdout.ToString());
            var submitted = ParseRecord(lines[0]);
            Assert.Equal("submitted", submitted.GetProperty("type").GetString());
            var completed = ParseRecord(lines[^1]);
            Assert.Equal("completed", completed.GetProperty("type").GetString());
            Assert.Equal("Cancelled", completed.GetProperty("status").GetString());
            Assert.Equal("USER_CANCELLED", completed.GetProperty("error_code").GetString());
            Assert.Equal("build-jsonl-cancel", completed.GetProperty("build_id").GetString());
        }

        [Fact]
        public void Submit_FormatJsonl_EveryStdoutLineIsIndependentlyValidJson()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.JobCreated, BuildId = "build-jsonl-4", Status = "Building" },
                new BuildEvent { Kind = BuildEventKind.JobRunning, BuildId = "build-jsonl-4", Status = "Running" },
                new BuildEvent { Kind = BuildEventKind.Succeeded, BuildId = "build-jsonl-4" },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(0, exitCode);
            var lines = SplitNonEmptyLines(stdout.ToString());
            Assert.True(lines.Count >= 2);
            foreach (var line in lines)
            {
                // JsonDocument.Parse throws on anything that isn't valid, standalone JSON --
                // this is the "every line independently parses" contract check.
                using var doc = JsonDocument.Parse(line);
                Assert.Equal("nodekit.submit.v1", doc.RootElement.GetProperty("schema_version").GetString());
            }
        }

        [Fact]
        public void Submit_FormatHumanExplicit_MatchesDefaultOutput()
        {
            // --format human (explicit) must produce byte-identical output to omitting
            // --format entirely -- "human" is the default, not a distinct third mode.
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdoutDefault = new StringWriter();
            using var stderrDefault = new StringWriter();
            using var stdoutExplicit = new StringWriter();
            using var stderrExplicit = new StringWriter();
            var events = new[] { new BuildEvent { Kind = BuildEventKind.Succeeded } };

            var exitDefault = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdoutDefault,
                stderrDefault,
                toolSpecClient: new StubToolSpecClient(events));
            var exitExplicit = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "human" },
                stdoutExplicit,
                stderrExplicit,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(exitDefault, exitExplicit);
            Assert.Equal(stdoutDefault.ToString(), stdoutExplicit.ToString());
            Assert.Equal(stderrDefault.ToString(), stderrExplicit.ToString());
            Assert.DoesNotContain('{', stdoutDefault.ToString());
        }

        // ── V14: recovery-disposition (OP-V14-RECOVERY-CAP) ──────────────────
        // 세 값(none/terminal/uncertain)이 각 terminal 경로에서 결정론적으로
        // 나오는지 검증한다. jsonl 전용이며 human 출력/exit code는 불변.

        [Fact]
        public void Submit_FormatJsonl_Succeeded_EmitsRecoveryNone()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.JobCreated, BuildId = "build-rec-ok" },
                new BuildEvent { Kind = BuildEventKind.Succeeded, BuildId = "build-rec-ok" },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(0, exitCode);
            var completed = ParseRecord(SplitNonEmptyLines(stdout.ToString())[^1]);
            Assert.Equal("Succeeded", completed.GetProperty("status").GetString());
            Assert.Equal("none", completed.GetProperty("recovery").GetString());
        }

        [Fact]
        public void Submit_FormatJsonl_WatchFailed_EmitsRecoveryTerminal()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.JobCreated, BuildId = "build-rec-fail" },
                new BuildEvent { Kind = BuildEventKind.Failed, BuildId = "build-rec-fail", Message = "이미지 없음" },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(1, exitCode);
            var completed = ParseRecord(SplitNonEmptyLines(stdout.ToString())[^1]);
            Assert.Equal("BUILD_FAILED", completed.GetProperty("error_code").GetString());
            // 확정 실패 -> terminal. 재제출은 재시도가 아니라 새 작업이다.
            Assert.Equal("terminal", completed.GetProperty("recovery").GetString());
        }

        [Fact]
        public void Submit_FormatJsonl_PreWatchFailed_EmitsRecoveryUncertain()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.Failed, Message = "NodeVault에 연결할 수 없습니다." },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(1, exitCode);
            var completed = ParseRecord(Assert.Single(SplitNonEmptyLines(stdout.ToString())));
            Assert.Equal("PRE_WATCH_FAILED", completed.GetProperty("error_code").GetString());
            // build ID를 못 받아 원격 결과 미확인 -> uncertain (idempotency key로 reconcile).
            Assert.Equal("uncertain", completed.GetProperty("recovery").GetString());
        }

        [Fact]
        public void Submit_FormatJsonl_StreamEndedWithoutResult_EmitsRecoveryUncertain()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.JobCreated, BuildId = "build-rec-stream" },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(1, exitCode);
            var completed = ParseRecord(SplitNonEmptyLines(stdout.ToString())[^1]);
            Assert.Equal("STREAM_ENDED_WITHOUT_RESULT", completed.GetProperty("error_code").GetString());
            Assert.Equal("uncertain", completed.GetProperty("recovery").GetString());
        }

        [Fact]
        public void Submit_FormatJsonl_UserCancelled_EmitsRecoveryTerminal()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var client = new CancellingToolSpecClient("build-rec-cancel", new OperationCanceledException());

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: client);

            Assert.Equal(130, exitCode);
            var completed = ParseRecord(SplitNonEmptyLines(stdout.ToString())[^1]);
            Assert.Equal("USER_CANCELLED", completed.GetProperty("error_code").GetString());
            // 사용자 abort는 확정 종료 -> terminal.
            Assert.Equal("terminal", completed.GetProperty("recovery").GetString());
        }

        [Fact]
        public void Submit_FormatJsonl_UnexpectedError_EmitsRecoveryUncertain()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var client = new ThrowingToolSpecClient("build-rec-error", new InvalidOperationException("예상치 못한 오류"));

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: client);

            Assert.Equal(1, exitCode);
            var completed = ParseRecord(SplitNonEmptyLines(stdout.ToString())[^1]);
            Assert.Equal("UNEXPECTED_ERROR", completed.GetProperty("error_code").GetString());
            Assert.Equal("uncertain", completed.GetProperty("recovery").GetString());
        }

        [Fact]
        public void Submit_FormatJsonl_ConnectTimeout_EmitsRecoveryUncertain()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var client = new HangingBeforeAnyEventToolSpecClient();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--connect-timeout", "1", "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: client);

            Assert.Equal(124, exitCode);
            var completed = ParseRecord(Assert.Single(SplitNonEmptyLines(stdout.ToString())));
            Assert.Equal("CONNECT_TIMEOUT", completed.GetProperty("error_code").GetString());
            Assert.Equal("uncertain", completed.GetProperty("recovery").GetString());
        }

        [Fact]
        public void Submit_FormatJsonl_WatchTimeout_EmitsRecoveryUncertain()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var client = new HangingDuringWatchToolSpecClient();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--watch-timeout", "1s", "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: client);

            Assert.Equal(125, exitCode);
            var completed = ParseRecord(SplitNonEmptyLines(stdout.ToString())[^1]);
            Assert.Equal("WATCH_TIMEOUT", completed.GetProperty("error_code").GetString());
            Assert.Equal("uncertain", completed.GetProperty("recovery").GetString());
        }

        [Fact]
        public void Submit_FormatJsonl_Recovery_OnlyOnCompletedRecords_NotSubmittedOrState()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.JobCreated, BuildId = "build-rec-scope", Status = "Building" },
                new BuildEvent { Kind = BuildEventKind.JobRunning, BuildId = "build-rec-scope", Status = "Running" },
                new BuildEvent { Kind = BuildEventKind.Succeeded, BuildId = "build-rec-scope" },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(0, exitCode);
            var lines = SplitNonEmptyLines(stdout.ToString());
            foreach (var line in lines)
            {
                var record = ParseRecord(line);
                var type = record.GetProperty("type").GetString();
                if (type == "completed")
                {
                    Assert.True(record.TryGetProperty("recovery", out _), "completed 레코드에는 recovery가 있어야 합니다.");
                }
                else
                {
                    Assert.False(record.TryGetProperty("recovery", out _), $"{type} 레코드에는 recovery가 없어야 합니다.");
                }
            }
        }

        [Fact]
        public void Submit_HumanMode_NeverEmitsRecoveryField()
        {
            // "UI 수정 금지": human 출력에는 recovery 개념이 전혀 나타나지 않는다.
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var events = new[]
            {
                new BuildEvent { Kind = BuildEventKind.JobCreated, BuildId = "build-human" },
                new BuildEvent { Kind = BuildEventKind.Succeeded, BuildId = "build-human" },
            };

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(events));

            Assert.Equal(0, exitCode);
            Assert.DoesNotContain("recovery", stdout.ToString());
            Assert.DoesNotContain("recovery", stderr.ToString());
        }

        // ── 제출 이전(pre-submit) terminal 경로도 jsonl completed 레코드를 낸다 ──
        // 계약(NODEKIT_CLI_USAGE.md §--format jsonl): completed는 "스트림의 마지막
        // 레코드, 항상 정확히 한 번". 예전에는 제출 전 로컬 실패(주소 누락, recipe
        // 읽기/파싱 실패, buildKind 누락, L1 실패, 잘못된 --url)가 stderr에만 사람용
        // 메시지를 쓰고 stdout에는 아무 jsonl 레코드도 안 내보내, jsonl 소비자에게는
        // stdout이 비어 보였다. 이 경로들은 원격 빌드가 아예 생성되지 않은 로컬
        // 확정 실패이므로 recovery는 terminal이다.

        [Fact]
        public void Submit_FormatJsonl_MissingUrl_EmitsSingleCompletedTerminalRecord()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var previousUrl = Environment.GetEnvironmentVariable("NODEKIT_NODEVAULT_URL");
            Environment.SetEnvironmentVariable("NODEKIT_NODEVAULT_URL", null);
            try
            {
                var exitCode = SubmitCommand.Run(
                    new[] { "submit", recipePath, "--format", "jsonl" }, stdout, stderr);

                Assert.Equal(2, exitCode);
                AssertSingleCompletedTerminalFailure(stdout.ToString(), "URL_REQUIRED");
            }
            finally
            {
                Environment.SetEnvironmentVariable("NODEKIT_NODEVAULT_URL", previousUrl);
            }
        }

        [Fact]
        public void Submit_FormatJsonl_RecipeReadFailed_EmitsSingleCompletedTerminalRecord()
        {
            var missingPath = Path.Join(_workDir, "nonexistent.json");
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", missingPath, "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(new[] { new BuildEvent { Kind = BuildEventKind.Succeeded } }));

            Assert.Equal(2, exitCode);
            AssertSingleCompletedTerminalFailure(stdout.ToString(), "RECIPE_READ_FAILED");
        }

        [Fact]
        public void Submit_FormatJsonl_RecipeParseFailed_EmitsSingleCompletedTerminalRecord()
        {
            var recipePath = WriteFile("bad.json", "{ not valid json }");
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(new[] { new BuildEvent { Kind = BuildEventKind.Succeeded } }));

            Assert.Equal(2, exitCode);
            AssertSingleCompletedTerminalFailure(stdout.ToString(), "RECIPE_PARSE_FAILED");
        }

        [Fact]
        public void Submit_FormatJsonl_RecipeEmpty_EmitsSingleCompletedTerminalRecord()
        {
            // JSON 리터럴 null → Deserialize가 null 반환 → "recipe 파일이 비어있습니다."
            var recipePath = WriteFile("empty.json", "null");
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(new[] { new BuildEvent { Kind = BuildEventKind.Succeeded } }));

            Assert.Equal(2, exitCode);
            AssertSingleCompletedTerminalFailure(stdout.ToString(), "RECIPE_EMPTY");
        }

        [Fact]
        public void Submit_FormatJsonl_MissingBuildKind_EmitsSingleCompletedTerminalRecord()
        {
            var recipePath = WriteFile("recipe.json", MissingBuildKindRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(new[] { new BuildEvent { Kind = BuildEventKind.Succeeded } }));

            Assert.Equal(2, exitCode);
            AssertSingleCompletedTerminalFailure(stdout.ToString(), "MISSING_BUILD_KIND");
        }

        [Fact]
        public void Submit_FormatJsonl_L1ValidationFailure_EmitsSingleCompletedTerminalRecordAndViolationsOnStderr()
        {
            var recipePath = WriteFile("recipe.json", InvalidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--format", "jsonl" },
                stdout,
                stderr,
                toolSpecClient: new StubToolSpecClient(new[] { new BuildEvent { Kind = BuildEventKind.Succeeded } }));

            Assert.Equal(1, exitCode);
            AssertSingleCompletedTerminalFailure(stdout.ToString(), "L1_VALIDATION_FAILED");
            // 상세 위반 내역은 사람용으로 stderr에만 남고, stdout은 JSON 전용이다.
            Assert.Contains("L1-SRC-001", stderr.ToString());
            Assert.DoesNotContain("L1-SRC-001", stdout.ToString());
        }

        [Fact]
        public void Submit_FormatJsonl_InvalidUrl_EmitsSingleCompletedTerminalRecord()
        {
            var recipePath = WriteFile("recipe.json", ValidRecipeJson);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            // toolSpecClient를 주입하지 않아 실제 GrpcToolSpecClient(url) 생성 경로를
            // 태운다 — "not-a-url"은 ctor에서 예외를 던진다.
            var exitCode = SubmitCommand.Run(
                new[] { "submit", recipePath, "--url", "not-a-url", "--format", "jsonl" },
                stdout,
                stderr);

            Assert.Equal(2, exitCode);
            AssertSingleCompletedTerminalFailure(stdout.ToString(), "INVALID_URL");
        }

        [Fact]
        public void Submit_HumanMode_MissingBuildKind_WritesStderrAndNoJsonOnStdout()
        {
            // human 모드 회귀 보호: stdout에 JSON completed 레코드가 새지 않고,
            // 기존대로 사람용 메시지는 stderr로 간다(계약 불변).
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
            Assert.DoesNotContain("\"type\"", stdout.ToString());
            Assert.DoesNotContain("completed", stdout.ToString());
        }

        private static void AssertSingleCompletedTerminalFailure(string stdoutText, string expectedErrorCode)
        {
            var lines = SplitNonEmptyLines(stdoutText);
            Assert.Single(lines);
            var completed = ParseRecord(lines[0]);
            Assert.Equal("completed", completed.GetProperty("type").GetString());
            Assert.Equal("Failed", completed.GetProperty("status").GetString());
            Assert.Equal(expectedErrorCode, completed.GetProperty("error_code").GetString());
            Assert.Equal("terminal", completed.GetProperty("recovery").GetString());
        }

        private static IReadOnlyList<string> SplitNonEmptyLines(string text) =>
            text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        private static JsonElement ParseRecord(string line) => JsonDocument.Parse(line).RootElement;

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

        // Yields a build_id-bearing event, then throws a NON-cancellation exception
        // to drive SubmitCommand's final-fallback catch (UNEXPECTED_ERROR path).
        private sealed class ThrowingToolSpecClient : IToolSpecBuildClient
        {
            private readonly string _buildId;
            private readonly Exception _exception;

            public ThrowingToolSpecClient(string buildId, Exception exception)
            {
                _buildId = buildId;
                _exception = exception;
            }

#pragma warning disable CS1998
            public async IAsyncEnumerable<BuildEvent> ResolveAndBuildAsync(
                string toolName,
                string version,
                string rawSpec,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield return new BuildEvent { Kind = BuildEventKind.JobCreated, BuildId = _buildId };
                throw _exception;
            }
#pragma warning restore CS1998

            public Task CancelBuildAsync(string buildId, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
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
