using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using NodeKit.Authoring;
using NodeKit.Policy;
using NodeKit.Validation;

namespace NodeKit.UI
{
    public partial class MainWindow : Window
    {
        private readonly ImageUriValidator _imageUriValidator = new();
        private readonly PackageVersionValidator _packageVersionValidator = new();
        private readonly WasmPolicyChecker? _policyChecker;

        private bool _l1Passed;

        public MainWindow()
        {
            InitializeComponent();

            _policyChecker = TryLoadPolicyChecker();

            AddInputButton.Click += (_, _) => AddIoRow(InputRowsPanel);
            AddOutputButton.Click += (_, _) => AddIoRow(OutputRowsPanel);
            ValidateButton.Click += OnValidateClicked;
            SendBuildButton.Click += OnSendBuildClicked;

            // 초기 행 1개씩
            AddIoRow(InputRowsPanel);
            AddIoRow(OutputRowsPanel);
        }

        private static WasmPolicyChecker? TryLoadPolicyChecker()
        {
            try
            {
                var wasmPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "assets",
                    "policy",
                    "dockguard.wasm");

                if (!File.Exists(wasmPath))
                {
                    return null;
                }

                var bytes = File.ReadAllBytes(wasmPath);
                return new WasmPolicyChecker(new PolicyBundle(bytes, $"local:{Path.GetFileName(wasmPath)}"));
            }
#pragma warning disable CA1031
            catch
            {
                return null;
            }
#pragma warning restore CA1031
        }

        // ─── I/O 동적 행 관리 ─────────────────────────────────────────────────

        /// <summary>
        /// 이름 입력 TextBox + 삭제 버튼으로 구성된 I/O 행을 panel에 추가한다.
        /// </summary>
        private static void AddIoRow(StackPanel panel)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,4,Auto"),
            };

            var nameBox = new TextBox
            {
                Watermark = "파일 이름 (예: input.fastq.gz)",
                Background = new SolidColorBrush(Color.Parse("#1e1d2e")),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.Parse("#333")),
                Padding = new Avalonia.Thickness(8, 4),
            };
            Grid.SetColumn(nameBox, 0);

            var removeButton = new Button
            {
                Content = "×",
                Background = new SolidColorBrush(Color.Parse("#2a1a1a")),
                Foreground = new SolidColorBrush(Color.Parse("#c0392b")),
                BorderBrush = new SolidColorBrush(Color.Parse("#c0392b")),
                Padding = new Avalonia.Thickness(8, 4),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(removeButton, 2);

            removeButton.Click += (_, _) =>
            {
                // 마지막 1개는 삭제하지 않음 (최소 1행 유지)
                if (panel.Children.Count > 1)
                {
                    panel.Children.Remove(row);
                }
                else
                {
                    nameBox.Text = string.Empty;
                }
            };

            row.Children.Add(nameBox);
            row.Children.Add(removeButton);
            panel.Children.Add(row);
        }

        /// <summary>
        /// panel에 있는 모든 I/O 행에서 비어 있지 않은 이름을 수집한다.
        /// </summary>
        private static List<string> CollectIoNames(StackPanel panel)
        {
            var names = new List<string>();
            foreach (var child in panel.Children)
            {
                if (child is not Grid row)
                {
                    continue;
                }

                var textBox = row.Children.OfType<TextBox>().FirstOrDefault();
                var name = textBox?.Text?.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    names.Add(name);
                }
            }

            return names;
        }

        // ─── 검증 및 빌드 ─────────────────────────────────────────────────────

        private void OnValidateClicked(object? sender, RoutedEventArgs e)
        {
            _l1Passed = false;
            SendBuildButton.IsEnabled = false;
            StatusBar.Text = "검증 중...";

            var definition = BuildDefinitionFromForm();

            // L1-Static: 이미지 URI + 패키지 버전 검증
            var staticResults = new[]
            {
                _imageUriValidator.Validate(definition),
                _packageVersionValidator.Validate(definition),
            };
            var staticCombined = ValidationResult.Combine(staticResults);

            // L1-Policy: DockGuard WASM 검증 (Dockerfile이 있는 경우)
            var allViolations = new List<ValidationViolation>(staticCombined.Violations);
            if (!string.IsNullOrWhiteSpace(definition.DockerfileContent))
            {
                if (_policyChecker != null)
                {
                    var policyResult = _policyChecker.Check(definition.DockerfileContent);
                    foreach (var pv in policyResult.Violations)
                    {
                        allViolations.Add(new ValidationViolation(pv.RuleId, pv.Message, "DockerfileContent"));
                    }
                }
                else
                {
                    allViolations.Add(new ValidationViolation(
                        "POLICY-UNAVAIL",
                        "DockGuard 정책 번들을 로드할 수 없습니다 (assets/policy/dockguard.wasm 확인 필요).",
                        "DockerfileContent"));
                }
            }

            if (allViolations.Count == 0)
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
                ViolationsList.ItemsSource = allViolations
                    .Select(v => $"[{v.RuleId}] {v.Message}")
                    .ToList();
                StatusBar.Text = $"L1 검증 실패 — {allViolations.Count}개 위반";
            }
        }

        private void OnSendBuildClicked(object? sender, RoutedEventArgs e)
        {
            if (!_l1Passed)
            {
                return;
            }

            // Phase 0: 연결 대상(NodeForge)이 아직 없으므로 placeholder
            StatusBar.Text = "BuildRequest 생성 완료 — NodeForge gRPC 연결은 Phase 2에서 구성됩니다.";
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
                Inputs = CollectIoNames(InputRowsPanel)
                    .Select(n => new ToolInput { Name = n }).ToList(),
                Outputs = CollectIoNames(OutputRowsPanel)
                    .Select(n => new ToolOutput { Name = n }).ToList(),
            };
        }
    }
}
