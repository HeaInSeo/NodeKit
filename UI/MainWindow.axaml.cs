using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NodeKit.Authoring;
using NodeKit.Grpc;
using NodeKit.Policy;
using NodeKit.Validation;

namespace NodeKit.UI
{
    public partial class MainWindow : Window
    {
        private readonly ImageUriValidator _imageUriValidator = new();
        private readonly PackageVersionValidator _packageVersionValidator = new();
        private readonly RequiredFieldsValidator _requiredFieldsValidator = new();
        private readonly ValidatedDefinitionState _validatedDefinitionState = new();

#pragma warning disable CA1001 // Disposed in OnWindowClosed (Window has no IDisposable)
        private WasmPolicyChecker? _policyChecker;
        private GrpcBuildClient? _buildClient;
        private GrpcToolRegistryClient? _toolRegistryClient;
        private GrpcPolicyBundleProvider? _policyProvider;
        private CancellationTokenSource? _buildCts;
#pragma warning restore CA1001
        private string? _buildClientAddress;
        private string? _toolRegistryClientAddress;
        private string? _policyProviderAddress;

        public MainWindow()
        {
            InitializeComponent();

            _policyChecker = TryLoadPolicyChecker();

            AddInputButton.Click += (_, _) => AddInputRow(InputRowsPanel);
            AddOutputButton.Click += (_, _) => AddOutputRow(OutputRowsPanel);
            ValidateButton.Click += OnValidateClicked;
            SendBuildButton.Click += OnSendBuildClicked;
            Closed += OnWindowClosed;

            NavAuthoringButton.Click += (_, _) => ShowPanel(AuthoringPanel);
            NavToolListButton.Click += (_, _) => { ShowPanel(ToolListPanel); _ = LoadToolListAsync(); };
            NavPolicyButton.Click += (_, _) => { ShowPanel(PolicyPanel); _ = LoadPolicyListAsync(); };
            RefreshToolListButton.Click += (_, _) => _ = LoadToolListAsync();
            RefreshPolicyListButton.Click += (_, _) => _ = LoadPolicyListAsync();
            ReloadBundleButton.Click += OnReloadBundleClicked;
            RegisterValidationInvalidationHandlers();

            // 초기 행 1개씩
            AddInputRow(InputRowsPanel);
            AddOutputRow(OutputRowsPanel);
        }

        private void ShowPanel(Avalonia.Controls.Control target)
        {
            AuthoringPanel.IsVisible = target == AuthoringPanel;
            ToolListPanel.IsVisible = target == ToolListPanel;
            PolicyPanel.IsVisible = target == PolicyPanel;
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

        /// <summary>Input 포트 행 추가: name / role / format / shape / required / ×</summary>
        private void AddInputRow(StackPanel panel)
        {
            // columns: name(2*) gap role(1.5*) gap format(1.2*) gap shape(60) gap ×
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("2*,4,1.5*,4,1.2*,4,60,4,Auto"),
            };

            var nameBox   = MakePortTextBox("이름 (예: reads)", 0, row);
            var roleBox   = MakePortTextBox("역할 (예: sample-fastq)", 2, row);
            var formatBox = MakePortTextBox("형식 (예: fastq)", 4, row);

            var shapeBox = new ComboBox
            {
                ItemsSource = new[] { "single", "pair" },
                SelectedIndex = 0,
                Background = new SolidColorBrush(Color.Parse("#1e1d2e")),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.Parse("#333")),
            };
            Grid.SetColumn(shapeBox, 6);
            row.Children.Add(shapeBox);

            AddRemoveButton(row, 8, panel, () => nameBox.Text = string.Empty);

            panel.Children.Add(row);
            InvalidateValidationState();
        }

        /// <summary>Output 포트 행 추가: name / role / format / shape / class / ×</summary>
        private void AddOutputRow(StackPanel panel)
        {
            // columns: name(2*) gap role(1.5*) gap format(1.2*) gap shape(60) gap class(60) gap ×
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("2*,4,1.5*,4,1.2*,4,60,4,60,4,Auto"),
            };

            var nameBox   = MakePortTextBox("이름 (예: aligned_bam)", 0, row);
            var roleBox   = MakePortTextBox("역할 (예: aligned-bam)", 2, row);
            var formatBox = MakePortTextBox("형식 (예: bam)", 4, row);

