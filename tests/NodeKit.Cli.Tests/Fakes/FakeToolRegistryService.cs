using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Nodevault.V1;

namespace NodeKit.Cli.Tests.Fakes
{
    /// <summary>
    /// NodeVault ToolRegistryService의 in-process 대역(P08.registry). 실제 generated gRPC로 역직렬화된
    /// RegisterToolFunction 요청과 그 wire bytes를 그대로 남기고, 테스트가 지정한 고정 응답/오류를 돌려준다.
    /// 서버 쪽 canonical hash 계산·durable 멱등성은 흉내 내지 않는다 — 실제 NodeVault 등록 증거가 아니다.
    /// </summary>
    internal sealed class FakeToolRegistryService : ToolRegistryService.ToolRegistryServiceBase
    {
        public Func<RegisterToolFunctionRequest, RegisterToolFunctionResponse> OnRegisterToolFunction { get; set; } =
            _ => new RegisterToolFunctionResponse();

        /// <summary>요청을 받은 직후, 응답 전에 호출된다(예: 그 시점의 로컬 receipt 상태 확인).</summary>
        public Action<RegisterToolFunctionRequest>? OnReceived { get; set; }

        /// <summary>
        /// 있으면 <see cref="OnRegisterToolFunction"/> 대신 쓴다 — 응답 지연·hang(P08.timeout)을 server call의
        /// 취소 토큰과 함께 흉내 낸다.
        /// </summary>
        public Func<RegisterToolFunctionRequest, ServerCallContext, Task<RegisterToolFunctionResponse>>? OnRegisterToolFunctionAsync { get; set; }

        public List<RegisterToolFunctionRequest> RegisterRequests { get; } = new();

        public List<byte[]> RegisterRequestBytes { get; } = new();

        public override Task<RegisterToolFunctionResponse> RegisterToolFunction(RegisterToolFunctionRequest request, ServerCallContext context)
        {
            RegisterRequests.Add(request);
            RegisterRequestBytes.Add(request.ToByteArray());
            OnReceived?.Invoke(request);
            return OnRegisterToolFunctionAsync is { } async
                ? async(request, context)
                : Task.FromResult(OnRegisterToolFunction(request));
        }
    }
}
