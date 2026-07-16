using System;
using System.IO;
using Spectre.Console;

namespace NodeKit.Cli
{
    internal sealed class AnsiRecipeConsole : IRecipeConsole
    {
        private readonly IAnsiConsole _ansi;
        private readonly TextReader? _inputOverride;
        private string? _pendingHints;

        public AnsiRecipeConsole() : this(AnsiConsole.Console) { }

        internal AnsiRecipeConsole(IAnsiConsole ansi, TextReader? inputOverride = null)
        {
            _ansi = ansi;
            _inputOverride = inputOverride;
        }

        public void BeginStep()
        {
            _pendingHints = null;
#pragma warning disable CA1031 // Console.Clear() can throw for many unrelated reasons (redirected/non-interactive output) — never worth failing the wizard over.
            try { Console.Clear(); }
            catch (Exception) { }
#pragma warning restore CA1031
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
            return (_inputOverride ?? Console.In).ReadLine();
        }
    }
}
