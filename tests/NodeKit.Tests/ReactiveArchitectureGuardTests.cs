using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace NodeKit.Tests
{
    public class ReactiveArchitectureGuardTests
    {
        private const int MainWindowCodeBehindLineBaseline = 667;
        private const int MainWindowClickSubscriptionBaseline = 16;
        private const int MainWindowAsyncVoidHandlerBaseline = 2;

        [Fact]
        public void MainWindowCodeBehind_DoesNotGrowBeyondLegacyBaseline()
        {
            var path = RepoPath("UI", "MainWindow.axaml.cs");
            var lineCount = File.ReadLines(path).Count();

            Assert.True(
                lineCount <= MainWindowCodeBehindLineBaseline,
                $"MainWindow.axaml.cs has {lineCount} lines; baseline is {MainWindowCodeBehindLineBaseline}. "
                + "Move new UI state into ReactiveUI/System.Reactive ViewModel or state classes instead of expanding code-behind.");
        }

        [Fact]
        public void MainWindowCodeBehind_DoesNotAddMoreClickSubscriptions()
        {
            var source = ReadRepoFile("UI", "MainWindow.axaml.cs");
            var clickSubscriptionCount = Regex.Matches(source, "\\.Click\\s*\\+=").Count;

            Assert.True(
                clickSubscriptionCount <= MainWindowClickSubscriptionBaseline,
                $"MainWindow.axaml.cs has {clickSubscriptionCount} Click subscriptions; baseline is {MainWindowClickSubscriptionBaseline}. "
                + "Route new interactions through commands on a reactive ViewModel instead.");
        }

        [Fact]
        public void MainWindowCodeBehind_DoesNotAddMoreAsyncVoidHandlers()
        {
            var source = ReadRepoFile("UI", "MainWindow.axaml.cs");
            var asyncVoidHandlerCount = Regex.Matches(source, "private\\s+async\\s+void\\s+On[A-Za-z0-9_]+").Count;

            Assert.True(
                asyncVoidHandlerCount <= MainWindowAsyncVoidHandlerBaseline,
                $"MainWindow.axaml.cs has {asyncVoidHandlerCount} async void handlers; baseline is {MainWindowAsyncVoidHandlerBaseline}. "
                + "Use ReactiveCommand or testable async services for new async UI behavior.");
        }

        [Fact]
        public void ViewModelFiles_UseReactiveBaseTypesOrCommands()
        {
            var viewModelRoot = Path.Join(RepoRoot, "UI", "ViewModels");
            if (!Directory.Exists(viewModelRoot))
            {
                return;
            }

            var offenders = Directory
                .EnumerateFiles(viewModelRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !Path.GetFileName(path).EndsWith(".g.cs", StringComparison.Ordinal))
                .Where(path =>
                {
                    var source = File.ReadAllText(path);
                    return !source.Contains("ReactiveObject", StringComparison.Ordinal)
                        && !source.Contains("ReactiveCommand", StringComparison.Ordinal)
                        && !source.Contains("ObservableAsPropertyHelper", StringComparison.Ordinal)
                        && !source.Contains("IObservable<", StringComparison.Ordinal);
                })
                .Select(path => Path.GetRelativePath(RepoRoot, path))
                .ToArray();

            Assert.True(
                offenders.Length == 0,
                "ViewModel files should use ReactiveUI/System.Reactive primitives. Offenders: "
                + string.Join(", ", offenders));
        }

        private static string ReadRepoFile(params string[] paths) =>
            File.ReadAllText(RepoPath(paths));

        private static string RepoPath(params string[] paths) =>
            Path.Join(new[] { RepoRoot }.Concat(paths).ToArray());

        private static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir is not null && !File.Exists(Path.Join(dir.FullName, "NodeKit.sln")))
                {
                    dir = dir.Parent;
                }

                return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate NodeKit.sln from test output directory.");
            }
        }
    }
}
