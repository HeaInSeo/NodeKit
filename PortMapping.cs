using System;

namespace NodeKit
{
    /// <summary>
    /// 연결선 위의 단일 포트 매핑.
    /// 부모 노드의 출력 포트 하나와 자식 노드의 입력 포트 하나를 연결한다.
    /// </summary>
    public class PortMapping
    {
        public Guid SourcePortId { get; set; }

        public Guid TargetPortId { get; set; }

        public string SourcePortName { get; set; } = string.Empty;

        public string TargetPortName { get; set; } = string.Empty;
    }
}
