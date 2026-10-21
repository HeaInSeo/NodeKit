using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using NodeKit.Authoring.Recipes;
using NodeKit.Authoring.ToolFunctionRecipes;
using NodeKit.Cli.Localization;
using NodeKit.Grpc;
using NodeKit.Validation;
using NodeKit.Validation.Recipes;
using NodeKit.Validation.ToolFunctionRecipes;

namespace NodeKit.Cli
{
    /// <summary>
    /// nodekit CLI 진입점 로직. 로컬 recipe authoring/render 경로를 다룬다:
    /// RecipeDocument → RecipeValidator → RecipeRenderer → ToolDefinition →
    /// 기존 L1 validator 체인 → ToolDefinition/BuildRequest JSON preview 또는 submit용 raw_spec.
    /// SubmitCommand가 NodeVault ToolSpec gRPC 전송을 담당한다.
    /// </summary>
    internal static class CliApp
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public static int Run(string[] args, TextWriter stdout, TextWriter stderr) =>
            Run(args, TextReader.Null, stdout, stderr);

        public static int Run(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr) =>
            Run(args, stdin, stdout, stderr, Environment.GetEnvironmentVariable);

        // UF01 (#96): --lang/NODEKIT_LANG로 정한 언어는 이 호출 범위에만 적용된다(CliText.Use). --lang은 하위 명령의
        // 옵션 파서에 닿기 전에 args에서 지운다.
        internal static int Run(
            string[] args,
            TextReader stdin,
            TextWriter stdout,
            TextWriter stderr,
            Func<string, string?> getEnvironmentVariable)
        {
            if (!CliLanguageSelector.TrySelect(args, getEnvironmentVariable, stderr, out var language, out var commandArgs))
            {
                return 2;
            }

            using (CliText.Use(language))
            {
                return Dispatch(commandArgs, stdin, stdout, stderr);
            }
        }

        private static string TopLevelUsage => CliText.Get("Usage_TopLevel");

        private static string FunctionRecipeUsage => CliText.Get("Usage_FunctionRecipe");

        private static string ValidateUsage => CliText.Get("Usage_Validate");

        private static string FunctionRecipeValidateUsage => CliText.Get("Usage_FunctionRecipeValidate");

        private static string RenderUsageLine1 => CliText.Get("Usage_RenderLine1");

        private static string RenderUsageLine2 => CliText.Get("Usage_RenderLine2");

        private static string RecipeCreateUsage => CliText.Get("Usage_RecipeCreate");

        private static int Dispatch(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr)
        {
            if (args.Length == 0)
            {
                stderr.WriteLine(TopLevelUsage);
                return 2;
            }

            if (args[0] is "--help" or "-h")
            {
                stdout.WriteLine(TopLevelUsage);
                return 0;
            }

            return args[0] switch
            {
                "validate" => RunValidate(args, stdout, stderr),
                "render" => RunRender(args, stdout, stderr),
                "submit" => SubmitCommand.Run(args, stdout, stderr),
                "recipe" => RunRecipe(args, stdin, stdout, stderr),
                "function-recipe" => RunFunctionRecipe(args, stdin, stdout, stderr),
                "receipt" => Operations.ReceiptCommand.Run(args, stdout, stderr),
                _ => Unknown(args[0], stderr),
            };
        }

        private static int RunFunctionRecipe(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr)
        {
            if (args.Length < 2 || (args.Length >= 2 && args[1] is "--help" or "-h"))
            {
                stdout.WriteLine(FunctionRecipeUsage);
                return args.Length < 2 ? 2 : 0;
            }

            return args[1] switch
            {
                "create" => RunFunctionRecipeCreate(args, stdin, stdout, stderr),
                "validate" => RunFunctionRecipeValidate(args, stdout, stderr),
                "render" => ToolFunctionRecipeRenderCommand.Run(args, stdout, stderr),
                "submit" => ToolFunctionRecipeSubmitCommand.Run(args, stdout, stderr),
                "register-replay" => ToolFunctionRegistrar.RunReplay(args, stdout, stderr),
                _ when IsHelpRequested(args) => Help(FunctionRecipeUsage, stdout),
                _ => Unknown($"function-recipe {args[1]}", stderr),
            };
        }

        private static int Help(string usage, TextWriter stdout)
        {
            stdout.WriteLine(usage);
            return 0;
        }

