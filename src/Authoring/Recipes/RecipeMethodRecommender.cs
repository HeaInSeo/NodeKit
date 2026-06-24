using System;
using System.Collections.Generic;
using System.Linq;

namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// Beginner-facing method recommender: the IsRestrictedNetwork gate, then
    /// the general priority table. Answer.Unknown is never treated like No —
    /// it withholds evidence rather than excluding a method. See
    /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md Section 11.
    /// </summary>
    internal static class RecipeMethodRecommender
    {
        private const string ExternalDependencyWarning =
            "내부망에서는 외부 registry나 source archive에 접근하지 못할 수 있습니다.";

        private const string RestrictedNetworkUnknownWarning =
            "내부망인지 확실하지 않다고 답했습니다. 내부망이라면 public channel, 외부 registry, GitHub source 접근이 실패할 수 있습니다.";

        private static readonly (RecipeMethodId Method, Func<RecipeMethodAnswers, Answer> Signal, string Reason)[] _generalPriority =
        {
            (RecipeMethodId.Container, a => a.HasExistingContainerImage, "이미 있는 이미지를 쓰는 것이 가장 빠르고 단순합니다."),
            (RecipeMethodId.Package, a => a.HasPackageInPublicChannels, "일반적인 bioinformatics 도구에 적합합니다."),
            (RecipeMethodId.Source, a => a.HasSourceArchiveAndChecksum, "패키지가 없거나 특정 소스가 필요할 때 사용합니다."),
            (RecipeMethodId.Dockerfile, a => a.HasExistingDockerfile, "마지막 수단이지만 기존 Dockerfile이 있으면 가능합니다."),
        };

        private static readonly RecipeMethodId[] _mirrorYesAlternatives =
        {
            RecipeMethodId.Source, RecipeMethodId.Container, RecipeMethodId.Dockerfile,
        };

        private static readonly RecipeMethodId[] _mirrorUnknownAlternatives =
        {
            RecipeMethodId.Mirror, RecipeMethodId.Source, RecipeMethodId.Container, RecipeMethodId.Dockerfile,
        };

        private static readonly string[] _mirrorYesEvidence =
        {
            "내부망이라고 답했습니다.", "내부 package mirror가 있다고 답했습니다.",
        };

        private static readonly string[] _restrictedNetworkOnlyEvidence = { "내부망이라고 답했습니다." };

        private static readonly string[] _externalDependencyWarnings = { ExternalDependencyWarning };

        private static readonly string[] _mirrorUriMissingInformation = { "내부 package mirror URI를 아는지" };

        public static RecipeMethodRecommendation Recommend(RecipeMethodAnswers answers)
        {
            ArgumentNullException.ThrowIfNull(answers);

            return answers.IsRestrictedNetwork == Answer.Yes
                ? RecommendForRestrictedNetwork(answers)
                : RecommendGeneral(answers);
        }

        private static RecipeMethodRecommendation RecommendForRestrictedNetwork(RecipeMethodAnswers answers)
        {
            if (answers.HasInternalPackageMirror == Answer.Yes)
            {
                return new RecipeMethodRecommendation(
                    RecipeMethodId.Mirror,
                    "내부망에서는 내부 mirror가 가장 자연스럽습니다.",
                    Evidence: _mirrorYesEvidence,
                    Warnings: _externalDependencyWarnings,
                    Alternatives: RestrictedAlternatives(_mirrorYesAlternatives),
                    MissingInformation: Array.Empty<string>());
            }

            var alternativeMethods = answers.HasInternalPackageMirror == Answer.Unknown
                ? _mirrorUnknownAlternatives
                : _mirrorYesAlternatives;

            var missingInformation = answers.HasInternalPackageMirror == Answer.Unknown
                ? _mirrorUriMissingInformation
                : Array.Empty<string>();

            return new RecipeMethodRecommendation(
                RecommendedMethod: null,
                "내부망에서는 mirror 정보 없이 package 설치를 기본 추천하지 않습니다.",
                Evidence: _restrictedNetworkOnlyEvidence,
                Warnings: _externalDependencyWarnings,
                Alternatives: RestrictedAlternatives(alternativeMethods),
                MissingInformation: missingInformation);
        }

        private static RecipeMethodRecommendation RecommendGeneral(RecipeMethodAnswers answers)
        {
            var signals = _generalPriority
                .Select(p => (p.Method, Answer: p.Signal(answers), p.Reason))
                .ToList();

            var yes = signals.Where(s => s.Answer == Answer.Yes).ToList();

            RecipeMethodRecommendation recommendation;
            if (yes.Count > 0)
            {
                var recommended = yes[0];
                var alternatives = signals
                    .Where(s => s.Method != recommended.Method && s.Answer != Answer.No)
                    .Select((s, i) => new RecipeMethodCandidate(s.Method, Label(s.Method), s.Reason, i + 1))
                    .ToList();

                recommendation = new RecipeMethodRecommendation(
                    recommended.Method,
                    recommended.Reason,
                    Evidence: new[] { EvidenceText(recommended.Method) },
                    Warnings: Array.Empty<string>(),
                    Alternatives: alternatives,
                    MissingInformation: Array.Empty<string>());
            }
            else
            {
                var candidates = signals.Where(s => s.Answer != Answer.No).ToList();

                recommendation = new RecipeMethodRecommendation(
                    RecommendedMethod: null,
                    "아직 하나의 작성 방법을 확정 추천하기 어렵습니다.",
                    Evidence: Array.Empty<string>(),
                    Warnings: Array.Empty<string>(),
                    Alternatives: candidates
                        .Select((s, i) => new RecipeMethodCandidate(s.Method, Label(s.Method), s.Reason, i + 1))
                        .ToList(),
                    MissingInformation: candidates.Select(s => MissingInfoText(s.Method)).ToList());
            }

            if (answers.IsRestrictedNetwork == Answer.Unknown
                && recommendation.RecommendedMethod is RecipeMethodId.Container or RecipeMethodId.Package or RecipeMethodId.Source)
            {
                recommendation = recommendation with
                {
                    Warnings = recommendation.Warnings.Append(RestrictedNetworkUnknownWarning).ToList(),
                };
            }

            return recommendation;
        }

        private static List<RecipeMethodCandidate> RestrictedAlternatives(IReadOnlyList<RecipeMethodId> methods) =>
            methods
                .Select((m, i) => new RecipeMethodCandidate(m, Label(m), RestrictedAlternativeReason(m), i + 1))
                .ToList();

        private static string RestrictedAlternativeReason(RecipeMethodId method) => method switch
        {
            RecipeMethodId.Mirror => "내부 mirror URI가 필요합니다.",
            RecipeMethodId.Source => "SourceUri가 내부 mirror 또는 접근 가능한 위치에 있어야 합니다.",
            RecipeMethodId.Container => "ImageRef가 내부 registry에서 접근 가능해야 합니다.",
            RecipeMethodId.Dockerfile => "base image와 build dependency가 내부망에서 접근 가능해야 합니다.",
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "package는 restricted network 대안에 포함되지 않습니다."),
        };

        private static string Label(RecipeMethodId method) => RecipeMethodCatalog.For(method).Label.Get("ko");

        private static string EvidenceText(RecipeMethodId method) => method switch
        {
            RecipeMethodId.Container => "기존 컨테이너 이미지 URI가 있다고 답했습니다.",
            RecipeMethodId.Package => "public channel 패키지가 있다고 답했습니다.",
            RecipeMethodId.Source => "source URL과 checksum이 있다고 답했습니다.",
            RecipeMethodId.Dockerfile => "기존 Dockerfile이 있다고 답했습니다.",
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "지원하지 않는 method입니다."),
        };

        private static string MissingInfoText(RecipeMethodId method) => method switch
        {
            RecipeMethodId.Container => "기존 컨테이너 이미지 URI가 있는지",
            RecipeMethodId.Package => "public channel 패키지가 있는지",
            RecipeMethodId.Source => "source URL과 checksum이 있는지",
            RecipeMethodId.Dockerfile => "기존 Dockerfile이 있는지",
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "지원하지 않는 method입니다."),
        };
    }
}
