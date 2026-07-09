using System;
using NodeKit.Authoring.Recipes;

namespace NodeKit.Cli
{
    /// <summary>
    /// Heuristic, non-blocking check for SourceBuild's BaseImage. Live testing
    /// against a real NodeVault (docs/NODEKIT_CLI_FIRST_SPRINT_PLAN.md §13 R20)
    /// found that SourceBuild's single BaseImage field plays both roles today
    /// — it fetches the source AND becomes the final runtime image — so a
    /// fetch-purpose-only image (e.g. curlimages/curl) works for the build
    /// step but ships as a production final image with little else in it.
    /// True multi-stage fetch/builder/final recipes are a larger redesign
    /// (deferred to a follow-up sprint); this only warns, since a custom
    /// image may legitimately be fine either way.
    /// </summary>
    internal static class SourceBuildBaseImageAdvisor
    {
        private static readonly string[] _fetchOnlyImagePatterns =
        {
            "curlimages/curl",
            "busybox",
            "alpine/curl",
        };

        public static string? Describe(RecipeBuildKind buildKind, string? baseImage)
        {
            if (buildKind != RecipeBuildKind.SourceBuild || string.IsNullOrEmpty(baseImage))
            {
                return null;
            }

            foreach (var pattern in _fetchOnlyImagePatterns)
            {
                if (baseImage.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return $"BaseImage('{baseImage}')가 fetch 전용 이미지로 보입니다. SourceBuild는 이 이미지를 " +
                        "소스를 내려받는 데도, 최종 실행 이미지로도 그대로 사용합니다 — curl 등 fetch 도구만 있고 " +
                        "실제 도구 실행에 필요한 다른 구성요소는 없을 수 있으니 확인하세요.";
                }
            }

            return null;
        }
    }
}