        private static int RunFunctionRecipeCreate(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr)
        {
            IRecipeConsole console = (!Console.IsOutputRedirected && !Console.IsInputRedirected)
                ? new AnsiRecipeConsole()
                : new PlainTextRecipeConsole(stdin, stdout);

            return ToolFunctionRecipeCreateCommand.Run(args, console, stdout, stderr);
        }

        private static int RunFunctionRecipeValidate(string[] args, TextWriter stdout, TextWriter stderr)
        {
            // S4-03-D: --help는 파일을 열지 않고 사용법만 출력한다.
            if (IsHelpRequested(args))
            {
                stdout.WriteLine(FunctionRecipeValidateUsage);
                return 0;
            }

            if (args.Length < 3)
            {
                stderr.WriteLine(FunctionRecipeValidateUsage);
                return 2;
            }

            // S4-03-E: 지원 옵션이 없으므로 path 뒤의 옵션은 조용히 무시하지 않고 exit 2로 거부한다.
            if (!CliOptionParser.TryParse(
                args,
                startIndex: 3,
                stderr,
                valueOptions: System.Array.Empty<string>(),
                flagOptions: System.Array.Empty<string>(),
                out _,
                out _))
            {
                return 2;
            }

            if (!ToolFunctionRecipeCliIo.TryLoad(args[2], stderr, out var recipe))
            {
                return 2;
            }

            var result = ToolFunctionRecipeValidationPipeline.Validate(recipe!);
            if (!result.IsValid)
            {
                PrintViolations(result.Violations, stderr);
                return 1;
            }

            // 검증 결과(State 등)를 입력 파일에 되돌려 쓴다. 저장 실패면 "검증 통과"를
            // 출력하지 않고 원본 파일을 그대로 둔다.
            var exitCode = AtomicFileWriter.WriteForCli(args[2], JsonSerializer.Serialize(recipe, ToolFunctionRecipeCliIo.JsonOptions), stderr);
            if (exitCode != 0)
            {
                return exitCode;
            }

            stdout.WriteLine(CliText.Get("FunctionRecipeValidate_Passed"));
            return 0;
        }

        // 여러 서브커맨드가 각자 --help/-h를 인식해야 해서(P2 리뷰: 최상위와
        // 서브커맨드 4개 전부) 하나로 통일 — args 어디에 있든 인식한다("nodekit
        // submit recipe.json --help"도 동작).
        private static bool IsHelpRequested(string[] args) =>
            args.Any(a => a is "--help" or "-h");

        private static int RunRecipe(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr)
        {
            if (IsHelpRequested(args))
            {
                stdout.WriteLine(RecipeCreateUsage);
                return 0;
            }

            if (args.Length < 2 || args[1] != "create")
            {
                stderr.WriteLine(RecipeCreateUsage);
                return 2;
            }

            // Path argument is optional. If args[2] exists and is not a flag, treat it as
            // the output path hint; otherwise the wizard prompts for a path at the end.
            string? outPathHint;
            string[] options;
            if (args.Length >= 3 && !args[2].StartsWith("--", StringComparison.Ordinal))
            {
                outPathHint = args[2];
                options = args[3..];
            }
            else
            {
                outPathHint = null;
                options = args.Length >= 3 ? args[2..] : Array.Empty<string>();
            }

            IRecipeConsole console = (!Console.IsOutputRedirected && !Console.IsInputRedirected)
                ? new AnsiRecipeConsole()
                : new PlainTextRecipeConsole(stdin, stdout);

            return RecipeCreateCommand.Run(outPathHint, options, console, stdout, stderr);
        }

        private static int Unknown(string command, TextWriter stderr)
        {
            stderr.WriteLine(CliText.Format("Error_UnknownCommand", command));
            return 2;
        }

        private static int RunValidate(string[] args, TextWriter stdout, TextWriter stderr)
        {
            if (IsHelpRequested(args))
            {
                stdout.WriteLine(ValidateUsage);
                return 0;
            }

            if (args.Length < 2)
            {
                stderr.WriteLine(ValidateUsage);
                return 2;
            }

            if (!CliOptionParser.TryParse(
                args,
                startIndex: 2,
                stderr,
                valueOptions: System.Array.Empty<string>(),
                flagOptions: new[] { "--strict-reproducible" },
                out _,
                out var flags))
            {
                return 2;
            }

            if (!TryLoadRecipe(args[1], stderr, out var recipe))
            {
                return 2;
            }

            var result = RecipeValidationPipeline.ValidateRecipe(recipe!, flags.Contains("--strict-reproducible"));
            if (result.IsValid)
            {
                stdout.WriteLine("OK");
                return 0;
            }

            PrintViolations(result.Violations, stderr);
            return 1;
        }

