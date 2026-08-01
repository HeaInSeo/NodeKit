using System;
using System.Linq;
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

        // 리뷰 지적: NodeVault가 가장 흔히 돌려주는 실제 거부 사유(재현성 위반,
        // DockGuard 정책 거부, resolve 결과 만료)가 전부 아래 일반 fallthrough로
        // 빠져서 그냥 "gRPC 오류 (코드): 상세"만 나왔다 — 정작 recipe 작성자가
        // 가장 필요로 하는 메시지인데 제일 부실했다. NodeVault 쪽 상세 문자열은
        // 자유 형식이라 더 세분화해서 파싱하지 않는다(--format jsonl 작업에서
        // 이미 정한 원칙과 동일 — 문자열 매칭으로 세부 원인을 추측하지 않음) —
        // 대신 이 세 StatusCode 자체가 이미 신뢰할 수 있는 분류 신호이므로,
        // 무슨 "종류"의 문제인지만 한국어로 명확히 하고 원문 상세는 그대로 붙인다.
        private static string DescribeRpc(RpcException rpc) => rpc.StatusCode switch
        {
            StatusCode.Unavailable => "NodeVault에 연결할 수 없습니다. 주소와 네트워크 상태를 확인하세요.",
            StatusCode.Unauthenticated => "인증 오류: NodeVault 접근 자격을 확인하세요.",
            StatusCode.PermissionDenied => "권한 오류: NodeVault 접근 권한을 확인하세요.",
            StatusCode.DeadlineExceeded => "요청 시간이 초과되었습니다.",
            StatusCode.Cancelled => "요청이 취소되었습니다.",
            StatusCode.Unimplemented => DescribeUnimplemented(rpc),
            StatusCode.FailedPrecondition =>
                $"NodeVault가 재현성 조건을 만족하지 않는다고 판단해 이 recipe를 거부했습니다 " +
                $"(예: base image digest 미고정). 상세: {rpc.Status.Detail}",
            StatusCode.InvalidArgument =>
                $"NodeVault가 이 recipe를 거부했습니다 — 정책 위반이거나 요청 형식이 잘못됐을 수 있습니다. " +
                $"상세: {rpc.Status.Detail}",
            StatusCode.NotFound =>
                $"NodeVault에서 이전 단계의 결과를 찾지 못했습니다 — resolve 결과가 만료됐을 수 있습니다. " +
                $"처음부터 다시 시도하세요. 상세: {rpc.Status.Detail}",
            _ => $"gRPC 오류 ({rpc.StatusCode}): {rpc.Status.Detail}",
        };

        private static string DescribeUnimplemented(RpcException rpc)
        {
            var detail = rpc.Status.Detail ?? string.Empty;
            var missingMethod = _toolSpecMethodNames.FirstOrDefault(name => detail.Contains(name, StringComparison.Ordinal));
            if (missingMethod != null)
            {
                return "연결된 NodeVault가 구버전이라 ToolSpec API를 지원하지 않는 것 같습니다 " +
                    $"(누락된 메서드: {missingMethod}). NodeVault를 최신 버전으로 업그레이드하거나 " +
                    "다른 주소를 사용하세요.";
            }

            return $"연결된 NodeVault가 이 요청을 지원하지 않습니다: {detail}";
        }
    }
}
