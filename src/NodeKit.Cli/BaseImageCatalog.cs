using System;
using System.Collections.Generic;
using NodeKit.Authoring.Recipes;

namespace NodeKit.Cli
{
    internal record BaseImageEntry(string Reference, string Description);

    /// <summary>
    /// Curated list of recommended base images for each method. Used in step 4
    /// of RecipeCreateFlow to present auto-resolvable candidates to the user.
    /// Container and Dockerfile methods are excluded because they manage their
    /// own image references independently.
    /// </summary>
    internal static class BaseImageCatalog
    {
        private static readonly IReadOnlyList<BaseImageEntry> _condaCandidates = new[]
        {
            new BaseImageEntry(
                "condaforge/miniforge3:24.3.0-0",
                "공식 conda-forge Miniforge 기반 이미지 (conda/mamba 포함)"),
            new BaseImageEntry(
                "mambaorg/micromamba:1.5.8",
                "Micromamba 경량 기반 이미지 (빠른 설치)"),
        };

        // Mirror 방식은 PackageEngine 필드가 없고 RecipeRenderer가 항상 "conda"를
        // 하드코딩하므로(recipe.PackageMirrorUri를 conda channel처럼 취급), micromamba
        // 전용 이미지를 후보로 보여주면 conda 바이너리가 없는 이미지에 "conda install"을
        // 렌더링하는 조합이 만들어져 100% 빌드 실패한다.
        private static readonly IReadOnlyList<BaseImageEntry> _mirrorCandidates = new[]
        {
            new BaseImageEntry(
                "condaforge/miniforge3:24.3.0-0",
                "공식 conda-forge Miniforge 기반 이미지 (conda/mamba 포함)"),
        };

        private static readonly IReadOnlyList<BaseImageEntry> _empty =
            Array.Empty<BaseImageEntry>();

        public static IReadOnlyList<BaseImageEntry> CandidatesFor(RecipeMethodId method) =>
            method switch
            {
                RecipeMethodId.Package => _condaCandidates,
                RecipeMethodId.Mirror => _mirrorCandidates,
                RecipeMethodId.Source => _condaCandidates,
                _ => _empty,
            };
    }
}