            var shapeBox = new ComboBox
            {
                ItemsSource = new[] { "single", "pair" },
                SelectedIndex = 0,
                Background = new SolidColorBrush(Color.Parse("#1e1d2e")),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.Parse("#333")),
            };
            Grid.SetColumn(shapeBox, 6);
            row.Children.Add(shapeBox);

            var classBox = new ComboBox
            {
                ItemsSource = new[] { "primary", "secondary" },
                SelectedIndex = 0,
                Background = new SolidColorBrush(Color.Parse("#1e1d2e")),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.Parse("#333")),
            };
            Grid.SetColumn(classBox, 8);
            row.Children.Add(classBox);

            AddRemoveButton(row, 10, panel, () => nameBox.Text = string.Empty);

            panel.Children.Add(row);
            InvalidateValidationState();
        }

        private TextBox MakePortTextBox(string watermark, int column, Grid parent)
        {
            var box = new TextBox
            {
                Watermark = watermark,
                Background = new SolidColorBrush(Color.Parse("#1e1d2e")),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.Parse("#333")),
                Padding = new Avalonia.Thickness(6, 4),
            };
            Grid.SetColumn(box, column);
            box.TextChanged += (_, _) => InvalidateValidationState();
            parent.Children.Add(box);
            return box;
        }

        private void AddRemoveButton(Grid row, int column, StackPanel panel, Action clearFirst)
        {
            var btn = new Button
            {
                Content = "×",
                Background = new SolidColorBrush(Color.Parse("#2a1a1a")),
                Foreground = new SolidColorBrush(Color.Parse("#c0392b")),
                BorderBrush = new SolidColorBrush(Color.Parse("#c0392b")),
                Padding = new Avalonia.Thickness(8, 4),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(btn, column);
            btn.Click += (_, _) =>
            {
                if (panel.Children.Count > 1)
                {
                    panel.Children.Remove(row);
                }
                else
                {
                    clearFirst();
                }

                InvalidateValidationState();
            };
            row.Children.Add(btn);
        }

        /// <summary>Input 행에서 ToolInput 목록을 수집한다.</summary>
        private static List<ToolInput> CollectInputSpecs(StackPanel panel)
        {
            var result = new List<ToolInput>();
            foreach (var child in panel.Children)
            {
                if (child is not Grid row)
                {
                    continue;
                }

                var boxes = row.Children.OfType<TextBox>().ToList();
                var combos = row.Children.OfType<ComboBox>().ToList();
                var name = boxes.ElementAtOrDefault(0)?.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                result.Add(new ToolInput
                {
                    Name = name,
                    Role = boxes.ElementAtOrDefault(1)?.Text?.Trim() ?? string.Empty,
                    Format = boxes.ElementAtOrDefault(2)?.Text?.Trim() ?? string.Empty,
                    Shape = combos.ElementAtOrDefault(0)?.SelectedItem?.ToString() ?? "single",
                    Required = true,
                });
            }

            return result;
        }

        /// <summary>Output 행에서 ToolOutput 목록을 수집한다.</summary>
        private static List<ToolOutput> CollectOutputSpecs(StackPanel panel)
        {
            var result = new List<ToolOutput>();
            foreach (var child in panel.Children)
            {
                if (child is not Grid row)
                {
                    continue;
                }

                var boxes = row.Children.OfType<TextBox>().ToList();
                var combos = row.Children.OfType<ComboBox>().ToList();
                var name = boxes.ElementAtOrDefault(0)?.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                result.Add(new ToolOutput
                {
                    Name = name,
                    Role = boxes.ElementAtOrDefault(1)?.Text?.Trim() ?? string.Empty,
                    Format = boxes.ElementAtOrDefault(2)?.Text?.Trim() ?? string.Empty,
                    Shape = combos.ElementAtOrDefault(0)?.SelectedItem?.ToString() ?? "single",
                    Class = combos.ElementAtOrDefault(1)?.SelectedItem?.ToString() ?? "primary",
                });
            }

            return result;
        }

        // ─── 검증 및 빌드 ─────────────────────────────────────────────────────

        private void OnValidateClicked(object? sender, RoutedEventArgs e)
        {
            InvalidateValidationState();
            StatusBar.Text = "검증 중...";

            var definition = BuildDefinitionFromForm();

            // L1-Static: 이미지 URI + 패키지 버전 검증
            var staticResults = new[]
            {
                _requiredFieldsValidator.Validate(definition),
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
                _validatedDefinitionState.MarkValidated(definition);
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

        private async void OnSendBuildClicked(object? sender, RoutedEventArgs e)
        {
            if (!_validatedDefinitionState.HasValidatedDefinition)
            {
                return;
            }

            var address = NodeForgeAddressBox.Text?.Trim();
            if (string.IsNullOrEmpty(address))
            {
                StatusBar.Text = "오류: NodeForge 주소를 입력하세요.";
                return;
            }

            var definition = BuildDefinitionFromForm();
            if (!_validatedDefinitionState.Matches(definition))
            {
                InvalidateValidationState();
                ValidationPassPanel.IsVisible = false;
                StatusBar.Text = "입력값이 검증 이후 변경되었습니다. 다시 L1 검증을 실행하세요.";
                return;
            }

            var buildClient = GetBuildClient(address);
            var request = BuildRequestFactory.FromToolDefinition(definition);

            // UI 초기화
            BuildLogPanel.IsVisible = true;
            BuildLogBox.Text = string.Empty;
            BuildSuccessPanel.IsVisible = false;
            SendBuildButton.IsEnabled = false;
            StatusBar.Text = "빌드 요청 전송 중...";

            _buildCts?.Cancel();
            _buildCts = new CancellationTokenSource();
            var cts = _buildCts;

            try
            {
#pragma warning disable CA2007 // IAsyncEnumerable does not support ConfigureAwait directly
                await foreach (var ev in buildClient.BuildAndRegisterAsync(request, cts.Token))
#pragma warning restore CA2007
                {
                    var captured = ev;
                    Dispatcher.UIThread.Post(() => HandleBuildEvent(captured));
                }
            }
#pragma warning disable CA1031
            catch (Exception ex) when (!cts.IsCancellationRequested)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    StatusBar.Text = $"gRPC 오류: {ex.Message}";
                    AppendLog($"[ERROR] {ex.Message}");
                });
            }
#pragma warning restore CA1031
            finally
            {
                Dispatcher.UIThread.Post(() => SendBuildButton.IsEnabled = _validatedDefinitionState.HasValidatedDefinition);
            }
        }

        private void HandleBuildEvent(BuildEvent ev)
        {
            var line = $"[{ev.Timestamp:HH:mm:ss}] [{ev.Kind}] {ev.Message}";
            AppendLog(line);

            switch (ev.Kind)
            {
                case BuildEventKind.DigestAcquired:
                    BuildDigestLabel.Text = $"digest: {ev.Digest}";
                    break;

                case BuildEventKind.Succeeded:
                    BuildSuccessPanel.IsVisible = true;
                    StatusBar.Text = "빌드 및 등록 완료";
                    break;

                case BuildEventKind.Failed:
                    StatusBar.Text = $"빌드 실패: {ev.Message}";
                    break;
            }
        }

        private void AppendLog(string line)
        {
            BuildLogBox.Text += line + "\n";
            BuildLogScroll.ScrollToEnd();
        }

        private async System.Threading.Tasks.Task LoadToolListAsync()
        {
            var address = NodeForgeAddressBox.Text?.Trim();
            if (string.IsNullOrEmpty(address))
            {
                StatusBar.Text = "오류: NodeForge 주소를 입력하세요.";
                return;
            }

            StatusBar.Text = "툴 목록 조회 중...";
            var toolRegistryClient = GetToolRegistryClient(address);

            try
            {
                var tools = await toolRegistryClient.ListToolsAsync().ConfigureAwait(false);
                Dispatcher.UIThread.Post(() =>
                {
                    if (tools.Count == 0)
                    {
                        ToolListEmptyPanel.IsVisible = true;
                        ToolListItems.ItemsSource = null;
                    }
                    else
                    {
                        ToolListEmptyPanel.IsVisible = false;
                        ToolListItems.ItemsSource = tools
                            .Select(t =>
                            {
                                var label = string.IsNullOrEmpty(t.DisplayLabel) ? t.ToolName : t.DisplayLabel;
                                var cat = string.IsNullOrEmpty(t.DisplayCategory) ? string.Empty : $"  [{t.DisplayCategory}]";
                                return $"{label}{cat}  phase:{t.LifecyclePhase}  cas:{t.CasHash[..Math.Min(12, t.CasHash.Length)]}  등록:{t.RegisteredAt:yyyy-MM-dd HH:mm}";
                            })
                            .ToList();
                    }

                    StatusBar.Text = $"툴 목록: {tools.Count}개";
                });
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => StatusBar.Text = $"목록 조회 오류: {ex.Message}");
            }
