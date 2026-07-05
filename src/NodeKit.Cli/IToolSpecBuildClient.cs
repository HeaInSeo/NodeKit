using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NodeKit.Grpc;

namespace NodeKit.Cli
{
    /// <summary>
    /// NodeVault 신규 경로: ResolveToolSpec → SubmitToolBuild → WatchToolBuild.
    /// </summary>
    internal interface IToolSpecBuildClient
    {
        IAsyncEnumerable<BuildEvent> ResolveAndBuildAsync(
            string toolName,
            string version,
            string rawSpec,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 사용자 취소(Ctrl-C) 시 서버에 실제 빌드 중단을 요청한다. 이 호출이 없으면
        /// 클라이언트만 스트림을 끊고 서버 빌드는 그대로 계속 진행된다.
        /// </summary>
        Task CancelBuildAsync(string buildId, CancellationToken cancellationToken = default);
    }
}
