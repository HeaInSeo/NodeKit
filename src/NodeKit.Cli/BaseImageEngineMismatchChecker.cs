using System;
using NodeKit.Authoring.Recipes;

namespace NodeKit.Cli
{
    /// <summary>
    /// Heuristic, non-blocking check for a BaseImage that names one package
    /// manager while the recipe's BuildKind renders another — the same bug
    /// class as issues #15/#16, but for the free-text entry points (manual
    /// step-4 entry, non-interactive --field BaseImage=...) that auto-detection
    /// on the curated candidate list can't cover. Never blocks: a custom image
    /// may legitimately contain both, so this only surfaces a warning.
    /// </summary>
    internal static class BaseImageEngineMismatchChecker
    {
        public static string? DescribeMismatch(RecipeBuildKind buildKind, string? baseImage)
        {
            if (string.IsNullOrEmpty(baseImage))
            {
                return null;
            }

            var looksLikeMicromambaImage = baseImage.Contains("micromamba", StringComparison.OrdinalIgnoreCase);
            var looksLikeCondaImage = baseImage.Contains("miniforge", StringComparison.OrdinalIgnoreCase)
                || baseImage.Contains("condaforge", StringComparison.OrdinalIgnoreCase)
                || baseImage.Contains("anaconda", StringComparison.OrdinalIgnoreCase);

            return buildKind switch
            {
                RecipeBuildKind.Conda when looksLikeMicromambaImage =>
                    "BaseImage가 micromamba 전용 이미지로 보이는데 PackageEngine은 conda입니다. " +
                    "--engine micromamba를 빠뜨리지 않았는지, 또는 base image가 맞는지 확인하세요.",
                RecipeBuildKind.Micromamba when looksLikeCondaImage =>
                    "BaseImage가 conda 계열 이미지로 보이는데 PackageEngine은 micromamba입니다. " +
                    "의도한 조합이 맞는지, 이미지에 micromamba 바이너리가 있는지 확인하세요.",
                RecipeBuildKind.PackageMirror when looksLikeMicromambaImage =>
                    "BaseImage가 micromamba 전용 이미지로 보이는데 mirror 방식은 항상 conda로 렌더링됩니다. " +
                    "conda가 포함된 base image를 사용하세요.",
                _ => null,
            };
        }
    }
}
