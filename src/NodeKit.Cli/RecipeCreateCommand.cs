using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using NodeKit.Authoring;
using NodeKit.Authoring.Recipes;
using NodeKit.Validation;
using NodeKit.Validation.Recipes;

namespace NodeKit.Cli
{
    /// <summary>
    /// nodekit recipe create entry point. Builds a RecipeDocument via
    /// RecipeAuthoringSession (interactive wizard or non-interactive flags)
    /// and ends in the same RecipeValidationPipeline.ValidateRecipe call as
    /// nodekit validate/render — see docs/NODEKIT_CLI_FIRST_SPRINT_PLAN.md
    /// Sprint R7 and docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md
    /// Section 5.
    /// </summary>
    internal static class RecipeCreateCommand
    {
        internal static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        private static readonly IReadOnlyDictionary<string, RecipeMethodId> _publicMethodNames =
            new Dictionary<string, RecipeMethodId>(StringComparer.Ordinal)
            {
                ["container"] = RecipeMethodId.Container,
                ["package"] = RecipeMethodId.Package,
                ["mirror"] = RecipeMethodId.Mirror,
                ["source"] = RecipeMethodId.Source,
                ["dockerfile"] = RecipeMethodId.Dockerfile,
            };

        private static readonly string[] _internalBuildKindNames =
        {
            "conda", "micromamba", "source-build", "dockerfile-fallback",
        };

        public static int Run(string? outPathHint, string[] options, IRecipeConsole console, TextWriter stdout, TextWriter stderr)
        {
            var parsed = ParseOptions(options);
            if (parsed.Error != null)
            {
                stderr.WriteLine(parsed.Error);
                return 2;
            }

            if (parsed.NonInteractive && string.IsNullOrEmpty(outPathHint))
            {
                stderr.WriteLine("--non-interactive 모드에서는 출력 경로가 필요합니다.");
                return 2;
            }

            try
            {
                return parsed.NonInteractive
                    ? RunNonInteractive(outPathHint!, parsed, stdout, stderr)
                    : RecipeCreateInteractiveRunner.Run(outPathHint, parsed, console, stderr);
            }
            catch (InvalidOperationException ex)
            {
                stderr.WriteLine(ex.Message);
                return 1;
            }
        }

        internal static bool IsListType(RecipeFieldDescriptor field) =>
            field.Type is RecipeFieldType.StringList;

        internal static void SaveDocument(RecipeDocument document, string outPath, TextWriter stdout) =>
            SaveDocument(document, outPath, new PlainTextRecipeConsole(TextReader.Null, stdout));

        internal static void SaveDocument(RecipeDocument document, string outPath, IRecipeConsole console)
        {
            File.WriteAllText(outPath, JsonSerializer.Serialize(document, JsonOptions));
            console.WriteLine($"저장되었습니다: {outPath}");
        }

        private static int RunNonInteractive(string outPath, RecipeCreateOptions parsed, TextWriter stdout, TextWriter stderr)
        {
            var method = parsed.Method!.Value;
            var session = new RecipeAuthoringSession();
            session.SelectMethod(method);

            if (parsed.AcceptDockerfileWarning)
            {
                session.AcceptDockerfileWarning();
            }

            if (parsed.Engine != null)
            {
                var engineViolations = session.SetField("PackageEngine", parsed.Engine);
                if (engineViolations.Count > 0)
                {
                    CliApp.PrintViolations(engineViolations, stderr);
                    return 1;
                }
            }

            var catalogFields = RecipeFieldCatalog.FieldsFor(method);
            var fieldByName = catalogFields.ToDictionary(f => f.Name, f => f, StringComparer.Ordinal);

            foreach (var (name, value) in parsed.Fields)
            {
                if (!fieldByName.TryGetValue(name, out var field))
                {
                    stderr.WriteLine($"{method} method에서 알 수 없는 필드입니다: {name}");
                    return 2;
                }

                var violations = IsListType(field)
                    ? session.AppendListItem(name, value)
                    : session.SetField(name, value);

                if (violations.Count > 0)
                {
                    CliApp.PrintViolations(violations, stderr);
                    return 1;
                }
            }

            foreach (var field in catalogFields.Where(f => IsListType(f)))
            {
                session.CompleteListField(field.Name);
            }

            var setFieldNames = parsed.Fields.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var field in catalogFields.Where(f => f.Requirement == RecipeFieldRequirement.Optional && !IsListType(f)))
            {
                if (!setFieldNames.Contains(field.Name))
                {
                    session.SkipOptionalField(field.Name);
                }
            }

            foreach (var warningField in session.Snapshot().RecommendedWarnings)
            {
                stderr.WriteLine($"권장 필드가 비어 있습니다 (계속 진행합니다): {warningField}");
            }

