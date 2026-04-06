using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NodeKit
{
    public partial class MainWindow : Window
    {
        private readonly Pipeline _pipeline = new();

        // 마지막으로 선택된 노드 두 개를 추적 (연결용)
        private readonly List<PipelineNodeControl> _selected = new();

        public MainWindow()
        {
            InitializeComponent();
            WireButtons();
        }

        private void WireButtons()
        {
            AddBwaButton.Click += (_, _) => AddNode(SampleDefinitions.BwaMem2(), new Point(80, 120));
            AddGatkButton.Click += (_, _) => AddNode(SampleDefinitions.GatkHc(), new Point(360, 120));
            AddTrimButton.Click += (_, _) => AddNode(SampleDefinitions.Trimmomatic(), new Point(640, 120));
            ConnectButton.Click += OnConnectClicked;
            ClearButton.Click += (_, _) => ClearAll();
        }

        private void AddNode(NodeDefinition definition, Point location)
        {
            var model = _pipeline.AddNode(definition, location);

            var ctrl = new PipelineNodeControl();
            ctrl.ApplyDefinition(definition);
            ctrl.Location = location;

            // 노드 클릭 시 선택 목록에 추가 (최대 2개)
            ctrl.PointerPressed += (_, _) =>
            {
                if (_selected.Contains(ctrl))
                {
                    return;
                }

                if (_selected.Count >= 2)
                {
                    _selected[0].IsSelected = false;
                    _selected.RemoveAt(0);
                }

                _selected.Add(ctrl);
                ctrl.IsSelected = true;
            };

            // model NodeId를 컨트롤 태그에 보관
            ctrl.Tag = model.NodeId;

            Canvas.Children.Add(ctrl);
        }

        private void OnConnectClicked(object? sender, RoutedEventArgs e)
        {
            if (_selected.Count != 2)
            {
                return;
            }

            var sourceId = (Guid)_selected[0].Tag!;
            var targetId = (Guid)_selected[1].Tag!;

            if (_pipeline.TryConnect(sourceId, targetId, out var connection) && connection != null)
            {
                DrawConnection(_selected[0], _selected[1], connection);
            }

            foreach (var ctrl in _selected)
            {
                ctrl.IsSelected = false;
            }

            _selected.Clear();
        }

        private void DrawConnection(
            PipelineNodeControl source,
            PipelineNodeControl target,
            PipelineConnection connection)
        {
            var line = new ConnectionLine
            {
                SourceNode = source,
                TargetNode = target,
                PortCount = connection.PortCount,
                ConnectionId = connection.ConnectionId,
            };

            // 연결선은 노드보다 아래 레이어 (인덱스 0에 삽입)
            Canvas.Children.Insert(0, line);
        }

        private void ClearAll()
        {
            Canvas.Children.Clear();
            _pipeline.Nodes.Clear();
            _pipeline.Connections.Clear();
            _selected.Clear();
        }
    }
}
