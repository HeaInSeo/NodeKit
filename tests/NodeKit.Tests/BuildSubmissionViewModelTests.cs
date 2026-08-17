using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using NodeKit.Grpc;
using NodeKit.UI.ViewModels;
using Xunit;

namespace NodeKit.Tests
{
    /// <summary>
    /// External-review follow-up: BuildSubmissionViewModel (the GUI's
    /// counterpart to SubmitCommand's cancellation handling) had no test
    /// coverage at all, and had drifted from two fixes the CLI already
    /// carries -- both in its best-effort server-cancel path.
    /// </summary>
    public class BuildSubmissionViewModelTests
    {
        // Bug: CancelServerBuildBestEffort used to call CancelBuildAsync with
        // CancellationToken.None -- if the server never responds, the call
        // (awaited both by the build-supersede path and by Dispose) hung
        // forever. Mirrors SubmitCommandTests
        // .Submit_ServerCancelRpcHangs_StillReturnsWithinOwnTimeout exactly.
        [Fact]
        public async Task CancelServerBuildBestEffort_ServerNeverResponds_StillReturnsWithinOwnTimeout()
        {
            var client = new HangingCancelToolSpecClient();

            var task = BuildSubmissionViewModel.CancelServerBuildBestEffort(client, "build-123");
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

            Assert.Same(task, completed);
        }

        [Fact]
        public async Task CancelServerBuildBestEffort_EmptyBuildId_DoesNotCallCancelBuildAsync()
        {
            var client = new RecordingToolSpecClient();

            await BuildSubmissionViewModel.CancelServerBuildBestEffort(client, string.Empty);

            Assert.Empty(client.CancelledBuildIds);
        }

        [Fact]
        public async Task CancelServerBuildBestEffort_CancelRpcThrows_IsSwallowed()
        {
            var client = new ThrowingToolSpecClient();

            // Must not throw -- this is explicitly a best-effort notification.
            await BuildSubmissionViewModel.CancelServerBuildBestEffort(client, "build-123");
        }

        // Bug: MainWindow's build-submit loop used to catch only
        // OperationCanceledException for the "a new build superseded this
        // one" case -- gRPC layers don't reliably surface cancellation as
        // that exact type (see GrpcToolSpecClientWireTests on the CLI side),
        // so a normal supersede could fall through to the generic failure
        // handler and flash a spurious "빌드 실패" panel.
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void IsCancellationShaped_OperationCanceledException_ReturnsTrue(bool useDerivedType)
        {
            Exception ex = useDerivedType
                ? new TaskCanceledException()
                : new OperationCanceledException();

            Assert.True(BuildSubmissionViewModel.IsCancellationShaped(ex));
        }

        [Fact]
        public void IsCancellationShaped_RpcExceptionCancelled_ReturnsTrue()
        {
            var ex = new RpcException(new Status(StatusCode.Cancelled, "stream cancelled"));

            Assert.True(BuildSubmissionViewModel.IsCancellationShaped(ex));
        }

        [Fact]
        public void IsCancellationShaped_RpcExceptionUnknown_ReturnsFalse()
        {
            // The CLI's own regression: a cancelled in-process call can
            // surface as RpcException(Unknown, "Exception was thrown by
            // handler.") instead of Cancelled -- that shape must NOT be
            // treated as cancellation (it needs its own catch-all handling).
            var ex = new RpcException(new Status(StatusCode.Unknown, "Exception was thrown by handler."));

            Assert.False(BuildSubmissionViewModel.IsCancellationShaped(ex));
        }

        [Fact]
        public void IsCancellationShaped_GenericException_ReturnsFalse()
        {
            Assert.False(BuildSubmissionViewModel.IsCancellationShaped(new InvalidOperationException()));
        }

        private sealed class HangingCancelToolSpecClient : IToolSpecBuildClient
        {
#pragma warning disable CS1998, IDE0060
            public async IAsyncEnumerable<BuildEvent> ResolveAndBuildAsync(
                string toolName,
                string version,
                string rawSpec,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield break;
            }
#pragma warning restore CS1998, IDE0060

            public Task CancelBuildAsync(string buildId, CancellationToken cancellationToken = default) =>
                Task.Delay(Timeout.Infinite, cancellationToken);
        }

        private sealed class RecordingToolSpecClient : IToolSpecBuildClient
        {
            public List<string> CancelledBuildIds { get; } = new();

#pragma warning disable CS1998, IDE0060
            public async IAsyncEnumerable<BuildEvent> ResolveAndBuildAsync(
                string toolName,
                string version,
                string rawSpec,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield break;
            }
#pragma warning restore CS1998, IDE0060

            public Task CancelBuildAsync(string buildId, CancellationToken cancellationToken = default)
            {
                CancelledBuildIds.Add(buildId);
                return Task.CompletedTask;
            }
        }

        private sealed class ThrowingToolSpecClient : IToolSpecBuildClient
        {
#pragma warning disable CS1998, IDE0060
            public async IAsyncEnumerable<BuildEvent> ResolveAndBuildAsync(
                string toolName,
                string version,
                string rawSpec,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield break;
            }
#pragma warning restore CS1998, IDE0060

            public Task CancelBuildAsync(string buildId, CancellationToken cancellationToken = default) =>
                throw new RpcException(new Status(StatusCode.Unavailable, "server gone"));
        }
    }
}
