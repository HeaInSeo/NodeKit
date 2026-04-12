using System;
using System.Collections.Generic;

namespace NodeKit.Authoring
{
    /// <summary>
    /// NodeKit authoring 단계의 Tool 초안 모델.
    /// BuildRequest 생성 전까지의 작업 중인 정의다.
    /// RegisteredToolDefinition과 구분: 이 객체는 NodeForge에 전달 전 초안이다.
    /// </summary>
    public class ToolDefinition
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Tool 이름 (예: "bwa-mem").</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 툴 버전 (예: "0.7.17"). stable_ref = Name@Version 로 조립된다.
        /// </summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// 컨테이너 이미지 URI.
        /// 반드시 digest(@sha256:...)를 포함해야 한다.
        /// </summary>
        public string ImageUri { get; set; } = string.Empty;

        /// <summary>Dockerfile 내용. DockGuard 정책 검사 대상.</summary>
        public string DockerfileContent { get; set; } = string.Empty;

        /// <summary>실행할 쉘 스크립트.</summary>
        public string Script { get; set; } = string.Empty;

        /// <summary>Named input 포트 목록.</summary>
        public List<ToolInput> Inputs { get; set; } = new();

        /// <summary>Named output 포트 목록.</summary>
        public List<ToolOutput> Outputs { get; set; } = new();

        /// <summary>
        /// 환경 스펙 파일 내용 (conda environment.yml, requirements.txt 등).
        /// 패키지 버전 고정 검증 대상.
        /// </summary>
        public string EnvironmentSpec { get; set; } = string.Empty;

        // ── UI 팔레트 표시 메타데이터 (display 섹션) ──────────────────────────

        /// <summary>UI 카드 제목 (예: "BWA-MEM 0.7.17").</summary>
        public string DisplayLabel { get; set; } = string.Empty;

        /// <summary>툴팁 설명.</summary>
        public string DisplayDescription { get; set; } = string.Empty;

        /// <summary>팔레트 카테고리 (예: "Alignment").</summary>
        public string DisplayCategory { get; set; } = string.Empty;

        /// <summary>검색 태그 (쉼표 구분 입력 → List로 변환).</summary>
        public List<string> DisplayTags { get; set; } = new();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
