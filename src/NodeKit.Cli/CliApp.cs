using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using NodeKit.Authoring.Recipes;
using NodeKit.Grpc;
using NodeKit.Validation;
using NodeKit.Validation.Recipes;

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
        private const string TopLevelUsage =
            "사용법:\n" +
            "  nodekit validate <recipe.json> [--strict-reproducible]\n" +
            "  nodekit render <recipe.json> --out <build-request.json> [--format build-request|raw-spec] [--pretty] [--strict-reproducible]\n" +
            "  nodekit submit <recipe.json> [--url <url>] [--connect-timeout <seconds>] [--watch-timeout <duration>] [--format human|jsonl] [--strict-reproducible]\n" +
            "  nodekit recipe create [<recipe.json>] [--method ...] [--non-interactive ...]\n" +
            "\n" +
            "각 명령의 자세한 옵션은 `nodekit <명령> --help`로 확인하세요 (예: nodekit submit --help).";

        private const string ValidateUsage = "사용법: nodekit validate <recipe.json> [--strict-reproducible]";

        private const string RenderUsageLine1 =
            "사용법: nodekit render <recipe.json> --out <build-request.json> [--format build-request|raw-spec] [--pretty] [--strict-reproducible]";

        private const string RenderUsageLine2 =
            "  (로컬 미리보기 전용 — 네트워크 호출 없음. --format 기본값 build-request는 submit의 입력이 아님, raw-spec은 실제 submit이 ResolveToolSpec에 보내는 ToolSpecRequest의 raw_spec 필드 값과 동일(tool_name/version/requested_at 등 나머지 필드는 포함 안 함). raw-spec은 기본적으로 실제 전송 payload와 동일한 한 줄 JSON — 사람이 읽기 편하게 보려면 --pretty. 실제 제출은 nodekit submit <recipe.json>)";

        private const string RecipeCreateUsage = "사용법: nodekit recipe create [<recipe.json>] [--method ...] [--non-interactive ...]";

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public static int Run(string[] args, TextWriter stdout, TextWriter stderr) =>
            Run(args, TextReader.Null, stdout, stderr);

        public static int Run(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr)
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
                _ => Unknown(args[0], stderr),
            };
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
            stderr.WriteLine($"알 수 없는 명령입니다: {command} (validate | render | submit | recipe 만 지원합니다)");
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
                stderr.WriteLine("--out <build-request.json> 옵션이 필요합니다.");
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
            }
            else
            {
                File.WriteAllText(outPath, json);
            }

            return 0;
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
            stderr.WriteLine($"--format 옵션 값이 올바르지 않습니다: {rawFormat} (build-request 또는 raw-spec)");
            return false;
        }

        private static string PrettyPrintJson(string compactJson)
        {
            using var document = JsonDocument.Parse(compactJson);
            return JsonSerializer.Serialize(document.RootElement, _jsonOptions);
        }

        private static bool TryLoadRecipe(string path, TextWriter stderr, out RecipeDocument? recipe)
        {
            recipe = null;
            string content;
            try
            {
                content = File.ReadAllText(path);
            }
            catch (IOException ex)
            {
                stderr.WriteLine($"recipe 파일을 읽을 수 없습니다: {path} ({ex.Message})");
                return false;
            }

            try
            {
                recipe = JsonSerializer.Deserialize<RecipeDocument>(content, _jsonOptions);
            }
            catch (JsonException ex)
            {
                stderr.WriteLine($"recipe JSON 파싱에 실패했습니다: {path} ({ex.Message})");
                return false;
            }

            if (recipe is null)
            {
                stderr.WriteLine($"recipe 파일이 비어있습니다: {path}");
                return false;
            }

            recipe.Normalize();

            if (recipe.BuildKind is null)
            {
                stderr.WriteLine(
                    $"recipe 파일에 buildKind가 없습니다: {path} " +
                    "(Conda | Micromamba | BioContainer | SourceBuild | PackageMirror | DockerfileFallback 중 하나를 지정하세요.)");
                recipe = null;
                return false;
            }

            return true;
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
