using System;
using System.Collections.Generic;
using NodeKit.Authoring;

namespace NodeKit.Grpc
{
    /// <summary>
    /// NodeKit → NodeVault gRPC 전송 단위 (참조 데이터 등록).
    /// L1 검증 통과 후 DataRegisterRequestFactory.FromDataDefinition()으로 생성된다.
    /// </summary>
    public class DataRegisterRequest
    {
        public string RequestId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>원본 DataDefinition ID (추적용).</summary>
        public Guid DataDefinitionId { get; set; }

        /// <summary>참조 데이터 이름 (예: "hg38-reference").</summary>
        public string DataName { get; set; } = string.Empty;

        /// <summary>데이터 버전 (예: "2024-01").</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>데이터 설명.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>데이터 형식 (예: "FASTA", "VCF", "BED").</summary>
        public string Format { get; set; } = string.Empty;

        /// <summary>원본 소스 URI.</summary>
        public string SourceUri { get; set; } = string.Empty;

        /// <summary>데이터 artifact SHA256 체크섬.</summary>
        public string Checksum { get; set; } = string.Empty;

        /// <summary>UI 팔레트 표시 레이블.</summary>
        public string DisplayLabel { get; set; } = string.Empty;

        /// <summary>UI 팔레트 표시 설명.</summary>
        public string DisplayDescription { get; set; } = string.Empty;

        /// <summary>UI 팔레트 카테고리.</summary>
        public string DisplayCategory { get; set; } = string.Empty;

        /// <summary>검색 태그 목록.</summary>
        public List<string> DisplayTags { get; set; } = new();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
