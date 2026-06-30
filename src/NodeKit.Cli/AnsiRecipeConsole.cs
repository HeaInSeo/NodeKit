using System;
using Spectre.Console;

namespace NodeKit.Cli
{
    internal sealed class AnsiRecipeConsole : IRecipeConsole
    {
        private readonly IAnsiConsole _ansi;

        public AnsiRecipeConsole() : this(AnsiConsole.Console) { }

        internal AnsiRecipeConsole(IAnsiConsole ansi)
        {
            _ansi = ansi;
        }

        public void BeginStep()
        {
            try { Console.Clear(); }
            catch (Exception) { }
        }

        public void WriteLine(string text = "") => _ansi.WriteLine(text);

        public void Write(string text) => _ansi.Write(text);

        public void WriteHints(string hintsLine)
        {
            _ansi.WriteLine();
            _ansi.Write(new Rule($"[dim]{Markup.Escape(hintsLine)}[/]").RuleStyle("grey"));
            _ansi.WriteLine();
        }

        public string? ReadLine()
        {
            _ansi.Markup("[cyan]>[/] ");
            return Console.ReadLine();
        }
    }
}
