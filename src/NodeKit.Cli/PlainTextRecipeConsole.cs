using System;
using System.IO;

namespace NodeKit.Cli
{
    internal sealed class PlainTextRecipeConsole : IRecipeConsole
    {
        private readonly TextReader _stdin;
        private readonly TextWriter _stdout;

        public PlainTextRecipeConsole(TextReader stdin, TextWriter stdout)
        {
            _stdin = stdin;
            _stdout = stdout;
        }

        public void BeginStep()
        {
            if (ReferenceEquals(_stdout, Console.Out) && !Console.IsOutputRedirected)
            {
                try
                {
                    Console.Clear();
                    return;
                }
                catch (IOException)
                {
                    // Console.Clear() throws when the console handle is unusual
                    // (e.g. a non-interactive terminal) even though
                    // IsOutputRedirected reported false — fall through to the
                    // text separator below instead of crashing the wizard.
                }
            }

            _stdout.WriteLine();
            _stdout.WriteLine("------------------------------------------------------------");
            _stdout.WriteLine();
        }

        public void WriteLine(string text = "") => _stdout.WriteLine(text);
        public void Write(string text) => _stdout.Write(text);
        public void WriteHints(string hintsLine) => _stdout.WriteLine(hintsLine);
        public string? ReadLine() => _stdin.ReadLine();
    }
}
