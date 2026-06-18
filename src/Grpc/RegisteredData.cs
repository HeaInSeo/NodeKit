using System;

namespace NodeKit.Grpc
{
    /// <summary>NodeKit 내부 표현의 등록된 참조 데이터 정보.</summary>
    internal sealed class RegisteredData
    {
        public string CasHash { get; init; } = string.Empty;

        public string DataName { get; init; } = string.Empty;

        public string Version { get; init; } = string.Empty;

        public string StableRef { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public string Format { get; init; } = string.Empty;

        public string SourceUri { get; init; } = string.Empty;

        public string Checksum { get; init; } = string.Empty;

        public string StorageUri { get; init; } = string.Empty;

        public string DisplayLabel { get; init; } = string.Empty;

        public string DisplayCategory { get; init; } = string.Empty;

        /// <summary>운영 의도 축. NodeVault 명시적 호출만 변경.</summary>
        public string LifecyclePhase { get; init; } = string.Empty;

        /// <summary>Harbor 정합성 관찰 축. reconcile loop만 변경.</summary>
        public string IntegrityHealth { get; init; } = string.Empty;

        public DateTimeOffset RegisteredAt { get; init; }
    }
}
