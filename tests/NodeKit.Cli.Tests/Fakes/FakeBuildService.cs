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

        public Func<SubmitToolBuildRequest, SubmitToolBuildResponse> OnSubmitToolBuild { get; set; } =
            _ => new SubmitToolBuildResponse { BuildId = "fake-build-id", Status = "Requested" };

        public List<BuildEvent> WatchEvents { get; set; } = new();

        /// <summary>true면 WatchEvents를 다 보낸 뒤 스트림 취소 전까지 계속 대기한다
        /// (취소 시나리오 재현용).</summary>
        public bool HangAfterEvents { get; set; }

        public Func<ResolveRecipeRequest, ResolveRecipeResponse> OnResolveRecipe { get; set; } =
            _ => new ResolveRecipeResponse();

        public List<string> CancelledBuildIds { get; } = new();

        public override Task<ResolvedToolSpecResponse> ResolveToolSpec(
            ToolSpecRequest request, ServerCallContext context) =>
            Task.FromResult(OnResolveToolSpec(request));

        public override Task<SubmitToolBuildResponse> SubmitToolBuild(
            SubmitToolBuildRequest request, ServerCallContext context) =>
            Task.FromResult(OnSubmitToolBuild(request));

        public override async Task WatchToolBuild(
            WatchToolBuildRequest request,
            IServerStreamWriter<BuildEvent> responseStream,
            ServerCallContext context)
        {
            foreach (var ev in WatchEvents)
            {
                await responseStream.WriteAsync(ev);
            }

            if (HangAfterEvents)
            {
                await Task.Delay(System.Threading.Timeout.Infinite, context.CancellationToken);
            }
        }

        public override Task<CancelToolBuildResponse> CancelToolBuild(
            CancelToolBuildRequest request, ServerCallContext context)
        {
            CancelledBuildIds.Add(request.BuildId);
            return Task.FromResult(new CancelToolBuildResponse
            {
                BuildId = request.BuildId,
                Status = "Interrupted",
            });
        }

        public override Task<ResolveRecipeResponse> ResolveRecipe(
            ResolveRecipeRequest request, ServerCallContext context) =>
            Task.FromResult(OnResolveRecipe(request));
    }
}
