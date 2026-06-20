using System;
using Grpc.Core;

namespace NodeKit.Grpc
{
    /// <summary>
    /// BuildAndRegisterAsync 실패 예외를 사용자에게 actionable한 한국어 메시지로 변환한다.
    /// 순수 함수 — UI/네트워크에 의존하지 않아 단위 테스트로 직접 검증 가능하다.
    /// </summary>
    internal static class BuildErrorMessages
    {
        internal static string Describe(Exception ex) => ex switch
        {
            UriFormatException => "NodeVault 주소 형식이 올바르지 않습니다. http://<host>:<port> 형식으로 입력하세요 (예: http://100.123.80.48:50051).",
            RpcException rpc => DescribeRpc(rpc),
            _ => $"gRPC 오류: {ex.Message}",
        };

        private static string DescribeRpc(RpcException rpc) => rpc.StatusCode switch
        {
            StatusCode.Unavailable => "NodeVault에 연결할 수 없습니다. 주소와 네트워크 상태를 확인하세요.",
            StatusCode.Unauthenticated => "인증 오류: NodeVault 접근 자격을 확인하세요.",
            StatusCode.PermissionDenied => "권한 오류: NodeVault 접근 권한을 확인하세요.",
            StatusCode.DeadlineExceeded => "요청 시간이 초과되었습니다.",
            StatusCode.Cancelled => "요청이 취소되었습니다.",
            _ => $"gRPC 오류 ({rpc.StatusCode}): {rpc.Status.Detail}",
        };
    }
}
