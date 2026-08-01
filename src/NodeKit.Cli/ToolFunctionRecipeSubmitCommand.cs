using System.IO;
using System.Linq;

namespace NodeKit.Cli
{
    /// <summary>
    /// nodekit function-recipe submit &lt;path&gt; — contracts/cli-function-recipe-commands.md
    /// §submit (2026-07-24 정정). 파일 내용/State와 무관하게 항상 NodeVault
    /// ToolFunction 빌드 게이트 미개방 안내로 차단한다(FR-021, SC-005).
    /// 네트워크 호출을 하지 않으며 State를 바꾸지 않는다(FR-020).
    /// </summary>
    internal static class ToolFunctionRecipeSubmitCommand
    {
        private const string UsageLine = "사용법: nodekit function-recipe submit <path>";

        private const string GateNotOpenMessage =
            "NodeVault ToolFunction 빌드 게이트가 아직 열려 있지 않습니다. 이 기능은 issue #19가 해결된 이후 지원됩니다.";

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

            // Ready 여부는 검사하지 않는다 — 게이트가 닫혀 있다는 사실은
            // Recipe의 완결성과 무관하게 항상 참이다(contracts §submit).
            // 다만 파일 자체는 읽어서 최소한의 I/O 오류 처리 관례를 따른다.
            if (!ToolFunctionRecipeCliIo.TryLoad(args[2], stderr, out _))
            {
                return 2;
            }

            stdout.WriteLine(GateNotOpenMessage);
            return 1;
        }
    }
}
