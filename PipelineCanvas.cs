using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace NodeKit
{
    /// <summary>
    /// 파이프라인 노드를 배치하는 캔버스.
    /// Location 프로퍼티로 각 PipelineNodeControl의 위치를 결정한다.
    /// </summary>
    public class PipelineCanvas : Canvas
    {
        protected override Size ArrangeOverride(Size finalSize)
        {
            foreach (var child in Children)
            {
                if (child is PipelineNodeControl node)
                {
                    var loc = node.Location;
                    child.Arrange(new Rect(loc.X, loc.Y, child.DesiredSize.Width, child.DesiredSize.Height));
                }
                else
                {
                    child.Arrange(new Rect(child.DesiredSize));
                }
            }

            return finalSize;
        }
    }
}
