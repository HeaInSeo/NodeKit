using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;

namespace NodeKit
{
    /// <summary>
    /// 파이프라인 모델 — 캔버스 위의 노드 인스턴스와 연결 목록.
    /// </summary>
    public class Pipeline
    {
        public List<PipelineNode> Nodes { get; } = new();

        public List<PipelineConnection> Connections { get; } = new();

        public PipelineNode AddNode(NodeDefinition definition, Point location)
        {
            var node = new PipelineNode
            {
                NodeId = Guid.NewGuid(),
                Definition = definition,
                Location = location,
            };
            Nodes.Add(node);
            return node;
        }

        /// <summary>
        /// 두 노드를 연결한다.
        /// 부모의 출력 포트 수와 자식의 입력 포트 수가 일치해야 연결이 허용된다.
        /// </summary>
        public bool TryConnect(Guid sourceNodeId, Guid targetNodeId, out PipelineConnection? connection)
        {
            connection = null;

            var source = Nodes.FirstOrDefault(n => n.NodeId == sourceNodeId);
            var target = Nodes.FirstOrDefault(n => n.NodeId == targetNodeId);

            if (source == null || target == null)
            {
                return false;
            }

            var outputPorts = source.Definition.Ports
                .Where(p => p.Direction == PortDirection.Output)
                .ToList();

            var inputPorts = target.Definition.Ports
                .Where(p => p.Direction == PortDirection.Input)
                .ToList();

            // 포트 수가 일치해야 연결 허용 (Unix 파이프 의미)
            if (outputPorts.Count != inputPorts.Count || outputPorts.Count == 0)
            {
                return false;
            }

            connection = new PipelineConnection
            {
                SourceNodeId = sourceNodeId,
                TargetNodeId = targetNodeId,
            };

            for (int i = 0; i < outputPorts.Count; i++)
            {
                connection.Mappings.Add(new PortMapping
                {
                    SourcePortId = outputPorts[i].PortId,
                    SourcePortName = outputPorts[i].Name,
                    TargetPortId = inputPorts[i].PortId,
                    TargetPortName = inputPorts[i].Name,
                });
            }

            Connections.Add(connection);
            return true;
        }
    }

    /// <summary>캔버스 위에 배치된 노드 인스턴스.</summary>
    public class PipelineNode
    {
        public Guid NodeId { get; set; } = Guid.NewGuid();

        public NodeDefinition Definition { get; set; } = new();

        public Point Location { get; set; }
    }
}
