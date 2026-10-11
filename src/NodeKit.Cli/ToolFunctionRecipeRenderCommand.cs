using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NodeKit.Grpc;

namespace NodeKit.Cli
{
    /// <summary>
    /// nodekit function-recipe render &lt;path&gt; --out &lt;out.json&gt; [--pretty] —
    /// contracts/cli-function-recipe-commands.md §render. Ready 상태가 아니거나
    /// 현재 내용이 current preflight(S3-09)를 통과하지 못하면 거부하고(FR-019),
    /// 네트워크 호출은 하지 않으며 State를 바꾸지 않는다(FR-020).
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

            // 파일의 State=Ready 표지만 믿지 않는다 — 출력 파일을 쓰기 전에 현재
            // 내용·base image A·script source를 다시 검증한다(S3-09, S4-03-C/F).
            // render는 typed 등록을 하지 않으므로 C1 형식 guard는 적용하지 않는다.
            if (!ToolFunctionSubmissionPreflight.TryRun(
                recipe!, args[2], requireTypedRegistration: false, stderr, out _, out var exitCode))
            {
                return exitCode;
            }

            var json = ToolFunctionBuildRequestPreviewFactory.Build(recipe!);
            if (flags.Contains("--pretty"))
            {
                using var document = JsonDocument.Parse(json);
                json = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
            }

            return AtomicFileWriter.WriteForCli(outPath, json, stderr);
        }
    }
}
