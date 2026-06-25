namespace NodeKit.Cli
{
    /// <summary>
    /// Testable seam for the Ctrl+C cancellation signal — see design doc
    /// Section 18.5. Production wiring is ConsoleCancelKeyCancellationSource;
    /// tests inject a fake to simulate Ctrl+C without a real signal.
    /// </summary>
    internal interface IRecipeCreateCancellationSource
    {
        bool IsCancellationRequested { get; }
    }
}
