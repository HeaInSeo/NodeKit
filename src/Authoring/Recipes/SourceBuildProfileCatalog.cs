using System.Collections.Generic;
using System.Linq;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Curated Build/Runtime profile -> pinned image map for
    /// RecipeKind.SourceBuildStructured (§13 R22-B). Every image below
    /// was verified locally with `buildah run` for the tools its role
    /// requires before being added — see
    /// docs/NODEKIT_SOURCEBUILD_STRUCTURED_INTENT_DESIGN.md §16. This is a
    /// deliberately small v1 set (one entry per role) — the design doc's
    /// own guidance is to start minimal and expand once more profiles are
    /// curated and verified, not to guess at image contents.
    ///
    /// "advanced" is not a catalog entry; it is the sentinel that tells
    /// RecipeValidator/RecipeRenderer to use BuildProfileImage/
    /// RuntimeProfileImage directly instead of a curated profile (the same
    /// escape-hatch shape as RecipeFieldCatalog.BaseImageField()'s "0: 직접
    /// 입력" pattern).
    /// </summary>
    internal static class SourceBuildProfileCatalog
    {
        public const string AdvancedKey = "advanced";

        private static readonly IReadOnlyList<SourceBuildProfileEntry> _buildProfiles = new[]
        {
            new SourceBuildProfileEntry(
                "generic",
                "docker.io/library/buildpack-deps:bookworm@sha256:4efddd9a54ddc095e672b2fdf514f1ee4d3bb6e1f6ffc988b022c75e6ea99383",
                Text("범용 빌드 환경", "Generic build environment"),
                Text(
                    "curl/tar/sha256sum과 기본 빌드 도구를 포함합니다. buildah run으로 직접 확인함(2026-07-13) — condaforge/miniforge3 등 기존 candidate는 curl이 없어 재사용하지 못했습니다.",
                    "Includes curl/tar/sha256sum and common build tools. Verified directly with buildah run (2026-07-13) — existing candidates like condaforge/miniforge3 lack curl and could not be reused.")),
        };

        private static readonly IReadOnlyList<SourceBuildProfileEntry> _runtimeProfiles = new[]
        {
            new SourceBuildProfileEntry(
                "minimal",
                "docker.io/library/debian:bookworm-slim@sha256:60eac759739651111db372c07be67863818726f754804b8707c90979bda511df",
                Text("최소 런타임", "Minimal runtime"),
                Text(
                    "빌드 도구 없이 셸(sh/bash)만 포함하는 최소 이미지입니다. buildah run으로 직접 확인함(2026-07-13).",
                    "A minimal image with just a shell (sh/bash), no build tools. Verified directly with buildah run (2026-07-13).")),
        };

        public static IReadOnlyList<SourceBuildProfileEntry> BuildProfiles => _buildProfiles;

        public static IReadOnlyList<SourceBuildProfileEntry> RuntimeProfiles => _runtimeProfiles;

        public static SourceBuildProfileEntry? FindBuildProfile(string key) =>
            _buildProfiles.FirstOrDefault(p => p.Key == key);

        public static SourceBuildProfileEntry? FindRuntimeProfile(string key) =>
            _runtimeProfiles.FirstOrDefault(p => p.Key == key);

        private static LocalizedText Text(string ko, string en) =>
            new(new Dictionary<string, string> { ["ko"] = ko, ["en"] = en });
    }
}
