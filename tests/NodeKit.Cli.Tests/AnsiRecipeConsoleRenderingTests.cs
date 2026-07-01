using System.IO;
using Spectre.Console.Testing;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// U1-4 regression tests: verify that the 3-zone TUI layout (top border /
    /// description / hints-then-prompt) renders correctly via AnsiRecipeConsole
    /// backed by Spectre.Console.Testing.TestConsole.
    ///
    /// These are smoke tests — they guard against structural regressions without
    /// comparing fragile ANSI escape sequences.
    /// Existing wizard flow tests remain on PlainTextRecipeConsole (unchanged).
    /// </summary>
    public class AnsiRecipeConsoleRenderingTests
    {
        [Fact]
        public void BeginStep_RendersTopSeparatorRule()
        {
            var testConsole = new TestConsole();
            var console = new AnsiRecipeConsole(testConsole);

            console.BeginStep();

            var output = testConsole.Output;
            Assert.True(
                output.Contains('─') || output.Contains('━') || output.Contains('-'),
                $"BeginStep() must render a horizontal separator rule. Actual output: {output}");
        }

        [Fact]
        public void WriteHints_DeferredUntilReadLine_AllThreeZonesPresentInOutput()
        {
            var testConsole = new TestConsole();
            var console = new AnsiRecipeConsole(testConsole, new StringReader("bwa-mem\n"));

            // Zone 1 + 2 content written before ReadLine
            console.BeginStep();
            console.WriteLine("[2 / 6]");
            console.WriteHints("/back   /cancel   /review");
            console.WriteLine("도구 이름 — 도구를 식별하는 이름입니다.");

            // Hints must NOT appear yet (deferred)
            Assert.DoesNotContain("/back", testConsole.Output);

            // ReadLine flushes pending hints, then shows ">" prompt
            console.ReadLine();

            var output = testConsole.Output;
            // Zone 1 (description): progress indicator
            Assert.Contains("[2 / 6]", output);
            // Zone 1 (description): field label
            Assert.Contains("도구 이름", output);
            // Zone 2 (hints): flushed just before prompt
            Assert.Contains("/back", output);
            // Zone 3 (input): ">" prompt marker
            Assert.Contains('>', output);
        }
    }
}
