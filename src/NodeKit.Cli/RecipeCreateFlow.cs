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
    internal enum RecipeCreateFlowResult
    {
        Saved,
        RestartWizard,
        ValidationFailed,
    }

    /// <summary>
    /// Shared recipe-create orchestration for steps 3-9, driven after a method
    /// has been selected by either the guided beginner flow or the quick-setup
    /// Q&amp;A. Encapsulates the explicit channel-confirmation step (step 3, Package
    /// method only), the field loop (step 5), build + validation recovery
    /// (step 6), package build-string resolution (step 7), port selection
    /// (step 8), and save confirmation (step 9).
    /// </summary>
    internal static class RecipeCreateFlow
    {
        private const string HelpCommand = "/help";
        private const string ReviewCommand = "/review";
        private const string BackCommand = "/back";
        private const string ChangeMethodCommand = "/change-method";
        private const string CancelCommand = "/cancel";
        private const string QuitCommand = "/quit";
        private const string ExitCommand = "/exit";

        internal static RecipeCreateFlowResult Execute(
            string? outPathHint,
            RecipeAuthoringSession session,
            IRecipeConsole console,
            TextWriter stderr,
            IRecipeCreateCancellationSource cancellation,
            IResolveRecipeClient recipeResolver,
            IImageDigestResolver? imageDigestResolver = null)
        {
            // 단계 3: 채널 확정 (Package 방식만)
            ConfirmChannels(session, console, cancellation);

            // 단계 4: Base image 선택 + digest 자동 조회 (resolver 구성 시)
            if (imageDigestResolver is not null)
            {
                PromptBaseImageSelection(session, imageDigestResolver, console, cancellation);
            }

            // 단계 5: 나머지 필드 입력
            RecipeCreateScreen.ClearForNewStep(console);
            RunFieldLoop(session, console, cancellation);

            // 단계 6: 빌드 + 검증 + recovery
            var method = session.Snapshot().SelectedMethod!.Value;
            var document = session.Build();
            document.BuildKind = RecipeBuildKindResolver.Resolve(method, document);

            var result = RecipeValidationPipeline.ValidateRecipe(document);
            while (!result.IsValid)
            {
                RecipeCreateScreen.ClearForNewStep(console);
                if (!RunRecoveryLoop(session, result.Violations, console, cancellation))
                {
                    stderr.WriteLine("최종 검증을 통과하지 못해 저장하지 않습니다.");
                    CliApp.PrintViolations(result.Violations, stderr);
                    return RecipeCreateFlowResult.ValidationFailed;
                }

                document = session.Build();
                document.BuildKind = RecipeBuildKindResolver.Resolve(session.Snapshot().SelectedMethod!.Value, document);
                result = RecipeValidationPipeline.ValidateRecipe(document);
            }

            // 단계 7: 패키지 빌드 문자열 선택 (ResolveRecipe)
            if (document.Packages.Count > 0)
            {
                var resolveResult = recipeResolver
                    .ResolveAsync(document.ToolName ?? string.Empty, document.Version ?? string.Empty,
                        document.Packages, System.Threading.CancellationToken.None, document.BuildKind)
                    .GetAwaiter().GetResult();

                if (resolveResult.Source != RecipeResolutionSource.Unsupported
                    && resolveResult.Packages.Count > 0)
                {
                    RecipeCreateScreen.ClearForNewStep(console);
                    console.WriteLine("패키지 빌드 문자열 선택");
                    console.WriteLine();

                    var selections = PackageCandidatePresenter.Present(
                        resolveResult.Packages, console, cancellation);
                    if (selections is null)
                    {
                        stderr.WriteLine("패키지 선택이 완료되지 않아 저장하지 않습니다.");
                        return RecipeCreateFlowResult.ValidationFailed;
                    }

                    document.Packages = new List<string>(
                        PackageCandidatePresenter.ApplySelections(document.Packages, selections));
                }
                else if (resolveResult.Source == RecipeResolutionSource.NotFound)
                {
                    console.WriteLine();
                    console.WriteLine("⚠  Harbor에 동일 tool+version 이미지가 없습니다.");
                    console.WriteLine("   폐쇄망 환경이라면 관리자가 Harbor에 이미지를 사전 등록해야 합니다.");
                    console.WriteLine("   열린망이라면 빌드 서버가 외부 채널에서 직접 해소합니다.");
                    console.WriteLine();
                }
            }

            // 단계 8: 포트 설정
            PromptPortSelection(document, console, cancellation);

            // 단계 9: 저장 경로 확정 + 저장
            RecipeCreateScreen.ClearForNewStep(console);
            console.WriteLine("── 저장 확인 ──────────────────────────────────────────");
            PrintDocumentSummary(document, console);
            console.WriteLine();

            string finalPath;
            if (!string.IsNullOrEmpty(outPathHint) && !Directory.Exists(outPathHint))
            {
                // Explicit file path: confirm save or restart.
                console.WriteLine("[Enter / y] 저장   [n] 처음부터 다시 작성");
                var saveConfirm = (console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
                RecipeCreateEscapeCommands.ThrowIfCancel(saveConfirm);
                if (saveConfirm == "n")
                {
                    console.WriteLine("저장을 취소합니다. 처음부터 다시 작성합니다.");
                    RecipeCreateScreen.ClearForNewStep(console);
                    return RecipeCreateFlowResult.RestartWizard;
                }

                finalPath = outPathHint;
            }
            else
            {
                // No path / directory hint: prompt user for save path.
                var prompted = PromptSavePath(document, outPathHint, console, cancellation);
                if (prompted is null)
                {
                    console.WriteLine("저장을 취소합니다. 처음부터 다시 작성합니다.");
                    RecipeCreateScreen.ClearForNewStep(console);
                    return RecipeCreateFlowResult.RestartWizard;
                }

                finalPath = prompted;
            }

            RecipeCreateCommand.SaveDocument(document, finalPath, console);
            return RecipeCreateFlowResult.Saved;
        }

        // ── 채널 확정 단계 ───────────────────────────────────────────────────────

        private static void ConfirmChannels(
            RecipeAuthoringSession session, IRecipeConsole console, IRecipeCreateCancellationSource cancellation)
        {
            var method = session.Snapshot().SelectedMethod!.Value;
            if (method != RecipeMethodId.Package)
            {
                return;
            }

            var preSetChannels = session.Snapshot().Values
                .Where(v => v.FieldName == "Channels")
                .Select(v => v.DisplayValue)
                .ToList();

            if (preSetChannels.Count > 0)
            {
                RecipeCreateScreen.ClearForNewStep(console);
                console.WriteLine("채널 확인");
                console.WriteLine();
                foreach (var ch in preSetChannels)
                {
                    console.WriteLine($"  채널: {ch}");
                }

                console.WriteLine();
                console.WriteHints("/cancel: 종료");
                console.WriteLine("[Enter / y] 이 채널 사용   [n] 채널 다시 입력:");

                var answer = (console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
                RecipeCreateEscapeCommands.ThrowIfCancel(answer);

                if (answer == "n")
                {
                    session.ClearField("Channels");
                    PromptChannelEntry(session, console, cancellation);
                }
            }
            else
            {
                PromptChannelEntry(session, console, cancellation);
            }
        }

        private static void PromptChannelEntry(
            RecipeAuthoringSession session, IRecipeConsole console, IRecipeCreateCancellationSource cancellation)
        {
            RecipeCreateScreen.ClearForNewStep(console);
            console.WriteLine("채널 설정");
            console.WriteLine();
            console.WriteLine("사용할 conda 채널을 입력하세요.");
            console.WriteLine("줄바꿈으로 여러 채널을 입력하고, 빈 줄을 입력하면 완료됩니다.");
            console.WriteLine();
            console.WriteLine("  예: bioconda");
            console.WriteLine("      conda-forge");
            console.WriteLine();
            console.WriteHints("/back: 방식 다시 선택   /cancel: 종료");
            console.WriteLine("채널:");

            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                var line = console.ReadLine() ?? string.Empty;
                RecipeCreateEscapeCommands.ThrowIfCancel(line);
                RecipeCreateEscapeCommands.ThrowIfBack(line);

                if (line.Trim().Length == 0)
                {
                    try
                    {
                        session.CompleteListField("Channels");
                        return;
                    }
                    catch (InvalidOperationException ex)
                    {
                        console.WriteLine(ex.Message);
                        continue;
                    }
                }

                var violations = session.AppendListItem("Channels", line);
                if (violations.Count > 0)
                {
                    foreach (var v in violations)
                    {
                        console.WriteLine($"{v.RuleId}: {v.Message}");
                    }
                }
            }
        }

        // ── Base image 선택 단계 (step 4) ────────────────────────────────────────

        private static void PromptBaseImageSelection(
            RecipeAuthoringSession session,
            IImageDigestResolver digestResolver,
            IRecipeConsole console,
            IRecipeCreateCancellationSource cancellation)
        {
            var method = session.Snapshot().SelectedMethod!.Value;
            var candidates = BaseImageCatalog.CandidatesFor(method);
            if (candidates.Count == 0)
            {
                return;
            }

            RecipeCreateScreen.ClearForNewStep(console);
            console.WriteLine("── Base image 선택 ─────────────────────────────────────────");
            console.WriteLine("사용할 기반 이미지를 선택하세요. Digest는 자동으로 조회합니다.");
            console.WriteLine();

            for (var i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                console.WriteLine($"  [{i + 1}] {c.Reference}");
                console.WriteLine($"      {c.Description}");
            }

            console.WriteLine();
            console.WriteLine("  [0] 직접 입력 (다음 단계에서 직접 입력)");
            console.WriteLine();
            console.WriteHints("/cancel: 종료");
            console.WriteLine($"번호를 선택하세요 (1–{candidates.Count}, 0 = 직접 입력):");

            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                var line = (console.ReadLine() ?? string.Empty).Trim();
                RecipeCreateEscapeCommands.ThrowIfCancel(line);

                if (line == "0" || line.Length == 0)
                {
                    return;
                }

                if (!int.TryParse(line, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var idx)
                    || idx < 1 || idx > candidates.Count)
                {
                    console.WriteLine($"1–{candidates.Count} 사이의 번호 또는 0을 입력하세요.");
                    continue;
                }

                var selected = candidates[idx - 1];
                console.WriteLine($"{selected.Reference} 의 digest를 조회합니다...");

                var result = digestResolver
                    .ResolveAsync(selected.Reference, System.Threading.CancellationToken.None)
                    .GetAwaiter().GetResult();

                if (result.Status == ImageDigestResolutionStatus.Resolved
                    && !string.IsNullOrEmpty(result.Digest))
                {
                    var combined = $"{selected.Reference}@{result.Digest}";
                    var violations = session.SetField("ImageRef", combined);
                    if (violations.Count == 0)
                    {
                        console.WriteLine($"설정 완료: {combined}");
                    }
                    else
                    {
                        PrintViolations(violations, console);
                    }

                    return;
                }

                console.WriteLine($"digest 조회 실패: {result.Message ?? result.Status.ToString()}");
                console.WriteLine("다시 시도하려면 번호를, 직접 입력하려면 0을 입력하세요.");
            }
        }

        // ── RunFieldLoop 및 PromptField 계열 ─────────────────────────────────────

        private static void RunFieldLoop(RecipeAuthoringSession session, IRecipeConsole console, IRecipeCreateCancellationSource cancellation)
        {
            var total = RecipeFieldCatalog.FieldsFor(session.Snapshot().SelectedMethod!.Value).Count;
            var history = new List<RecipeFieldDescriptor>();

            RecipeFieldDescriptor? field;
            while ((field = session.NextField()) != null)
            {
                RecipeCreateScreen.ClearForNewStep(console);
                console.WriteLine($"[{history.Count + 1} / {total}]");
                console.WriteHints("/back: 이전 필드   /cancel: 종료   /review: 현재 값   /change-method: 작성 방식 변경");
                console.WriteLine();

                try
                {
                    PromptField(session, field, console, cancellation);
                    history.Add(field);
                }
                catch (RecipeCreateBackRequestedException)
                {
                    // Clear any in-progress list items for the current field before
                    // going back — without this, items typed before /back would
                    // survive to the next pass of the same list field.
                    session.ClearField(field.Name);
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

        private static void PromptField(RecipeAuthoringSession session, RecipeFieldDescriptor field, IRecipeConsole console, IRecipeCreateCancellationSource cancellation)
        {
            switch (field.Type)
            {
                case RecipeFieldType.Scalar:
                    PromptScalarField(session, field, console, cancellation);
                    break;
                case RecipeFieldType.Choice:
                    PromptChoiceField(session, field, console, cancellation);
                    break;
                case RecipeFieldType.StringList:
                    PromptStringListField(session, field, console, cancellation);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(field), field.Type, "Unsupported field type.");
            }
        }

        private static void PromptScalarField(RecipeAuthoringSession session, RecipeFieldDescriptor field, IRecipeConsole console, IRecipeCreateCancellationSource cancellation)
        {
            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                console.WriteLine($"{field.Label.Get("ko")} — {field.Help.Get("ko")}");
                if (field.Examples.Count > 0)
                {
                    console.WriteLine($"   예: {string.Join(", ", field.Examples)}");
                }

                var line = console.ReadLine() ?? string.Empty;

                if (TryHandleChangeMethod(session, line, console))
                {
                    return;
                }

                if (TryHandleCancel(line, console))
                {
                    continue;
                }

                RecipeCreateEscapeCommands.ThrowIfBack(line);

                if (TryHandleReview(session, line, console))
                {
                    continue;
                }

                if (TryHandleHelp(field, line, console))
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

                PrintViolations(violations, console);
            }
        }

        private static void PromptChoiceField(RecipeAuthoringSession session, RecipeFieldDescriptor field, IRecipeConsole console, IRecipeCreateCancellationSource cancellation)
        {
            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                console.WriteLine($"{field.Label.Get("ko")} — {field.Help.Get("ko")}");
                for (var i = 0; i < field.Choices.Count; i++)
                {
                    console.WriteLine($"  [{i + 1}] {field.Choices[i].Label.Get("ko")} — {field.Choices[i].Description.Get("ko")}");
                }

                var line = console.ReadLine() ?? string.Empty;
                if (TryHandleChangeMethod(session, line, console))
                {
                    return;
                }

                if (TryHandleCancel(line, console))
                {
                    continue;
                }

                RecipeCreateEscapeCommands.ThrowIfBack(line);

                if (TryHandleReview(session, line, console))
                {
                    continue;
                }

                if (TryHandleHelp(field, line, console))
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

                PrintViolations(violations, console);
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

        private static void PromptStringListField(RecipeAuthoringSession session, RecipeFieldDescriptor field, IRecipeConsole console, IRecipeCreateCancellationSource cancellation)
        {
            console.WriteLine($"{field.Label.Get("ko")} — {field.Help.Get("ko")} (빈 줄 입력 시 종료)");
            if (field.Examples.Count > 0)
            {
                console.WriteLine($"   예: {string.Join(", ", field.Examples)}");
            }

            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                var line = console.ReadLine() ?? string.Empty;
                if (TryHandleChangeMethod(session, line, console))
                {
                    return;
                }

                if (TryHandleCancel(line, console))
                {
                    continue;
                }

                RecipeCreateEscapeCommands.ThrowIfBack(line);

                if (TryHandleReview(session, line, console))
                {
                    continue;
                }

                if (TryHandleHelp(field, line, console))
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
                        console.WriteLine(ex.Message);
                        continue;
                    }
                }

                var violations = session.AppendListItem(field.Name, line);
                if (violations.Count > 0)
                {
                    PrintViolations(violations, console);
                }
            }
        }

        private static bool TryHandleHelp(RecipeFieldDescriptor field, string line, IRecipeConsole console)
        {
            if (line.Trim() != HelpCommand)
            {
                return false;
            }

            console.WriteLine($"{field.Label.Get("ko")} — {field.Help.Get("ko")}");
            if (field.Examples.Count > 0)
            {
                console.WriteLine($"예시: {string.Join(", ", field.Examples)}");
            }

            console.WriteLine(DescribeRequirement(field.Requirement));
            return true;
        }

        private static bool TryHandleReview(RecipeAuthoringSession session, string line, IRecipeConsole console)
        {
            if (line.Trim() != ReviewCommand)
            {
                return false;
            }

            PrintReview(session, console);
            return true;
        }

        private static void PrintReview(RecipeAuthoringSession session, IRecipeConsole console)
        {
            var snapshot = session.Snapshot();
            var valueByField = snapshot.Values.ToDictionary(v => v.FieldName, v => v.DisplayValue, StringComparer.Ordinal);

            console.WriteLine("현재까지 입력한 내용:");
            console.WriteLine($"작성 방식: {RecipeMethodCatalog.For(snapshot.SelectedMethod!.Value).Label.Get("ko")} ({snapshot.SelectedMethod})");
            foreach (var field in RecipeFieldCatalog.FieldsFor(snapshot.SelectedMethod!.Value))
            {
                var value = valueByField.TryGetValue(field.Name, out var displayValue) ? displayValue : "아직 입력 안 함";
                console.WriteLine($"  {field.Label.Get("ko")} ({field.Name}): {value}");
            }
        }

        private static bool TryHandleCancel(string line, IRecipeConsole console)
        {
            var trimmed = line.Trim();
            if (trimmed != CancelCommand && trimmed != QuitCommand && trimmed != ExitCommand)
            {
                return false;
            }

            console.WriteLine("recipe 생성을 중단하려고 합니다.");
            console.WriteLine("[1] 저장하지 않고 종료");
            console.WriteLine("[2] 계속 작성");
            console.WriteLine("선택:");

            var selection = (console.ReadLine() ?? string.Empty).Trim();
            RecipeCreateEscapeCommands.ThrowIfCancel(selection);
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

        private static bool TryHandleChangeMethod(RecipeAuthoringSession session, string line, IRecipeConsole console)
        {
            if (line.Trim() != ChangeMethodCommand)
            {
                return false;
            }

            console.WriteLine("변경할 방법 번호를 입력하세요: [1] container [2] package [3] mirror [4] source [5] dockerfile");
            var selection = (console.ReadLine() ?? string.Empty).Trim();
            RecipeCreateEscapeCommands.ThrowIfCancel(selection);
            if (RecipeCreateEscapeCommands.IsBack(selection))
            {
                console.WriteLine("method 변경을 취소하고 현재 입력 단계로 돌아갑니다.");
                return true;
            }

            if (!MethodRecommendationPresenter.TryParseMethodSelection(selection, out var nextMethod))
            {
                console.WriteLine("알 수 없는 방법입니다. 변경을 취소합니다.");
                return true;
            }

            var preview = session.PreviewMethodChange(nextMethod);
            var fieldLabelMap = RecipeFieldCatalog.FieldsFor(preview.CurrentMethod)
                .Concat(RecipeFieldCatalog.FieldsFor(preview.NextMethod))
                .GroupBy(f => f.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Label.Get("ko"), StringComparer.Ordinal);
            console.WriteLine($"유지되는 필드: {string.Join(", ", preview.PreservedFields.Select(n => fieldLabelMap.TryGetValue(n, out var l) ? $"{l} ({n})" : n))}");
            console.WriteLine($"재확인이 필요한 필드: {string.Join(", ", preview.FieldsRequiringRevalidation.Select(n => fieldLabelMap.TryGetValue(n, out var l) ? $"{l} ({n})" : n))}");
            console.WriteLine($"버려지는 필드: {string.Join(", ", preview.DiscardedFields.Select(n => fieldLabelMap.TryGetValue(n, out var l) ? $"{l} ({n})" : n))}");
            console.WriteLine("계속할까요? [y/N]");

            var confirm = (console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
            RecipeCreateEscapeCommands.ThrowIfCancel(confirm);
            session.ChangeMethod(nextMethod, confirm == "y" ? ChangeMethodDecision.Proceed : ChangeMethodDecision.Cancel);
            return true;
        }

        private static bool RunRecoveryLoop(RecipeAuthoringSession session, IReadOnlyList<ValidationViolation> violations, IRecipeConsole console, IRecipeCreateCancellationSource cancellation)
        {
            var plan = session.BuildRecoveryPlan(violations);

            console.WriteLine("최종 검증에 실패했습니다. 다음 중 수정할 항목을 선택하세요:");
            for (var i = 0; i < plan.Actions.Count; i++)
            {
                var action = plan.Actions[i];
                console.WriteLine($"  [{i + 1}] {action.Label} — {action.Description.Get("ko")}");
                console.WriteLine($"      힌트: {action.BeginnerHint.Get("ko")}");
            }

            console.WriteLine();
            console.WriteLine("/cancel: 저장하지 않고 종료");
            console.WriteLine("번호를 입력하세요 (빈 줄 = 저장 없이 종료):");
            var selection = (console.ReadLine() ?? string.Empty).Trim();
            RecipeCreateEscapeCommands.ThrowIfCancel(selection);
            if (selection.Length == 0)
            {
                return false;
            }

            if (!int.TryParse(selection, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var index)
                || index < 1 || index > plan.Actions.Count)
            {
                console.WriteLine("알 수 없는 선택입니다.");
                return RunRecoveryLoop(session, violations, console, cancellation);
            }

            var chosen = plan.Actions[index - 1];
            try
            {
                foreach (var fieldName in chosen.RelatedFields)
                {
                    ReEditField(session, fieldName, console, cancellation);
                }
            }
            catch (RecipeCreateBackRequestedException)
            {
                console.WriteLine("/back은 수정 단계에서 지원하지 않습니다. /cancel로 종료하거나 값을 입력하세요.");
            }

            return true;
        }

        private static void ReEditField(RecipeAuthoringSession session, string fieldName, IRecipeConsole console, IRecipeCreateCancellationSource cancellation)
        {
            var field = RecipeFieldCatalog.FieldsFor(session.Snapshot().SelectedMethod!.Value).FirstOrDefault(f => f.Name == fieldName);
            if (field is null)
            {
                return;
            }

            session.ConfirmInvalidatedField(fieldName);
            PromptField(session, field, console, cancellation);
        }

        private static void PrintViolations(IReadOnlyList<ValidationViolation> violations, IRecipeConsole console)
        {
            foreach (var violation in violations)
            {
                console.WriteLine(violation.Field is null
                    ? $"{violation.RuleId}: {violation.Message}"
                    : $"{violation.RuleId} ({violation.Field}): {violation.Message}");
            }
        }

        private static void PromptPortSelection(
            RecipeDocument document, IRecipeConsole console, IRecipeCreateCancellationSource cancellation)
        {
            RecipeCreateScreen.ClearForNewStep(console);
            console.WriteLine("── 포트 설정 (선택사항) ────────────────────────────────────");
            console.WriteLine("이 도구가 받는 입력 파일 유형을 설정합니다.");
            console.WriteLine("나중에 ToolFunctionSpec으로 교체할 초안입니다.");
            console.WriteLine();

            var inputPresets = InputOutputPresetCatalog.InputPresets;
            for (var i = 0; i < inputPresets.Count; i++)
            {
                var p = inputPresets[i];
                console.WriteLine($"  [{i + 1}] {p.Label.Get("ko")}");
                console.WriteLine($"      {p.Description.Get("ko")}");
                if (p.Examples.Count > 0)
                {
                    console.WriteLine($"      예: {string.Join(", ", p.Examples)}");
                }
            }

            console.WriteLine();
            console.WriteLine("번호 입력 (쉼표 구분, 빈 줄 = 건너뛰기):");

            if (cancellation.IsCancellationRequested)
            {
                throw new RecipeCreateCancelledException();
            }

            var inputLine = (console.ReadLine() ?? string.Empty).Trim();
            RecipeCreateEscapeCommands.ThrowIfCancel(inputLine);

            if (inputLine.Length > 0 && !RecipeCreateEscapeCommands.IsBack(inputLine))
            {
                foreach (var idx in ParseNumberList(inputLine, inputPresets.Count))
                {
                    var preset = inputPresets[idx];
                    if (preset.Id != InputOutputPresetCatalog.CustomPresetId)
                    {
                        document.Inputs.Add(new ToolInput
                        {
                            Name = preset.Role,
                            Role = preset.Role,
                            Format = preset.Format,
                            Shape = preset.Shape,
                            Required = true,
                        });
                    }
                }
            }

            console.WriteLine();
            console.WriteLine("이 도구가 생성하는 출력 파일 유형을 설정합니다.");
            console.WriteLine();

            var outputPresets = InputOutputPresetCatalog.OutputPresets;
            for (var i = 0; i < outputPresets.Count; i++)
            {
                var p = outputPresets[i];
                console.WriteLine($"  [{i + 1}] {p.Label.Get("ko")}");
                console.WriteLine($"      {p.Description.Get("ko")}");
                if (p.Examples.Count > 0)
                {
                    console.WriteLine($"      예: {string.Join(", ", p.Examples)}");
                }
            }

            console.WriteLine();
            console.WriteLine("번호 입력 (쉼표 구분, 빈 줄 = 건너뛰기):");

            var outputLine = (console.ReadLine() ?? string.Empty).Trim();
            RecipeCreateEscapeCommands.ThrowIfCancel(outputLine);

            if (outputLine.Length > 0 && !RecipeCreateEscapeCommands.IsBack(outputLine))
            {
                foreach (var idx in ParseNumberList(outputLine, outputPresets.Count))
                {
                    var preset = outputPresets[idx];
                    if (preset.Id != InputOutputPresetCatalog.CustomPresetId)
                    {
                        document.Outputs.Add(new ToolOutput
                        {
                            Name = preset.Role,
                            Role = preset.Role,
                            Format = preset.Format,
                            Shape = "single",
                            Class = preset.Class,
                        });
                    }
                }
            }
        }

        private static IReadOnlyList<int> ParseNumberList(string input, int maxCount)
        {
            var result = new List<int>();
            foreach (var part in input.Split(','))
            {
                var trimmed = part.Trim();
                if (int.TryParse(trimmed, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n)
                    && n >= 1 && n <= maxCount)
                {
                    result.Add(n - 1);
                }
            }

            return result;
        }

        // Returns the resolved save path, or null if the user chose to restart the wizard.
        private static string? PromptSavePath(
            RecipeDocument document,
            string? dirHint,
            IRecipeConsole console,
            IRecipeCreateCancellationSource cancellation)
        {
            var toolName = document.ToolName ?? "recipe";
            var version = document.Version ?? "1.0.0";
            var defaultName = $"{toolName}-{version}.json";
            var dir = !string.IsNullOrEmpty(dirHint)
                ? dirHint
                : Directory.GetCurrentDirectory();
            var defaultPath = Path.Combine(dir, defaultName);

            console.WriteLine("저장 위치를 확인하세요.");
            console.WriteLine();
            console.WriteLine($"기본 경로: {defaultPath}");
            console.WriteHints("/cancel: 종료");
            console.WriteLine("다른 경로를 입력하거나 Enter로 기본 경로를 사용 [n = 처음부터 다시 작성]:");

            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    throw new RecipeCreateCancelledException();
                }

                var pathInput = (console.ReadLine() ?? string.Empty).Trim();
                RecipeCreateEscapeCommands.ThrowIfCancel(pathInput);

                if (pathInput.Equals("n", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var savePath = string.IsNullOrEmpty(pathInput) ? defaultPath : pathInput;

                if (!File.Exists(savePath))
                {
                    return savePath;
                }

                console.WriteLine($"파일이 이미 존재합니다: {savePath}");
                console.WriteLine("[Enter / y] 덮어쓰기   [n] 다른 경로 입력");
                var overwrite = (console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
                RecipeCreateEscapeCommands.ThrowIfCancel(overwrite);

                if (overwrite != "n")
                {
                    return savePath;
                }

                console.WriteLine("다른 경로를 입력하세요:");
            }
        }

        private static void PrintDocumentSummary(RecipeDocument document, IRecipeConsole console)
        {
            console.WriteLine($"  도구 이름: {document.ToolName}");
            console.WriteLine($"  도구 버전: {document.Version}");
            console.WriteLine($"  기본 실행 명령: {document.Script}");
            if (!string.IsNullOrEmpty(document.BaseImage))
            {
                console.WriteLine($"  기반 이미지: {document.BaseImage}");
            }

            if (document.Packages.Count > 0)
            {
                console.WriteLine($"  패키지: {string.Join(", ", document.Packages)}");
            }

            if (document.Inputs.Count > 0)
            {
                console.WriteLine($"  입력 포트: {string.Join(", ", document.Inputs.Select(i => i.Name))}");
            }

            if (document.Outputs.Count > 0)
            {
                console.WriteLine($"  출력 포트: {string.Join(", ", document.Outputs.Select(o => o.Name))}");
            }
        }
    }
}
