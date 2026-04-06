using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace NodeKit
{
    /// <summary>
    /// 캔버스 위에 배치되는 파이프라인 노드 컨트롤.
    /// NodeDefinition 기반으로 이름과 포트 수를 표시한다.
    /// </summary>
    public class PipelineNodeControl : ContentControl
    {
        #region Styled Properties

        public static readonly StyledProperty<string> NodeNameProperty =
            AvaloniaProperty.Register<PipelineNodeControl, string>(nameof(NodeName), string.Empty);

        public static readonly StyledProperty<int> InputPortCountProperty =
            AvaloniaProperty.Register<PipelineNodeControl, int>(nameof(InputPortCount));

        public static readonly StyledProperty<int> OutputPortCountProperty =
            AvaloniaProperty.Register<PipelineNodeControl, int>(nameof(OutputPortCount));

        public static readonly StyledProperty<bool> IsSelectedProperty =
            AvaloniaProperty.Register<PipelineNodeControl, bool>(nameof(IsSelected));

        public static readonly StyledProperty<Point> LocationProperty =
            AvaloniaProperty.Register<PipelineNodeControl, Point>(nameof(Location));

        public string NodeName
        {
            get => GetValue(NodeNameProperty);
            set => SetValue(NodeNameProperty, value);
        }

        public int InputPortCount
        {
            get => GetValue(InputPortCountProperty);
            set => SetValue(InputPortCountProperty, value);
        }

        public int OutputPortCount
        {
            get => GetValue(OutputPortCountProperty);
            set => SetValue(OutputPortCountProperty, value);
        }

        public bool IsSelected
        {
            get => GetValue(IsSelectedProperty);
            set => SetValue(IsSelectedProperty, value);
        }

        public Point Location
        {
            get => GetValue(LocationProperty);
            set => SetValue(LocationProperty, value);
        }

        #endregion

        #region Fields

        private bool _isDragging;
        private Point _dragStart;
        private Point _locationAtDragStart;
        private const int GridSize = 15;

        #endregion

        static PipelineNodeControl()
        {
            LocationProperty.Changed.AddClassHandler<PipelineNodeControl>((ctrl, _) =>
                (ctrl.GetVisualParent() as Avalonia.Layout.Layoutable)?.InvalidateArrange());
        }

        public PipelineNodeControl()
        {
            Focusable = true;
        }

        /// <summary>NodeDefinition으로 이 컨트롤을 초기화한다.</summary>
        public void ApplyDefinition(NodeDefinition definition)
        {
            NodeName = definition.Name;
            InputPortCount = definition.Ports.Count(p => p.Direction == PortDirection.Input);
            OutputPortCount = definition.Ports.Count(p => p.Direction == PortDirection.Output);
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                var canvas = this.GetVisualParent() as Canvas;
                if (canvas == null)
                {
                    return;
                }

                e.Pointer.Capture(this);
                _isDragging = true;
                _dragStart = e.GetPosition(canvas);
                _locationAtDragStart = Location;
                IsSelected = true;
                e.Handled = true;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            if (!_isDragging || !Equals(e.Pointer.Captured))
            {
                return;
            }

            var canvas = this.GetVisualParent() as Canvas;
            if (canvas == null)
            {
                return;
            }

            var current = e.GetPosition(canvas);
            var delta = current - _dragStart;

            var snappedX = SnapToGrid(_locationAtDragStart.X + delta.X);
            var snappedY = SnapToGrid(_locationAtDragStart.Y + delta.Y);
            Location = new Point(snappedX, snappedY);

            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            if (_isDragging && Equals(e.Pointer.Captured))
            {
                e.Pointer.Capture(null);
                _isDragging = false;
                e.Handled = true;
            }
        }

        private static double SnapToGrid(double value)
            => System.Math.Round(value / GridSize) * GridSize;
    }
}
