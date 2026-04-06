using System;
using System.Collections.Generic;

namespace NodeKit
{
    /// <summary>
    /// 두 노드 간 연결.
    /// 단일 선으로 표현되며, 포트 매핑 목록을 보유한다.
    /// 연결선 위에 포트 수(Mappings.Count)를 숫자로 표시한다.
    /// </summary>
    public class PipelineConnection
    {
        public Guid ConnectionId { get; set; } = Guid.NewGuid();

        public Guid SourceNodeId { get; set; }

        public Guid TargetNodeId { get; set; }

        /// <summary>확정된 포트 매핑 목록.</summary>
        public List<PortMapping> Mappings { get; set; } = new();

        /// <summary>연결선에 표시할 포트 수.</summary>
        public int PortCount => Mappings.Count;
    }
}
