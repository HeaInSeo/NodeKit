using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grpc.Core;
using Nodevault.V1;

namespace NodeKit.Cli.Tests.Fakes
{
    /// <summary>
    /// NodeVault BuildService의 in-process 대역. 실제 gRPC 직렬화/전송 경로를
    /// 그대로 타면서도 seoy/NodeVault 없이 동작한다 — 각 RPC의 응답을 테스트가
    /// 스크립트로 지정한다.
    /// </summary>
    internal sealed class FakeBuildService : BuildService.BuildServiceBase
    {
        public Func<ToolSpecRequest, ResolvedToolSpecResponse> OnResolveToolSpec { get; set; } =
            _ => new ResolvedToolSpecResponse { ToolSpecDigest = "fake-digest" };

        /// <summary>true면 ResolveToolSpec이 응답하지 않고 클라이언트가 취소할 때까지
        /// 대기한다 (연결 타임아웃/취소 전파 시나리오 재현용).</summary>
        public bool HangOnResolveToolSpec { get; set; }

        public Func<SubmitToolBuildRequest, SubmitToolBuildResponse> OnSubmitToolBuild { get; set; } =
            _ => new SubmitToolBuildResponse { BuildId = "fake-build-id", Status = "Requested" };

        /// <summary>true면 SubmitToolBuild 요청을 기록한 뒤 응답하지 않고 클라이언트가 취소할 때까지
        /// 대기한다 (요청은 서버에 도달했지만 응답이 늦거나 유실된 경우 재현용).</summary>
        public bool HangOnSubmitToolBuild { get; set; }

        public List<BuildEvent> WatchEvents { get; set; } = new();

        /// <summary>WatchEvents 각 이벤트를 보내기 전 기다리는 시간 (취소 token 준수).</summary>
        public TimeSpan WatchEventDelay { get; set; }

        /// <summary>null이 아니면 WatchEvents를 다 보낸 뒤 이 예외로 스트림을 끝낸다.</summary>
        public RpcException? WatchFailure { get; set; }

        /// <summary>true면 CancelToolBuild가 요청을 기록한 뒤 클라이언트가 취소할 때까지 응답하지 않는다
        /// (token-cooperative hanging cancel).</summary>
        public bool HangOnCancelToolBuild { get; set; }

        /// <summary>true면 WatchEvents를 다 보낸 뒤 스트림 취소 전까지 계속 대기한다
        /// (취소 시나리오 재현용).</summary>
        public bool HangAfterEvents { get; set; }

        public Func<ResolveRecipeRequest, ResolveRecipeResponse> OnResolveRecipe { get; set; } =
            _ => new ResolveRecipeResponse();

        public List<string> CancelledBuildIds { get; } = new();

        /// <summary>null이 아니면 CancelToolBuild가 요청을 기록한 뒤 이 예외로 실패한다.</summary>
        public RpcException? CancelToolBuildFailure { get; set; }

        // S2-01 wire capture: 실제 generated gRPC로 역직렬화된 요청과 RPC 순서를
        // 그대로 남긴다. fake는 raw_spec parse/DisallowUnknownFields/full pin/
        // server dedup/registry 존재 여부를 검증하지 않는다 — 전송 계약만 본다.
        public List<ToolSpecRequest> ResolveRequests { get; } = new();

        public List<SubmitToolBuildRequest> SubmitRequests { get; } = new();

        public List<WatchToolBuildRequest> WatchRequests { get; } = new();

        public List<string> CallOrder { get; } = new();

        public override async Task<ResolvedToolSpecResponse> ResolveToolSpec(
            ToolSpecRequest request, ServerCallContext context)
        {
            lock (CallOrder)
            {
                ResolveRequests.Add(request);
                CallOrder.Add("Resolve");
            }

            if (HangOnResolveToolSpec)
            {
                await Task.Delay(System.Threading.Timeout.Infinite, context.CancellationToken);
            }

            return OnResolveToolSpec(request);
        }

        public override async Task<SubmitToolBuildResponse> SubmitToolBuild(
            SubmitToolBuildRequest request, ServerCallContext context)
        {
            lock (CallOrder)
            {
                SubmitRequests.Add(request);
                CallOrder.Add("Submit");
            }

            if (HangOnSubmitToolBuild)
            {
                await Task.Delay(System.Threading.Timeout.Infinite, context.CancellationToken);
            }

            return OnSubmitToolBuild(request);
        }

        public override async Task WatchToolBuild(
            WatchToolBuildRequest request,
            IServerStreamWriter<BuildEvent> responseStream,
            ServerCallContext context)
        {
            lock (CallOrder)
            {
                WatchRequests.Add(request);
                CallOrder.Add("Watch");
            }

            foreach (var ev in WatchEvents)
            {
                if (WatchEventDelay > TimeSpan.Zero)
                {
                    await Task.Delay(WatchEventDelay, context.CancellationToken);
                }

                await responseStream.WriteAsync(ev);
            }

            if (WatchFailure is { } watchFailure)
            {
                throw watchFailure;
            }

            if (HangAfterEvents)
            {
                await Task.Delay(System.Threading.Timeout.Infinite, context.CancellationToken);
            }
        }

        public override async Task<CancelToolBuildResponse> CancelToolBuild(
            CancelToolBuildRequest request, ServerCallContext context)
        {
            lock (CancelledBuildIds)
            {
                CancelledBuildIds.Add(request.BuildId);
            }

            if (CancelToolBuildFailure is { } failure)
            {
                throw failure;
            }

            if (HangOnCancelToolBuild)
            {
                await Task.Delay(System.Threading.Timeout.Infinite, context.CancellationToken);
            }

            return new CancelToolBuildResponse
            {
                BuildId = request.BuildId,
                Status = "Interrupted",
            };
        }

        public override Task<ResolveRecipeResponse> ResolveRecipe(
            ResolveRecipeRequest request, ServerCallContext context) =>
            Task.FromResult(OnResolveRecipe(request));
    }
}
