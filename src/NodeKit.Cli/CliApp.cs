using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NodeKit.Authoring.Recipes;
using NodeKit.Grpc;
using NodeKit.Validation;
using NodeKit.Validation.Recipes;

namespace NodeKit.Cli
{
    /// <summary>
    /// nodekit CLI 진입점 로직. legacy BuildRequest 경로만 다룬다:
    /// RecipeDocument → RecipeValidator → RecipeRenderer → ToolDefinition →
    /// 기존 L1 validator 체인 → BuildRequest 직렬화.
    /// submit/build/gRPC 전송/NodeVault 조회는 이 CLI의 범위 밖이다.
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

        public static int Run(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr)
        {
            if (args.Length == 0)
            {
                stderr.WriteLine("사용법: nodekit validate <recipe.json> | nodekit render <recipe.json> --out <build-request.json> | nodekit recipe create <recipe.json> [...]");
                return 2;
            }

            return args[0] switch
            {
                "validate" => RunValidate(args, stdout, stderr),
                "render" => RunRender(args, stdout, stderr),
                "recipe" => RunRecipe(args, stdin, stdout, stderr),
                _ => Unknown(args[0], stderr),
            };
        }

        private static int RunRecipe(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr)
        {
            if (args.Length < 3 || args[1] != "create")
            {
                stderr.WriteLine("사용법: nodekit recipe create <recipe.json> [--method ...] [--non-interactive ...]");
                return 2;
            }

            return RecipeCreateCommand.Run(args[2], args[3..], stdin, stdout, stderr);
        }

        private static int Unknown(string command, TextWriter stderr)
        {
            stderr.WriteLine($"알 수 없는 명령입니다: {command} (validate | render | recipe 만 지원합니다)");
            return 2;
        }

        private static int RunValidate(string[] args, TextWriter stdout, TextWriter stderr)
        {
            if (args.Length < 2)
            {
                stderr.WriteLine("사용법: nodekit validate <recipe.json>");
                return 2;
            }

            if (!TryLoadRecipe(args[1], stderr, out var recipe))
            {
                return 2;
            }

            var result = RecipeValidationPipeline.ValidateRecipe(recipe!);
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
            if (args.Length < 2)
            {
                stderr.WriteLine("사용법: nodekit render <recipe.json> --out <build-request.json>");
                return 2;
            }

            var outPath = ParseOutOption(args, stderr);
            if (outPath is null)
            {
                return 2;
            }

            if (!TryLoadRecipe(args[1], stderr, out var recipe))
            {
                return 2;
            }

            var result = RecipeValidationPipeline.ValidateRecipe(recipe!);
            if (!result.IsValid)
            {
                PrintViolations(result.Violations, stderr);
                return 1;
            }

            var definition = RecipeRenderer.Render(recipe!);
            var buildRequest = BuildRequestFactory.FromToolDefinition(definition);
            var json = JsonSerializer.Serialize(buildRequest, _jsonOptions);

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

        private static string? ParseOutOption(string[] args, TextWriter stderr)
        {
            var outIndex = Array.IndexOf(args, "--out");
            if (outIndex < 0 || outIndex + 1 >= args.Length)
            {
                stderr.WriteLine("--out <build-request.json> 옵션이 필요합니다.");
                return null;
            }

            return args[outIndex + 1];
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
