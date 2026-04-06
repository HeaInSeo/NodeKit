using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace NodeKit
{
    /// <summary>
    /// 두 노드를 잇는 연결선 + 포트 수 레이블.
    /// PipelineCanvas 위에 직접 놓이며, 노드가 이동하면 InvalidateVisual()로 재그림한다.
    /// </summary>
    public class ConnectionLine : Control
    {
        public PipelineNodeControl? SourceNode { get; set; }

        public PipelineNodeControl? TargetNode { get; set; }

        public int PortCount { get; set; }

        public Guid ConnectionId { get; set; }

        private static readonly Pen LinePen = new(Brushes.DodgerBlue, 2);
        private static readonly IBrush LabelBg = new SolidColorBrush(Color.Parse("#CC1c1b2b"));

        public ConnectionLine()
        {
            IsHitTestVisible = true;
        }

        public override void Render(DrawingContext context)
        {
            if (SourceNode == null || TargetNode == null)
            {
                return;
            }

            var src = GetRightAnchor(SourceNode);
            var tgt = GetLeftAnchor(TargetNode);

            // 연결선
            context.DrawLine(LinePen, src, tgt);

            // 포트 수 레이블 (선 중앙)
            var mid = new Point((src.X + tgt.X) / 2, (src.Y + tgt.Y) / 2);
            var label = PortCount.ToString();
            var ft = new FormattedText(
                label,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                11,
                Brushes.White);

            var labelRect = new Rect(mid.X - 10, mid.Y - 9, 20, 18);
            context.FillRectangle(LabelBg, labelRect, 4);
            context.DrawText(ft, new Point(mid.X - ft.Width / 2, mid.Y - ft.Height / 2));
        }

        private static Point GetRightAnchor(PipelineNodeControl node)
        {
            var loc = node.Location;
            return new Point(loc.X + node.Bounds.Width, loc.Y + node.Bounds.Height / 2);
        }

        private static Point GetLeftAnchor(PipelineNodeControl node)
        {
            var loc = node.Location;
            return new Point(loc.X, loc.Y + node.Bounds.Height / 2);
        }
    }
}
