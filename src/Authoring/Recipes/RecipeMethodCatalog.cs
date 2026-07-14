using System.Collections.Generic;
using System.Linq;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Beginner-facing list of the 5 authoring methods, with description,
    /// preparation hint, and warning text per method. See
    /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md Section 4.2.
    /// </summary>
    internal static class RecipeMethodCatalog
    {
        public static IReadOnlyList<RecipeMethodInfo> Methods { get; } = new[]
        {
            new RecipeMethodInfo(
                RecipeMethodId.Container,
                Text("기존 컨테이너 이미지 사용", "Use an existing container image"),
                Text(
                    "이미 존재하는 컨테이너 이미지를 그대로 사용합니다.",
                    "Uses an existing container image as-is."),
                Text(
                    "digest로 고정된 이미지 URI가 필요합니다.",
                    "You need an image URI pinned by digest."),
                Text(
                    "tag만 알고 있다면 우선 진행할 수 있지만, digest 없이는 최종 저장 시점에 검증이 막힙니다.",
                    "You can proceed with just a tag for now, but saving will be blocked at final validation without a digest.")),
            new RecipeMethodInfo(
                RecipeMethodId.Package,
                Text("패키지로 설치하기", "Install via packages"),
                Text(
                    "conda 또는 micromamba로 패키지를 설치합니다.",
                    "Installs packages via conda or micromamba."),
                Text(
                    "public channel에 패키지가 있어야 합니다.",
                    "The package must exist in a public channel."),
                Warning: null),
            new RecipeMethodInfo(
                RecipeMethodId.Mirror,
                Text("내부 패키지 미러에서 설치하기", "Install from an internal package mirror"),
                Text(
                    "내부망에서 접근 가능한 package mirror를 사용해 설치합니다.",
                    "Installs using a package mirror reachable from the internal network."),
                Text(
                    "내부 mirror URI가 필요합니다.",
                    "You need the internal mirror URI."),
                Warning: null),
            new RecipeMethodInfo(
                RecipeMethodId.Source,
                Text("소스코드로 직접 빌드하기", "Build directly from source"),
                Text(
                    "source archive를 받아 직접 빌드합니다.",
                    "Downloads the source archive and builds it directly."),
                Text(
                    "SourceUri와 SourceChecksum(sha256)이 필요합니다.",
                    "You need a SourceUri and a SourceChecksum (sha256)."),
                Warning: null),
            new RecipeMethodInfo(
                RecipeMethodId.SourceStructured,
                Text("소스코드로 직접 빌드하기 (구조화, 고급)", "Build directly from source (structured, advanced)"),
                Text(
                    "빌드 환경과 런타임 환경을 분리해서 최종 이미지에 빌드 도구가 남지 않게 합니다.",
                    "Separates the build environment from the runtime environment so build tools don't leak into the final image."),
                Text(
                    "SourceUri, SourceChecksum(sha256), BuildProfile, RuntimeProfile이 필요합니다.",
                    "You need a SourceUri, a SourceChecksum (sha256), a BuildProfile, and a RuntimeProfile."),
                Warning: null),
            new RecipeMethodInfo(
                RecipeMethodId.Dockerfile,
                Text("Dockerfile 직접 작성하기", "Write a Dockerfile directly"),
                Text(
                    "Dockerfile을 직접 작성하거나 기존 Dockerfile을 사용합니다.",
                    "Writes a Dockerfile directly or uses an existing one."),
                Text(
                    "Dockerfile 경로 또는 내용이 필요합니다.",
                    "You need a Dockerfile path or its content."),
                Text(
                    "마지막 수단이지만 기존 Dockerfile이 있으면 가능합니다.",
                    "A last resort, but workable if an existing Dockerfile is available.")),
        };

        public static RecipeMethodInfo For(RecipeMethodId method) =>
            Methods.Single(m => m.Method == method);

        private static LocalizedText Text(string ko, string en) =>
            new(new Dictionary<string, string> { ["ko"] = ko, ["en"] = en });
    }
}
