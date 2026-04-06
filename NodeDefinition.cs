using System;
using System.Collections.Generic;

namespace NodeKit
{
    /// <summary>
    /// 노드 템플릿 — 관리자가 dry-run으로 검증한 K8s Job 정의.
    /// 레지스트리에 등록된 이미지, 쉘 스크립트, 확정된 포트 목록을 보유한다.
    /// </summary>
    public class NodeDefinition
    {
        public Guid DefinitionId { get; set; } = Guid.NewGuid();

        /// <summary>표시 이름 (예: "BWA-MEM2", "GATK HaplotypeCaller").</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>컨테이너 이미지 (예: "registry.example.com/bwa-mem2:2.2.1").</summary>
        public string Image { get; set; } = string.Empty;

        /// <summary>실행할 쉘 스크립트.</summary>
        public string Script { get; set; } = string.Empty;

        /// <summary>dry-run으로 확정된 포트 목록 (입력 + 출력 포함).</summary>
        public List<NodePort> Ports { get; set; } = new();

        public IEnumerable<NodePort> InputPorts
        {
            get
            {
                foreach (var port in Ports)
                {
                    if (port.Direction == PortDirection.Input)
                    {
                        yield return port;
                    }
                }
            }
        }

        public IEnumerable<NodePort> OutputPorts
        {
            get
            {
                foreach (var port in Ports)
                {
                    if (port.Direction == PortDirection.Output)
                    {
                        yield return port;
                    }
                }
            }
        }
    }
}
