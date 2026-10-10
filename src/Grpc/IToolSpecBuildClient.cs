using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NodeKit.Grpc
{
    /// <summary>
    /// NodeVault 신규 경로: ResolveToolSpec → SubmitToolBuild → WatchToolBuild.
    /// Shared by the CLI (SubmitCommand) and the Avalonia GUI (MainWindow) —
    /// this is the one production submit path since Sprint 7 (legacy
    /// BuildAndRegister/IBuildClient removed).
    /// </summary>
    internal interface IToolSpecBuildClient
    {
        IAsyncEnumerable<BuildEvent> ResolveAndBuildAsync(
            string toolName,
            string version,
            string rawSpec,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// staged 제출: 호출자가 request ID를 정하고, ResolveToolSpec 결과(전체
        /// resolved basis)를 SubmitToolBuild 전에 durable 저장할 수 있다.
        /// 기본 구현은 옵션이 비어 있을 때만 기존 오버로드로 위임하고, 그 외에는
        /// 옵션을 조용히 무시하지 않도록 NotSupportedException을 던진다.
        /// </summary>
        IAsyncEnumerable<BuildEvent> ResolveAndBuildAsync(
            string toolName,
            string version,
            string rawSpec,
            ToolSpecBuildOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (!options.IsDefault)
            {
                throw new NotSupportedException(
                    $"{GetType().Name}은 caller request ID/저장 seam이 있는 staged 제출을 지원하지 않습니다.");
            }

            return ResolveAndBuildAsync(toolName, version, rawSpec, cancellationToken);
        }

        /// <summary>
        /// 이미 받은 build ID 하나만 WatchToolBuild로 관찰한다(S2-03 재진입). ResolveToolSpec/
        /// SubmitToolBuild는 보내지 않는다. Watch 요청에는 build ID만 있고 서버 event offset이나
        /// cursor가 없으므로, 서버가 다시 보낸 관측을 그대로 돌려줄 뿐 이전 관찰을 이어받지 않는다.
        /// 기본 구현은 이 경로를 지원하지 않는 client가 조용히 다른 RPC로 대신하지 않도록
        /// NotSupportedException을 던진다.
        /// </summary>
        IAsyncEnumerable<BuildEvent> WatchBuildAsync(string buildId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException($"{GetType().Name}은 build ID만으로 관찰하는 재진입을 지원하지 않습니다.");

        /// <summary>
        /// 사용자 취소(Ctrl-C) 시 서버에 실제 빌드 중단을 요청한다. 이 호출이 없으면
        /// 클라이언트만 스트림을 끊고 서버 빌드는 그대로 계속 진행된다.
        /// </summary>
        Task CancelBuildAsync(string buildId, CancellationToken cancellationToken = default);
    }
}