#pragma warning restore CA1031
        }

        private async System.Threading.Tasks.Task LoadPolicyListAsync()
        {
            var address = NodeForgeAddressBox.Text?.Trim();
            if (string.IsNullOrEmpty(address))
            {
                StatusBar.Text = "오류: NodeForge 주소를 입력하세요.";
                return;
            }

            StatusBar.Text = "정책 목록 조회 중...";
            var policyProvider = GetPolicyProvider(address);

            try
            {
                var result = await policyProvider.ListPoliciesAsync().ConfigureAwait(false);
                Dispatcher.UIThread.Post(() =>
                {
                    PolicyBundleVersionLabel.Text = result.BundleVersion;
                    PolicyListItems.ItemsSource = result.Policies
                        .Select(p => $"[{p.RuleId}] {p.Name} — {p.Description}")
                        .ToList();
                    StatusBar.Text = $"정책 목록: {result.Policies.Count}개";
                });
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => StatusBar.Text = $"정책 조회 오류: {ex.Message}");
            }
#pragma warning restore CA1031
        }

        private async void OnReloadBundleClicked(object? sender, RoutedEventArgs e)
        {
            var address = NodeForgeAddressBox.Text?.Trim();
            if (string.IsNullOrEmpty(address))
            {
                StatusBar.Text = "오류: NodeForge 주소를 입력하세요.";
                return;
            }

            StatusBar.Text = "번들 갱신 중...";
            var policyProvider = GetPolicyProvider(address);

            try
            {
                var bundle = await policyProvider.GetLatestBundleAsync().ConfigureAwait(false);
                var newChecker = new WasmPolicyChecker(bundle);
                _policyChecker?.Dispose();
                _policyChecker = newChecker;
                Dispatcher.UIThread.Post(() =>
                {
                    PolicyBundleVersionLabel.Text = bundle.Version;
                    StatusBar.Text = $"번들 갱신 완료: {bundle.Version}";
                });
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => StatusBar.Text = $"번들 갱신 오류: {ex.Message}");
            }
