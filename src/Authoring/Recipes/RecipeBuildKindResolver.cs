using System;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Resolves the user-facing RecipeMethodId to the internal RecipeBuildKind.
    /// Must only be called after Build() has applied Defaulted fields — see
    /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md Section 4.4.
    /// </summary>
    internal static class RecipeBuildKindResolver
    {
        public static RecipeBuildKind Resolve(RecipeMethodId method, RecipeDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);

            if (method == RecipeMethodId.Package && string.IsNullOrWhiteSpace(document.PackageEngine))
            {
                throw new InvalidOperationException(
                    "PackageEngine must be defaulted before resolving RecipeBuildKind.");
            }

            return method switch
            {
                RecipeMethodId.Container => RecipeBuildKind.BioContainer,
                RecipeMethodId.Mirror => RecipeBuildKind.PackageMirror,
                RecipeMethodId.Source => RecipeBuildKind.SourceBuild,
                RecipeMethodId.SourceStructured => RecipeBuildKind.SourceBuildStructured,
                RecipeMethodId.Dockerfile => RecipeBuildKind.DockerfileFallback,

                RecipeMethodId.Package => document.PackageEngine == "micromamba"
                    ? RecipeBuildKind.Micromamba
                    : RecipeBuildKind.Conda,

                _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unsupported recipe method."),
            };
        }
    }
}
