namespace NodeKit.Cli
{
    internal interface IRecipeConsole
    {
        void BeginStep();
        void WriteLine(string text = "");
        void Write(string text);
        void WriteHints(string hintsLine);
        string? ReadLine();
    }
}
