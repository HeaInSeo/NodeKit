using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using NodeKit.Authoring.ToolFunctionRecipes;
using NodeKit.Validation;
using NodeKit.Validation.ToolFunctionRecipes;

namespace NodeKit.Cli
{
    /// <summary>
    /// nodekit function-recipe create entry point — contracts/cli-function-recipe-commands.md
    /// §create. T001 기술 스파이크 결정(아래 참고)에 따라 --non-interactive
    /// --field는 스칼라 필드만 지원한다.
    ///
    /// **T001 결정**: (a) 인덱스/중첩 경로 파서를 새로 만든다 — quickstart.md
    /// 시나리오 1이 `--field InputPorts[0].Name=bam` 형태를 요구하므로,
    /// RecipeFieldCatalog의 평평한 카탈로그를 그대로 재사용하는 대신
    /// ToolFunctionRecipeFieldApplier가 이 문법을 전용으로 파싱한다.
    /// </summary>
    internal static class ToolFunctionRecipeCreateCommand
    {
        private const string UsageLine =
            "사용법: nodekit function-recipe create [<path>] --tool-spec-digest <digest> --base-tool-image-digest <digest> [--non-interactive --field Name=Value ...]";

        internal static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public static int Run(string[] args, IRecipeConsole console, TextWriter stdout, TextWriter stderr)
        {
            if (args.Any(a => a is "--help" or "-h"))
            {
                stdout.WriteLine(UsageLine);
                return 0;
            }

            string? outPathHint = null;
            var optionsStart = 2;
            if (args.Length >= 3 && !args[2].StartsWith("--", StringComparison.Ordinal))
            {
                outPathHint = args[2];
                optionsStart = 3;
            }

            if (!TryParseOptions(args, optionsStart, stderr, out var toolSpecDigest, out var baseToolImageDigest, out var nonInteractive, out var fieldEntries))
            {
                return 2;
            }

            if (nonInteractive && string.IsNullOrEmpty(outPathHint))
            {
                stderr.WriteLine("--non-interactive 모드에서는 출력 경로가 필요합니다.");
                return 2;
            }

            ToolFunctionRecipe recipe;
            if (nonInteractive)
            {
                recipe = new ToolFunctionRecipe { ToolSpecDigest = toolSpecDigest, BaseToolImageDigest = baseToolImageDigest };
                foreach (var (name, value) in fieldEntries)
                {
                    if (!ToolFunctionRecipeFieldApplier.TryApply(recipe, name, value, out var error))
                    {
                        stderr.WriteLine(error);
                        return 2;
                    }
                }
            }
            else
            {
                recipe = ToolFunctionRecipeCreateFlow.Run(console, toolSpecDigest, baseToolImageDigest);
            }

            recipe.Normalize();

            var savePath = outPathHint ?? PromptForPath(console, recipe);

            if (FindConflictingFile(savePath, recipe, out var conflictPath))
            {
                stderr.WriteLine($"동일한 functionId/revision('{recipe.FunctionId}'/'{recipe.Revision}')을 가진 기존 파일이 있습니다: {conflictPath}");
                return 1;
            }

            File.WriteAllText(savePath, JsonSerializer.Serialize(recipe, JsonOptions));
            stdout.WriteLine(savePath);
            return 0;
        }

        // CliOptionParser는 값 옵션이 여러 번 지정되면 에러로 취급해 --field가
        // 반복될 수 있는 이 커맨드에는 그대로 쓸 수 없다(RecipeCreateCommand의
        // --field 파싱과 동일한 이유로 자체 루프를 쓴다).
        private static bool TryParseOptions(
            string[] args,
            int startIndex,
            TextWriter stderr,
            out string toolSpecDigest,
            out string baseToolImageDigest,
            out bool nonInteractive,
            out List<(string Name, string Value)> fieldEntries)
        {
            toolSpecDigest = string.Empty;
            baseToolImageDigest = string.Empty;
            nonInteractive = false;
            fieldEntries = new List<(string, string)>();

            for (var i = startIndex; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--tool-spec-digest":
                        if (!TryTakeNext(args, ref i, stderr, "--tool-spec-digest", out toolSpecDigest))
                        {
                            return false;
                        }

                        break;
                    case "--base-tool-image-digest":
                        if (!TryTakeNext(args, ref i, stderr, "--base-tool-image-digest", out baseToolImageDigest))
                        {
                            return false;
                        }

                        break;
                    case "--non-interactive":
                        nonInteractive = true;
                        break;
                    case "--field":
                        if (!TryTakeNext(args, ref i, stderr, "--field", out var fieldSpec))
                        {
                            return false;
                        }

                        var separatorIndex = fieldSpec.IndexOf('=', StringComparison.Ordinal);
                        if (separatorIndex < 0)
                        {
                            stderr.WriteLine($"--field 옵션은 --field Name=Value 형식이어야 합니다: '{fieldSpec}'");
                            return false;
                        }

                        fieldEntries.Add((fieldSpec[..separatorIndex], fieldSpec[(separatorIndex + 1)..]));
                        break;
                    default:
                        stderr.WriteLine($"알 수 없는 옵션입니다: {args[i]}");
                        return false;
                }
            }

            if (string.IsNullOrWhiteSpace(toolSpecDigest))
            {
                stderr.WriteLine("--tool-spec-digest <digest> 옵션이 필요합니다.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(baseToolImageDigest))
            {
                stderr.WriteLine("--base-tool-image-digest <digest> 옵션이 필요합니다.");
                return false;
            }

            return true;
        }

        private static bool TryTakeNext(string[] args, ref int i, TextWriter stderr, string optionName, out string value)
        {
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                stderr.WriteLine($"{optionName} 옵션에는 값이 필요합니다.");
                value = string.Empty;
                return false;
            }

            i++;
            value = args[i];
            return true;
        }

        private static string PromptForPath(IRecipeConsole console, ToolFunctionRecipe recipe)
        {
            var defaultName = $"{recipe.FunctionId}-{recipe.Revision}.json";
            console.Write($"저장 경로 (Enter로 기본값 사용: {defaultName}): ");
            var entered = (console.ReadLine() ?? string.Empty).Trim();
            return string.IsNullOrEmpty(entered) ? defaultName : entered;
        }

        // FR-023: 같은 디렉터리 내 기존 *.json 파일 중 같은 functionId+revision을
        // 가진 파일이 있으면 충돌로 취급한다(저장 대상 파일 자신은 제외 — 같은
        // 경로로 재저장하는 경우는 충돌이 아니다).
        private static bool FindConflictingFile(string savePath, ToolFunctionRecipe recipe, out string conflictPath)
        {
            conflictPath = string.Empty;
            var directory = Path.GetDirectoryName(Path.GetFullPath(savePath));
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return false;
            }

            var targetFullPath = Path.GetFullPath(savePath);
            foreach (var candidate in Directory.EnumerateFiles(directory, "*.json"))
            {
                if (string.Equals(Path.GetFullPath(candidate), targetFullPath, StringComparison.Ordinal))
                {
                    continue;
                }

                ToolFunctionRecipe? existing;
                try
                {
                    existing = JsonSerializer.Deserialize<ToolFunctionRecipe>(File.ReadAllText(candidate), JsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }
                catch (IOException)
                {
                    continue;
                }

                if (existing is null)
                {
                    continue;
                }

                if (string.Equals(existing.FunctionId, recipe.FunctionId, StringComparison.Ordinal)
                    && string.Equals(existing.Revision, recipe.Revision, StringComparison.Ordinal))
                {
                    conflictPath = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}
