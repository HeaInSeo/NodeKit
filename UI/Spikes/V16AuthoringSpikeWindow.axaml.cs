using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

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

            Width = ReadPositiveDouble("NODEKIT_UI_CAPTURE_WIDTH", Width);
            Height = ReadPositiveDouble("NODEKIT_UI_CAPTURE_HEIGHT", Height);
            Opened += (_, _) =>
            {
                _captureTimer = DispatcherTimer.RunOnce(
                    () => CaptureAndClose(capturePath),
                    TimeSpan.FromMilliseconds(350),
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

        private void CaptureAndClose(string capturePath)
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

            var diagnostic = new
            {
                requestedWidth = Width,
                requestedHeight = Height,
                clientWidth = ClientSize.Width,
                clientHeight = ClientSize.Height,
                minWidth = MinWidth,
                minHeight = MinHeight,
                renderScaling = scaling,
                pixelWidth = pixelSize.Width,
                pixelHeight = pixelSize.Height,
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
