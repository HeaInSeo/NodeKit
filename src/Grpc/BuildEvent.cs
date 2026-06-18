using System;

namespace NodeKit.Grpc
{
    /// <summary>NodeVault로부터 수신하는 빌드 진행 이벤트.</summary>
    internal class BuildEvent
    {
        public BuildEventKind Kind { get; set; }

        public string Message { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>DIGEST_ACQUIRED 이벤트 시 채워지는 이미지 digest.</summary>
        public string Digest { get; set; } = string.Empty;
    }
}
