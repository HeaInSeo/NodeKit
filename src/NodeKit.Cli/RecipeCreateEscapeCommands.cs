namespace NodeKit.Cli
{
    internal static class RecipeCreateEscapeCommands
    {
        public static bool IsCancel(string input)
        {
            var trimmed = input.Trim();
            return trimmed is "/cancel" or "/quit" or "/exit";
        }

        public static bool IsBack(string input) => input.Trim() == "/back";

        public static void ThrowIfCancel(string input)
        {
            if (IsCancel(input))
            {
                throw new RecipeCreateCancelledException();
            }
        }

        public static void ThrowIfBack(string input)
        {
            if (IsBack(input))
            {
                throw new RecipeCreateBackRequestedException();
            }
        }

        public static void ThrowIfEscape(string input)
        {
            ThrowIfCancel(input);
            ThrowIfBack(input);
        }
    }
}
