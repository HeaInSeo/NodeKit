using System;
using System.IO;
using NodeKit.Authoring.Recipes;
using Spectre.Console.Testing;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// S1-01-C03 직접 선택 요약(Codex P2 4235855878): Present가 요약을 쓰고 돌아오면 입력 없이 다음
    /// 화면의 BeginStep이 화면을 지운다. 실제 터미널에서 요약이 읽히기 전에 사라지지 않도록, 화면을
    /// 지운 경우 새 화면 맨 위에 요약이 다시 보여야 한다. StringWriter 출력은 지워지지 않으므로 기존
    /// 흐름 테스트로는 이 동작을 볼 수 없다 — 여기서는 clear seam으로 "실제로 지웠다"를 재현한다.
    /// </summary>
    public class ManualChoiceSummaryVisibilityTests
    {
        private static readonly string _summaryLine =
            "선택한 작성 방식: " + RecipeMethodCatalog.For(RecipeMethodId.Mirror).Label.Get("ko");

        // 추천 보류 → 바로 수동 목록. "3" = mirror.
        private static readonly RecipeMethodRecommendation _noRecommendation = new(
            null,
            "정보가 부족합니다.",
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<RecipeMethodCandidate>(),
            Array.Empty<string>());

        [Fact]
        public void Ansi_ManualChoiceSummary_IsRedrawnAfterNextStepClearsScreen()
        {
            var testConsole = new TestConsole();
            var clearedAt = -1;
            var console = new AnsiRecipeConsole(testConsole, new StringReader("3\n"), () => clearedAt = testConsole.Output.Length);

            var method = MethodRecommendationPresenter.Present(_noRecommendation, console);
            console.BeginStep();

            Assert.Equal(RecipeMethodId.Mirror, method);
            Assert.True(clearedAt >= 0, "BeginStep must call the clear seam.");
            var afterClear = testConsole.Output[clearedAt..];
            Assert.Contains(_summaryLine, afterClear, StringComparison.Ordinal);
            Assert.Contains("선택 이유: 목록에서 직접 선택했습니다.", afterClear, StringComparison.Ordinal);
            Assert.Contains("이 방식으로 만들면:", afterClear, StringComparison.Ordinal);
        }

        [Fact]
        public void Ansi_ManualChoiceSummary_IsRedrawnOnlyOnce()
        {
            var testConsole = new TestConsole();
            var clearedAt = -1;
            var console = new AnsiRecipeConsole(testConsole, new StringReader("3\n"), () => clearedAt = testConsole.Output.Length);

            MethodRecommendationPresenter.Present(_noRecommendation, console);
            console.BeginStep();
            console.BeginStep();

            Assert.DoesNotContain(_summaryLine, testConsole.Output[clearedAt..], StringComparison.Ordinal);
        }

        [Fact]
        public void Ansi_ClearFails_SummaryIsNotDuplicated()
        {
            var testConsole = new TestConsole();
            var console = new AnsiRecipeConsole(testConsole, new StringReader("3\n"), () => throw new IOException("not a terminal"));

            MethodRecommendationPresenter.Present(_noRecommendation, console);
            console.BeginStep();

            Assert.Equal(1, Occurrences(testConsole.Output, _summaryLine));
        }

        [Fact]
        public void PlainText_ScreenCleared_SummaryIsRedrawnOnNextScreenOnce()
        {
            var stdout = new StringWriter();
            var console = new PlainTextRecipeConsole(new StringReader("3\n"), stdout, () => true);

            MethodRecommendationPresenter.Present(_noRecommendation, console);
            console.BeginStep();
            console.BeginStep();

            // 첫 출력 1회(지워짐) + 다음 화면 맨 위 1회. 그 다음 화면에는 다시 나오지 않는다.
            Assert.Equal(2, Occurrences(stdout.ToString(), _summaryLine));
        }

        [Fact]
        public void PlainText_NotCleared_SummaryIsWrittenOnce()
        {
            var stdout = new StringWriter();
            var console = new PlainTextRecipeConsole(new StringReader("3\n"), stdout, () => false);

            MethodRecommendationPresenter.Present(_noRecommendation, console);
            console.BeginStep();
            console.BeginStep();

            Assert.Equal(1, Occurrences(stdout.ToString(), _summaryLine));
        }

        private static int Occurrences(string text, string value)
        {
            var count = 0;
            for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }
    }
}
