using System.Threading;

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

        /// <summary>
        /// 저장 중 AtomicFileWriter가 교체 직전에 확인하는 token. 저장 도중
        /// 신호를 받을 수 있는 source(Ctrl+C)는 실제 token으로 재정의한다.
        /// </summary>
        CancellationToken Token => IsCancellationRequested ? new CancellationToken(canceled: true) : CancellationToken.None;
    }
}
