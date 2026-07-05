namespace NodeKit.Cli
{
    internal static class RecipeConsoleExtensions
    {
        // IRecipeConsole.ReadLine()이 null이면 stdin이 EOF에 도달한 것이다.
        // 이걸 ""(빈 줄 입력)과 구분하지 않고 그냥 넘기면, 유효한 선택을 영원히
        // 못 받는 while(true) 루프가 무한 반복된다 — 즉시 취소 처리해서
        // /cancel과 동일하게 깨끗이 종료시킨다.
        public static string ReadLineOrCancel(this IRecipeConsole console) =>
            console.ReadLine() ?? throw new RecipeCreateCancelledException();
    }
}
