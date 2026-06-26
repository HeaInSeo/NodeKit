namespace NodeKit.Cli
{
    internal static class RecipeCreateEscapeCommands
    {
        public static bool IsCancel(string input)
        {
            var trimmed = input.Trim();
            return trimmed is "/cancel" or "/quit" or "/exit";
        }

        public static void ThrowIfCancel(string input)
        {
            if (IsCancel(input))
            {
                throw new RecipeCreateCancelledException();
            }
        }
    }
}
