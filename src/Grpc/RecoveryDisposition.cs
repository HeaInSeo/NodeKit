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
        /// 작업이 확정적으로 실패했거나 사용자가 중단(abort)했다. 재제출은
        /// 기존 작업의 재시도(retry)가 아니라 완전히 새로운 작업이다 —
        /// 이 결과 자체는 확정이므로 원격 상태를 다시 조정할 필요가 없다.
        /// </summary>
        Terminal,

        /// <summary>
        /// 원격 결과가 확인되지 않았다(예: build ID를 받기 전 실패, 최종
        /// 이벤트 없이 끝난 스트림, 타임아웃, 예기치 못한 오류). 호출자는
        /// 어떤 재제출보다 먼저 반드시 빌드의 idempotency key로 원격 상태를
        /// 조정(reconcile)해야 한다 — 이것은 일반적인 "그냥 다시 시도해도
        /// 안전함"이 아니다.
        /// </summary>
        Uncertain,
    }
}
