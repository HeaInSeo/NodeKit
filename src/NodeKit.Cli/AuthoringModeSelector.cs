using System.IO;

namespace NodeKit.Cli
{
    /// <summary>
    /// Entry-screen mode selector shown at the start of `nodekit recipe create`.
    /// See docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_V0.9.2.md Section 7.
    /// </summary>
    internal static class AuthoringModeSelector
    {
        internal enum Mode { GuidedBeginner, QuickSetup }

        /// <summary>
        /// Prints the Section 7 entry screen and reads the user's mode choice.
        /// Returns null when the user chose [3] CI-mode usage (caller exits 0).
        /// </summary>
        public static Mode? Prompt(TextReader stdin, TextWriter stdout)
        {
            while (true)
            {
                stdout.WriteLine();
                stdout.WriteLine("NodeKit recipe create");
                stdout.WriteLine();
                stdout.WriteLine("이 명령은 실행 도구를 컨테이너 recipe로 만드는 마법사입니다.");
                stdout.WriteLine("처음이라도 괜찮습니다. 모르는 항목은 \"잘 모르겠다\"를 선택할 수 있습니다.");
                stdout.WriteLine();
                stdout.WriteLine("언제든 사용할 수 있는 명령:");
                stdout.WriteLine("  /help           지금 질문 도움말 보기");
                stdout.WriteLine("  /review         지금까지 입력한 내용 보기");
                stdout.WriteLine("  /back           이전 주요 화면으로 돌아가기");
                stdout.WriteLine("  /change-method  작성 방식 다시 선택하기");
                stdout.WriteLine("  /cancel         저장하지 않고 종료하기");
                stdout.WriteLine("  /quit           /cancel과 동일");
                stdout.WriteLine("  /exit           /cancel과 동일");
                stdout.WriteLine();
                stdout.WriteLine("진행 방식을 선택하세요.");
                stdout.WriteLine();
                stdout.WriteLine("[1] 쉬운 안내 모드");
                stdout.WriteLine("    도구 이름만 알아도 시작할 수 있습니다.");
                stdout.WriteLine("    설치 명령, 이미지 주소, GitHub 주소 등을 예시와 함께 하나씩 확인합니다.");
                stdout.WriteLine("    처음 사용하는 사람에게 추천합니다.");
                stdout.WriteLine();
                stdout.WriteLine("[2] 빠른 설정 모드");
                stdout.WriteLine("    내부망, mirror, public channel, source checksum, Dockerfile 여부를 알고 있는 경우 사용합니다.");
                stdout.WriteLine("    기존 Q&A 방식과 비슷하지만 각 선택의 영향과 예시를 함께 보여줍니다.");
                stdout.WriteLine();
                stdout.WriteLine("[3] 스크립트/CI 모드 사용법 보기");
                stdout.WriteLine("    프롬프트 없이 한 줄 명령으로 recipe를 만들 때 사용합니다.");
                stdout.WriteLine();
                stdout.WriteLine("선택:");

                var line = (stdin.ReadLine() ?? string.Empty).Trim();
                RecipeCreateEscapeCommands.ThrowIfCancel(line);
                switch (line)
                {
                    case "1":
                        return Mode.GuidedBeginner;
                    case "2":
                        return Mode.QuickSetup;
                    case "3":
                        PrintCiUsage(stdout);
                        return null;
                    case "/back":
                        stdout.WriteLine("이전 화면이 없습니다. 진행 방식을 선택하거나 /cancel로 종료하세요.");
                        break;
                    default:
                        stdout.WriteLine("알 수 없는 선택입니다. 다시 선택하세요.");
                        break;
                }
            }
        }

        private static void PrintCiUsage(TextWriter stdout)
        {
            stdout.WriteLine();
            stdout.WriteLine("스크립트/CI 모드 — 프롬프트 없이 recipe를 생성합니다.");
            stdout.WriteLine();
            stdout.WriteLine("기본 형식:");
            stdout.WriteLine("  nodekit recipe create <출력 경로> --non-interactive --method <method> [옵션]");
            stdout.WriteLine();
            stdout.WriteLine("method 목록:");
            stdout.WriteLine("  container    기존 컨테이너 이미지 사용");
            stdout.WriteLine("  package      conda/micromamba 패키지 설치");
            stdout.WriteLine("  mirror       내부 package mirror 설치");
            stdout.WriteLine("  source       소스코드에서 직접 빌드");
            stdout.WriteLine("  dockerfile   기존 Dockerfile 사용 (--accept-dockerfile-warning 필요)");
            stdout.WriteLine();
            stdout.WriteLine("옵션:");
            stdout.WriteLine("  --field ToolName=<이름>     필드 값 지정 (여러 번 사용 가능)");
            stdout.WriteLine("  --field ToolVersion=<버전>");
            stdout.WriteLine("  --field Script=<명령 또는 스크립트 경로>");
            stdout.WriteLine("  --field ImageRef=<이미지>");
            stdout.WriteLine("  --input <이름>=<preset>     입력 정의 (preset: fastq-paired, bam, ...)");
            stdout.WriteLine("  --output <이름>=<preset>    출력 정의");
            stdout.WriteLine("  --engine conda|micromamba    패키지 엔진 (--method package 전용)");
            stdout.WriteLine("  --accept-dockerfile-warning  Dockerfile 경고 확인 (--method dockerfile 전용)");
            stdout.WriteLine();
            stdout.WriteLine("예시 (package):");
            stdout.WriteLine("  nodekit recipe create recipe.json \\");
            stdout.WriteLine("    --non-interactive --method package \\");
            stdout.WriteLine("    --field ToolName=bwa-mem \\");
            stdout.WriteLine("    --field ToolVersion=0.7.17 \\");
            stdout.WriteLine("    --field \"Script=bwa mem\" \\");
            stdout.WriteLine("    --field \"ImageRef=condaforge/miniforge3:24.3.0-0@sha256:<digest>\" \\");
            stdout.WriteLine("    --field Packages=bwa=0.7.17=h5bf99c6_8 \\");
            stdout.WriteLine("    --field Channels=bioconda \\");
            stdout.WriteLine("    --input reads=fastq-paired \\");
            stdout.WriteLine("    --output bam=bam-primary");
        }
    }
}
