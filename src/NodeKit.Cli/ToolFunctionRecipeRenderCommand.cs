using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NodeKit.Authoring.ToolFunctionRecipes;
using NodeKit.Grpc;

namespace NodeKit.Cli
{
    /// <summary>
    /// nodekit function-recipe render &lt;path&gt; --out &lt;out.json&gt; [--pretty] —
    /// contracts/cli-function-recipe-commands.md §render. Ready 상태가 아니면
    /// 거부하고(FR-019), 네트워크 호출은 하지 않으며 State를 바꾸지 않는다
    /// (FR-020).
    /// </summary>
    internal static class ToolFunctionRecipeRenderCommand
    {
        private const string UsageLine =
            "사용법: nodekit function-recipe render <path> --out <out.json> [--pretty]";

        public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
        {
            if (args.Any(a => a is "--help" or "-h"))
            {
                stdout.WriteLine(UsageLine);
                return 0;
            }

            if (args.Length < 3)
            {
                stderr.WriteLine(UsageLine);
                return 2;
            }

            if (!CliOptionParser.TryParse(
                args,
                startIndex: 3,
                stderr,
                valueOptions: new[] { "--out" },
                flagOptions: new[] { "--pretty" },
                out var values,
                out var flags))
            {
                return 2;
            }

            if (!values.TryGetValue("--out", out var outPath))
            {
                stderr.WriteLine("--out <out.json> 옵션이 필요합니다.");
                return 2;
            }

            if (!ToolFunctionRecipeCliIo.TryLoad(args[2], stderr, out var recipe))
            {
                return 2;
            }

            if (recipe!.State != ToolFunctionRecipeState.Ready)
            {
                stderr.WriteLine("먼저 검증을 통과해 Ready 상태가 되어야 합니다 (nodekit function-recipe validate 실행).");
                return 1;
            }

            var json = ToolFunctionBuildRequestPreviewFactory.Build(recipe);
            if (flags.Contains("--pretty"))
            {
                using var document = JsonDocument.Parse(json);
                json = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
            }

            File.WriteAllText(outPath, json);
            return 0;
        }
    }
}
