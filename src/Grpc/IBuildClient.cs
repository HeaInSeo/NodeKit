using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NodeKit.Grpc
{
    /// <summary>NodeVault BuildService gRPC 클라이언트 인터페이스.</summary>
    internal interface IBuildClient
    {
        /// <summary>
        /// BuildRequest를 NodeVault에 전송하고 빌드 이벤트 스트림을 수신한다.
        /// </summary>
        IAsyncEnumerable<BuildEvent> BuildAndRegisterAsync(
            BuildRequest request,
            CancellationToken cancellationToken = default);
    }
}
