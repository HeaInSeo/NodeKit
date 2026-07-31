using System;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Resolves the user-facing RecipeMethodId to the internal RecipeKind.
    /// Must only be called after Build() has applied Defaulted fields — see
    /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md Section 4.4.
    /// </summary>
    internal static class RecipeKindResolver
    {
        public static RecipeKind Resolve(RecipeMethodId method, RecipeDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);

            if (method == RecipeMethodId.Package && string.IsNullOrWhiteSpace(document.PackageEngine))
            {
                throw new InvalidOperationException(
                    "PackageEngine must be defaulted before resolving RecipeKind.");
            }

            return method switch
            {
                RecipeMethodId.Container => RecipeKind.BioContainer,
                RecipeMethodId.Mirror => RecipeKind.PackageMirror,
                RecipeMethodId.Source => RecipeKind.SourceBuild,
                RecipeMethodId.SourceStructured => RecipeKind.SourceBuildStructured,
                RecipeMethodId.Dockerfile => RecipeKind.DockerfileFallback,

                RecipeMethodId.Package => document.PackageEngine == "micromamba"
                    ? RecipeKind.Micromamba
                    : RecipeKind.Conda,

                _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unsupported recipe method."),
            };
        }
    }
}
