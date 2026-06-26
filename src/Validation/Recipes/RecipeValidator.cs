using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NodeKit.Authoring.Recipes;

namespace NodeKit.Validation.Recipes
{
    /// <summary>
    /// Recipe-level L1 validation. Checks only what is invisible after
    /// RecipeRenderer flattens a RecipeDocument into a ToolDefinition —
    /// build-kind payload completeness and source checksum format. Everything
    /// else (name/version/digest pinning/Dockerfile structure) is already
    /// covered by the existing ToolDefinition-level validators once rendered,
    /// so it is intentionally not duplicated here.
    /// </summary>
    internal static class RecipeValidator
    {
        private static readonly Regex _sourceChecksumPattern =
            new(@"^sha256:[0-9a-fA-F]{64}$", RegexOptions.Compiled);

        public static ValidationResult Validate(RecipeDocument recipe)
        {
            ArgumentNullException.ThrowIfNull(recipe);

            var violations = new List<ValidationViolation>();

            switch (recipe.BuildKind!.Value)
            {
                case RecipeBuildKind.Conda:
                case RecipeBuildKind.Micromamba:
                    ValidateBaseImagePresent(recipe, violations);
                    ValidatePackagesPresent(recipe, violations);
                    break;
                case RecipeBuildKind.PackageMirror:
                    ValidateBaseImagePresent(recipe, violations);
                    ValidatePackagesPresent(recipe, violations);
                    if (string.IsNullOrWhiteSpace(recipe.PackageMirrorUri))
                    {
                        violations.Add(new ValidationViolation(
                            "L1-RCP-003",
                            "package mirror build kind에는 PackageMirrorUri가 필요합니다.",
                            nameof(recipe.PackageMirrorUri)));
                    }

                    break;
                case RecipeBuildKind.BioContainer:
                    if (string.IsNullOrWhiteSpace(recipe.BioContainerImageUri))
                    {
                        violations.Add(new ValidationViolation(
                            "L1-RCP-005",
                            "BioContainer build kind에는 BioContainerImageUri가 필요합니다.",
                            nameof(recipe.BioContainerImageUri)));
                    }

                    break;
                case RecipeBuildKind.SourceBuild:
                    ValidateBaseImagePresent(recipe, violations);
                    ValidateSourceBuild(recipe, violations);
                    break;
                case RecipeBuildKind.DockerfileFallback:
                    ValidateBaseImagePresent(recipe, violations);
                    if (string.IsNullOrWhiteSpace(recipe.DockerfileContent))
                    {
                        violations.Add(new ValidationViolation(
                            "L1-RCP-008",
                            "dockerfile fallback build kind에는 DockerfileContent가 필요합니다.",
                            nameof(recipe.DockerfileContent)));
                    }

                    break;
            }

            return new ValidationResult(violations);
        }

        private static void ValidateBaseImagePresent(RecipeDocument recipe, List<ValidationViolation> violations)
        {
            if (string.IsNullOrWhiteSpace(recipe.BaseImage))
            {
                violations.Add(new ValidationViolation(
                    "L1-RCP-001",
                    $"{recipe.BuildKind} build kind에는 BaseImage가 필요합니다.",
                    nameof(recipe.BaseImage)));
            }
        }

        private static void ValidatePackagesPresent(RecipeDocument recipe, List<ValidationViolation> violations)
        {
            if (recipe.Packages.Count == 0)
            {
                violations.Add(new ValidationViolation(
                    "L1-RCP-002",
                    $"{recipe.BuildKind} build kind에는 최소 1개 이상의 패키지가 필요합니다.",
                    nameof(recipe.Packages)));
            }
        }

        private static void ValidateSourceBuild(RecipeDocument recipe, List<ValidationViolation> violations)
        {
            if (string.IsNullOrWhiteSpace(recipe.SourceUri))
            {
                violations.Add(new ValidationViolation(
                    "L1-RCP-006",
                    "source build kind에는 SourceUri가 필요합니다.",
                    nameof(recipe.SourceUri)));
            }

            if (string.IsNullOrWhiteSpace(recipe.SourceChecksum))
            {
                violations.Add(new ValidationViolation(
                    "L1-SRC-001",
                    "source build kind에는 SourceChecksum이 필요합니다 — 체크섬 없이는 재현성을 보장할 수 없습니다.",
                    nameof(recipe.SourceChecksum)));
            }
            else if (!_sourceChecksumPattern.IsMatch(recipe.SourceChecksum))
            {
                violations.Add(new ValidationViolation(
                    "L1-SRC-002",
                    $"SourceChecksum 형식이 올바르지 않습니다. 'sha256:<64자리 16진수>' 형식이어야 합니다. ({recipe.SourceChecksum})",
                    nameof(recipe.SourceChecksum)));
            }

            if (recipe.SourceBuildCommands.Count == 0)
            {
                violations.Add(new ValidationViolation(
                    "L1-RCP-007",
                    "source build kind에는 최소 1개 이상의 build command가 필요합니다.",
                    nameof(recipe.SourceBuildCommands)));
            }
        }
    }
}
