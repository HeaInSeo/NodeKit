using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace NodeKit.UI.Spikes
{
    internal partial class V16AuthoringSpikeWindow : Window
    {
        private static readonly JsonSerializerOptions _diagnosticJsonOptions = new() { WriteIndented = true };
        private IDisposable? _captureTimer;

        public V16AuthoringSpikeWindow()
        {
            InitializeComponent();

            var capturePath = Environment.GetEnvironmentVariable("NODEKIT_UI_CAPTURE");
            if (string.IsNullOrWhiteSpace(capturePath))
            {
                return;
            }

            var captureFocus = Environment.GetEnvironmentVariable("NODEKIT_UI_CAPTURE_FOCUS");
            Width = ReadPositiveDouble("NODEKIT_UI_CAPTURE_WIDTH", Width);
            Height = ReadPositiveDouble("NODEKIT_UI_CAPTURE_HEIGHT", Height);
            MinWidth = ReadNonNegativeDouble("NODEKIT_UI_CAPTURE_MIN_WIDTH", MinWidth);
            MinHeight = ReadNonNegativeDouble("NODEKIT_UI_CAPTURE_MIN_HEIGHT", MinHeight);
            Opened += (_, _) =>
            {
                _captureTimer = DispatcherTimer.RunOnce(
                    () =>
                    {
                        ApplyCaptureFocus(captureFocus);
                        _captureTimer?.Dispose();
                        _captureTimer = DispatcherTimer.RunOnce(
                            () => CaptureAndClose(capturePath, captureFocus),
                            TimeSpan.FromMilliseconds(120),
                            DispatcherPriority.Loaded);
                    },
                    TimeSpan.FromMilliseconds(250),
                    DispatcherPriority.Loaded);
            };
        }

        private static double ReadPositiveDouble(string variable, double fallback)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                && double.IsFinite(parsed)
                && parsed > 0
                ? parsed
                : fallback;
        }

        private static double ReadNonNegativeDouble(string variable, double fallback)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                && double.IsFinite(parsed)
                && parsed >= 0
                ? parsed
                : fallback;
        }

        private void ApplyCaptureFocus(string? captureFocus)
        {
            if (string.Equals(captureFocus, "center-input", StringComparison.OrdinalIgnoreCase))
            {
                this.GetVisualDescendants()
                    .OfType<TextBox>()
                    .FirstOrDefault()
                    ?.Focus(NavigationMethod.Tab, KeyModifiers.None);
                return;
            }

            if (!string.Equals(captureFocus, "activity", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            this.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(
                    button => button.Content is StackPanel panel
                        && panel.Children.OfType<TextBlock>().Any(
                            text => string.Equals(text.Text, "Activity", StringComparison.Ordinal)))
                ?.Focus(NavigationMethod.Tab, KeyModifiers.None);
        }

        private void CaptureAndClose(string capturePath, string? captureFocus)
        {
            var fullPath = Path.GetFullPath(capturePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var scaling = RenderScaling;
            var pixelSize = PixelSize.FromSize(ClientSize, scaling);
            using (var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96 * scaling, 96 * scaling)))
            {
                bitmap.Render(this);
                bitmap.Save(fullPath);
            }

            var screen = Screens.ScreenFromWindow(this);
            var diagnostic = new
            {
                requestedWidth = Width,
                requestedHeight = Height,
                clientWidth = ClientSize.Width,
                clientHeight = ClientSize.Height,
                minWidth = MinWidth,
                minHeight = MinHeight,
                captureFocus,
                renderScaling = scaling,
                pixelWidth = pixelSize.Width,
                pixelHeight = pixelSize.Height,
                screenScaling = screen?.Scaling,
                screenBoundsPixelWidth = screen?.Bounds.Width,
                screenBoundsPixelHeight = screen?.Bounds.Height,
                workingAreaPixelWidth = screen?.WorkingArea.Width,
                workingAreaPixelHeight = screen?.WorkingArea.Height,
                workingAreaDipWidth = screen is null ? (double?)null : screen.WorkingArea.Width / screen.Scaling,
                workingAreaDipHeight = screen is null ? (double?)null : screen.WorkingArea.Height / screen.Scaling,
                platform = Environment.OSVersion.ToString(),
            };
            var jsonPath = Path.ChangeExtension(fullPath, ".json");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(diagnostic, _diagnosticJsonOptions));

            _captureTimer?.Dispose();
            _captureTimer = null;
            Close();
        }
    }
}
