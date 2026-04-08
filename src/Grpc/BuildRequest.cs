using System;
using System.Collections.Generic;
using NodeKit.Authoring;

namespace NodeKit.Grpc
{
    /// <summary>
    /// NodeKit → NodeForge gRPC 전송 단위.
    /// L1 검증 통과 후 생성된다.
    /// Phase 0에서는 수동 DTO. proto 생성 후 교체된다.
    /// </summary>
    public class BuildRequest
    {
        public string RequestId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>원본 ToolDefinition ID (추적용).</summary>
        public Guid ToolDefinitionId { get; set; }

        public string ToolName { get; set; } = string.Empty;

        /// <summary>베이스 이미지 URI (digest 포함).</summary>
        public string ImageUri { get; set; } = string.Empty;

        /// <summary>Dockerfile 내용.</summary>
        public string DockerfileContent { get; set; } = string.Empty;

        /// <summary>실행 스크립트.</summary>
        public string Script { get; set; } = string.Empty;

        /// <summary>환경 스펙 파일 내용.</summary>
        public string EnvironmentSpec { get; set; } = string.Empty;

        /// <summary>Named input 이름 목록.</summary>
        public List<string> InputNames { get; set; } = new();

        /// <summary>Named output 이름 목록.</summary>
        public List<string> OutputNames { get; set; } = new();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
