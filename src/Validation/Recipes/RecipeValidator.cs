using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NodeKit.Authoring.Recipes;
using NodeKit.Policy;

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

        // DockGuard policy/security/security.rego DSF001/DSF002와 동일한 규칙.
        // WasmPolicyChecker는 GUI에만 배선되어 있고 NodeVault도 이 정책을
        // 재검사하지 않으므로(PLATFORM_MAP.md 확인), dockerfile fallback으로
        // 제출되는 recipe는 이 두 규칙을 하드코딩으로 재구현하지 않으면 완전히
        // 우회한다. 다른 build kind(Conda/Micromamba/PackageMirror/SourceBuild/
        // BioContainer)는 RecipeRenderer가 자동 생성하는 템플릿이라 USER 명령어를
        // 두지 않으므로 여기 적용하면 안 된다 — dockerfile fallback만 사용자가
        // Dockerfile 전체를 직접 작성하는 유일한 build kind다.
        private static readonly Regex _envSecretKeyPattern = new(
            @"\b(PASSWORD|SECRET|API_KEY|TOKEN|PASSWD)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
                    else
                    {
                        ValidateDockerfileFallbackSecurity(recipe, violations);
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

        private static void ValidateDockerfileFallbackSecurity(RecipeDocument recipe, List<ValidationViolation> violations)
        {
            var instructions = DockerfileParser.Parse(recipe.DockerfileContent);
            if (instructions.Count == 0)
            {
                return;
            }

            var userInstructions = instructions.FindAll(i => string.Equals(i.Cmd, "USER", StringComparison.Ordinal));
            if (userInstructions.Count == 0)
            {
                violations.Add(new ValidationViolation(
                    "L1-RCP-009",
                    "Dockerfile에 USER 명령어가 없습니다. root 권한 실행을 방지하려면 비루트 사용자를 지정하세요. (예: USER 1000 또는 USER nonroot)",
                    nameof(recipe.DockerfileContent)));
            }
            else
            {
                foreach (var instruction in userInstructions)
                {
                    var user = instruction.Value.Count > 0 ? instruction.Value[0] : string.Empty;
                    if (string.Equals(user, "root", StringComparison.OrdinalIgnoreCase) || user == "0")
                    {
                        violations.Add(new ValidationViolation(
                            "L1-RCP-009",
                            $"USER root 또는 USER 0은 허용되지 않습니다. 비루트 사용자를 지정하세요. (예: USER 1000 또는 USER nonroot) ({instruction.Raw})",
                            nameof(recipe.DockerfileContent)));
                    }
                }
            }

            foreach (var instruction in instructions)
            {
                if (!string.Equals(instruction.Cmd, "ENV", StringComparison.Ordinal))
                {
                    continue;
                }

                if (_envSecretKeyPattern.IsMatch(instruction.Raw))
                {
                    violations.Add(new ValidationViolation(
                        "L1-RCP-010",
                        $"ENV 명령어에 비밀 키 패턴이 포함된 변수명이 있습니다: '{instruction.Raw}'. 비밀 값은 Dockerfile에 하드코딩하지 마세요.",
                        nameof(recipe.DockerfileContent)));
                }
            }
        }
    }
}
