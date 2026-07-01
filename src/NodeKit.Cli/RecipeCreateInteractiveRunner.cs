using System;
using System.Collections.Generic;
using System.IO;
using NodeKit.Authoring;
using NodeKit.Authoring.Recipes;

namespace NodeKit.Cli
{
    /// <summary>
    /// Entry point for the interactive `nodekit recipe create` wizard. Owns the
    /// outer restart loop, authoring-mode selection, and method selection
    /// (guided beginner flow or quick-setup Q&amp;A), then hands off to
    /// RecipeCreateFlow for the common steps 3-9. See
    /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md Sections 10-20
    /// and docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_V0.9.2.md Sections 17-18.
    /// </summary>
    internal static class RecipeCreateInteractiveRunner
    {
        private const int CancelledExitCode = 130;

        private const string DockerfileWarningText =
            "강한 주의: Dockerfile 방법은 재현성을 스스로 책임져야 합니다. base image digest 고정과 패키지 버전 고정을 직접 관리하지 않으면 " +
            "최종 검증에서 막히거나, 통과하더라도 나중에 다른 결과가 나올 수 있습니다.";

        public static int Run(string outPath, RecipeCreateOptions parsed, IRecipeConsole console, TextWriter stderr)
        {
            using var cancellation = new ConsoleCancelKeyCancellationSource();
            return Run(outPath, parsed, console, stderr, cancellation);
        }

        internal static int Run(
            string outPath,
            RecipeCreateOptions parsed,
            IRecipeConsole console,
            TextWriter stderr,
            IRecipeCreateCancellationSource cancellation,
            IResolveRecipeClient? resolveClient = null)
        {
            using var harborResolver = HarborImageDigestResolver.TryCreate();
            using var grpcResolver = GrpcResolveRecipeClient.TryCreate();
            IImageDigestResolver resolver = (IImageDigestResolver?)harborResolver ?? NullImageDigestResolver.Instance;
            IResolveRecipeClient recipeResolver = resolveClient
                ?? StubResolveRecipeClient.TryCreate()
                ?? (IResolveRecipeClient?)grpcResolver
                ?? NullResolveRecipeClient.Instance;

            try
            {
                while (true)
                {
                    var mode = AuthoringModeSelector.Prompt(console);
                    if (mode is null)
                    {
                        return 0;
                    }

                    var session = new RecipeAuthoringSession();

                    try
                    {
                        RecipeCreateScreen.ClearForNewStep(console);
                        if (mode == AuthoringModeSelector.Mode.GuidedBeginner)
                        {
                            var beginnerMethod = BeginnerGuideFlow.Run(session, console, cancellation, resolver);
                            if (beginnerMethod is null)
                            {
                                console.WriteLine("단서가 부족합니다. recipe를 저장하지 않고 종료합니다.");
                                return 0;
                            }
                            // Dockerfile warning confirmation is handled inside BeginnerGuideFlow
                        }
                        else
                        {
                            var method = SelectMethod(session, console);
                            if (method is null)
                            {
                                stderr.WriteLine("method 선택이 완료되지 않아 종료합니다.");
                                return 1;
                            }

                            if (method == RecipeMethodId.Dockerfile)
                            {
                                if (parsed.AcceptDockerfileWarning)
                                {
                                    session.AcceptDockerfileWarning();
                                }
                                else if (!ConfirmDockerfileWarning(console))
                                {
                                    console.WriteLine("Dockerfile 방법 진행이 취소되었습니다.");
                                    return 1;
                                }
                                else
                                {
                                    session.AcceptDockerfileWarning();
                                }
                            }
                        }
                    }
                    catch (RecipeCreateBackRequestedException)
                    {
                        console.WriteLine("이전 화면으로 돌아갑니다.");
                        RecipeCreateScreen.ClearForNewStep(console);
                        continue;
                    }

                    // 단계 3~9: 공통 흐름
                    RecipeCreateFlowResult flowResult;
                    try
                    {
                        flowResult = RecipeCreateFlow.Execute(outPath, session, console, stderr, cancellation, recipeResolver);
                    }
                    catch (RecipeCreateBackRequestedException)
                    {
                        console.WriteLine("이전 화면으로 돌아갑니다.");
                        RecipeCreateScreen.ClearForNewStep(console);
                        continue;
                    }

                    switch (flowResult)
                    {
                        case RecipeCreateFlowResult.Saved:
                            return 0;
                        case RecipeCreateFlowResult.RestartWizard:
                            continue;
                        case RecipeCreateFlowResult.ValidationFailed:
                            return 1;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(flowResult), flowResult, "Unsupported flow result.");
                    }
                }
            }
            catch (RecipeCreateCancelledException)
            {
                console.WriteLine("recipe 생성을 취소했습니다.");
                console.WriteLine("파일은 저장되지 않았습니다.");
                return CancelledExitCode;
            }
        }

