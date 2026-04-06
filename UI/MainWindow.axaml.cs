using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NodeKit.Authoring;
using NodeKit.Validation;

namespace NodeKit.UI
{
    public partial class MainWindow : Window
    {
        private readonly ImageUriValidator _imageUriValidator = new();
        private readonly PackageVersionValidator _packageVersionValidator = new();

        private bool _l1Passed;

        public MainWindow()
        {
            InitializeComponent();
            ValidateButton.Click += OnValidateClicked;
            SendBuildButton.Click += OnSendBuildClicked;
        }

        private void OnValidateClicked(object? sender, RoutedEventArgs e)
        {
            _l1Passed = false;
            SendBuildButton.IsEnabled = false;

            var definition = BuildDefinitionFromForm();
            var results = new[]
            {
                _imageUriValidator.Validate(definition),
                _packageVersionValidator.Validate(definition),
            };

            var combined = ValidationResult.Combine(results);

            if (combined.IsValid)
            {
                ValidationResultPanel.IsVisible = false;
                ValidationPassPanel.IsVisible = true;
                _l1Passed = true;
                SendBuildButton.IsEnabled = true;
                StatusBar.Text = "L1 검증 통과 — 빌드 요청 준비 완료";
            }
            else
            {
                ValidationPassPanel.IsVisible = false;
                ValidationResultPanel.IsVisible = true;
                ViolationsList.ItemsSource = combined.Violations
                    .Select(v => $"[{v.RuleId}] {v.Message}")
                    .ToList();
                StatusBar.Text = $"L1 검증 실패 — {combined.Violations.Count}개 위반";
            }
        }

        private void OnSendBuildClicked(object? sender, RoutedEventArgs e)
        {
            if (!_l1Passed)
            {
                return;
            }

            // Phase 0: 연결 대상(NodeForge)이 아직 없으므로 placeholder
            StatusBar.Text = "BuildRequest 생성 완료 — NodeForge gRPC 연결은 Phase 0-4에서 구성됩니다.";
        }

        private ToolDefinition BuildDefinitionFromForm()
        {
            return new ToolDefinition
            {
                Name = ToolNameBox.Text ?? string.Empty,
                ImageUri = ImageUriBox.Text ?? string.Empty,
                DockerfileContent = DockerfileBox.Text ?? string.Empty,
                Script = ScriptBox.Text ?? string.Empty,
                EnvironmentSpec = EnvSpecBox.Text ?? string.Empty,
                Inputs = ParseLines(InputsBox.Text)
                    .Select(n => new ToolInput { Name = n }).ToList(),
                Outputs = ParseLines(OutputsBox.Text)
                    .Select(n => new ToolOutput { Name = n }).ToList(),
            };
        }

        private static IEnumerable<string> ParseLines(string? text)
            => (text ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0);
    }
}
