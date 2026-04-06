using System;

namespace NodeKit
{
    /// <summary>포트 방향 (입력 / 출력).</summary>
    public enum PortDirection
    {
        Input,
        Output,
    }

    /// <summary>
    /// 노드의 단일 포트 정의.
    /// dry-run 결과로 확정된 입출력 포트 하나를 나타낸다.
    /// </summary>
    public class NodePort
    {
        public Guid PortId { get; set; } = Guid.NewGuid();

        /// <summary>포트 이름 (예: "input.fastq", "output.bam").</summary>
        public string Name { get; set; } = string.Empty;

        public PortDirection Direction { get; set; }
    }
}