        private static RecipeMethodId? SelectMethod(RecipeAuthoringSession session, IRecipeConsole console)
        {
            while (true)
            {
                var answers = AskRecommenderQuestions(console);
                var recommendation = RecipeMethodRecommender.Recommend(answers);
                RecipeMethodId method;
                try
                {
                    method = MethodRecommendationPresenter.Present(recommendation, console);
                }
                catch (RecipeCreateBackRequestedException)
                {
                    RecipeCreateScreen.ClearForNewStep(console);
                    continue;
                }

                session.SelectMethod(method);
                return method;
            }
        }

        private static RecipeMethodAnswers AskRecommenderQuestions(IRecipeConsole console)
        {
            var questions = RecipeMethodQuestionCatalog.Questions;
            var byField = new Dictionary<string, Answer>(StringComparer.Ordinal);
            var index = 0;

            while (index < questions.Count)
            {
                var question = questions[index];
                RecipeCreateScreen.ClearForNewStep(console);

                console.WriteLine($"빠른 설정 모드  [{index + 1} / {questions.Count}]");
                console.WriteHints("/back: 이전 질문   /cancel: 종료");
                console.WriteLine();

                if (RecipeMethodQuestionDetailCatalog.ByKey.TryGetValue(question.Key, out var detail))
                {
                    console.WriteLine(detail.Header);
                    console.WriteLine();
                    console.WriteLine("의미:");
                    console.WriteLine($"  {detail.Meaning}");
                    console.WriteLine();
                    console.WriteLine("예:");
                    foreach (var example in detail.Examples)
                    {
                        console.WriteLine($"  - {example}");
                    }

                    console.WriteLine();
                    console.WriteLine("y를 선택하면:");
                    foreach (var effect in detail.YesEffects)
                    {
                        console.WriteLine($"  - {effect}");
                    }

                    console.WriteLine();
                    console.WriteLine("n을 선택하면:");
                    foreach (var effect in detail.NoEffects)
                    {
                        console.WriteLine($"  - {effect}");
                    }

                    console.WriteLine();
                    console.WriteLine("Enter를 누르면:");
                    foreach (var effect in detail.EnterEffects)
                    {
                        console.WriteLine($"  - {effect}");
                    }

                    console.WriteLine();
                }
                else
                {
                    console.WriteLine($"{question.Prompt.Get("ko")}");
                    console.WriteLine();
                }

                console.WriteLine("선택 [y/n/Enter]:");
                try
                {
                    byField[question.Key] = ReadAnswer(console);
                    index++;
                }
                catch (RecipeCreateBackRequestedException)
                {
                    if (index > 0)
                    {
                        index--;
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            return new RecipeMethodAnswers(
                byField[nameof(RecipeMethodAnswers.IsRestrictedNetwork)],
                byField[nameof(RecipeMethodAnswers.HasInternalPackageMirror)],
                byField[nameof(RecipeMethodAnswers.HasExistingContainerImage)],
                byField[nameof(RecipeMethodAnswers.HasPackageInPublicChannels)],
                byField[nameof(RecipeMethodAnswers.HasSourceArchiveAndChecksum)],
                byField[nameof(RecipeMethodAnswers.HasExistingDockerfile)]);
        }

        private static Answer ReadAnswer(IRecipeConsole console)
        {
            var line = (console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
            RecipeCreateEscapeCommands.ThrowIfEscape(line);
            return line switch
            {
                "y" => Answer.Yes,
                "n" => Answer.No,
                _ => Answer.Unknown,
            };
        }

        private static bool ConfirmDockerfileWarning(IRecipeConsole console)
        {
            console.WriteLine(DockerfileWarningText);
            console.WriteLine("계속하시겠습니까? [y/N]");
            var line = (console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
            RecipeCreateEscapeCommands.ThrowIfEscape(line);
            return line == "y";
        }
    }
}
