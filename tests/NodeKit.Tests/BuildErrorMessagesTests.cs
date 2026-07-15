using System;
using Grpc.Core;
using NodeKit.Grpc;
using Xunit;

namespace NodeKit.Tests
{
    public class BuildErrorMessagesTests
    {
        [Fact]
        public void Describe_UriFormatException_ExplainsAddressFormat()
        {
            var message = BuildErrorMessages.Describe(new UriFormatException());

            Assert.Contains("주소 형식", message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(StatusCode.Unavailable, "연결할 수 없습니다")]
        [InlineData(StatusCode.Unauthenticated, "인증 오류")]
        [InlineData(StatusCode.PermissionDenied, "권한 오류")]
        [InlineData(StatusCode.DeadlineExceeded, "시간이 초과")]
        [InlineData(StatusCode.Cancelled, "취소되었습니다")]
        public void Describe_RpcException_MapsKnownStatusCodesToActionableMessages(StatusCode code, string expectedSubstring)
        {
            var message = BuildErrorMessages.Describe(new RpcException(new Status(code, "detail")));

            Assert.Contains(expectedSubstring, message, StringComparison.Ordinal);
        }

        [Fact]
        public void Describe_RpcException_UnknownStatusCode_IncludesStatusCodeAndDetail()
        {
            var message = BuildErrorMessages.Describe(new RpcException(new Status(StatusCode.Internal, "boom")));

            Assert.Contains("Internal", message, StringComparison.Ordinal);
            Assert.Contains("boom", message, StringComparison.Ordinal);
        }

        // 적대적 리뷰 follow-up: 구버전 NodeVault(ToolSpec RPC가 아직 없는
        // 서버)에 연결하면 gRPC가 "unknown method ResolveToolSpec for
        // service nodevault.v1.BuildService" 형태의 UNIMPLEMENTED를
        // 그대로 돌려준다 — 사용자에게 원문 그대로 노출하지 않고 구버전
        // NodeVault라는 걸 명확히 안내해야 한다.
        [Theory]
        [InlineData("unknown method ResolveToolSpec for service nodevault.v1.BuildService")]
        [InlineData("unknown method SubmitToolBuild for service nodevault.v1.BuildService")]
        [InlineData("unknown method WatchToolBuild for service nodevault.v1.BuildService")]
        [InlineData("unknown method CancelToolBuild for service nodevault.v1.BuildService")]
        public void Describe_RpcException_UnimplementedToolSpecMethod_ExplainsOutdatedNodeVault(string detail)
        {
            var message = BuildErrorMessages.Describe(new RpcException(new Status(StatusCode.Unimplemented, detail)));

            Assert.Contains("구버전", message, StringComparison.Ordinal);
            Assert.Contains("ToolSpec", message, StringComparison.Ordinal);
        }

        [Fact]
        public void Describe_RpcException_UnimplementedOtherMethod_FallsBackToGenericMessage()
        {
            var message = BuildErrorMessages.Describe(
                new RpcException(new Status(StatusCode.Unimplemented, "unknown method SomeOtherRpc for service x")));

            Assert.DoesNotContain("구버전", message, StringComparison.Ordinal);
            Assert.Contains("SomeOtherRpc", message, StringComparison.Ordinal);
        }

        [Fact]
        public void Describe_OtherException_FallsBackToMessage()
        {
            var message = BuildErrorMessages.Describe(new InvalidOperationException("unexpected"));

            Assert.Contains("unexpected", message, StringComparison.Ordinal);
        }
    }
}
