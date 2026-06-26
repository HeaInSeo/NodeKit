using System;
using System.IO;

namespace NodeKit.Cli
{
    internal static class RecipeCreateScreen
    {
        public static void ClearForNewStep(TextWriter stdout)
        {
            if (ReferenceEquals(stdout, Console.Out) && !Console.IsOutputRedirected)
            {
                try
                {
                    Console.Clear();
                    return;
                }
                catch (IOException)
                {
                }
            }

            stdout.WriteLine();
            stdout.WriteLine("------------------------------------------------------------");
            stdout.WriteLine();
        }
    }
}