#pragma warning restore CA1031
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            _buildCts?.Cancel();
            _buildCts?.Dispose();
            _buildClient?.Dispose();
            _toolRegistryClient?.Dispose();
            _policyProvider?.Dispose();
            _policyChecker?.Dispose();
        }

        private ToolDefinition BuildDefinitionFromForm()
        {
            var tagsRaw = DisplayTagsBox.Text ?? string.Empty;
            var tags = tagsRaw
                .Split(',', System.StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            return new ToolDefinition
            {
                Name = ToolNameBox.Text ?? string.Empty,
                Version = ToolVersionBox.Text?.Trim() ?? string.Empty,
                ImageUri = ImageUriBox.Text ?? string.Empty,
                DockerfileContent = DockerfileBox.Text ?? string.Empty,
                Script = ScriptBox.Text ?? string.Empty,
                Command = ParseCommandJson(CommandBox.Text),
                EnvironmentSpec = EnvSpecBox.Text ?? string.Empty,
                Inputs = CollectInputSpecs(InputRowsPanel),
                Outputs = CollectOutputSpecs(OutputRowsPanel),
                DisplayLabel = DisplayLabelBox.Text?.Trim() ?? string.Empty,
                DisplayDescription = DisplayDescriptionBox.Text?.Trim() ?? string.Empty,
                DisplayCategory = DisplayCategoryBox.Text?.Trim() ?? string.Empty,
                DisplayTags = tags,
            };
        }

        /// <summary>
        /// 사용자 입력 문자열을 JSON 배열로 파싱해 커맨드 목록을 반환한다.
        /// 빈 입력이거나 파싱 실패 시 빈 목록을 반환한다.
        /// </summary>
        private static List<string> ParseCommandJson(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<string>();
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(raw.Trim());
                return parsed ?? new List<string>();
            }
#pragma warning disable CA1031
            catch
            {
                return new List<string>();
            }
#pragma warning restore CA1031
        }

        private void RegisterValidationInvalidationHandlers()
        {
            ToolNameBox.TextChanged += (_, _) => InvalidateValidationState();
            ToolVersionBox.TextChanged += (_, _) => InvalidateValidationState();
            ImageUriBox.TextChanged += (_, _) => InvalidateValidationState();
            DockerfileBox.TextChanged += (_, _) => InvalidateValidationState();
            ScriptBox.TextChanged += (_, _) => InvalidateValidationState();
            CommandBox.TextChanged += (_, _) => InvalidateValidationState();
            EnvSpecBox.TextChanged += (_, _) => InvalidateValidationState();
            NodeForgeAddressBox.TextChanged += (_, _) => InvalidateValidationState();
        }

        private void InvalidateValidationState()
        {
            _validatedDefinitionState.Invalidate();
            SendBuildButton.IsEnabled = false;
        }

        private GrpcBuildClient GetBuildClient(string address)
        {
            if (_buildClient == null || !string.Equals(_buildClientAddress, address, StringComparison.Ordinal))
            {
                _buildClient?.Dispose();
                _buildClient = new GrpcBuildClient(address);
                _buildClientAddress = address;
            }

            return _buildClient;
        }

        private GrpcToolRegistryClient GetToolRegistryClient(string address)
        {
            if (_toolRegistryClient == null || !string.Equals(_toolRegistryClientAddress, address, StringComparison.Ordinal))
            {
                _toolRegistryClient?.Dispose();
                _toolRegistryClient = new GrpcToolRegistryClient(address);
                _toolRegistryClientAddress = address;
            }

            return _toolRegistryClient;
        }

        private GrpcPolicyBundleProvider GetPolicyProvider(string address)
        {
            if (_policyProvider == null || !string.Equals(_policyProviderAddress, address, StringComparison.Ordinal))
            {
                _policyProvider?.Dispose();
                _policyProvider = new GrpcPolicyBundleProvider(address);
                _policyProviderAddress = address;
            }

            return _policyProvider;
        }
    }
}
