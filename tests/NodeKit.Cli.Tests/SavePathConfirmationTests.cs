using System;
using System.IO;
using NodeKit.Cli;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// Tests for U4 — optional output path and save-path confirmation step in
    /// RecipeCreateFlow step 9. When no explicit file path is provided, the wizard
    /// prompts for a save path at the end with a sensible default derived from
    /// ToolName and ToolVersion.
    /// </summary>
    public class SavePathConfirmationTests : IDisposable
    {
        private static readonly IRecipeCreateCancellationSource _noCancellation =
            new FixedCancellationSource(false);

        private readonly string _workDir =
            Path.Join(Path.GetTempPath(), "nodekit-savepath-tests-" + Guid.NewGuid());

        private readonly IDisposable _resolveClientOverride =
            ResolveRecipeClientTestOverride.Use(NullResolveRecipeClient.Instance);

        public SavePathConfirmationTests()
        {
            Directory.CreateDirectory(_workDir);
        }

        public void Dispose()
        {
            Directory.Delete(_workDir, recursive: true);
            _resolveClientOverride.Dispose();
        }

        // ── CLI arg parsing ───────────────────────────────────────────────────────

        [Fact]
        public void CliApp_RecipeCreate_WithoutPath_IsAccepted()
        {
            // "nodekit recipe create" (no path) → wizard starts, then needs transcript
            // to avoid hanging; we give /cancel to exit immediately.
            var transcript = new[]
            {
                "1",        // GuidedBeginner
                "/cancel",  // immediately cancel at clue picker
                "1",        // confirm cancel
            };
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(
                new[] { "recipe", "create" },
                new StringReader(string.Join("\n", transcript)),
                stdout,
                stderr);

            // Cancelled exit = 130 (not exit 2 which would mean argument error).
            Assert.Equal(130, exitCode);
        }

        [Fact]
        public void CliApp_RecipeCreate_FlagWithoutPath_IsAccepted()
        {
            // "nodekit recipe create --method package" (path comes from flags only)
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = CliApp.Run(
                new[] { "recipe", "create", "--non-interactive", "--method", "package" },
                new StringReader(string.Empty),
                stdout,
                stderr);

            // Non-interactive mode without path → error 2.
            Assert.Equal(2, exitCode);
            Assert.Contains("경로", stderr.ToString());
        }

        // ── Path confirmation UI: null outPath ────────────────────────────────────

        [Fact]
        public void NullOutPath_Step9_PromptsForPath_EnterUsesDefault()
        {
            // When outPath is null, step 9 shows the default path and user presses Enter.
            // Default = {_workDir}/{ToolName}-{ToolVersion}.json (from document fields).
            var transcript = new[]
            {
                "2",    // 빠른 설정 모드
                "n", "n", "n", "y", "n", "n",
                "",     // accept package
                "bioconda", "",
                "0", // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh",
                "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                "bwa=0.7.17=h5bf99c6_8", "",
                // PackageEngine: Defaulted → null→"" skip
                // Port selection: null→"" skip both
                // Step 9: path prompt
                "",     // Enter → use default path (bwa-mem-0.7.17.json in current dir)
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = RecipeCreateInteractiveRunner.Run(
                null,   // no path hint
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                _noCancellation,
                resolveClient: NullResolveRecipeClient.Instance);

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr.ToString());
            // Default file is created in working directory
            var defaultFile = Path.Join(Directory.GetCurrentDirectory(), "bwa-mem-0.7.17.json");
            Assert.True(File.Exists(defaultFile));
            File.Delete(defaultFile); // cleanup
        }

        [Fact]
        public void NullOutPath_Step9_PromptsForPath_UserEntersCustomPath()
        {
            var customPath = Path.Join(_workDir, "custom-output.json");
            var transcript = new[]
            {
                "2",    // 빠른 설정 모드
                "n", "n", "n", "y", "n", "n",
                "",     // accept package
                "bioconda", "",
                "0", // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh",
                "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                "bwa=0.7.17=h5bf99c6_8", "",
                // Step 9: path prompt
                customPath, // user enters full custom path
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = RecipeCreateInteractiveRunner.Run(
                null,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                _noCancellation,
                resolveClient: NullResolveRecipeClient.Instance);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(customPath));
        }

        [Fact]
        public void DirectoryHint_Step9_DefaultsToToolNameVersionInThatDir()
        {
            // outPath is an existing directory → default path = dir/toolName-version.json
            var transcript = new[]
            {
                "2",    // 빠른 설정 모드
                "n", "n", "n", "y", "n", "n",
                "",     // accept package
                "bioconda", "",
                "0", // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh",
                "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                "bwa=0.7.17=h5bf99c6_8", "",
                // Step 9: Enter → use default (bwa-mem-0.7.17.json under _workDir)
                "",
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = RecipeCreateInteractiveRunner.Run(
                _workDir,   // directory hint
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                _noCancellation,
                resolveClient: NullResolveRecipeClient.Instance);

            Assert.Equal(0, exitCode);
            var expectedPath = Path.Join(_workDir, "bwa-mem-0.7.17.json");
            Assert.True(File.Exists(expectedPath));
        }

        [Fact]
        public void ExplicitFilePath_Step9_ShowsSaveOrRestartChoice()
        {
            // Existing behavior: explicit outPath → [y/n] save or restart.
            var outPath = Path.Join(_workDir, "recipe.json");
            var transcript = new[]
            {
                "2",    // 빠른 설정 모드
                "n", "n", "n", "y", "n", "n",
                "",     // accept package
                "bioconda", "",
                "0", // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh",
                "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                "bwa=0.7.17=h5bf99c6_8", "",
                // Step 9: [y/n] because explicit path
                "",     // Enter = save
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = RecipeCreateInteractiveRunner.Run(
                outPath,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                _noCancellation,
                resolveClient: NullResolveRecipeClient.Instance);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
        }

        [Fact]
        public void NullOutPath_Step9_NRestarts_ThenSavesOnSecondAttempt()
        {
            // If user enters "n" at the path prompt, the wizard restarts.
            // On the second attempt, enter a concrete path to save.
            var outPath = Path.Join(_workDir, "recipe-second.json");
            const string imageRef =
                "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

            var transcript = new[]
            {
                // First attempt (QuickSetup, Package)
                "2", "n", "n", "n", "y", "n", "n",
                "",             // accept package
                "bioconda", "", // channels
                "0",             // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh", imageRef,
                "bwa=0.7.17=h5bf99c6_8", "",
                "n",    // Step 9 path prompt: restart

                // Second attempt (QuickSetup, Package again)
                "2", "n", "n", "n", "y", "n", "n",
                "",             // accept package
                "bioconda", "", // channels
                "0",             // 기반 이미지: 직접 입력
                "bwa-mem", "0.7.17", "run.sh", imageRef,
                "bwa=0.7.17=h5bf99c6_8", "",
                outPath,  // Step 9: save to concrete path
            };

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = RecipeCreateInteractiveRunner.Run(
                null,
                new RecipeCreateOptions(null, null, false, false, Array.Empty<(string, string)>(), null),
                new PlainTextRecipeConsole(new StringReader(string.Join("\n", transcript)), stdout),
                stderr,
                _noCancellation,
                resolveClient: NullResolveRecipeClient.Instance);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outPath));
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private sealed class FixedCancellationSource : IRecipeCreateCancellationSource
        {
            private readonly bool _cancelled;

            public FixedCancellationSource(bool cancelled) => _cancelled = cancelled;

            public bool IsCancellationRequested => _cancelled;
        }
    }
}
