using System;
using System.Collections.Generic;
using System.Linq;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Single source of truth for which fields each RecipeMethodId needs and
    /// in what order. Interactive and non-interactive authoring must both
    /// read from here instead of hardcoding per-method field checks. See
    /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md Sections 8-9.
    /// Any field CLAUDE.md Section 3 blocks at L1 (unpinned tag, missing
    /// digest, unpinned package version) is Required here, never
    /// Recommended/Optional — see Section 6 of the same design doc.
    /// </summary>
    internal static class RecipeFieldCatalog
    {
        private static readonly string[] _baseImageFieldExamples =
        {
            "condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
        };

        public static IReadOnlyList<RecipeFieldDescriptor> CommonScalarFields { get; } = new[]
        {
            new RecipeFieldDescriptor(
                Name: "ToolName",
                Type: RecipeFieldType.Scalar,
                Requirement: RecipeFieldRequirement.Required,
                DefaultValue: null,
                Label: Text("도구 이름", "Tool name"),
                Help: Text("recipe에서 식별할 도구 이름입니다.", "The tool name this recipe identifies."),
                Examples: new[] { "bwa-mem" },
                Choices: Array.Empty<RecipeChoice>(),
                Apply: (recipe, value) => recipe.ToolName = (string)value),
            new RecipeFieldDescriptor(
                Name: "ToolVersion",
                Type: RecipeFieldType.Scalar,
                Requirement: RecipeFieldRequirement.Required,
                DefaultValue: null,
                Label: Text("도구 버전", "Tool version"),
                Help: Text("도구 버전 또는 고정된 release/version입니다.", "The tool version or a pinned release/version."),
                Examples: new[] { "0.7.17" },
                Choices: Array.Empty<RecipeChoice>(),
                Apply: (recipe, value) => recipe.Version = (string)value),
            new RecipeFieldDescriptor(
                Name: "Script",
                Type: RecipeFieldType.Scalar,
                Requirement: RecipeFieldRequirement.Required,
                DefaultValue: null,
                Label: Text("실행 스크립트", "Run script"),
                Help: Text(
                    "도구 실행 시 사용할 스크립트 경로 또는 명령입니다. BuildRequest의 필수 필드입니다.",
                    "The script path or command to run the tool. Required by BuildRequest."),
                Examples: new[] { "run.sh" },
                Choices: Array.Empty<RecipeChoice>(),
                Apply: (recipe, value) => recipe.Script = (string)value),
        };

        public static RecipeFieldDescriptor InputsField { get; } = new(
            Name: "Inputs",
            Type: RecipeFieldType.InputList,
            Requirement: RecipeFieldRequirement.Required,
            DefaultValue: null,
            Label: Text("입력", "Inputs"),
            Help: Text("최소 1개 이상의 입력 정의가 필요합니다.", "At least one input definition is required."),
            Examples: Array.Empty<string>(),
            Choices: Array.Empty<RecipeChoice>(),
            Apply: (recipe, value) => recipe.Inputs.Add((ToolInput)value));

        public static RecipeFieldDescriptor OutputsField { get; } = new(
            Name: "Outputs",
            Type: RecipeFieldType.OutputList,
            Requirement: RecipeFieldRequirement.Required,
            DefaultValue: null,
            Label: Text("출력", "Outputs"),
            Help: Text("최소 1개 이상의 출력 정의가 필요합니다.", "At least one output definition is required."),
            Examples: Array.Empty<string>(),
            Choices: Array.Empty<RecipeChoice>(),
            Apply: (recipe, value) => recipe.Outputs.Add((ToolOutput)value));

        public static IReadOnlyDictionary<RecipeMethodId, IReadOnlyList<RecipeFieldDescriptor>> MethodFields { get; } =
            new Dictionary<RecipeMethodId, IReadOnlyList<RecipeFieldDescriptor>>
            {
                [RecipeMethodId.Container] = new[]
                {
                    new RecipeFieldDescriptor(
                        Name: "ImageRef",
                        Type: RecipeFieldType.Scalar,
                        Requirement: RecipeFieldRequirement.Required,
                        DefaultValue: null,
                        Label: Text("이미지 참조", "Image reference"),
                        Help: Text(
                            "사용할 컨테이너 이미지 참조입니다. tag-only 입력은 authoring 중에는 막지 않지만, digest가 없으면 최종 검증에서 block됩니다.",
                            "The container image reference to use. Tag-only input is allowed mid-authoring, but final validation blocks it without a digest."),
                        Examples: new[] { "condaforge/miniforge3:24.3.0-0" },
                        Choices: Array.Empty<RecipeChoice>(),
                        Apply: (recipe, value) => recipe.BaseImage = (string)value),
                    new RecipeFieldDescriptor(
                        Name: "ImageDigest",
                        Type: RecipeFieldType.Scalar,
                        Requirement: RecipeFieldRequirement.Required,
                        DefaultValue: null,
                        Label: Text("이미지 digest", "Image digest"),
                        Help: Text(
                            "digest 고정입니다. CLAUDE.md 3번 섹션 L1 규칙에 따라 final validation에서 필수입니다.",
                            "The pinned digest. CLAUDE.md Section 3's L1 rule requires it at final validation."),
                        Examples: new[] { "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" },
                        Choices: Array.Empty<RecipeChoice>(),
                        Apply: (recipe, value) => recipe.ImageDigest = (string)value),
                    new RecipeFieldDescriptor(
                        Name: "Command",
                        Type: RecipeFieldType.StringList,
                        Requirement: RecipeFieldRequirement.Optional,
                        DefaultValue: null,
                        Label: Text("실행 명령", "Command"),
                        Help: Text(
                            "이미지 기본 entrypoint를 그대로 쓰지 않을 경우의 명령입니다.",
                            "The command to use instead of the image's default entrypoint."),
                        Examples: Array.Empty<string>(),
                        Choices: Array.Empty<RecipeChoice>(),
                        Apply: (recipe, value) => recipe.Command.Add((string)value)),
                },
                [RecipeMethodId.Package] = new[]
                {
                    BaseImageField(),
                    new RecipeFieldDescriptor(
                        Name: "Packages",
                        Type: RecipeFieldType.StringList,
                        Requirement: RecipeFieldRequirement.Required,
                        DefaultValue: null,
                        Label: Text("패키지 목록", "Packages"),
                        Help: Text("설치할 package 목록입니다.", "The list of packages to install."),
                        Examples: new[] { "bwa=0.7.17=h5bf99c6_8" },
                        Choices: Array.Empty<RecipeChoice>(),
                        Apply: (recipe, value) => recipe.Packages.Add((string)value)),
                    new RecipeFieldDescriptor(
                        Name: "Channels",
                        Type: RecipeFieldType.StringList,
                        Requirement: RecipeFieldRequirement.Required,
                        DefaultValue: null,
                        Label: Text("채널 목록", "Channels"),
                        Help: Text("package channel 목록입니다.", "The list of package channels."),
                        Examples: new[] { "bioconda", "conda-forge" },
                        Choices: Array.Empty<RecipeChoice>(),
                        Apply: (recipe, value) => recipe.Channels.Add((string)value)),
                    new RecipeFieldDescriptor(
                        Name: "PackageEngine",
                        Type: RecipeFieldType.Choice,
                        Requirement: RecipeFieldRequirement.Defaulted,
                        DefaultValue: "conda",
                        Label: Text("패키지 엔진", "Package engine"),
                        Help: Text(
                            "값이 없으면 Build() 단계에서 conda가 적용됩니다.",
                            "If absent, Build() applies conda."),
                        Examples: Array.Empty<string>(),
                        Choices: new[]
                        {
                            new RecipeChoice("conda", Text("conda", "conda"), Text("conda 패키지 매니저", "The conda package manager")),
                            new RecipeChoice("micromamba", Text("micromamba", "micromamba"), Text("micromamba 패키지 매니저", "The micromamba package manager")),
                        },
                        Apply: (recipe, value) => recipe.PackageEngine = (string)value),
                },
                [RecipeMethodId.Mirror] = new[]
                {
                    BaseImageField(),
                    new RecipeFieldDescriptor(
                        Name: "MirrorUri",
                        Type: RecipeFieldType.Scalar,
                        Requirement: RecipeFieldRequirement.Required,
                        DefaultValue: null,
                        Label: Text("미러 URI", "Mirror URI"),
                        Help: Text("내부 package mirror URI입니다.", "The internal package mirror URI."),
                        Examples: new[] { "https://mirror.internal/conda-channel" },
                        Choices: Array.Empty<RecipeChoice>(),
                        Apply: (recipe, value) => recipe.PackageMirrorUri = (string)value),
                    new RecipeFieldDescriptor(
                        Name: "Packages",
                        Type: RecipeFieldType.StringList,
                        Requirement: RecipeFieldRequirement.Required,
                        DefaultValue: null,
                        Label: Text("패키지 목록", "Packages"),
                        Help: Text("설치할 package 목록입니다.", "The list of packages to install."),
                        Examples: new[] { "bwa=0.7.17=h5bf99c6_8" },
                        Choices: Array.Empty<RecipeChoice>(),
                        Apply: (recipe, value) => recipe.Packages.Add((string)value)),
                    new RecipeFieldDescriptor(
                        Name: "MirrorKind",
                        Type: RecipeFieldType.Scalar,
                        Requirement: RecipeFieldRequirement.Optional,
                        DefaultValue: null,
                        Label: Text("미러 종류", "Mirror kind"),
                        Help: Text("mirror 종류입니다. v1에서는 optional입니다.", "The mirror kind. Optional in v1."),
                        Examples: Array.Empty<string>(),
                        Choices: Array.Empty<RecipeChoice>(),
                        Apply: (recipe, value) => recipe.MirrorKind = (string)value),
                },
                [RecipeMethodId.Source] = new[]
                {
                    BaseImageField(),
                    new RecipeFieldDescriptor(
                        Name: "SourceUri",
                        Type: RecipeFieldType.Scalar,
                        Requirement: RecipeFieldRequirement.Required,
                        DefaultValue: null,
                        Label: Text("소스 URI", "Source URI"),
                        Help: Text("source archive 또는 release URI입니다.", "The source archive or release URI."),
                        Examples: new[] { "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz" },
                        Choices: Array.Empty<RecipeChoice>(),
                        Apply: (recipe, value) => recipe.SourceUri = (string)value),
                    new RecipeFieldDescriptor(
                        Name: "SourceChecksum",
                        Type: RecipeFieldType.Scalar,
                        Requirement: RecipeFieldRequirement.Required,
                        DefaultValue: null,
                        Label: Text("소스 체크섬", "Source checksum"),
                        Help: Text(
                            "v1에서는 sha256:<64 hex> 형식만 허용합니다.",
                            "v1 only accepts the sha256:<64 hex> format."),
                        Examples: new[] { "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" },
                        Choices: Array.Empty<RecipeChoice>(),
                        Apply: (recipe, value) => recipe.SourceChecksum = (string)value),
                    new RecipeFieldDescriptor(
                        Name: "SourceBuildCommands",
                        Type: RecipeFieldType.StringList,
                        Requirement: RecipeFieldRequirement.Required,
                        DefaultValue: null,
                        Label: Text("빌드 명령", "Build commands"),
                        Help: Text("source를 빌드하는 명령어 목록입니다.", "The list of commands that build the source."),
                        Examples: new[] { "make", "make install" },
                        Choices: Array.Empty<RecipeChoice>(),
                        Apply: (recipe, value) => recipe.SourceBuildCommands.Add((string)value)),
                    new RecipeFieldDescriptor(
                        Name: "BuildDependencies",
                        Type: RecipeFieldType.StringList,
                        Requirement: RecipeFieldRequirement.Recommended,
                        DefaultValue: null,
                        Label: Text("빌드 의존성", "Build dependencies"),
                        Help: Text(
                            "빌드에 필요한 dependency 목록입니다 — 없어도 되지만 재현성을 위해 명시를 권장합니다.",
                            "The dependencies the build needs — optional, but recommended for reproducibility."),
                        Examples: Array.Empty<string>(),
                        Choices: Array.Empty<RecipeChoice>(),
                        Apply: (recipe, value) => recipe.BuildDependencies.Add((string)value)),
                },
                [RecipeMethodId.Dockerfile] = new[]
                {
                    BaseImageField(),
                    new RecipeFieldDescriptor(
                        Name: "DockerfilePath",
                        Type: RecipeFieldType.Scalar,
                        Requirement: RecipeFieldRequirement.Required,
                        DefaultValue: null,
                        Label: Text("Dockerfile 경로", "Dockerfile path"),
                        Help: Text(
                            "Dockerfile 위치입니다. DockerfileContent 대신 사용할 수 있습니다.",
                            "The Dockerfile location. Usable instead of DockerfileContent."),
                        Examples: new[] { "./Dockerfile" },
                        Choices: Array.Empty<RecipeChoice>(),
                        Apply: (recipe, value) => recipe.DockerfilePath = (string)value),
                    new RecipeFieldDescriptor(
                        Name: "DockerfileContent",
                        Type: RecipeFieldType.Scalar,
                        Requirement: RecipeFieldRequirement.Required,
                        DefaultValue: null,
                        Label: Text("Dockerfile 내용", "Dockerfile content"),
                        Help: Text(
                            "Dockerfile 내용입니다. DockerfilePath 대신 사용할 수 있습니다.",
                            "The Dockerfile content. Usable instead of DockerfilePath."),
                        Examples: Array.Empty<string>(),
                        Choices: Array.Empty<RecipeChoice>(),
                        Apply: (recipe, value) => recipe.DockerfileContent = (string)value),
                    new RecipeFieldDescriptor(
                        Name: "BuildContext",
                        Type: RecipeFieldType.Scalar,
                        Requirement: RecipeFieldRequirement.Defaulted,
                        DefaultValue: ".",
                        Label: Text("빌드 컨텍스트", "Build context"),
                        Help: Text(
                            "Docker build context입니다. 값이 없으면 현재 디렉터리가 적용됩니다.",
                            "The Docker build context. Defaults to the current directory when absent."),
                        Examples: Array.Empty<string>(),
                        Choices: Array.Empty<RecipeChoice>(),
                        Apply: (recipe, value) => recipe.BuildContext = (string)value),
                },
            };

        public static IReadOnlyList<RecipeFieldDescriptor> FieldsFor(RecipeMethodId method) =>
            CommonScalarFields
                .Concat(MethodFields[method])
                .Append(InputsField)
                .Append(OutputsField)
                .ToList();

        public static IReadOnlyList<RecipeFieldDescriptor> BlockingRequiredFieldsFor(RecipeMethodId method) =>
            FieldsFor(method).Where(f => f.Requirement == RecipeFieldRequirement.Required).ToList();

        public static IReadOnlyList<RecipeFieldDescriptor> DefaultedFieldsFor(RecipeMethodId method) =>
            FieldsFor(method).Where(f => f.Requirement == RecipeFieldRequirement.Defaulted).ToList();

        public static IReadOnlyList<RecipeFieldDescriptor> RecommendedFieldsFor(RecipeMethodId method) =>
            FieldsFor(method).Where(f => f.Requirement == RecipeFieldRequirement.Recommended).ToList();

        // Package/Mirror/Source/Dockerfile methods all render recipe.BaseImage
        // directly as ToolDefinition.ImageUri (no separate ImageDigest field
        // combining like Container's BioContainerImageUri) — the digest must
        // be embedded in this field's value itself to pass ImageUriValidator's
        // L1 digest-pinning rule. See RecipeRenderer.RenderInstallerFamily/
        // RenderSourceBuild and RecipeValidator.ValidateBaseImagePresent.
        private static RecipeFieldDescriptor BaseImageField() => new(
            Name: "ImageRef",
            Type: RecipeFieldType.Scalar,
            Requirement: RecipeFieldRequirement.Required,
            DefaultValue: null,
            Label: Text("기반 이미지", "Base image"),
            Help: Text(
                "이 빌드의 기반이 되는 컨테이너 이미지입니다. 별도의 digest 필드가 없으므로 이 값 자체에 @sha256:... digest를 포함해야 최종 검증을 통과합니다.",
                "The base container image for this build. There is no separate digest field, so this value itself must include the @sha256:... digest to pass final validation."),
            Examples: _baseImageFieldExamples,
            Choices: Array.Empty<RecipeChoice>(),
            Apply: (recipe, value) => recipe.BaseImage = (string)value);

        private static LocalizedText Text(string ko, string en) =>
            new(new Dictionary<string, string> { ["ko"] = ko, ["en"] = en });
    }
}
