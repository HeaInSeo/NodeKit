using System;

namespace NodeKit.Grpc
{
    /// <summary>NodeKit 내부 표현의 등록된 툴 정보.</summary>
    internal sealed class RegisteredTool
    {
        public string CasHash { get; init; } = string.Empty;

        public string ToolName { get; init; } = string.Empty;

        public string Version { get; init; } = string.Empty;

        public string StableRef { get; init; } = string.Empty;

        public string ImageUri { get; init; } = string.Empty;

        public string Digest { get; init; } = string.Empty;

        public string DisplayLabel { get; init; } = string.Empty;

        public string DisplayCategory { get; init; } = string.Empty;

        /// <summary>
        /// 운영 의도 축. "Pending" | "Active" | "Retracted" | "Deleted".
        /// NodeVault 명시적 호출만 변경.
        /// </summary>
        public string LifecyclePhase { get; init; } = string.Empty;

        /// <summary>
        /// Harbor 정합성 관찰 축. "Healthy" | "Partial" | "Missing" | "Unreachable" | "Orphaned".
        /// reconcile loop만 변경. Catalog 노출 결정에는 영향 없음.
        /// </summary>
        public string IntegrityHealth { get; init; } = string.Empty;

        public DateTimeOffset RegisteredAt { get; init; }
    }
}
