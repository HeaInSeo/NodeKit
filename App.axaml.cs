using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NodeKit.UI;
using NodeKit.UI.Spikes;

namespace NodeKit
{
    internal partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var spike = System.Environment.GetEnvironmentVariable("NODEKIT_UI_SPIKE");
                desktop.MainWindow = string.Equals(spike, "v16", System.StringComparison.OrdinalIgnoreCase)
                    ? new V16AuthoringSpikeWindow()
                    : new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
