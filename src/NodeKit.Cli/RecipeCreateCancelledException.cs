using System;

namespace NodeKit.Cli
{
    /// <summary>
    /// Thrown when the interactive recipe create wizard is cancelled via
    /// /cancel, /quit, or /exit — see design doc Section 17.3. Caught in
    /// RecipeCreateInteractiveRunner.Run to exit with code 130 without a
    /// stack trace and without writing a recipe.json.
    /// </summary>
    internal sealed class RecipeCreateCancelledException : Exception
    {
    }
}
