using System;
using System.Collections.Generic;
using System.IO;
using Spectre.Console;

namespace NodeKit.Cli
{
    internal sealed class AnsiRecipeConsole : IRecipeConsole
    {
        private readonly IAnsiConsole _ansi;
        private readonly TextReader? _inputOverride;

        // 리뷰 지적: 예전엔 string? 필드 하나에 그냥 덮어써서, WriteHints를 연속으로
        // 여러 번 부르면(예: AuthoringModeSelector.Prompt의 8줄 명령어 목록)
        // 마지막 한 줄만 남고 나머지는 조용히 사라졌다 — PlainTextRecipeConsole은
        // 매 호출을 즉시 출력해서 이 문제가 없었으니 두 백엔드가 서로 다른 화면을
        // 보여주고 있었다. 리스트에 쌓아뒀다가 ReadLine에서 한 번에 렌더링한다.
        private readonly List<string> _pendingHints = new();

        public AnsiRecipeConsole() : this(AnsiConsole.Console) { }

        internal AnsiRecipeConsole(IAnsiConsole ansi, TextReader? inputOverride = null)
        {
            _ansi = ansi;
            _inputOverride = inputOverride;
        }

        public void BeginStep()
        {
            _pendingHints.Clear();
            try
            {
                Console.Clear();
            }
            catch (IOException)
            {
                // Console.Clear()'s only documented failure mode: the console
                // handle isn't a real interactive terminal (redirected output,
                // CI runner, etc.) even though the caller couldn't detect that
                // up front. Nothing was written yet, so skipping the clear and
                // falling through to draw the next screen normally is safe —
                // the user just doesn't get a cleared screen, no data loss.
            }

            _ansi.Write(new Rule().RuleStyle("grey dim"));
            _ansi.WriteLine();
        }

        public void WriteLine(string text = "") => _ansi.WriteLine(text);

        public void Write(string text) => _ansi.Write(text);

        public void WriteHints(string hintsLine)
        {
            _pendingHints.Add(hintsLine);
        }

        public string? ReadLine()
        {
            if (_pendingHints.Count > 0)
            {
                _ansi.WriteLine();
                _ansi.Write(new Rule().RuleStyle("grey dim"));
                foreach (var hint in _pendingHints)
                {
                    _ansi.MarkupLine($"[dim]{Markup.Escape(hint)}[/]");
                }

                _ansi.WriteLine();
                _pendingHints.Clear();
            }
            _ansi.Markup("[cyan]>[/] ");
            return (_inputOverride ?? Console.In).ReadLine();
        }
    }
}
