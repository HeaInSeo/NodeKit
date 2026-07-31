using System.Collections.Generic;
using NodeKit.Authoring.Recipes;

namespace NodeKit.Cli
{
    /// <summary>
    /// Heuristic, non-blocking notice for SourceBuild's BuildDependencies.
    /// Live testing (docs/NODEKIT_CLI_FIRST_SPRINT_PLAN.md §13 R21, extended
    /// test F-04/#10) confirmed RecipeRenderer never actually installs
    /// BuildDependencies — the field exists on RecipeDocument but the
    /// renderer doesn't read it, so a recipe listing "zlib1g-dev" gives no
    /// indication that nothing installs it. Per the improvement plan's own
    /// conservative policy, this only clarifies the current limitation; it
    /// does not add auto-install logic (that needs a pin/snapshot policy
    /// first, or it creates a new reproducibility problem while solving
    /// this one).
    /// </summary>
    internal static class BuildDependenciesAdvisor
    {
        public static string? Describe(RecipeKind buildKind, IReadOnlyList<string> buildDependencies)
        {
            if (buildKind != RecipeKind.SourceBuild || buildDependencies.Count == 0)
            {
                return null;
            }

            return "BuildDependencies는 현재 자동으로 설치되지 않습니다 — BaseImage에 이미 포함되어 " +
                "있는지 직접 확인하세요. (" + string.Join(", ", buildDependencies) + ")";
        }
    }
}
