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

        // RecipeRenderer는 Packages/Channels/PackageMirrorUri를 셸 인용 없이
        // 그대로 "RUN conda install ..." / "RUN conda config --add channels ..."
        // 줄에 이어 붙인다(RecipeRenderer.RenderInstallerFamily). PackageVersionValidator는
        // "버전이 있는가"만 보고 "&&"/파이프/백틱 같은 셸 메타문자를 막지 않으므로,
        // 렌더링 전 단계에서 conda 패키지/채널 문법(name=version[=build])만 허용하는
        // allowlist로 막아야 한다.
        private static readonly Regex _packageSpecPattern =
            new(@"^[A-Za-z0-9_.:+-]+(=[A-Za-z0-9_.:+-]+){1,2}$", RegexOptions.Compiled);

        private static readonly Regex _channelOrMirrorUriPattern =
            new(@"^[A-Za-z0-9_.:/+-]+$", RegexOptions.Compiled);

        // SourceUri는 RecipeRenderer.RenderSourceBuild에서 큰따옴표로 감싸 붙지만
        // ("curl -fsSL -o source.tar.gz \"" + SourceUri + "\""), 값 안에 큰따옴표/
        // 백틱/달러/백슬래시가 있으면 그 인용을 깨고 나올 수 있다. http(s) 스킴을
        // 강제하고 그 네 가지 이스케이프 문자와 공백(개행 포함)을 차단한다.
        private static readonly Regex _sourceUriPattern =
            new(@"^https?://[^\s""'`$\\]+$", RegexOptions.Compiled);

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
                    ValidatePackageFormats(recipe, violations);
                    ValidateChannelFormats(recipe, violations);
                    break;
                case RecipeBuildKind.PackageMirror:
                    ValidateBaseImagePresent(recipe, violations);
                    ValidatePackagesPresent(recipe, violations);
                    ValidatePackageFormats(recipe, violations);
                    if (string.IsNullOrWhiteSpace(recipe.PackageMirrorUri))
                    {
                        violations.Add(new ValidationViolation(
                            "L1-RCP-003",
                            "package mirror build kind에는 PackageMirrorUri가 필요합니다.",
                            nameof(recipe.PackageMirrorUri)));
                    }
                    else if (!_channelOrMirrorUriPattern.IsMatch(recipe.PackageMirrorUri.Trim()))
                    {
                        violations.Add(new ValidationViolation(
                            "L1-RCP-013",
                            $"PackageMirrorUri에 허용되지 않는 문자가 포함되어 있습니다: '{recipe.PackageMirrorUri}'",
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

        private static void ValidatePackageFormats(RecipeDocument recipe, List<ValidationViolation> violations)
        {
            foreach (var package in recipe.Packages)
            {
                if (string.IsNullOrWhiteSpace(package))
                {
                    continue;
                }

                if (!_packageSpecPattern.IsMatch(package.Trim()))
                {
                    violations.Add(new ValidationViolation(
                        "L1-RCP-011",
                        $"패키지 지정에 허용되지 않는 문자가 포함되어 있습니다. 'name=version' 또는 'name=version=build' 형식만 허용됩니다: '{package}'",
                        nameof(recipe.Packages)));
                }
            }
        }

        private static void ValidateChannelFormats(RecipeDocument recipe, List<ValidationViolation> violations)
        {
            foreach (var channel in recipe.Channels)
            {
                if (string.IsNullOrWhiteSpace(channel))
                {
                    continue;
                }

                if (!_channelOrMirrorUriPattern.IsMatch(channel.Trim()))
                {
                    violations.Add(new ValidationViolation(
                        "L1-RCP-012",
                        $"채널 이름에 허용되지 않는 문자가 포함되어 있습니다: '{channel}'",
                        nameof(recipe.Channels)));
                }
            }
        }

        // SourceBuildCommands는 의도적으로 allowlist 대상에서 제외한다 — 이 필드의
        // 목적 자체가 "make", "./configure --prefix=/usr && make -j4"처럼 셸
        // 빌드 단계를 그대로 실행하는 것이라, 셸 메타문자를 막으면 기능을 깨뜨린다.
        // Packages/Channels/SourceUri와 달리 "패키지명"이나 "URI"처럼 좁은 문법을
        // 갖지 않는 자유 형식 필드이므로 여기서는 무엇을 막을지 정의할 수 없다.
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

            if (!string.IsNullOrWhiteSpace(recipe.SourceUri) && !_sourceUriPattern.IsMatch(recipe.SourceUri.Trim()))
            {
                violations.Add(new ValidationViolation(
                    "L1-RCP-014",
                    $"SourceUri 형식이 올바르지 않거나 안전하지 않은 문자가 포함되어 있습니다. http(s) URI만 허용되며 공백/큰따옴표/작은따옴표/백틱/$/백슬래시는 사용할 수 없습니다: '{recipe.SourceUri}'",
                    nameof(recipe.SourceUri)));
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
