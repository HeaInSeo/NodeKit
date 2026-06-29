using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NodeKit.Authoring;
using NodeKit.Authoring.Recipes;
using NodeKit.Validation;
using NodeKit.Validation.Recipes;

namespace NodeKit.Cli
{
    /// <summary>
    /// Interactive `nodekit recipe create` wizard: recommender Q&amp;A,
    /// method selection, field-by-field prompts driven by
    /// RecipeAuthoringSession.NextField, /help, /review, /change-method,
    /// /cancel (/quit, /exit) escape hatches, Ctrl+C cancellation via
    /// IRecipeCreateCancellationSource, and final-validation recovery via
    /// BuildRecoveryPlan. See
    /// docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md Sections 10-20
    /// and docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_V0.9.2.md Sections 17-18.
    /// </summary>
    internal static class RecipeCreateInteractiveRunner
    {
        private const string HelpCommand = "/help";
        private const string ReviewCommand = "/review";
        private const string BackCommand = "/back";
        private const string ChangeMethodCommand = "/change-method";
        private const string CancelCommand = "/cancel";
        private const string QuitCommand = "/quit";
        private const string ExitCommand = "/exit";
        private const int CancelledExitCode = 130;

        private const string DockerfileWarningText =
            "강한 주의: Dockerfile 방법은 재현성을 스스로 책임져야 합니다. base image digest 고정과 패키지 버전 고정을 직접 관리하지 않으면 " +
            "최종 검증에서 막히거나, 통과하더라도 나중에 다른 결과가 나올 수 있습니다.";

        public static int Run(string outPath, RecipeCreateOptions parsed, TextReader stdin, TextWriter stdout, TextWriter stderr)
        {
            using var cancellation = new ConsoleCancelKeyCancellationSource();
            return Run(outPath, parsed, stdin, stdout, stderr, cancellation);
        }