            if (!session.IsComplete)
            {
                stderr.WriteLine($"필수 필드가 누락되었습니다: {string.Join(", ", session.Snapshot().MissingRequiredFields)}");
                return 1;
            }

            var document = session.Build();
            document.BuildKind = RecipeBuildKindResolver.Resolve(method, document);

            var mismatch = BaseImageEngineMismatchChecker.DescribeMismatch(document.BuildKind.Value, document.BaseImage);
            if (mismatch != null)
            {
                stderr.WriteLine($"경고: {mismatch}");
            }

            var sourceBuildAdvisory = SourceBuildBaseImageAdvisor.Describe(document.BuildKind.Value, document.BaseImage);
            if (sourceBuildAdvisory != null)
            {
                stderr.WriteLine($"경고: {sourceBuildAdvisory}");
            }

            var buildDependenciesAdvisory = BuildDependenciesAdvisor.Describe(document.BuildKind.Value, document.BuildDependencies);
            if (buildDependenciesAdvisory != null)
            {
                stderr.WriteLine($"경고: {buildDependenciesAdvisory}");
            }

            var result = RecipeValidationPipeline.ValidateRecipe(document);
            if (!result.IsValid)
            {
                CliApp.PrintViolations(result.Violations, stderr);
                return 1;
            }

            SaveDocument(document, outPath, stdout);
            return 0;
        }

        private static RecipeCreateOptions ParseOptions(string[] options)
        {
            string? methodRaw = null;
            string? engine = null;
            var acceptDockerfileWarning = false;
            var nonInteractive = false;
            var fields = new List<(string Name, string Value)>();

            for (var i = 0; i < options.Length; i++)
            {
                switch (options[i])
                {
                    case "--method":
                        if (!TryTakeNext(options, ref i, out methodRaw))
                        {
                            return Error("--method 옵션에는 값이 필요합니다.");
                        }

                        break;
                    case "--engine":
                        if (!TryTakeNext(options, ref i, out engine))
                        {
                            return Error("--engine 옵션에는 값이 필요합니다.");
                        }

                        break;
                    case "--accept-dockerfile-warning":
                        acceptDockerfileWarning = true;
                        break;
                    case "--non-interactive":
                        nonInteractive = true;
                        break;
                    case "--field":
                        if (!TryTakeNext(options, ref i, out var fieldSpec) || !TrySplitOnce(fieldSpec, out var fieldEntry))
                        {
                            return Error("--field 옵션은 --field Name=Value 형식이어야 합니다.");
                        }

                        fields.Add(fieldEntry);
                        break;
                    default:
                        return Error($"알 수 없는 옵션입니다: {options[i]}");
                }
            }

            if (methodRaw != null && _internalBuildKindNames.Contains(methodRaw, StringComparer.Ordinal))
            {
                return Error($"--method {methodRaw}는 내부 build kind 이름입니다. container/package/mirror/source/dockerfile 중 하나를 사용하세요.");
            }

            RecipeMethodId? method = null;
            if (methodRaw != null)
            {
                if (!_publicMethodNames.TryGetValue(methodRaw, out var resolvedMethod))
                {
                    return Error($"알 수 없는 method입니다: {methodRaw} (container | package | mirror | source | dockerfile)");
                }

                method = resolvedMethod;
            }

            if (engine != null && method != RecipeMethodId.Package)
            {
                return Error("--engine can only be used with --method package.");
            }

            if (acceptDockerfileWarning && method != RecipeMethodId.Dockerfile)
            {
                return Error("--accept-dockerfile-warning can only be used with --method dockerfile.");
            }

            if (nonInteractive && method is null)
            {
                return Error("--non-interactive 모드에는 --method가 필요합니다.");
            }

            if (nonInteractive && method == RecipeMethodId.Dockerfile && !acceptDockerfileWarning)
            {
                return Error("--method dockerfile은 --non-interactive 모드에서 --accept-dockerfile-warning이 필요합니다.");
            }

            return new RecipeCreateOptions(method, engine, acceptDockerfileWarning, nonInteractive, fields, Error: null);
        }

        private static bool TryTakeNext(string[] options, ref int i, out string value)
        {
            if (i + 1 >= options.Length)
            {
                value = string.Empty;
                return false;
            }

            i++;
            value = options[i];
            return true;
        }

        private static bool TrySplitOnce(string spec, out (string Name, string Value) entry)
        {
            var separatorIndex = spec.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                entry = default;
                return false;
            }

            entry = (spec[..separatorIndex], spec[(separatorIndex + 1)..]);
            return true;
        }

        private static RecipeCreateOptions Error(string message) =>
            new(null, null, false, false, Array.Empty<(string, string)>(), message);
    }
}
