using System;
using System.Threading;

namespace NodeKit.Cli
{
    /// <summary>
    /// Wires Console.CancelKeyPress to IRecipeCreateCancellationSource — see
    /// design doc Section 18.4. Sets e.Cancel = true so the process survives
    /// Ctrl+C instead of terminating immediately, letting
    /// RecipeCreateInteractiveRunner map the signal onto the same
    /// RecipeCreateCancelledException / exit code 130 path as /cancel.
    /// Token lets an atomic save that is already running see a Ctrl+C before
    /// its replace step.
    /// </summary>
    internal sealed class ConsoleCancelKeyCancellationSource : IRecipeCreateCancellationSource, IDisposable
    {
        private readonly CancellationTokenSource _cts = new();

        public ConsoleCancelKeyCancellationSource()
        {
            Console.CancelKeyPress += OnCancelKeyPress;
        }

        public bool IsCancellationRequested => _cts.IsCancellationRequested;

        public CancellationToken Token => _cts.Token;

        public void Dispose()
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
            _cts.Dispose();
        }

        private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Dispose와 동시에 들어온 신호 — 이미 흐름이 끝났으므로 할 일이 없다.
            }
        }
    }
}
