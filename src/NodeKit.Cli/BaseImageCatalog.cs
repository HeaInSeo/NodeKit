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

        private static readonly IReadOnlyList<BaseImageEntry> _empty =
            Array.Empty<BaseImageEntry>();

        public static IReadOnlyList<BaseImageEntry> CandidatesFor(RecipeMethodId method) =>
            method switch
            {
                RecipeMethodId.Package => _condaCandidates,
                RecipeMethodId.Mirror => _condaCandidates,
                RecipeMethodId.Source => _condaCandidates,
                _ => _empty,
            };
    }
}
