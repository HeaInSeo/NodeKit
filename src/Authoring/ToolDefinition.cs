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

        /// <summary>Tool 표시 이름.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 컨테이너 이미지 URI.
        /// 반드시 digest(@sha256:...)를 포함해야 한다.
        /// 예: "registry.example.com/bwa-mem2:2.2.1@sha256:abc123..."
        /// </summary>
        public string ImageUri { get; set; } = string.Empty;

        /// <summary>
        /// Dockerfile 내용.
        /// DockGuard 정책 검사 대상.
        /// </summary>
        public string DockerfileContent { get; set; } = string.Empty;

        /// <summary>실행할 쉘 스크립트.</summary>
        public string Script { get; set; } = string.Empty;

        /// <summary>Named input 목록.</summary>
        public List<ToolInput> Inputs { get; set; } = new();

        /// <summary>Named output 목록.</summary>
        public List<ToolOutput> Outputs { get; set; } = new();

        /// <summary>
        /// 환경 스펙 파일 내용 (conda environment.yml, requirements.txt 등).
        /// 패키지 버전 고정 검증 대상.
        /// </summary>
        public string EnvironmentSpec { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
