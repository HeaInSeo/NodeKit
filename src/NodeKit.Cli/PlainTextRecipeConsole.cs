using System;
using System.Collections.Generic;
using System.IO;

namespace NodeKit.Cli
{
    internal sealed class PlainTextRecipeConsole : IRecipeConsole
    {
        private readonly TextReader _stdin;
        private readonly TextWriter _stdout;

        // WriteCarriedLine으로 쓴 줄 — 다음 BeginStep이 화면을 실제로 지웠을 때 다시 쓴다.
        private readonly List<string> _carriedLines = new();
        private readonly Func<bool> _tryClearScreen;

        public PlainTextRecipeConsole(TextReader stdin, TextWriter stdout) : this(stdin, stdout, null) { }

        internal PlainTextRecipeConsole(TextReader stdin, TextWriter stdout, Func<bool>? tryClearScreen)
        {
            _stdin = stdin;
            _stdout = stdout;
            _tryClearScreen = tryClearScreen ?? TryClearRealConsole;
        }

        public void BeginStep()
        {
            if (_tryClearScreen())
            {
                foreach (var line in _carriedLines)
                {
                    _stdout.WriteLine(line);
                }

                _carriedLines.Clear();
                return;
            }

            _carriedLines.Clear();
            _stdout.WriteLine();
            _stdout.WriteLine("------------------------------------------------------------");
            _stdout.WriteLine();
        }

        public void WriteLine(string text = "") => _stdout.WriteLine(text);

        public void WriteCarriedLine(string text = "")
        {
            _stdout.WriteLine(text);
            _carriedLines.Add(text);
        }

        public void Write(string text) => _stdout.Write(text);
        public void WriteHints(string hintsLine) => _stdout.WriteLine(hintsLine);
        public string? ReadLine() => _stdin.ReadLine();

        private bool TryClearRealConsole()
        {
            if (!ReferenceEquals(_stdout, Console.Out) || Console.IsOutputRedirected)
            {
                return false;
            }

            try
            {
                Console.Clear();
                return true;
            }
            catch (IOException)
            {
                // Console.Clear() throws when the console handle is unusual
                // (e.g. a non-interactive terminal) even though
                // IsOutputRedirected reported false — fall through to the
                // text separator instead of crashing the wizard.
                return false;
            }
        }
    }
}
