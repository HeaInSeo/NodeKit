using System.Collections.Generic;
using NodeKit.Authoring.Recipes;

namespace NodeKit.Cli
{
    /// <summary>
    /// Section 16 post-recommendation summary screen: shows recommended method,
    /// reason, effects, upcoming fields, and cautions; handles accept/reject
    /// and manual method selection from a fixed 1-5 menu.
    /// See docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_V0.9.2.md Section 16.
    /// </summary>
    internal static class MethodRecommendationPresenter
    {
        private static readonly IReadOnlyList<RecipeMethodId> MethodOrder = new[]
        {
            RecipeMethodId.Container,
            RecipeMethodId.Package,
            RecipeMethodId.Mirror,
            RecipeMethodId.Source,
            RecipeMethodId.Dockerfile,
        };

        private static readonly IReadOnlyDictionary<RecipeMethodId, IReadOnlyList<string>> Effects =
            new Dictionary<RecipeMethodId, IReadOnlyList<string>>
            {
                [RecipeMethodId.Container] = new[]
                {
                    "기존 컨테이너 이미지를 그대로 사용하는 recipe를 생성합니다.",
                    "나중에 legacy BuildRequest로 render할 수 있습니다.",
                },
                [RecipeMethodId.Package] = new[]
                {
                    "conda/micromamba 기반 이미지 recipe를 생성합니다.",
                    "패키지와 채널 정보를 recipe에 기록합니다.",
                    "나중에 legacy BuildRequest로 render할 수 있습니다.",
                },
                [RecipeMethodId.Mirror] = new[]
                {
                    "내부 package mirror를 사용하는 recipe를 생성합니다.",
                    "나중에 legacy BuildRequest로 render할 수 있습니다.",
                },
                [RecipeMethodId.Source] = new[]
                {
                    "source archive를 받아 빌드하는 recipe를 생성합니다.",
                    "나중에 legacy BuildRequest로 render할 수 있습니다.",
                },
                [RecipeMethodId.Dockerfile] = new[]
                {
                    "Dockerfile을 직접 사용하는 recipe를 생성합니다.",
                    "나중에 legacy BuildRequest로 render할 수 있습니다.",
                },
            };

        private static readonly IReadOnlyDictionary<RecipeMethodId, IReadOnlyList<string>> Cautions =
            new Dictionary<RecipeMethodId, IReadOnlyList<string>>
            {
                [RecipeMethodId.Container] = new[]
                {
                    "ImageRef는 digest로 고정되어야 합니다.",
                    },
                [RecipeMethodId.Package] = new[]
                {
                    "BaseImage(ImageRef)는 digest로 고정되어야 합니다.",
                    "Packages는 버전이 고정되어야 합니다.",
                    },
                [RecipeMethodId.Mirror] = new[]
                {
                    "BaseImage(ImageRef)는 digest로 고정되어야 합니다.",
                    "Packages는 버전이 고정되어야 합니다.",
                    },
                [RecipeMethodId.Source] = new[]
                {
                    "BaseImage(ImageRef)는 digest로 고정되어야 합니다.",
                    "SourceChecksum은 sha256 형식이어야 합니다.",
                    },
                [RecipeMethodId.Dockerfile] = new[]
                {
                    "Dockerfile 내 모든 FROM 이미지는 latest 태그 없이 digest로 고정되어야 합니다.",
                    },
            };

        /// <summary>
        /// Shows the Section 16 recommendation result and returns the confirmed method.
        /// Loops internally until the user accepts or picks manually.
        /// </summary>
        public static RecipeMethodId Present(RecipeMethodRecommendation recommendation, IRecipeConsole console)
        {
            while (true)
            {
                if (recommendation.RecommendedMethod is { } recommended)
                {
                    PrintRecommendedSummary(recommended, recommendation, console);
                    console.WriteLine("이 방식으로 진행할까요? [Y/n]");
                    console.WriteHints("이전 질문 화면으로 돌아가려면 /back을 입력하세요.");
                    var response = (console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
                    RecipeCreateEscapeCommands.ThrowIfEscape(response);
                    if (response != "n")
                    {
                        return recommended;
                    }

                    console.WriteLine();
                }
                else
                {
                    console.WriteLine($"추천 보류: {recommendation.Reason}");
                    foreach (var mi in recommendation.MissingInformation)
                    {
                        console.WriteLine($"  추가로 필요한 정보: {mi}");
                    }

                    console.WriteLine();
                }

                var method = PromptManualMethodChoice(console);
                if (method is not null)
                {
                    return method.Value;
                }
            }
        }

        /// <summary>
        /// Parses a 1-5 / keyword method selection. Used by both this presenter
        /// and RecipeCreateInteractiveRunner.TryHandleChangeMethod.
        /// </summary>
        internal static bool TryParseMethodSelection(string selection, out RecipeMethodId method)
        {
            switch (selection)
            {
                case "1":
                case "container":
                    method = RecipeMethodId.Container;
                    return true;
                case "2":
                case "package":
                    method = RecipeMethodId.Package;
                    return true;
                case "3":
                case "mirror":
                    method = RecipeMethodId.Mirror;
                    return true;
                case "4":
                case "source":
                    method = RecipeMethodId.Source;
                    return true;
                case "5":
                case "dockerfile":
                    method = RecipeMethodId.Dockerfile;
                    return true;
                default:
                    method = default;
                    return false;
            }
        }

        private static void PrintRecommendedSummary(
            RecipeMethodId method,
            RecipeMethodRecommendation recommendation,
            IRecipeConsole console)
        {
            console.WriteLine();
            console.WriteLine($"추천 작성 방식: {RecipeMethodCatalog.For(method).Label.Get("ko")}");
            console.WriteLine();
            console.WriteLine("이유:");
            console.WriteLine($"  {recommendation.Reason}");

            if (recommendation.Warnings.Count > 0)
            {
                console.WriteLine();
                foreach (var warning in recommendation.Warnings)
                {
                    console.WriteLine($"경고: {warning}");
                }
            }

            console.WriteLine();
            console.WriteLine("이 방식으로 만들면:");
            foreach (var effect in Effects[method])
            {
                console.WriteLine($"  - {effect}");
            }

            console.WriteLine();
            console.WriteLine("앞으로 입력할 항목:");
            foreach (var field in RecipeFieldCatalog.FieldsFor(method))
            {
                console.WriteLine($"  - {field.Label.Get("ko")}");
            }

            console.WriteLine();
            console.WriteLine("주의:");
            foreach (var caution in Cautions[method])
            {
                console.WriteLine($"  - {caution}");
            }

            console.WriteLine();
        }

        private static RecipeMethodId? PromptManualMethodChoice(IRecipeConsole console)
        {
            console.WriteLine("다른 작성 방식을 선택하세요.");
            console.WriteLine();
            for (var i = 0; i < MethodOrder.Count; i++)
            {
                var m = MethodOrder[i];
                var info = RecipeMethodCatalog.For(m);
                console.WriteLine($"[{i + 1}] {info.Label.Get("ko")}");
                console.WriteLine($"    {info.Description.Get("ko")}");
                console.WriteLine();
            }

            console.WriteLine("선택:");
            var line = (console.ReadLine() ?? string.Empty).Trim();
            RecipeCreateEscapeCommands.ThrowIfEscape(line);
            if (TryParseMethodSelection(line, out var method))
            {
                return method;
            }

            console.WriteLine("알 수 없는 선택입니다. 다시 선택하세요.");
            return null;
        }
    }
}
