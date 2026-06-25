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
            IRecipeCreateCancellationSource cancellation)
        {
            var session = new RecipeAuthoringSession();

            try
            {
                var method = SelectMethod(session, stdin, stdout);
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

                RunFieldLoop(session, stdin, stdout, cancellation);

                var document = session.Build();
                document.BuildKind = RecipeBuildKindResolver.Resolve(session.Snapshot().SelectedMethod!.Value, document);

                var result = RecipeValidationPipeline.ValidateRecipe(document);
                while (!result.IsValid)
                {
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

                RecipeCreateCommand.SaveDocument(document, outPath, stdout);
                return 0;
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
                DisplayRecommendation(recommendation, stdout);

                var method = PromptMethodChoice(recommendation, stdin, stdout);
                if (method is null)
                {
                    continue;
                }

                session.SelectMethod(method.Value);
                return method.Value;
            }
        }

        private static RecipeMethodAnswers AskRecommenderQuestions(TextReader stdin, TextWriter stdout)
        {
            var byField = new Dictionary<string, Answer>(StringComparer.Ordinal);
            foreach (var question in RecipeMethodQuestionCatalog.Questions)
            {
                stdout.WriteLine($"{question.Prompt.Get("ko")} [y/n/u]");
                byField[question.Key] = ReadAnswer(stdin);
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
            return line switch
            {
                "y" => Answer.Yes,
                "n" => Answer.No,
                _ => Answer.Unknown,
            };
        }

        private static void DisplayRecommendation(RecipeMethodRecommendation recommendation, TextWriter stdout)
        {
            stdout.WriteLine(recommendation.RecommendedMethod is { } recommended
                ? $"추천: {RecipeMethodCatalog.For(recommended).Label.Get("ko")} — {recommendation.Reason}"
                : $"추천 보류: {recommendation.Reason}");

            foreach (var evidence in recommendation.Evidence)
            {
                stdout.WriteLine($"  근거: {evidence}");
            }

            foreach (var warning in recommendation.Warnings)
            {
                stdout.WriteLine($"  경고: {warning}");
            }

            foreach (var alternative in recommendation.Alternatives)
            {
                stdout.WriteLine($"  [{alternative.Priority}] {alternative.Label} — {alternative.Reason}");
            }

            foreach (var missing in recommendation.MissingInformation)
            {
                stdout.WriteLine($"  추가로 필요한 정보: {missing}");
            }
        }

        private static RecipeMethodId? PromptMethodChoice(RecipeMethodRecommendation recommendation, TextReader stdin, TextWriter stdout)
        {
            stdout.WriteLine(recommendation.RecommendedMethod is { }
                ? "추천을 사용하려면 Enter, 다른 방법은 번호를 입력하세요:"
                : "방법 번호를 입력하세요:");

            var line = (stdin.ReadLine() ?? string.Empty).Trim();

            if (line.Length == 0)
            {
                return recommendation.RecommendedMethod;
            }

            var alternative = recommendation.Alternatives.FirstOrDefault(a => a.Priority.ToString(System.Globalization.CultureInfo.InvariantCulture) == line);
            if (alternative != null)
            {
                return alternative.Method;
            }

            stdout.WriteLine("알 수 없는 선택입니다. 다시 질문합니다.");
            return null;
        }

        private static bool ConfirmDockerfileWarning(TextReader stdin, TextWriter stdout)
        {
            stdout.WriteLine(DockerfileWarningText);
            stdout.WriteLine("계속하시겠습니까? [y/N]");
            var line = (stdin.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
            return line == "y";
        }

        private static void RunFieldLoop(RecipeAuthoringSession session, TextReader stdin, TextWriter stdout, IRecipeCreateCancellationSource cancellation)
        {
            RecipeFieldDescriptor? field;
            while ((field = session.NextField()) != null)
            {
                PromptField(session, field, stdin, stdout, cancellation);
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
                case RecipeFieldType.InputList:
                    PromptInputListField(session, stdin, stdout, cancellation);
                    break;
                case RecipeFieldType.OutputList:
                    PromptOutputListField(session, stdin, stdout, cancellation);
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

        private static void PromptInputListField(RecipeAuthoringSession session, TextReader stdin, TextWriter stdout, IRecipeCreateCancellationSource cancellation) =>
            PromptPresetListField(
                session,
                "Inputs",
                "입력",
                InputOutputPresetCatalog.InputPresets.Select(p => (p.Id, p.Label.Get("ko"))).ToList(),
                stdin,
                stdout,
                cancellation,
                custom => PromptCustomInputSpec(custom, stdin, stdout),
                (name, spec) => RecipeCreateInputOutputSpec.ApplyInput(session, name, spec));

        private static void PromptOutputListField(RecipeAuthoringSession session, TextReader stdin, TextWriter stdout, IRecipeCreateCancellationSource cancellation) =>
            PromptPresetListField(
                session,
                "Outputs",
                "출력",
                InputOutputPresetCatalog.OutputPresets.Select(p => (p.Id, p.Label.Get("ko"))).ToList(),
                stdin,
                stdout,
                cancellation,
                custom => PromptCustomOutputSpec(custom, stdin, stdout),
                (name, spec) => RecipeCreateInputOutputSpec.ApplyOutput(session, name, spec));

        private static void PromptPresetListField(
            RecipeAuthoringSession session,
            string fieldName,
            string label,
            IReadOnlyList<(string Id, string Label)> presets,
            TextReader stdin,
            TextWriter stdout,
            IRecipeCreateCancellationSource cancellation,
            Func<string, string> buildCustomSpecSuffix,
            Func<string, string, IReadOnlyList<ValidationViolation>> apply)
        {
            stdout.WriteLine($"{label} 항목을 추가하세요 (빈 줄 입력 시 종료)");
            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                stdout.WriteLine("이름:");
                var name = stdin.ReadLine() ?? string.Empty;
                if (TryHandleChangeMethod(session, name, stdin, stdout))
                {
                    return;
                }

                if (TryHandleCancel(name, stdin, stdout))
                {
                    continue;
                }

                if (TryHandleReview(session, name, stdout))
                {
                    continue;
                }

                var listField = fieldName == "Inputs" ? RecipeFieldCatalog.InputsField : RecipeFieldCatalog.OutputsField;
                if (TryHandleHelp(listField, name, stdout))
                {
                    continue;
                }

                if (name.Trim().Length == 0)
                {
                    try
                    {
                        session.CompleteListField(fieldName);
                        return;
                    }
                    catch (InvalidOperationException ex)
                    {
                        stdout.WriteLine(ex.Message);
                        continue;
                    }
                }

                for (var i = 0; i < presets.Count; i++)
                {
                    stdout.WriteLine($"  [{i + 1}] {presets[i].Label}");
                }

                stdout.WriteLine("프리셋 번호 또는 'custom':");
                var selection = (stdin.ReadLine() ?? string.Empty).Trim();

                string spec;
                if (selection == InputOutputPresetCatalog.CustomPresetId)
                {
                    spec = $"{InputOutputPresetCatalog.CustomPresetId},{buildCustomSpecSuffix(name.Trim())}";
                }
                else if (int.TryParse(selection, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var index)
                    && index >= 1 && index <= presets.Count)
                {
                    spec = presets[index - 1].Id;
                }
                else
                {
                    stdout.WriteLine("알 수 없는 선택입니다. 이 항목을 다시 입력합니다.");
                    continue;
                }

                var violations = apply(name.Trim(), spec);
                if (violations.Count > 0)
                {
                    PrintViolations(violations, stdout);
                }
            }
        }

        private static string PromptCustomInputSpec(string portName, TextReader stdin, TextWriter stdout)
        {
            var role = PromptNormalizedRole(stdin, stdout);
            var format = PromptNormalizedFormat(stdin, stdout);
            stdout.WriteLine("shape (single/pair):");
            var shape = (stdin.ReadLine() ?? "single").Trim();
            stdout.WriteLine("optional 입력입니까? [y/N]");
            var optional = (stdin.ReadLine() ?? string.Empty).Trim().ToLowerInvariant() == "y";
            _ = portName;
            return optional ? $"{role},{format},{shape},optional" : $"{role},{format},{shape}";
        }

        private static string PromptCustomOutputSpec(string portName, TextReader stdin, TextWriter stdout)
        {
            var role = PromptNormalizedRole(stdin, stdout);
            var format = PromptNormalizedFormat(stdin, stdout);
            stdout.WriteLine("class (primary/secondary):");
            var outputClass = (stdin.ReadLine() ?? "primary").Trim();
            _ = portName;
            return $"{role},{format},{outputClass}";
        }

        private static string PromptNormalizedRole(TextReader stdin, TextWriter stdout)
        {
            stdout.WriteLine("role:");
            var input = stdin.ReadLine() ?? string.Empty;
            var normalized = RoleNormalizer.Normalize(input);
            if (normalized.Message != null)
            {
                stdout.WriteLine(normalized.Message);
            }

            return normalized.Value;
        }

        private static string PromptNormalizedFormat(TextReader stdin, TextWriter stdout)
        {
            stdout.WriteLine("format:");
            var input = stdin.ReadLine() ?? string.Empty;
            var normalized = FormatNormalizer.Normalize(input);

            if (normalized.Action != RecipeValueNormalizationAction.SuggestedPendingConfirmation)
            {
                if (normalized.Message != null)
                {
                    stdout.WriteLine(normalized.Message);
                }

                return normalized.Value;
            }

            stdout.WriteLine(normalized.Message);
            var confirm = (stdin.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
            return confirm == "n" ? normalized.OriginalInput : normalized.Value;
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
                stdout.WriteLine($"  {field.Name}: {value}");
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
            if (selection != "1")
            {
                return true;
            }

            throw new RecipeCreateCancelledException();
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
            if (!TryParseMethodSelection(selection, out var nextMethod))
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
            session.ChangeMethod(nextMethod, confirm == "y" ? ChangeMethodDecision.Proceed : ChangeMethodDecision.Cancel);
            return true;
        }

        private static bool TryParseMethodSelection(string selection, out RecipeMethodId method)
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

            stdout.WriteLine("번호를 입력하세요 (취소하려면 빈 줄):");
            var selection = (stdin.ReadLine() ?? string.Empty).Trim();
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
            if (fieldName is "Inputs" or "Outputs")
            {
                ReviewListSection(session, fieldName, stdin, stdout);

                if (fieldName == "Inputs")
                {
                    PromptInputListField(session, stdin, stdout, cancellation);
                }
                else
                {
                    PromptOutputListField(session, stdin, stdout, cancellation);
                }

                return;
            }

            var field = RecipeFieldCatalog.FieldsFor(session.Snapshot().SelectedMethod!.Value).FirstOrDefault(f => f.Name == fieldName);
            if (field is null)
            {
                return;
            }

            session.ConfirmInvalidatedField(fieldName);
            PromptField(session, field, stdin, stdout, cancellation);
        }

        private static void ReviewListSection(RecipeAuthoringSession session, string fieldName, TextReader stdin, TextWriter stdout)
        {
            while (true)
            {
                var items = session.ListItemsFor(fieldName);
                if (items.Count == 0)
                {
                    return;
                }

                stdout.WriteLine($"현재 {fieldName} 항목:");
                for (var i = 0; i < items.Count; i++)
                {
                    stdout.WriteLine($"  [{i}] {DescribeListItem(items[i])}");
                }

                stdout.WriteLine("수정: e<번호>, 삭제: d<번호>, 계속하려면 빈 줄:");
                var line = (stdin.ReadLine() ?? string.Empty).Trim();

                if (line.Length == 0)
                {
                    return;
                }

                if (line[0] == 'e' && int.TryParse(line[1..], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var editIndex)
                    && editIndex >= 0 && editIndex < items.Count)
                {
                    EditListItemInteractive(session, fieldName, editIndex, stdin, stdout);
                    continue;
                }

                if (line[0] == 'd' && int.TryParse(line[1..], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var deleteIndex)
                    && deleteIndex >= 0 && deleteIndex < items.Count)
                {
                    try
                    {
                        session.DeleteListItem(fieldName, deleteIndex);
                    }
                    catch (InvalidOperationException ex)
                    {
                        stdout.WriteLine(ex.Message);
                    }

                    continue;
                }

                stdout.WriteLine("알 수 없는 선택입니다.");
            }
        }

        private static void EditListItemInteractive(RecipeAuthoringSession session, string fieldName, int index, TextReader stdin, TextWriter stdout)
        {
            var existingName = DescribeListItemName(session.ListItemsFor(fieldName)[index]);

            if (fieldName == "Inputs")
            {
                EditPresetListItem(
                    index,
                    existingName,
                    InputOutputPresetCatalog.InputPresets.Select(p => (p.Id, p.Label.Get("ko"))).ToList(),
                    stdin,
                    stdout,
                    custom => PromptCustomInputSpec(custom, stdin, stdout),
                    (idx, name, spec) => RecipeCreateInputOutputSpec.EditInput(session, idx, name, spec));
            }
            else
            {
                EditPresetListItem(
                    index,
                    existingName,
                    InputOutputPresetCatalog.OutputPresets.Select(p => (p.Id, p.Label.Get("ko"))).ToList(),
                    stdin,
                    stdout,
                    custom => PromptCustomOutputSpec(custom, stdin, stdout),
                    (idx, name, spec) => RecipeCreateInputOutputSpec.EditOutput(session, idx, name, spec));
            }
        }

        private static void EditPresetListItem(
            int index,
            string existingName,
            IReadOnlyList<(string Id, string Label)> presets,
            TextReader stdin,
            TextWriter stdout,
            Func<string, string> buildCustomSpecSuffix,
            Func<int, string, string, IReadOnlyList<ValidationViolation>> edit)
        {
            while (true)
            {
                stdout.WriteLine($"이름 (빈 줄이면 '{existingName}' 유지):");
                var nameInput = (stdin.ReadLine() ?? string.Empty).Trim();
                var name = nameInput.Length == 0 ? existingName : nameInput;

                for (var i = 0; i < presets.Count; i++)
                {
                    stdout.WriteLine($"  [{i + 1}] {presets[i].Label}");
                }

                stdout.WriteLine("프리셋 번호 또는 'custom':");
                var selection = (stdin.ReadLine() ?? string.Empty).Trim();

                string spec;
                if (selection == InputOutputPresetCatalog.CustomPresetId)
                {
                    spec = $"{InputOutputPresetCatalog.CustomPresetId},{buildCustomSpecSuffix(name)}";
                }
                else if (int.TryParse(selection, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var presetIndex)
                    && presetIndex >= 1 && presetIndex <= presets.Count)
                {
                    spec = presets[presetIndex - 1].Id;
                }
                else
                {
                    stdout.WriteLine("알 수 없는 선택입니다. 다시 입력합니다.");
                    continue;
                }

                var violations = edit(index, name, spec);
                if (violations.Count > 0)
                {
                    PrintViolations(violations, stdout);
                    continue;
                }

                return;
            }
        }

        private static string DescribeListItem(object item) => item switch
        {
            ToolInput input => $"{input.Name} (role={input.Role}, format={input.Format}, shape={input.Shape}, required={input.Required})",
            ToolOutput output => $"{output.Name} (role={output.Role}, format={output.Format}, class={output.Class})",
            _ => item.ToString() ?? string.Empty,
        };

        private static string DescribeListItemName(object item) => item switch
        {
            ToolInput input => input.Name,
            ToolOutput output => output.Name,
            _ => string.Empty,
        };

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
