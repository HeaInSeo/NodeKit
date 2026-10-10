namespace NodeKit.Cli
{
    internal interface IRecipeConsole
    {
        void BeginStep();
        void WriteLine(string text = "");

        /// <summary>
        /// 지금 한 줄을 출력하고, 다음 BeginStep이 실제로 터미널을 지우면 새 화면 맨 위에
        /// 한 번 더 출력한다. 마지막 입력 뒤에 보여주는 요약이 다음 화면의 Clear로
        /// 읽기도 전에 사라지지 않게 한다. 화면을 지우지 않는 출력(리다이렉트 등)에는
        /// 한 번만 남는다.
        /// </summary>
        void WriteCarriedLine(string text = "");

        void Write(string text);
        void WriteHints(string hintsLine);
        string? ReadLine();
    }
}
