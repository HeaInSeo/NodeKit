using System;

namespace NodeKit.Cli
{
    /// <summary>
    /// Wires Console.CancelKeyPress to IRecipeCreateCancellationSource — see
    /// design doc Section 18.4. Sets e.Cancel = true so the process survives
    /// Ctrl+C instead of terminating immediately, letting
    /// RecipeCreateInteractiveRunner map the signal onto the same
    /// RecipeCreateCancelledException / exit code 130 path as /cancel.
    /// </summary>
    internal sealed class ConsoleCancelKeyCancellationSource : IRecipeCreateCancellationSource, IDisposable
    {
        private volatile bool _cancellationRequested;

        public ConsoleCancelKeyCancellationSource()
        {
            Console.CancelKeyPress += OnCancelKeyPress;
        }

        public bool IsCancellationRequested => _cancellationRequested;

        public void Dispose() => Console.CancelKeyPress -= OnCancelKeyPress;

        private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            _cancellationRequested = true;
        }
    }
}
