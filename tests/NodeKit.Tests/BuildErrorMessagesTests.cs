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

        [Fact]
        public void Describe_OtherException_FallsBackToMessage()
        {
            var message = BuildErrorMessages.Describe(new InvalidOperationException("unexpected"));

            Assert.Contains("unexpected", message, StringComparison.Ordinal);
        }
    }
}
