using System;
using System.Collections.Generic;
using NodeKit.Authoring;

namespace NodeKit.Grpc
{
    /// <summary>
    /// NodeKit → NodeForge gRPC 전송 단위.
    /// L1 검증 통과 후 BuildRequestFactory.FromToolDefinition()으로 생성된다.
    /// </summary>
    public class BuildRequest
    {
        public string RequestId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>원본 ToolDefinition ID (추적용).</summary>
        public Guid ToolDefinitionId { get; set; }

        public string ToolName { get; set; } = string.Empty;

        /// <summary>툴 버전 (예: "0.7.17").</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>베이스 이미지 URI (digest 포함).</summary>
        public string ImageUri { get; set; } = string.Empty;

        /// <summary>Dockerfile 내용.</summary>
        public string DockerfileContent { get; set; } = string.Empty;

        /// <summary>실행 스크립트.</summary>
        public string Script { get; set; } = string.Empty;

        /// <summary>
        /// K8s 런타임 커맨드 오버라이드 (선택).
        /// Dockerfile CMD를 대체한다. ENTRYPOINT가 아님.
        /// </summary>
        public List<string> Command { get; set; } = new();

        /// <summary>환경 스펙 파일 내용.</summary>
        public string EnvironmentSpec { get; set; } = string.Empty;

        /// <summary>Named input 포트 목록 (역할, 형식, shape 포함).</summary>
        public List<ToolInput> Inputs { get; set; } = new();

        /// <summary>Named output 포트 목록.</summary>
        public List<ToolOutput> Outputs { get; set; } = new();

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
