using System.Collections.Generic;

namespace NodeKit.Cli
{
    /// <summary>
    /// Per-question explanation/example/impact text for the 빠른 설정 모드 Q&amp;A.
    /// See docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_V0.9.2.md Sections 15.3-15.8.
    /// Keyed by RecipeMethodQuestion.Key (== RecipeMethodAnswers property name).
    /// </summary>
    internal static class RecipeMethodQuestionDetailCatalog
    {
        public static IReadOnlyDictionary<string, RecipeMethodQuestionDetail> ByKey { get; } =
            new Dictionary<string, RecipeMethodQuestionDetail>(System.StringComparer.Ordinal)
            {
                ["IsRestrictedNetwork"] = new(
                    Header: "Q1. 내부망/폐쇄망 환경인가요?",
                    Meaning: "현재 환경에서 public 인터넷으로 Docker 이미지나 conda 패키지를 받을 수 없는지 묻는 질문입니다.",
                    Examples: new[]
                    {
                        "회사/학교 내부망에서만 패키지를 받을 수 있음",
                        "외부 인터넷 접근이 차단됨",
                        "내부 mirror나 사내 registry만 사용해야 함",
                    },
                    YesEffects: new[]
                    {
                        "mirror 방식이 우선 후보가 됩니다.",
                        "내부 package mirror URI를 물어봅니다.",
                        "public channel 기반 package 방식은 뒤로 밀립니다.",
                    },
                    NoEffects: new[]
                    {
                        "public channel, container, source 방식이 일반 후보로 유지됩니다.",
                    },
                    EnterEffects: new[]
                    {
                        "unknown으로 처리합니다.",
                        "이후 답변을 바탕으로 보수적으로 추천합니다.",
                    }),

                ["HasInternalPackageMirror"] = new(
                    Header: "Q2. 내부 package mirror URI를 알고 있나요?",
                    Meaning: "회사/학교/기관에서 제공하는 내부 conda 또는 pip 저장소 주소를 알고 있는지 묻는 질문입니다.",
                    Examples: new[]
                    {
                        "https://mirror.company.local/conda",
                        "https://packages.school.local/conda",
                    },
                    YesEffects: new[]
                    {
                        "mirror 방식을 추천할 수 있습니다.",
                        "이후 PackageMirrorUri를 입력하게 됩니다.",
                    },
                    NoEffects: new[]
                    {
                        "mirror 방식 추천 우선순위가 낮아집니다.",
                    },
                    EnterEffects: new[]
                    {
                        "mirror 방식은 보수적으로 뒤로 밀립니다.",
                    }),

                ["HasExistingContainerImage"] = new(
                    Header: "Q3. 기존 컨테이너 이미지 주소를 알고 있나요?",
                    Meaning: "이미 실행 가능한 Docker/OCI 이미지 주소를 알고 있는지 묻는 질문입니다.",
                    Examples: new[]
                    {
                        "quay.io/biocontainers/bwa:0.7.17--h7132678_9@sha256:...",
                        "ghcr.io/example/tool:1.0.0@sha256:...",
                    },
                    YesEffects: new[]
                    {
                        "container 방식이 강한 후보가 됩니다.",
                        "이후 ImageRef와 ImageDigest를 입력하게 됩니다.",
                        "digest가 없으면 최종 검증에서 실패합니다.",
                    },
                    NoEffects: new[]
                    {
                        "package, source, dockerfile 방식이 더 적합할 수 있습니다.",
                    },
                    EnterEffects: new[]
                    {
                        "다른 답변을 바탕으로 추천합니다.",
                    }),

                ["HasPackageInPublicChannels"] = new(
                    Header: "Q4. public channel에 패키지가 있나요?",
                    Meaning: "bioconda, conda-forge 같은 공개 conda channel에서 설치할 수 있는지 묻는 질문입니다.",
                    Examples: new[]
                    {
                        "conda install -c bioconda bwa=0.7.17",
                        "conda install -c conda-forge python=3.11",
                    },
                    YesEffects: new[]
                    {
                        "package 방식을 추천할 수 있습니다.",
                        "이후 Packages, Channels, ImageRef를 입력하게 됩니다.",
                        "패키지 버전이 고정되어 있지 않으면 validate에서 실패할 수 있습니다.",
                    },
                    NoEffects: new[]
                    {
                        "source, container, dockerfile, mirror 방식이 더 적합할 수 있습니다.",
                    },
                    EnterEffects: new[]
                    {
                        "package 방식은 가능 후보로 유지하되 확신도는 낮게 둡니다.",
                    }),

                ["HasSourceArchiveAndChecksum"] = new(
                    Header: "Q5. source URL과 checksum이 있나요?",
                    Meaning: "소스코드 archive를 직접 받아 빌드할 수 있고, 그 파일의 sha256 checksum을 알고 있는지 묻는 질문입니다.",
                    Examples: new[]
                    {
                        "SourceUri: https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
                        "SourceChecksum: sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                    },
                    YesEffects: new[]
                    {
                        "source 방식이 후보가 됩니다.",
                        "이후 SourceUri, SourceChecksum, SourceBuildCommands를 입력하게 됩니다.",
                        "checksum이 없으면 validate에서 실패합니다.",
                    },
                    NoEffects: new[]
                    {
                        "source 방식 추천 우선순위가 낮아집니다.",
                    },
                    EnterEffects: new[]
                    {
                        "source 방식은 보수적으로 뒤로 밀립니다.",
                    }),

                ["HasExistingDockerfile"] = new(
                    Header: "Q6. 기존 Dockerfile이 있나요?",
                    Meaning: "이미 작성된 Dockerfile 파일이 있는지 묻는 질문입니다.",
                    Examples: new[] { "./Dockerfile" },
                    YesEffects: new[]
                    {
                        "dockerfile 방식을 선택할 수 있습니다.",
                        "이후 DockerfilePath 또는 DockerfileContent를 입력하게 됩니다.",
                        "Dockerfile의 첫 FROM과 BaseImage가 정확히 같아야 합니다.",
                        "모든 FROM 이미지는 latest 태그 없이 digest로 고정되어야 합니다.",
                    },
                    NoEffects: new[]
                    {
                        "dockerfile 방식 추천 우선순위가 낮아집니다.",
                    },
                    EnterEffects: new[]
                    {
                        "dockerfile 방식은 보수적으로 뒤로 밀립니다.",
                    }),
            };
    }
}
