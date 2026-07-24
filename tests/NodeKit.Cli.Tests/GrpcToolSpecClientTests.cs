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

        // 리뷰 지적: ev.Timestamp는 서버가 보내는 자유 형식 int64라 형식 계약이
        // 없다 — DateTimeOffset이 표현 가능한 범위를 벗어나면
        // DateTimeOffset.FromUnixTimeMilliseconds가 ArgumentOutOfRangeException을
        // 던져서, 이 이벤트 하나 때문에 WatchToolBuild 스트림 전체가 중단됐다.
        [Theory]
        [InlineData(long.MaxValue)]
        [InlineData(long.MinValue)]
        public void MapWatchEvent_TimestampOutOfDateTimeOffsetRange_DoesNotThrow(long timestamp)
        {
            var result = GrpcToolSpecClient.MapWatchEvent(
                new BuildEvent { Kind = BuildEventKind.JobRunning, Timestamp = timestamp });

            Assert.Equal(GrpcBuildEventKind.JobRunning, result.Kind);
        }

        [Fact]
        public void MapWatchEvent_ValidTimestamp_ConvertsCorrectly()
        {
            // 2026-01-01T00:00:00Z in Unix milliseconds.
            const long validTimestamp = 1767225600000;

            var result = GrpcToolSpecClient.MapWatchEvent(
                new BuildEvent { Kind = BuildEventKind.JobRunning, Timestamp = validTimestamp });

            Assert.Equal(2026, result.Timestamp.Year);
        }
    }
}
