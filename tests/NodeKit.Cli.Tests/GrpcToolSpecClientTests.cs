using Nodevault.V1;
using Xunit;
using GrpcBuildEventKind = NodeKit.Grpc.BuildEventKind;
using GrpcToolSpecClient = NodeKit.Grpc.GrpcToolSpecClient;

namespace NodeKit.Cli.Tests
{
    public class GrpcToolSpecClientTests
    {
        // WatchToolBuild은 모든 이벤트를 BUILD_EVENT_KIND_LOG로 보내고, 실제 종료
        // 상태는 status 필드(buildstate.Status 그대로 "Succeeded"/"Failed"/
        // "Interrupted" — PascalCase)로만 구분된다. 예전엔 이 switch가 소문자와
        // 비교해서 항상 매칭에 실패했다 (issue #5).
        [Fact]
        public void MapWatchEvent_SucceededStatus_MapsToSucceeded()
        {
            var result = GrpcToolSpecClient.MapWatchEvent(
                new BuildEvent { Kind = BuildEventKind.Log, Status = "Succeeded" });

            Assert.Equal(GrpcBuildEventKind.Succeeded, result.Kind);
        }

        [Fact]
        public void MapWatchEvent_FailedStatus_MapsToFailed()
        {
            var result = GrpcToolSpecClient.MapWatchEvent(
                new BuildEvent { Kind = BuildEventKind.Log, Status = "Failed" });

            Assert.Equal(GrpcBuildEventKind.Failed, result.Kind);
        }

        [Fact]
        public void MapWatchEvent_InterruptedStatus_MapsToFailed()
        {
            var result = GrpcToolSpecClient.MapWatchEvent(
                new BuildEvent { Kind = BuildEventKind.Log, Status = "Interrupted" });

            Assert.Equal(GrpcBuildEventKind.Failed, result.Kind);
        }

        [Fact]
        public void MapWatchEvent_NonTerminalStatus_FallsBackToProtoKind()
        {
            var protoEvent = new BuildEvent
            {
                Kind = BuildEventKind.JobRunning,
                Status = "Building",
            };

            var result = GrpcToolSpecClient.MapWatchEvent(protoEvent);

            Assert.Equal(GrpcBuildEventKind.JobRunning, result.Kind);
        }
    }
}
