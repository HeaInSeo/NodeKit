using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NodeKit.Grpc
{
    /// <summary>NodeForge BuildService gRPC 클라이언트 인터페이스.</summary>
    public interface IBuildClient
    {
        /// <summary>
        /// BuildRequest를 NodeForge에 전송하고 빌드 이벤트 스트림을 수신한다.
        /// </summary>
        IAsyncEnumerable<BuildEvent> BuildAndRegisterAsync(
            BuildRequest request,
            CancellationToken cancellationToken = default);
    }

    /// <summary>NodeForge로부터 수신하는 빌드 진행 이벤트.</summary>
    public class BuildEvent
    {
        public BuildEventKind Kind { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>DIGEST_ACQUIRED 이벤트 시 채워지는 이미지 digest.</summary>
        public string Digest { get; set; } = string.Empty;
    }

    public enum BuildEventKind
    {
        Log,
        JobCreated,
        JobRunning,
        RegistryPushSucceeded,
        DigestAcquired,
        Succeeded,
        Failed,
    }
}
