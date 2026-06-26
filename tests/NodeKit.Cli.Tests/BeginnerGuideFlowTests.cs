using System;
using System.IO;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// Transcript tests for BeginnerGuideFlow (Sections 8.2–14 of the UX v0.9.2 doc).
    /// Drives CliApp.Run with mode "1" (GuidedBeginner) and verifies per-clue
    /// sub-flow behavior, pre-population, and the safe-exit path.
    /// </summary>
    public class BeginnerGuideFlowTests : IDisposable
    {
        private const string ContainerImageRef =
            "quay.io/biocontainers/bwa:0.7.17--h7132678_9";

        private const string Digest =
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private const string ContainerWithDigest = ContainerImageRef + "@" + Digest;

        private const string BaseImageWithDigest =
            "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private readonly string _workDir = Path.Combine(
            Path.GetTempPath(), "nodekit-beginner-guide-tests-" + Guid.NewGuid());

        private static readonly NoOpCancellationSource NoCancellation = new();

        public BeginnerGuideFlowTests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
        }

        // ── Safe-exit paths (아무것도 모름) ─────────────────────────────────────

        [Fact]
        public void NoClue_ExitsWithCode0WithoutFile()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",  // GuidedBeginner
                "7",  // 잘 모르겠다 → NoClue flow
                "5",  // 저장하지 않고 종료 → return null
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(0, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("단서가 부족합니다", stdout);
        }

        [Fact]
        public void ToolName_ThenNoClue_ExitsWithCode0WithoutFile()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",    // GuidedBeginner
                "1",    // clue: 도구 이름만 알고 있다
                "bwa",  // 도구 이름
                "6",    // 아무것도 모른다 → NoClue flow
                "5",    // 저장하지 않고 종료
            };

            var exitCode = RunCli(outPath, transcript, out _, out _);

            Assert.Equal(0, exitCode);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void NoClue_InvalidInputThenExit_RepromptsAndExits()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",    // GuidedBeginner
                "7",    // 잘 모르겠다 → NoClue flow
                "9",    // invalid — re-prompt
                "5",    // 저장하지 않고 종료
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(0, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("1–5 중에서 선택", stdout);
        }

        // ── Install command clue ────────────────────────────────────────────────

        [Fact]
        public void InstallCommand_ParsedCommand_PrePopulatesAndSavesRecipe()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",   // GuidedBeginner
                "2",   // 설치 명령
                "conda install -c bioconda bwa=0.7.17=h5bf99c6_8 -y",
                "1",   // use understood values
                // RunFieldLoop:
                "bwa-mem", "0.7.17", "run.sh",
                BaseImageWithDigest,  // ImageRef (BaseImage for Package method)
                // Packages: pre-filled & completed → skipped
                // Channels: pre-filled & completed → skipped
                // PackageEngine: Defaulted → skipped
                "reads", "1", "",
                "bam", "1", "",
            };

            var exitCode = RunCli(outPath, transcript, out _, out var stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);
            Assert.True(File.Exists(outPath));

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"PackageEngine\": \"conda\"", json);
            Assert.Contains("bwa=0.7.17=h5bf99c6_8", json);
            Assert.Contains("bioconda", json);
        }

        [Fact]
        public void InstallCommand_FailedCommand_CanSwitchToManualPackage()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",            // GuidedBeginner
                "2",            // 설치 명령
                "pip install bwa==0.7.17",  // Failed
                "1",            // package 방식으로 계속 → no pre-population
                // RunFieldLoop (all Package fields must be filled):
                "bwa-mem", "0.7.17", "run.sh",
                BaseImageWithDigest,         // ImageRef
                "bwa=0.7.17=h5bf99c6_8", "", // Packages
                "bioconda", "",              // Channels
                "",                          // PackageEngine (Defaulted — skipped)
                "reads", "1", "",
                "bam", "1", "",
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            Assert.Contains("자동으로 이해하지 못했습니다", stdout);
        }

        [Fact]
        public void InstallCommand_PartiallyParsed_ShowsMissingFields()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",   // GuidedBeginner
                "2",   // 설치 명령
                "conda install bwa=0.7.17=h5bf99c6_8",  // PartiallyParsed: no channel
                "1",   // use understood values (channels missing → RunFieldLoop asks)
                // RunFieldLoop (Channels missing → will be asked):
                "bwa-mem", "0.7.17", "run.sh",
                BaseImageWithDigest,
                // Packages: pre-filled
                "bioconda", "",  // Channels: NOT pre-filled, must be entered
                // PackageEngine: Defaulted → skipped
                "reads", "1", "",
                "bam", "1", "",
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            Assert.Contains("추가로 필요한 값", stdout);
            Assert.Contains("Channels", stdout);
        }

        [Fact]
        public void InstallCommand_SwitchMethod_GoesBackToCluePicker()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",    // GuidedBeginner
                "2",    // 설치 명령
                "pip install bwa",  // Failed
                "3",    // 다른 작성 방식 → back to clue picker
                // Clue picker again:
                "3",    // container
                ContainerWithDigest,
                "bwa-mem", "0.7.17", "run.sh",
                "",     // Command (optional)
                "reads", "1", "",
                "bam", "1", "",
            };

            var exitCode = RunCli(outPath, transcript, out _, out _);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"BioContainer\"", json);
        }

        // ── Container image clue ──────────────────────────────────────────────

        [Fact]
        public void ContainerClue_WithEmbeddedDigest_SavesValidRecipe()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",                // GuidedBeginner
                "3",                // container image
                ContainerWithDigest, // embedded digest → Normalized
                // RunFieldLoop (ImageRef + ImageDigest pre-filled):
                "bwa-mem", "0.7.17", "run.sh",
                "",                 // Command (optional)
                "reads", "1", "",
                "bam", "1", "",
            };

            var exitCode = RunCli(outPath, transcript, out _, out var stderr);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);
            Assert.True(File.Exists(outPath));

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"BioContainer\"", json);
            Assert.Contains(ContainerImageRef, json);
        }

        [Fact]
        public void ContainerClue_NoDigest_ThenReenter_SavesValidRecipe()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",                    // GuidedBeginner
                "3",                    // container image
                ContainerImageRef,      // no digest → MissingDigest
                "1",                    // re-enter with digest
                ContainerWithDigest,    // now has digest → Normalized
                // RunFieldLoop:
                "bwa-mem", "0.7.17", "run.sh",
                "",                     // Command (optional)
                "reads", "1", "",
                "bam", "1", "",
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            Assert.Contains("digest가 없습니다", stdout);

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"BioContainer\"", json);
        }

        [Fact]
        public void ContainerClue_NoDigest_ThenSeparateDigest_SavesValidRecipe()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",                // GuidedBeginner
                "3",                // container image
                ContainerImageRef,  // no digest
                "2",                // input ImageDigest separately
                Digest,             // separate digest
                // RunFieldLoop:
                "bwa-mem", "0.7.17", "run.sh",
                "",                 // Command
                "reads", "1", "",
                "bam", "1", "",
            };

            var exitCode = RunCli(outPath, transcript, out _, out _);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"BioContainer\"", json);
        }

        [Fact]
        public void ContainerClue_MissingDigest_SwitchMethod_GoesBackToCluePicker()
        {
            // Verifies that [3] 다른 작성 방식 from MissingDigest screen loops back to clue picker.
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",                // GuidedBeginner
                "3",                // container image
                ContainerImageRef,  // no digest → MissingDigest
                "3",                // 다른 작성 방식 → back to clue picker
                "7",                // 잘 모르겠다 → NoClue
                "5",                // exit
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(0, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("digest가 없습니다", stdout);
        }

        // ── Source clue ───────────────────────────────────────────────────────

        [Fact]
        public void SourceClue_WithChecksum_SavesValidRecipe()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",  // GuidedBeginner
                "4",  // source
                "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                // RunFieldLoop (SourceUri + SourceChecksum pre-filled):
                "bwa-mem", "0.7.17", "run.sh",
                BaseImageWithDigest,  // ImageRef (BaseImage for Source method)
                // SourceUri: pre-filled
                // SourceChecksum: pre-filled
                "make", "make install", "",  // SourceBuildCommands
                "",                          // BuildDependencies (Recommended → skipped)
                "reads", "1", "",
                "bam", "1", "",
            };

            var exitCode = RunCli(outPath, transcript, out _, out _);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"SourceBuild\"", json);
        }

        // ── Dockerfile clue ───────────────────────────────────────────────────

        [Fact]
        public void DockerfileClue_AcceptWarning_SavesValidRecipe()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",           // GuidedBeginner
                "5",           // Dockerfile
                "./Dockerfile",
                "y",           // confirm warning
                // RunFieldLoop (DockerfilePath pre-filled by BeginnerGuideFlow):
                "bwa-mem", "0.7.17", "run.sh",
                BaseImageWithDigest,           // ImageRef (BaseImage for Dockerfile method)
                // DockerfilePath: pre-filled → skipped
                $"FROM {BaseImageWithDigest}", // DockerfileContent (Required — still asked)
                "",                            // BuildContext (Defaulted → skipped)
                "reads", "1", "",
                "bam", "1", "",
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            Assert.Contains("Dockerfile fallback", stdout);
            var json = File.ReadAllText(outPath);
            Assert.Contains("\"BuildKind\": \"DockerfileFallback\"", json);
        }

        [Fact]
        public void DockerfileClue_RejectWarning_GoesBackToCluePicker()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",           // GuidedBeginner
                "5",           // Dockerfile
                "./Dockerfile",
                "N",           // reject warning → back to clue picker
                "7",           // 잘 모르겠다 → NoClue flow
                "5",           // exit
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(0, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("Dockerfile fallback", stdout);
        }

        // ── Mirror clue ───────────────────────────────────────────────────────

        [Fact]
        public void MirrorClue_SavesValidRecipe()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",  // GuidedBeginner
                "6",  // internal mirror
                "https://mirror.internal/conda",
                // RunFieldLoop (MirrorUri pre-filled):
                "bwa-mem", "0.7.17", "run.sh",
                BaseImageWithDigest,            // ImageRef (BaseImage for Mirror method)
                // MirrorUri: pre-filled
                "bwa=0.7.17=h5bf99c6_8", "",   // Packages
                "",                              // MirrorKind (Optional → skip)
                "reads", "1", "",
                "bam", "1", "",
            };

            var exitCode = RunCli(outPath, transcript, out _, out _);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
            var json = File.ReadAllText(outPath);
            Assert.Contains("mirror.internal", json);
        }

        // ── Clue picker validation ────────────────────────────────────────────

        [Fact]
        public void InvalidClueChoice_RepromptsUntilValid()
        {
            var outPath = Path.Combine(_workDir, "recipe.json");
            var transcript = new[]
            {
                "1",    // GuidedBeginner
                "8",    // invalid → re-prompt
                "abc",  // invalid → re-prompt
                "7",    // 잘 모르겠다
                "5",    // exit
            };

            var exitCode = RunCli(outPath, transcript, out var stdout, out _);

            Assert.Equal(0, exitCode);
            Assert.False(File.Exists(outPath));
            Assert.Contains("1–7 중에서 선택", stdout);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static int RunCli(string outPath, string[] transcript, out string stdout, out string stderr)
        {
            var stdoutWriter = new StringWriter();
            var stderrWriter = new StringWriter();
            var exitCode = CliApp.Run(
                new[] { "recipe", "create", outPath },
                new StringReader(string.Join("\n", transcript)),
                stdoutWriter,
                stderrWriter);
            stdout = stdoutWriter.ToString();
            stderr = stderrWriter.ToString();
            return exitCode;
        }

        private sealed class NoOpCancellationSource : IRecipeCreateCancellationSource
        {
            public bool IsCancellationRequested => false;
        }
    }
}
