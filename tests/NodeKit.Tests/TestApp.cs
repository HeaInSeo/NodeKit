using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(NodeKit.Tests.TestApp))]

namespace NodeKit.Tests
{
    public class TestApp : Application
    {
        public override void Initialize()
        {
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<TestApp>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
