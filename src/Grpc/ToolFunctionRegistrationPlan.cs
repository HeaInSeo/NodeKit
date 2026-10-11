using Nodevault.V1;

namespace NodeKit.Grpc
{
    /// <summary>
    /// typed mapper 결과. Request는 보낼 typed 요청 그대로이고, SecondRequestBasis(C2)는
    /// 요청 밖에서 따로 보존한다.
    /// </summary>
    internal sealed record ToolFunctionRegistrationPlan(
        RegisterToolFunctionRequest Request,
        string? SecondRequestBasis);
}
