using System;
using Spectre.Console;

namespace NodeKit.Cli
{
    internal sealed class AnsiRecipeConsole : IRecipeConsole
    {
        private readonly IAnsiConsole _ansi;
        private string? _pendingHints;

        public AnsiRecipeConsole() : this(AnsiConsole.Console) { }

        internal AnsiRecipeConsole(IAnsiConsole ansi)
        {
            _ansi = ansi;
        }

        public void BeginStep()
        {
            _pendingHints = null;
            try { Console.Clear(); }
            catch (Exception) { }
            _ansi.Write(new Rule().RuleStyle("grey dim"));
            _ansi.WriteLine();
        }

        public void WriteLine(string text = "") => _ansi.WriteLine(text);

        public void Write(string text) => _ansi.Write(text);

        public void WriteHints(string hintsLine)
        {
            _pendingHints = hintsLine;
        }

        public string? ReadLine()
        {
            if (_pendingHints != null)
            {
                _ansi.WriteLine();
                _ansi.Write(new Rule($"[dim]{Markup.Escape(_pendingHints)}[/]").RuleStyle("grey"));
                _ansi.WriteLine();
                _pendingHints = null;
            }
            _ansi.Markup("[cyan]>[/] ");
            return Console.ReadLine();
        }
    }
}
