using System;
using System.Linq;
using NodeKit.Authoring.Recipes;

namespace NodeKit.Cli
{
    /// <summary>
    /// Heuristic, non-blocking check for SourceBuildStructured's
    /// RuntimeProfile/RuntimeProfileImage (design doc §10 D-5, §13 R22-D,
    /// Issue #39). The curated RuntimeProfile choices are already
    /// buildah-verified minimal images (SourceBuildProfileCatalog); this
    /// only applies to the "advanced" escape hatch, where an author types a
    /// raw image reference NodeKit has never inspected.
    ///
    /// This is a name-pattern guess, not a real content check — NodeKit has
    /// no way to inspect what's actually inside an arbitrary image. The
    /// warning text says so explicitly so it's never mistaken for the real
    /// enforcement, which is NodeVault's job: Sprint 9 (final-stage RUN
    /// static scan, live since 2026-07-13) catches an explicit "RUN curl ..."
    /// in the runtime stage, and Sprint 10 (post-build image content scan,
    /// not yet implemented) is what would actually confirm an image's
    /// contents — both out of this repo's scope.
    /// </summary>
    internal static class RuntimeProfileHygieneAdvisor
    {
        private static readonly string[] _notMinimalImagePatterns =
        {
            "curlimages/curl",
            "busybox",
            "alpine/curl",
            "buildpack-deps",
        };

        public static string? Describe(RecipeBuildKind buildKind, string? runtimeProfile, string? runtimeProfileImage)
        {
            if (buildKind != RecipeBuildKind.SourceBuildStructured
                || runtimeProfile != SourceBuildProfileCatalog.AdvancedKey
                || string.IsNullOrEmpty(runtimeProfileImage))
            {
                return null;
            }

            var matchedPattern = _notMinimalImagePatterns.FirstOrDefault(
                pattern => runtimeProfileImage.Contains(pattern, StringComparison.OrdinalIgnoreCase));
            if (matchedPattern != null)
            {
                return $"RuntimeProfileImage('{runtimeProfileImage}')가 이름 기준으로 빌드/fetch 도구를 포함한 " +
                    "이미지로 보입니다(추정 — 실제 이미지 콘텐츠를 검사한 결과가 아닙니다). RuntimeProfile은 " +
                    "최종 실행 이미지이므로 빌드 도구가 남아있지 않은 이미지를 사용하는 것이 안전합니다. " +
                    "실제 검증은 NodeVault의 몫입니다(최종 스테이지 RUN 정적 검사는 있음 — Sprint 9, " +
                    "2026-07-13; base image에 이미 포함된 도구 탐지는 아직 없음 — Sprint 10, 미구현).";
            }

            return null;
        }
    }
}
