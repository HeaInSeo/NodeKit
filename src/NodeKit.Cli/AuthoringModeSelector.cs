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
        public static Mode? Prompt(IRecipeConsole console)
        {
            while (true)
            {
                console.WriteLine();
                console.WriteLine("NodeKit recipe create");
                console.WriteLine();
                console.WriteLine("이 명령은 실행 도구를 컨테이너 recipe로 만드는 마법사입니다.");
                console.WriteLine("처음이라도 괜찮습니다. 모르는 항목은 \"잘 모르겠다\"를 선택할 수 있습니다.");
                console.WriteLine();
                console.WriteHints("언제든 사용할 수 있는 명령:");
                console.WriteHints("  /help           지금 질문 도움말 보기");
                console.WriteHints("  /review         지금까지 입력한 내용 보기");
                console.WriteHints("  /back           이전 주요 화면으로 돌아가기");
                console.WriteHints("  /change-method  작성 방식 다시 선택하기");
                console.WriteHints("  /cancel         저장하지 않고 종료하기");
                console.WriteHints("  /quit           /cancel과 동일");
                console.WriteHints("  /exit           /cancel과 동일");
                console.WriteLine();
                console.WriteLine("진행 방식을 선택하세요.");
                console.WriteLine();
                console.WriteLine("[1] 쉬운 안내 모드");
                console.WriteLine("    도구 이름만 알아도 시작할 수 있습니다.");
                console.WriteLine("    설치 명령, 이미지 주소, GitHub 주소 등을 예시와 함께 하나씩 확인합니다.");
                console.WriteLine("    처음 사용하는 사람에게 추천합니다.");
                console.WriteLine();
                console.WriteLine("[2] 빠른 설정 모드");
                console.WriteLine("    내부망, mirror, public channel, source checksum, Dockerfile 여부를 알고 있는 경우 사용합니다.");
                console.WriteLine("    기존 Q&A 방식과 비슷하지만 각 선택의 영향과 예시를 함께 보여줍니다.");
                console.WriteLine();
                console.WriteLine("[3] 스크립트/CI 모드 사용법 보기");
                console.WriteLine("    프롬프트 없이 한 줄 명령으로 recipe를 만들 때 사용합니다.");
                console.WriteLine();
                console.WriteLine("선택:");

                var line = (console.ReadLine() ?? string.Empty).Trim();
                RecipeCreateEscapeCommands.ThrowIfCancel(line);
                switch (line)
                {
                    case "1":
                        return Mode.GuidedBeginner;
                    case "2":
                        return Mode.QuickSetup;
                    case "3":
                        PrintCiUsage(console);
                        return null;
                    case "/back":
                        console.WriteLine("이전 화면이 없습니다. 진행 방식을 선택하거나 /cancel로 종료하세요.");
                        break;
                    default:
                        console.WriteLine("알 수 없는 선택입니다. 다시 선택하세요.");
                        break;
                }
            }
        }

        private static void PrintCiUsage(IRecipeConsole console)
        {
            console.WriteLine();
            console.WriteLine("스크립트/CI 모드 — 프롬프트 없이 recipe를 생성합니다.");
            console.WriteLine();
            console.WriteLine("기본 형식:");
            console.WriteLine("  nodekit recipe create <출력 경로> --non-interactive --method <method> [옵션]");
            console.WriteLine();
            console.WriteLine("method 목록:");
            console.WriteLine("  container    기존 컨테이너 이미지 사용");
            console.WriteLine("  package      conda/micromamba 패키지 설치");
            console.WriteLine("  mirror       내부 package mirror 설치");
            console.WriteLine("  source       소스코드에서 직접 빌드");
            console.WriteLine("  dockerfile   기존 Dockerfile 사용 (--accept-dockerfile-warning 필요)");
            console.WriteLine();
            console.WriteLine("옵션:");
            console.WriteLine("  --field ToolName=<이름>     필드 값 지정 (여러 번 사용 가능)");
            console.WriteLine("  --field ToolVersion=<버전>");
            console.WriteLine("  --field Script=<명령 또는 스크립트 경로>");
            console.WriteLine("  --field ImageRef=<이미지>");
            console.WriteLine("  --input <이름>=<preset>     입력 정의 (preset: fastq-paired, bam, ...)");
            console.WriteLine("  --output <이름>=<preset>    출력 정의");
            console.WriteLine("  --engine conda|micromamba    패키지 엔진 (--method package 전용)");
            console.WriteLine("  --accept-dockerfile-warning  Dockerfile 경고 확인 (--method dockerfile 전용)");
            console.WriteLine();
            console.WriteLine("예시 (package):");
            console.WriteLine("  nodekit recipe create recipe.json \\");
            console.WriteLine("    --non-interactive --method package \\");
            console.WriteLine("    --field ToolName=bwa-mem \\");
            console.WriteLine("    --field ToolVersion=0.7.17 \\");
            console.WriteLine("    --field \"Script=bwa mem\" \\");
            console.WriteLine("    --field \"ImageRef=condaforge/miniforge3:24.3.0-0@sha256:<digest>\" \\");
            console.WriteLine("    --field Packages=bwa=0.7.17=h5bf99c6_8 \\");
            console.WriteLine("    --field Channels=bioconda \\");
            console.WriteLine("    --input reads=fastq-paired \\");
            console.WriteLine("    --output bam=bam-primary");
        }
    }
}
