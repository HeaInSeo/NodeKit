using System.Collections.Generic;

namespace NodeKit.Policy
{
    /// <summary>
    /// Dockerfile의 단일 명령어를 OPA WASM 정책이 기대하는 형식으로 표현.
    /// 필드명은 DockGuard .rego의 normalize() 헬퍼와 일치해야 한다.
    /// </summary>
    internal class DockerfileInstruction
    {
        /// <summary>명령어 이름 (대문자, 예: "FROM", "RUN", "COPY").</summary>
        public string Cmd { get; set; } = string.Empty;

        /// <summary>
        /// 인자 목록. Moby parser AST의 Value 필드에 해당.
        /// FROM의 경우: ["ubuntu:22.04"] 또는 ["ubuntu:22.04", "AS", "builder"]
        /// </summary>
        public List<string> Value { get; set; } = new();

        /// <summary>
        /// 원본 라인 전체 (공백 제거 전).
        /// DFM002 regex 검사에서 사용.
        /// </summary>
        public string Raw { get; set; } = string.Empty;
    }
}