        internal static int Run(
            string outPath,
            RecipeCreateOptions parsed,
            TextReader stdin,
            TextWriter stdout,
            TextWriter stderr,
            IRecipeCreateCancellationSource cancellation,
            IResolveRecipeClient? resolveClient = null)
        {
            using var harborResolver = HarborImageDigestResolver.TryCreate();
            IImageDigestResolver resolver = (IImageDigestResolver?)harborResolver ?? NullImageDigestResolver.Instance;
            IResolveRecipeClient recipeResolver = resolveClient
                ?? StubResolveRecipeClient.TryCreate()
                ?? (IResolveRecipeClient)NullResolveRecipeClient.Instance;

            try
            {
                while (true)
                {
                    var mode = AuthoringModeSelector.Prompt(stdin, stdout);
                    if (mode is null)
                    {
                        return 0;
                    }

                    var session = new RecipeAuthoringSession();
                    RecipeMethodId? method;

                    try
                    {
                        RecipeCreateScreen.ClearForNewStep(stdout);
                        if (mode == AuthoringModeSelector.Mode.GuidedBeginner)
                        {
                            method = BeginnerGuideFlow.Run(session, stdin, stdout, cancellation, resolver);
                            if (method is null)
                            {
                                stdout.WriteLine("단서가 부족합니다. recipe를 저장하지 않고 종료합니다.");
                                return 0;
                            }
                            // Dockerfile warning confirmation is handled inside BeginnerGuideFlow
                        }
                        else
                        {
                            method = SelectMethod(session, stdin, stdout);
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
                                else if (!ConfirmDockerfileWarning(stdin, stdout))
                                {
                                    stdout.WriteLine("Dockerfile 방법 진행이 취소되었습니다.");
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
                        stdout.WriteLine("이전 화면으로 돌아갑니다.");
                        RecipeCreateScreen.ClearForNewStep(stdout);
                        continue;
                    }

                    RecipeCreateScreen.ClearForNewStep(stdout);
                    try
                    {
                        RunFieldLoop(session, stdin, stdout, cancellation);
                    }
                    catch (RecipeCreateBackRequestedException)
                    {
                        stdout.WriteLine("이전 화면으로 돌아갑니다.");
                        RecipeCreateScreen.ClearForNewStep(stdout);
                        continue;
                    }

                    var document = session.Build();
                    document.BuildKind = RecipeBuildKindResolver.Resolve(session.Snapshot().SelectedMethod!.Value, document);

                    var result = RecipeValidationPipeline.ValidateRecipe(document);
                    while (!result.IsValid)
                    {
                        RecipeCreateScreen.ClearForNewStep(stdout);
                        if (!RunRecoveryLoop(session, result.Violations, stdin, stdout, cancellation))
                        {
                            stderr.WriteLine("최종 검증을 통과하지 못해 저장하지 않습니다.");
                            CliApp.PrintViolations(result.Violations, stderr);
                            return 1;
                        }

                        document = session.Build();
                        document.BuildKind = RecipeBuildKindResolver.Resolve(session.Snapshot().SelectedMethod!.Value, document);
                        result = RecipeValidationPipeline.ValidateRecipe(document);
                    }

                    // ResolveRecipe 사전 조회 (트랙 D — proto 추가 전까지 NullResolveRecipeClient)
                    if (document.Packages.Count > 0)
                    {
                        var resolveResult = recipeResolver
                            .ResolveAsync(document.ToolName ?? string.Empty, document.Version ?? string.Empty,
                                document.Packages, System.Threading.CancellationToken.None)
                            .GetAwaiter().GetResult();

                        if (resolveResult.Source != RecipeResolutionSource.Unsupported
                            && resolveResult.Packages.Count > 0)
                        {
                            RecipeCreateScreen.ClearForNewStep(stdout);
                            stdout.WriteLine("패키지 빌드 문자열 선택");
                            stdout.WriteLine();

                            var selections = PackageCandidatePresenter.Present(
                                resolveResult.Packages, stdin, stdout, cancellation);
                            if (selections is null)
                            {
                                stderr.WriteLine("패키지 선택이 완료되지 않아 저장하지 않습니다.");
                                return 1;
                            }

                            document.Packages = new System.Collections.Generic.List<string>(
                                PackageCandidatePresenter.ApplySelections(document.Packages, selections));
                        }
                        else if (resolveResult.Source == RecipeResolutionSource.NotFound)
                        {
                            stdout.WriteLine();
                            stdout.WriteLine("⚠  Harbor에 동일 tool+version 이미지가 없습니다.");
                            stdout.WriteLine("   폐쇄망 환경이라면 관리자가 Harbor에 이미지를 사전 등록해야 합니다.");
                            stdout.WriteLine("   열린망이라면 빌드 서버가 외부 채널에서 직접 해소합니다.");
                            stdout.WriteLine();
                        }
                    }

                    RecipeCreateCommand.SaveDocument(document, outPath, stdout);
                    return 0;
                }
            }
            catch (RecipeCreateCancelledException)
            {
                stdout.WriteLine("recipe 생성을 취소했습니다.");
                stdout.WriteLine("파일은 저장되지 않았습니다.");
                return CancelledExitCode;
            }
        }

        private static RecipeMethodId? SelectMethod(RecipeAuthoringSession session, TextReader stdin, TextWriter stdout)
        {
            while (true)
            {
                var answers = AskRecommenderQuestions(stdin, stdout);
                var recommendation = RecipeMethodRecommender.Recommend(answers);
                RecipeMethodId method;
                try
                {
                    method = MethodRecommendationPresenter.Present(recommendation, stdin, stdout);
                }
                catch (RecipeCreateBackRequestedException)
                {
                    RecipeCreateScreen.ClearForNewStep(stdout);
                    continue;
                }

                session.SelectMethod(method);
                return method;
            }
        }

        private static RecipeMethodAnswers AskRecommenderQuestions(TextReader stdin, TextWriter stdout)
        {
            var questions = RecipeMethodQuestionCatalog.Questions;
            var byField = new Dictionary<string, Answer>(StringComparer.Ordinal);
            var index = 0;

            while (index < questions.Count)
            {
                var question = questions[index];
                RecipeCreateScreen.ClearForNewStep(stdout);

                stdout.WriteLine($"빠른 설정 모드  [{index + 1} / {questions.Count}]");
                stdout.WriteLine("/back: 이전 질문   /cancel: 종료");
                stdout.WriteLine();

                if (RecipeMethodQuestionDetailCatalog.ByKey.TryGetValue(question.Key, out var detail))
                {
                    stdout.WriteLine(detail.Header);
                    stdout.WriteLine();
                    stdout.WriteLine("의미:");
                    stdout.WriteLine($"  {detail.Meaning}");
                    stdout.WriteLine();
                    stdout.WriteLine("예:");
                    foreach (var example in detail.Examples)
                    {
                        stdout.WriteLine($"  - {example}");
                    }

                    stdout.WriteLine();
                    stdout.WriteLine("y를 선택하면:");
                    foreach (var effect in detail.YesEffects)
                    {
                        stdout.WriteLine($"  - {effect}");
                    }

                    stdout.WriteLine();
                    stdout.WriteLine("n을 선택하면:");
                    foreach (var effect in detail.NoEffects)
                    {
                        stdout.WriteLine($"  - {effect}");
                    }

                    stdout.WriteLine();
                    stdout.WriteLine("Enter를 누르면:");
                    foreach (var effect in detail.EnterEffects)
                    {
                        stdout.WriteLine($"  - {effect}");
                    }

                    stdout.WriteLine();
                }
                else
                {
                    stdout.WriteLine($"{question.Prompt.Get("ko")}");
                    stdout.WriteLine();
                }

                stdout.WriteLine("선택 [y/n/Enter]:");
                try
                {
                    byField[question.Key] = ReadAnswer(stdin);
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

        private static Answer ReadAnswer(TextReader stdin)
        {
            var line = (stdin.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
            RecipeCreateEscapeCommands.ThrowIfEscape(line);
            return line switch
            {
                "y" => Answer.Yes,
                "n" => Answer.No,
                _ => Answer.Unknown,
            };
        }

        private static bool ConfirmDockerfileWarning(TextReader stdin, TextWriter stdout)
        {
            stdout.WriteLine(DockerfileWarningText);
            stdout.WriteLine("계속하시겠습니까? [y/N]");
            var line = (stdin.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
            RecipeCreateEscapeCommands.ThrowIfEscape(line);
            return line == "y";
        }

        private static void RunFieldLoop(RecipeAuthoringSession session, TextReader stdin, TextWriter stdout, IRecipeCreateCancellationSource cancellation)
        {
            var total = RecipeFieldCatalog.FieldsFor(session.Snapshot().SelectedMethod!.Value).Count;
            var history = new System.Collections.Generic.List<RecipeFieldDescriptor>();

            RecipeFieldDescriptor? field;
            while ((field = session.NextField()) != null)
            {
                RecipeCreateScreen.ClearForNewStep(stdout);
                stdout.WriteLine($"[{history.Count + 1} / {total}]");
                stdout.WriteLine("/back: 이전 필드   /cancel: 종료   /review: 현재 값   /change-method: 작성 방식 변경");
                stdout.WriteLine();

                try
                {
                    PromptField(session, field, stdin, stdout, cancellation);
                    history.Add(field);
                }
                catch (RecipeCreateBackRequestedException)
                {
                    if (history.Count > 0)
                    {
                        var prev = history[history.Count - 1];
                        history.RemoveAt(history.Count - 1);
                        session.ClearField(prev.Name);
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }

        private static void PromptField(RecipeAuthoringSession session, RecipeFieldDescriptor field, TextReader stdin, TextWriter stdout, IRecipeCreateCancellationSource cancellation)
        {
            switch (field.Type)
            {
                case RecipeFieldType.Scalar:
                    PromptScalarField(session, field, stdin, stdout, cancellation);
                    break;
                case RecipeFieldType.Choice:
                    PromptChoiceField(session, field, stdin, stdout, cancellation);
                    break;
                case RecipeFieldType.StringList:
                    PromptStringListField(session, field, stdin, stdout, cancellation);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(field), field.Type, "Unsupported field type.");
            }
        }

        private static void PromptScalarField(RecipeAuthoringSession session, RecipeFieldDescriptor field, TextReader stdin, TextWriter stdout, IRecipeCreateCancellationSource cancellation)
        {
            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                stdout.WriteLine($"{field.Label.Get("ko")} — {field.Help.Get("ko")}");
                var line = stdin.ReadLine() ?? string.Empty;

                if (TryHandleChangeMethod(session, line, stdin, stdout))
                {
                    return;
                }

                if (TryHandleCancel(line, stdin, stdout))
                {
                    continue;
                }

                RecipeCreateEscapeCommands.ThrowIfBack(line);

                if (TryHandleReview(session, line, stdout))
                {
                    continue;
                }

                if (TryHandleHelp(field, line, stdout))
                {
                    continue;
                }

                if (line.Trim().Length == 0 && field.Requirement == RecipeFieldRequirement.Optional)
                {
                    session.SkipOptionalField(field.Name);
                    return;
                }

                var violations = session.SetField(field.Name, line);
                if (violations.Count == 0)
                {
                    return;
                }

                PrintViolations(violations, stdout);
            }
        }

        private static void PromptChoiceField(RecipeAuthoringSession session, RecipeFieldDescriptor field, TextReader stdin, TextWriter stdout, IRecipeCreateCancellationSource cancellation)
        {
            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                stdout.WriteLine($"{field.Label.Get("ko")} — {field.Help.Get("ko")}");
                for (var i = 0; i < field.Choices.Count; i++)
                {
                    stdout.WriteLine($"  [{i + 1}] {field.Choices[i].Label.Get("ko")} — {field.Choices[i].Description.Get("ko")}");
                }

                var line = stdin.ReadLine() ?? string.Empty;
                if (TryHandleChangeMethod(session, line, stdin, stdout))
                {
                    return;
                }

                if (TryHandleCancel(line, stdin, stdout))
                {
                    continue;
                }

                RecipeCreateEscapeCommands.ThrowIfBack(line);

                if (TryHandleReview(session, line, stdout))
                {
                    continue;
                }

                if (TryHandleHelp(field, line, stdout))
                {
                    continue;
                }

                var trimmed = line.Trim();
                if (trimmed.Length == 0 && field.Requirement == RecipeFieldRequirement.Defaulted)
                {
                    return;
                }

                var choiceValue = ResolveChoiceValue(field, trimmed);
                var violations = session.SetField(field.Name, choiceValue);
                if (violations.Count == 0)
                {
                    return;
                }

                PrintViolations(violations, stdout);
            }
        }

        private static string ResolveChoiceValue(RecipeFieldDescriptor field, string input)
        {
            if (int.TryParse(input, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var index)
                && index >= 1 && index <= field.Choices.Count)
            {
                return field.Choices[index - 1].Value;
            }

            return input;
        }

        private static void PromptStringListField(RecipeAuthoringSession session, RecipeFieldDescriptor field, TextReader stdin, TextWriter stdout, IRecipeCreateCancellationSource cancellation)
        {
            stdout.WriteLine($"{field.Label.Get("ko")} — {field.Help.Get("ko")} (빈 줄 입력 시 종료)");
            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                var line = stdin.ReadLine() ?? string.Empty;
                if (TryHandleChangeMethod(session, line, stdin, stdout))
                {
                    return;
                }

                if (TryHandleCancel(line, stdin, stdout))
                {
                    continue;
                }

                RecipeCreateEscapeCommands.ThrowIfBack(line);

                if (TryHandleReview(session, line, stdout))
                {
                    continue;
                }

                if (TryHandleHelp(field, line, stdout))
                {
                    continue;
                }

                if (line.Trim().Length == 0)
                {
                    try
                    {
                        session.CompleteListField(field.Name);
                        return;
                    }
                    catch (InvalidOperationException ex)
                    {
                        stdout.WriteLine(ex.Message);
                        continue;
                    }
                }

                var violations = session.AppendListItem(field.Name, line);
                if (violations.Count > 0)
                {
                    PrintViolations(violations, stdout);
                }
            }
        }


        private static bool TryHandleHelp(RecipeFieldDescriptor field, string line, TextWriter stdout)
        {
            if (line.Trim() != HelpCommand)
            {
                return false;
            }

            stdout.WriteLine($"{field.Label.Get("ko")} — {field.Help.Get("ko")}");
            if (field.Examples.Count > 0)
            {
                stdout.WriteLine($"예시: {string.Join(", ", field.Examples)}");
            }

            stdout.WriteLine(DescribeRequirement(field.Requirement));
            return true;
        }

        private static bool TryHandleReview(RecipeAuthoringSession session, string line, TextWriter stdout)
        {
            if (line.Trim() != ReviewCommand)
            {
                return false;
            }

            PrintReview(session, stdout);
            return true;
        }

        private static void PrintReview(RecipeAuthoringSession session, TextWriter stdout)
        {
            var snapshot = session.Snapshot();
            var valueByField = snapshot.Values.ToDictionary(v => v.FieldName, v => v.DisplayValue, StringComparer.Ordinal);

            stdout.WriteLine("현재까지 입력한 내용:");
            stdout.WriteLine($"작성 방식: {snapshot.SelectedMethod}");
            foreach (var field in RecipeFieldCatalog.FieldsFor(snapshot.SelectedMethod!.Value))
            {
                var value = valueByField.TryGetValue(field.Name, out var displayValue) ? displayValue : "아직 입력 안 함";
                stdout.WriteLine($"  {field.Label.Get("ko")} ({field.Name}): {value}");
            }
        }

        private static bool TryHandleCancel(string line, TextReader stdin, TextWriter stdout)
        {
            var trimmed = line.Trim();
            if (trimmed != CancelCommand && trimmed != QuitCommand && trimmed != ExitCommand)
            {
                return false;
            }

            stdout.WriteLine("recipe 생성을 중단하려고 합니다.");
            stdout.WriteLine("[1] 저장하지 않고 종료");
            stdout.WriteLine("[2] 계속 작성");
            stdout.WriteLine("선택:");

            var selection = (stdin.ReadLine() ?? string.Empty).Trim();
            RecipeCreateEscapeCommands.ThrowIfCancel(selection);
            if (selection != "1")
            {
                return true;
            }

            throw new RecipeCreateCancelledException();
        }

        private static bool TryHandleBack(string line, TextWriter stdout)
        {
            if (line.Trim() != BackCommand)
            {
                return false;
            }

            stdout.WriteLine("/back은 현재 v1.0에서 초기 선택, 쉬운 안내, 빠른 설정 화면 사이에서 지원합니다.");
            stdout.WriteLine("현재 입력 단계에서는 /review로 값을 확인하거나 /change-method로 작성 방식을 다시 선택하세요.");
            return true;
        }

        private static string DescribeRequirement(RecipeFieldRequirement requirement) => requirement switch
        {
            RecipeFieldRequirement.Required => "필수 항목입니다. 값이 없으면 최종 검증을 통과하지 못합니다.",
            RecipeFieldRequirement.Recommended => "권장 항목입니다. 비워둘 수 있지만 재현성을 위해 채우는 것을 권장합니다.",
            RecipeFieldRequirement.Optional => "선택 항목입니다. 비워두고 넘어갈 수 있습니다.",
            RecipeFieldRequirement.Defaulted => "기본값이 있는 항목입니다. 비워두면 기본값이 적용됩니다.",
            _ => throw new ArgumentOutOfRangeException(nameof(requirement), requirement, "Unsupported requirement tier."),
        };

        private static bool TryHandleChangeMethod(RecipeAuthoringSession session, string line, TextReader stdin, TextWriter stdout)
        {
            if (line.Trim() != ChangeMethodCommand)
            {
                return false;
            }

            stdout.WriteLine("변경할 방법 번호를 입력하세요: [1] container [2] package [3] mirror [4] source [5] dockerfile");
            var selection = (stdin.ReadLine() ?? string.Empty).Trim();
            RecipeCreateEscapeCommands.ThrowIfCancel(selection);
            if (RecipeCreateEscapeCommands.IsBack(selection))
            {
                stdout.WriteLine("method 변경을 취소하고 현재 입력 단계로 돌아갑니다.");
                return true;
            }

            if (!MethodRecommendationPresenter.TryParseMethodSelection(selection, out var nextMethod))
            {
                stdout.WriteLine("알 수 없는 방법입니다. 변경을 취소합니다.");
                return true;
            }

            var preview = session.PreviewMethodChange(nextMethod);
            stdout.WriteLine($"유지되는 필드: {string.Join(", ", preview.PreservedFields)}");
            stdout.WriteLine($"재확인이 필요한 필드: {string.Join(", ", preview.FieldsRequiringRevalidation)}");
            stdout.WriteLine($"버려지는 필드: {string.Join(", ", preview.DiscardedFields)}");
            stdout.WriteLine("계속할까요? [y/N]");

            var confirm = (stdin.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
            RecipeCreateEscapeCommands.ThrowIfCancel(confirm);
            session.ChangeMethod(nextMethod, confirm == "y" ? ChangeMethodDecision.Proceed : ChangeMethodDecision.Cancel);
            return true;
        }

        private static bool RunRecoveryLoop(RecipeAuthoringSession session, IReadOnlyList<ValidationViolation> violations, TextReader stdin, TextWriter stdout, IRecipeCreateCancellationSource cancellation)
        {
            var plan = session.BuildRecoveryPlan(violations);

            stdout.WriteLine("최종 검증에 실패했습니다. 다음 중 수정할 항목을 선택하세요:");
            for (var i = 0; i < plan.Actions.Count; i++)
            {
                var action = plan.Actions[i];
                stdout.WriteLine($"  [{i + 1}] {action.Label} — {action.Description.Get("ko")}");
                stdout.WriteLine($"      힌트: {action.BeginnerHint.Get("ko")}");
            }

            stdout.WriteLine();
            stdout.WriteLine("/cancel: 저장하지 않고 종료");
            stdout.WriteLine("번호를 입력하세요 (빈 줄 = 저장 없이 종료):");
            var selection = (stdin.ReadLine() ?? string.Empty).Trim();
            RecipeCreateEscapeCommands.ThrowIfCancel(selection);
            if (selection.Length == 0)
            {
                return false;
            }

            if (!int.TryParse(selection, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var index)
                || index < 1 || index > plan.Actions.Count)
            {
                stdout.WriteLine("알 수 없는 선택입니다.");
                return RunRecoveryLoop(session, violations, stdin, stdout, cancellation);
            }

            var chosen = plan.Actions[index - 1];
            foreach (var fieldName in chosen.RelatedFields)
            {
                ReEditField(session, fieldName, stdin, stdout, cancellation);
            }

            return true;
        }

        private static void ReEditField(RecipeAuthoringSession session, string fieldName, TextReader stdin, TextWriter stdout, IRecipeCreateCancellationSource cancellation)
        {
            var field = RecipeFieldCatalog.FieldsFor(session.Snapshot().SelectedMethod!.Value).FirstOrDefault(f => f.Name == fieldName);
            if (field is null)
            {
                return;
            }

            session.ConfirmInvalidatedField(fieldName);
            PromptField(session, field, stdin, stdout, cancellation);
        }

        private static void PrintViolations(IReadOnlyList<ValidationViolation> violations, TextWriter stdout)
        {
            foreach (var violation in violations)
            {
                stdout.WriteLine(violation.Field is null
                    ? $"{violation.RuleId}: {violation.Message}"
                    : $"{violation.RuleId} ({violation.Field}): {violation.Message}");
            }
        }
    }
}
