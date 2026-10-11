using System.Threading;
using System.Threading.Tasks;
using Nodevault.V1;

namespace NodeKit.Grpc
{
    /// <summary>
    /// NodeVault ToolRegistryService의 typed ToolFunction 등록 client (P08.registry, v0.9.1 S3-07).
    /// 요청은 P06 typed mapper가 만든 그대로 보내고, 응답의 F/H/P/version을 해석하거나 다시 계산하지 않는다.
    /// gRPC 오류(RpcException)는 그대로 호출자에게 올린다 — 확정 거부/결과 불명 분류는 호출자 몫이다.
    /// </summary>
    internal interface IToolFunctionRegistryClient
    {
        Task<RegisterToolFunctionResponse> RegisterToolFunctionAsync(RegisterToolFunctionRequest request, CancellationToken ct = default);
    }
}