        private static int RunRender(string[] args, TextWriter stdout, TextWriter stderr)
        {
            if (IsHelpRequested(args))
            {
                stdout.WriteLine(RenderUsageLine1);
                stdout.WriteLine(RenderUsageLine2);
                return 0;
            }

            if (args.Length < 2)
            {
                stderr.WriteLine(RenderUsageLine1);
                stderr.WriteLine(RenderUsageLine2);
                return 2;
            }

            if (!CliOptionParser.TryParse(
                args,
                startIndex: 2,
                stderr,
                valueOptions: new[] { "--out", "--format" },
                flagOptions: new[] { "--strict-reproducible", "--pretty" },
                out var values,
                out var flags))
            {
                return 2;
            }

            if (!values.TryGetValue("--out", out var outPath))
            {
                stderr.WriteLine(CliText.Get("Render_OutRequired"));
                return 2;
            }

            if (!TryNormalizeRenderFormat(values.GetValueOrDefault("--format", "build-request"), stderr, out var format))
            {
                return 2;
            }

            if (!TryLoadRecipe(args[1], stderr, out var recipe))
            {
                return 2;
            }

            var result = RecipeValidationPipeline.ValidateRecipe(recipe!, flags.Contains("--strict-reproducible"));
            if (!result.IsValid)
            {
                PrintViolations(result.Violations, stderr);
                return 1;
            }

            var definition = RecipeRenderer.Render(recipe!);
            var json = format == "raw-spec"
                ? ToolSpecRawSpecFactory.Build(definition)
                : JsonSerializer.Serialize(BuildRequestFactory.FromToolDefinition(definition), _jsonOptions);

            if (format == "raw-spec" && flags.Contains("--pretty"))
            {
                json = PrettyPrintJson(json);
            }

            if (outPath == "-")
            {
                stdout.WriteLine(json);
                return 0;
            }

            // --out .(디렉터리), 없는 상위 디렉터리, 권한 없는 경로는 exit 2와
            // 진단으로 끝나고 기존 출력 파일은 byte 단위로 보존된다.
            return AtomicFileWriter.WriteForCli(outPath, json, stderr);
        }

        // Accepts case/underscore variants (RAW-SPEC, raw_spec, Raw-Spec, ...) so a
        // typo'd --format value doesn't need to match "build-request"/"raw-spec"
        // exactly — this mirrors --strict-reproducible etc. being case-sensitive
        // flags the user types verbatim, but --format is a value users are more
        // likely to guess-type from memory.
        private static bool TryNormalizeRenderFormat(string rawFormat, TextWriter stderr, out string format)
        {
            var normalized = rawFormat.Trim().ToLowerInvariant().Replace('_', '-');
            if (normalized is "build-request" or "raw-spec")
            {
                format = normalized;
                return true;
            }

            format = string.Empty;
            stderr.WriteLine(CliText.Format("Render_InvalidFormat", rawFormat));
            return false;
        }

        private static string PrettyPrintJson(string compactJson)
        {
            using var document = JsonDocument.Parse(compactJson);
            return JsonSerializer.Serialize(document.RootElement, _jsonOptions);
        }

        private static bool TryLoadRecipe(string path, TextWriter stderr, out RecipeDocument? recipe)
        {
            if (AuthoringFileLoader.TryLoadRecipe(path, _jsonOptions, out recipe, out var error))
            {
                return true;
            }

            stderr.WriteLine(error!.Message);
            return false;
        }

        internal static void PrintViolations(System.Collections.Generic.IReadOnlyList<ValidationViolation> violations, TextWriter stderr)
        {
            foreach (var violation in violations)
            {
                stderr.WriteLine(violation.Field is null
                    ? $"{violation.RuleId}: {violation.Message}"
                    : $"{violation.RuleId} ({violation.Field}): {violation.Message}");
            }
        }
    }
}
