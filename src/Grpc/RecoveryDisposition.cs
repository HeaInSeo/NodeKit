namespace NodeKit.Grpc
{
    /// <summary>
    /// V14 recovery-disposition 계약 — `nodekit submit --format jsonl`의 terminal
    /// completed 레코드에만 실려 나가는, 기계가 읽는 회복 성격 분류다. 세 값은
    /// 코드에 이미 존재하는 프로토콜 구조(build ID 유무, 관측한 terminal 이벤트
    /// 종류, 어떤 타임아웃이 발동했는지)에서만 결정론적으로 파생된다 — 로그/경과
    /// 시간/상태 문자열을 파싱해 추론하지 않는다. 일반적인 "retryable" 개념은
    /// 일부러 두지 않는다(OP-V14-RECOVERY-CAP): 백엔드가 지원하지 않는 회복
    /// 방식을 노출하지 않기 위해서다.
    /// </summary>
    internal enum RecoveryDisposition
    {
        /// <summary>
        /// 회복이 무의미한 상태 — 작업이 성공했다. 재제출/재조정 대상이 아니다.
        /// </summary>
        None,

        /// <summary>
        /// 로컬 작업이 종료되었다 — 확정 실패이거나 사용자가 중단(abort)했다.
        /// 재제출은 기존 작업의 재시도(retry)가 아니라 완전히 새로운 작업이다.
        /// (사용자 취소는 서버 빌드가 여전히 진행 중일 수 있으므로 '원격
        /// 상태까지 확정됐다'고 단정하지는 않는다 — 필요하면 NodeVault
        /// 인덱스/로그로 직접 확인할 수 있다.)
        /// </summary>
        Terminal,

        /// <summary>
        /// 원격 결과가 확인되지 않았다(예: build ID를 받기 전 실패, 최종
        /// 이벤트 없이 끝난 스트림, 타임아웃, 예기치 못한 오류). 원격 빌드가
        /// 실제로 생성/진행됐는지 알 수 없으므로, 재제출을 고려하기 전에
        /// NodeVault 인덱스/로그로 원격 상태를 직접 확인해야 한다 — 이것은
        /// 일반적인 "그냥 다시 시도해도 안전함"이 아니다. idempotency key
        /// 기반의 자동 조회/재조정(reconcile)은 현재 CLI에 없는 미구현
        /// 기능이며 Issue #86에서 추적된다.
        /// </summary>
        Uncertain,
    }
}
