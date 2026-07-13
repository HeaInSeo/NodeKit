using System;
using NodeKit.Authoring.Recipes;

namespace NodeKit.Cli
{
    /// <summary>
    /// Non-blocking check for legacy SourceBuild (single-stage). Originally
    /// just a BaseImage heuristic (docs/NODEKIT_CLI_FIRST_SPRINT_PLAN.md §13
    /// R20): SourceBuild's single BaseImage field plays both roles — it
    /// fetches the source AND becomes the final runtime image — so a
    /// fetch-purpose-only image (e.g. curlimages/curl) works for the build
    /// step but ships as a production final image with little else in it.
    ///
    /// Broadened after the NodeVault↔NodeKit adversarial review (2026-07-13,
    /// Major 1, Issue TBD): NodeVault's Sprint 9 P2a
    /// (pkg/build/validate.go, commit 645c594) now statically rejects a
    /// final-stage RUN line that invokes curl/wget/git/make/etc. Legacy
    /// SourceBuild is single-stage, so its one RUN line (curl fetch +
    /// SourceBuildCommands, commonly "make"/"make install") *is* the final
    /// stage — meaning essentially every legacy SourceBuild recipe submitted
    /// today will be rejected by NodeVault's build gate. This is now an
    /// unconditional warning, not just a BaseImage-pattern heuristic. It
    /// still only warns (not blocks): NodeVault's actual policy decision is
    /// out of NodeKit's L1 authority, and allow_runtime_tools could exempt a
    /// given submission (though NodeKit has no UI for that field yet).
    /// RecipeBuildKind.SourceBuildStructured (2-stage, no RUN in the final
    /// stage) is not affected by this NodeVault policy.
    /// </summary>
    internal static class SourceBuildBaseImageAdvisor
    {
        private const string NodeVaultRejectionWarning =
            "레거시 SourceBuild(단일 스테이지)는 fetch(curl)와 빌드(make 등) 명령이 최종 실행 이미지와 " +
            "같은 스테이지에 있습니다. NodeVault가 최종 스테이지 RUN의 risky tool(curl/wget/git/make 등)을 " +
            "정적으로 거부하는 정책을 도입해서(Sprint 9, 2026-07-13), 이 방식으로 만든 recipe는 실제 제출 시 " +
            "NodeVault에서 거부될 가능성이 매우 높습니다. 가능하면 `--non-interactive --method source-structured`" +
            "(빌드/런타임 스테이지 분리)를 사용하세요.";

        private static readonly string[] _fetchOnlyImagePatterns =
        {
            "curlimages/curl",
            "busybox",
            "alpine/curl",
        };

        public static string? Describe(RecipeBuildKind buildKind, string? baseImage)
        {
            if (buildKind != RecipeBuildKind.SourceBuild)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(baseImage))
            {
                foreach (var pattern in _fetchOnlyImagePatterns)
                {
                    if (baseImage.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        return NodeVaultRejectionWarning + " " +
                            $"덧붙여 BaseImage('{baseImage}')가 fetch 전용 이미지로 보입니다 — curl 등 fetch 도구만 " +
                            "있고 실제 도구 실행에 필요한 다른 구성요소는 없을 수 있으니 확인하세요.";
                    }
                }
            }

            return NodeVaultRejectionWarning;
        }
    }
}
