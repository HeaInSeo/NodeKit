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

        /// <summary>NodeVault가 발급한 빌드 Job ID.</summary>
        public string BuildId { get; set; } = string.Empty;

        /// <summary>빌드 Job의 현재 상태 문자열 (예: Running, Succeeded).</summary>
        public string Status { get; set; } = string.Empty;
    }
}
