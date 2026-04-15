using System;
using System.Collections.Generic;

namespace NodeKit.Authoring
{
    /// <summary>
    /// NodeKit authoring 단계의 참조 데이터(reference data) 초안 모델.
    /// DataRegisterRequest 생성 전까지의 작업 중인 정의다.
    /// RegisteredDataDefinition(NodeVault 확정 객체)과 구분:
    /// 이 객체는 NodeVault에 전달 전 초안이다.
    /// </summary>
    public class DataDefinition
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>참조 데이터 이름 (예: "hg38-reference").</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 데이터 버전 (예: "2024-01").
        /// stable_ref = Name@Version 로 조립된다.
        /// </summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>데이터 설명.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 데이터 형식 (예: "FASTA", "VCF", "BED", "GTF").
        /// </summary>
        public string Format { get; set; } = string.Empty;

        /// <summary>
        /// 원본 데이터 소스 URI (추적용, 예: "https://ftp.ncbi.nlm.nih.gov/...").
        /// </summary>
        public string SourceUri { get; set; } = string.Empty;

        /// <summary>
        /// 데이터 artifact의 SHA256 체크섬.
        /// 재현성 보장: 파이프라인 실행 시 체크섬 불일치는 L1에서 차단.
        /// </summary>
        public string Checksum { get; set; } = string.Empty;

        // ── UI 팔레트 표시 메타데이터 (display 섹션) ──────────────────────────

        /// <summary>UI 카드 제목 (예: "Human GRCh38 Reference").</summary>
        public string DisplayLabel { get; set; } = string.Empty;

        /// <summary>툴팁 설명.</summary>
        public string DisplayDescription { get; set; } = string.Empty;

        /// <summary>팔레트 카테고리 (예: "Reference Genome").</summary>
        public string DisplayCategory { get; set; } = string.Empty;

        /// <summary>검색 태그 (쉼표 구분 입력 → List로 변환).</summary>
        public List<string> DisplayTags { get; set; } = new();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
