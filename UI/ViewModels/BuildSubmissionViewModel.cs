using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Authoring;
using NodeKit.Grpc;
using ReactiveUI;

namespace NodeKit.UI.ViewModels
{
    /// <summary>
    /// Owns the GrpcToolSpecClient lifecycle and in-flight build tracking for
    /// the Avalonia GUI's submit flow (Sprint 7 Task 1: migration off legacy
    /// BuildAndRegister/IBuildClient, docs/NODEKIT_CLI_FIRST_SPRINT_PLAN.md).
    /// WatchToolBuild is an observation stream only, so — unlike legacy
    /// BuildAndRegister — cancelling it client-side does not stop the
    /// server-side build. This tracks the in-flight build ID so a
    /// superseding SubmitAsync call or Dispose can best-effort call
    /// CancelBuildAsync (same reasoning as the CLI's
    /// SubmitCommand.CancelServerBuildBestEffort).
    /// </summary>
    internal sealed class BuildSubmissionViewModel : ReactiveObject, IDisposable
    {
        private GrpcToolSpecClient? _client;
        private string? _clientAddress;
        private CancellationTokenSource? _buildCts;
        private string? _lastSubmittedBuildId;
        private bool _disposed;

        public async IAsyncEnumerable<BuildEvent> SubmitAsync(ToolDefinition definition, string address)
        {
            if (_buildCts is not null)
            {
                // 새 빌드가 이전 빌드를 대체한다 — 클라이언트 스트림만 끊는 게
                // 아니라 서버 빌드도 실제로 멈추도록 best-effort 취소를 시도한다.
                await _buildCts.CancelAsync().ConfigureAwait(false);
                await CancelServerBuildBestEffort(GetClient(address), _lastSubmittedBuildId).ConfigureAwait(false);
            }

            _lastSubmittedBuildId = null;
            _buildCts = new CancellationTokenSource();
            var cts = _buildCts;

            var rawSpec = ToolSpecRawSpecFactory.Build(definition);
            var client = GetClient(address);

#pragma warning disable CA2007 // IAsyncEnumerable does not support ConfigureAwait directly
            await foreach (var ev in EnumerateEvents(client, definition, rawSpec, cts.Token))
#pragma warning restore CA2007
            {
                if (!string.IsNullOrEmpty(ev.BuildId))
                {
                    _lastSubmittedBuildId = ev.BuildId;
                }

                yield return ev;
            }
        }

        /// <summary>
        /// 설정이 바뀌었을 때(예: NodeVault 주소 변경) 캐시된 클라이언트를
        /// 버린다 — 다음 SubmitAsync 호출이 새 주소로 재연결한다.
        /// </summary>
        public void InvalidateClient()
        {
            _client?.Dispose();
            _client = null;
            _clientAddress = null;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _buildCts?.Cancel();
            _buildCts?.Dispose();

            if (_lastSubmittedBuildId is { } buildId && _client is { } client)
            {
                // Best-effort: 호출자가 완료를 기다리지 않는다 — 취소 RPC가
                // 끝난 뒤에(성공/실패 무관) 채널을 닫는다. CA2025는 client가
                // 다른 경로로 먼저 Dispose될 가능성을 우려하지만, 이 인스턴스는
                // 이 클래스에서만 소유/정리되므로 안전하다.
#pragma warning disable CA2025
                _ = CancelServerBuildBestEffort(client, buildId).ContinueWith(
                    _ => client.Dispose(),
                    TaskScheduler.Default);
#pragma warning restore CA2025
            }
            else
            {
                _client?.Dispose();
            }

            _disposed = true;
        }

        private static async IAsyncEnumerable<BuildEvent> EnumerateEvents(
            GrpcToolSpecClient client,
            ToolDefinition definition,
            string rawSpec,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
#pragma warning disable CA2007 // IAsyncEnumerable does not support ConfigureAwait directly
            await foreach (var ev in client.ResolveAndBuildAsync(definition.Name, definition.Version, rawSpec, cancellationToken))
#pragma warning restore CA2007
            {
                yield return ev;
            }
        }

        // 클라이언트 취소는 로컬 스트림만 끊을 뿐 서버 빌드를 멈추지 않는다 —
        // CancelBuildAsync를 명시적으로 호출해야 서버가 실제로 빌드를 중단한다.
        // nodekit submit(SubmitCommand.CancelServerBuildBestEffort)과 동일한 이유로
        // 동일한 패턴을 적용한다. 실패해도 새 빌드/Dispose를 막지 않는다.
        private static async Task CancelServerBuildBestEffort(GrpcToolSpecClient client, string? buildId)
        {
            if (string.IsNullOrEmpty(buildId))
            {
                return;
            }

#pragma warning disable CA1031 // best-effort — 실패해도 계속 진행해야 한다
            try
            {
                await client.CancelBuildAsync(buildId).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
#pragma warning restore CA1031
        }

        private GrpcToolSpecClient GetClient(string address)
        {
            if (_client == null || !string.Equals(_clientAddress, address, StringComparison.Ordinal))
            {
                _client?.Dispose();
                _client = new GrpcToolSpecClient(address);
                _clientAddress = address;
            }

            return _client;
        }
    }
}
