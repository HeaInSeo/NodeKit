using System;

namespace NodeKit.Grpc
{
    /// <summary>NodeVault로부터 수신하는 빌드 진행 이벤트.</summary>
    internal class BuildEvent
    {
        public BuildEventKind Kind { get; set; }

        public string Message { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>DIGEST_ACQUIRED 이벤트 시 채워지는 이미지 digest (legacy BuildAndRegister 경로 전용).</summary>
        public string Digest { get; set; } = string.Empty;

        /// <summary>NodeVault가 발급한 빌드 Job ID.</summary>
        public string BuildId { get; set; } = string.Empty;

        /// <summary>빌드 Job의 현재 상태 문자열 (예: Running, Succeeded).</summary>
        public string Status { get; set; } = string.Empty;

        // WatchToolBuild(ToolSpec 경로) 전용 필드. 이 경로는 Kind가 항상 LOG이고
        // 위 Digest/DIGEST_ACQUIRED는 절대 채워지지 않는다 — NodeVault Sprint 7
        // P1a(commit 03f5025)가 buildstate.Record를 매 이벤트마다 그대로 실어
        // 보내므로 여기서 digest 등을 읽는다. legacy BuildAndRegister 경로에서는
        // 항상 빈 문자열이다.

        /// <summary>레지스트리 이미지 참조(태그 포함, digest 제외). WatchToolBuild 전용.</summary>
        public string ImageRef { get; set; } = string.Empty;

        /// <summary>이미지 digest. WatchToolBuild 전용 — legacy Digest와 별개 필드.</summary>
        public string ImageDigest { get; set; } = string.Empty;

        /// <summary>ToolSpec referrer의 OCI digest. WatchToolBuild 전용.</summary>
        public string SpecReferrerDigest { get; set; } = string.Empty;

        /// <summary>reconcile axis의 read-through 스냅샷 (예: Healthy, Partial). WatchToolBuild 전용.</summary>
        public string IntegrityHealth { get; set; } = string.Empty;
    }
}
