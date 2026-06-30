using System;
using System.Collections.Generic;
using System.Linq;

namespace NodeKit.Cli
{
    // Presents ResolveRecipe candidates to the user and returns the selected
    // full package pins (e.g. "bwa=0.7.17=h5bf99c6_8").
    // Called after L1 validation passes, before RecipeDocument is saved.
    internal static class PackageCandidatePresenter
    {
        // Returns a mapping of package name → chosen full_pin string,
        // or null if the user cancels.
        // Packages with exactly one candidate are auto-selected silently.
        // Packages with multiple candidates prompt the user to pick.
        internal static IReadOnlyDictionary<string, string>? Present(
            IReadOnlyList<PackageResolution> packages,
            IRecipeConsole console,
            IRecipeCreateCancellationSource cancellation)
        {
            var selections = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var pkg in packages)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                if (pkg.Candidates.Count == 0)
                {
                    continue;
                }

                if (pkg.Candidates.Count == 1)
                {
                    selections[pkg.Name] = pkg.Candidates[0].FullPin;
                    continue;
                }

                var chosen = PromptCandidateSelection(pkg, console, cancellation);
                if (chosen is null)
                {
                    return null;
                }

                selections[pkg.Name] = chosen;
            }

            return selections;
        }

        private static string? PromptCandidateSelection(
            PackageResolution pkg,
            IRecipeConsole console,
            IRecipeCreateCancellationSource cancellation)
        {
            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                console.WriteLine($"패키지 '{pkg.Name}={pkg.Version}'에 대해 여러 빌드 문자열 후보가 있습니다.");
                console.WriteLine();

                for (var i = 0; i < pkg.Candidates.Count; i++)
                {
                    var c = pkg.Candidates[i];
                    console.WriteLine($"  [{i + 1}] {c.FullPin}");
                    if (!string.IsNullOrWhiteSpace(c.Channel))
                    {
                        console.WriteLine($"      채널: {c.Channel}");
                    }
                }

                console.WriteLine();
                console.WriteHints("/cancel: 저장하지 않고 종료");
                console.Write($"번호를 선택하세요 [1-{pkg.Candidates.Count}] (Enter = 1번): ");

                var line = console.ReadLine();

                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                if (line is null || line.Equals("/cancel", StringComparison.OrdinalIgnoreCase)
                    || line.Equals("/quit", StringComparison.OrdinalIgnoreCase)
                    || line.Equals("/exit", StringComparison.OrdinalIgnoreCase))
                {
                    throw new RecipeCreateCancelledException();
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    return pkg.Candidates[0].FullPin;
                }

                if (int.TryParse(line.Trim(), out var index)
                    && index >= 1 && index <= pkg.Candidates.Count)
                {
                    return pkg.Candidates[index - 1].FullPin;
                }

                console.WriteLine($"1부터 {pkg.Candidates.Count} 사이의 번호를 입력하세요.");
                console.WriteLine();
            }
        }

        // Applies the user-selected full_pins back into the package list.
        // Input pins like "bwa=0.7.17" are replaced with "bwa=0.7.17=h5bf99c6_8".
        internal static IReadOnlyList<string> ApplySelections(
            IReadOnlyList<string> packages,
            IReadOnlyDictionary<string, string> selections)
        {
            if (selections.Count == 0)
            {
                return packages;
            }

            var result = new List<string>(packages.Count);
            foreach (var pkg in packages)
            {
                var name = ExtractPackageName(pkg);
                if (name is not null && selections.TryGetValue(name, out var fullPin))
                {
                    result.Add(fullPin);
                }
                else
                {
                    result.Add(pkg);
                }
            }

            return result;
        }

        private static string? ExtractPackageName(string packageExpression)
        {
            if (string.IsNullOrWhiteSpace(packageExpression))
            {
                return null;
            }

            var eqIndex = packageExpression.IndexOf('=', StringComparison.Ordinal);
            return eqIndex < 0
                ? packageExpression.Trim()
                : packageExpression[..eqIndex].Trim();
        }
    }
}
