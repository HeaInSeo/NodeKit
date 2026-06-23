using System;

namespace NodeKit.Cli
{
    internal static class Program
    {
        private static int Main(string[] args) => CliApp.Run(args, Console.Out, Console.Error);
    }
}
