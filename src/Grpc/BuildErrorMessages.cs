using System;
using Grpc.Core;

namespace NodeKit.Grpc
{
    /// <summary>
    /// GrpcToolSpecClient(CLI/GUI 공유 submit 경로) 실패 예외를 사용자에게
    /// actionable한 한국어 메시지로 변환한다. 순수 함수 — UI/네트워크에
    /// 의존하지 않아 단위 테스트로 직접 검증 가능하다.
    /// </summary>
    internal static class BuildErrorMessages
    {
        // ToolSpec 경로(ResolveToolSpec/SubmitToolBuild/WatchToolBuild/CancelToolBuild,
        // Phase 6 이후 유일한 프로덕션 경로)가 아직 없는 구버전 NodeVault에
        // 연결하면 gRPC가 "unknown method <이름> for service ..." 형태의
        // UNIMPLEMENTED를 그대로 돌려준다 — 이 메시지가 사용자에게 그대로
        // 노출되면 무슨 뜻인지 알기 어렵다(적대적 리뷰 follow-up).
        private static readonly string[] _toolSpecMethodNames =
        {
            "ResolveToolSpec", "SubmitToolBuild", "WatchToolBuild", "CancelToolBuild",
        };

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
            StatusCode.Unimplemented => DescribeUnimplemented(rpc),
            _ => $"gRPC 오류 ({rpc.StatusCode}): {rpc.Status.Detail}",
        };

        private static string DescribeUnimplemented(RpcException rpc)
        {
            var detail = rpc.Status.Detail ?? string.Empty;
            foreach (var name in _toolSpecMethodNames)
            {
                if (detail.Contains(name, StringComparison.Ordinal))
                {
                    return "연결된 NodeVault가 구버전이라 ToolSpec API를 지원하지 않는 것 같습니다 " +
                        $"(누락된 메서드: {name}). NodeVault를 최신 버전으로 업그레이드하거나 " +
                        "다른 주소를 사용하세요.";
                }
            }

            return $"연결된 NodeVault가 이 요청을 지원하지 않습니다: {detail}";
        }
    }
}
