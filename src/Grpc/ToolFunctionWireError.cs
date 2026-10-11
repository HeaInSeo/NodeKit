namespace NodeKit.Grpc
{
    /// <summary>
    /// raw_spec/typed 등록 입력 검사 실패 하나. ExitCode 1은 입력 값의 local
    /// validation 실패다. 실패하면 payload/요청을 만들지 않는다.
    /// </summary>
    internal sealed record ToolFunctionWireError(string Code, string Message, int ExitCode);
}
