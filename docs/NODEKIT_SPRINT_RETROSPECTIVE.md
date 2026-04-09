# NodeKit 스프린트 회고

작성일: 2026-04-09
작성자: HeaInSeo
대상 스프린트: NodeKit Sprint 1 (2026-04-07 ~ 2026-04-09 실제 완료)

---

## 1. 요약

원래 4주(04-07~05-02)로 계획된 스프린트가 **3일(04-07~04-09) 만에 MUST 전체를 완료했다.**
Phase 0~3이 계획보다 약 3.5주 선행되었고, 발견된 이슈들도 별도 버그 수정 스프린트(04-08 완료)로
정리했다.

---

## 2. 계획 vs 실제 비교

| Phase | 계획 기간 | 실제 완료 | 선행 일수 |
|-------|-----------|-----------|-----------|
| Phase 0 — 환경 준비 + 구조 | 04-07~04-08 (2일) | 04-07 | 1일 |
| Phase 1 — L1 정적 검증 + WasmPolicyChecker | 04-09~04-16 (6일) | 04-08 | 8일 |
| Phase 2 — NodeForge L2~L3 + PolicyService | 04-17~04-23 (5일) | 04-08 | 9일 |
| Phase 3 — L4 smoke run + 전체 연동 | 04-24~04-29 (4일) | 04-09 | 15일 |
| Bug Fix Sprint | 04-22 이후 예상 | 04-08 완료 | — |

### 선행 가능 원인

- NodeKit과 NodeForge 두 프로젝트 모두 **스프린트 착수 전에 이미 상당 부분 구현 완료 상태**였다.
- kind 클러스터, 내부 레지스트리, api-protos 등 인프라가 사전에 준비되어 있었다.
- OPA CLI, protoc, kaniko 등 도구 설치가 사전에 완료되어 있었다.
- L1~L4 전체 흐름에 대한 설계 선행도가 높아 구현 단계에서 결정 지연이 없었다.

---

## 3. MUST 완료 기준 점검

| # | 항목 | 완료 | 확인 방법 |
|---|------|------|-----------|
| 1 | NodeKit에서 Tool 정보 입력 가능 | ✅ | UI 폼 + ToolDefinition 모델 구현 완료 |
| 2 | latest 태그/digest 미포함/버전 미고정 시 L1 차단 | ✅ | L1Validator 단위 테스트 통과 |
| 3 | DockGuard 정책 위반 시 규칙 ID UI 표시 | ✅ | WasmPolicyChecker + PolicyResultView 구현 |
| 4 | NodeKit → NodeForge gRPC build request 전송 | ✅ | GrpcBuildServiceClient 구현, 연동 확인 |
| 5 | NodeForge builder Job 생성 + 빌드 + digest 확보 | ✅ | 통합 테스트: kaniko alpine 빌드 성공, digest 확보 |
| 6 | kind dry-run 실행 + 결과 NodeKit 표시 | ✅ | ValidateService DryRun 구현, L3 통합 확인 |
| 7 | smoke run 실행 + 결과 NodeKit 표시 | ✅ | ValidateService SmokeRun 구현, L4 통합 확인 |
| 8 | 전체 흐름 성공 시 RegisteredToolDefinition 확정 + AdminToolList 등록 | ✅ | CAS 파일 저장 + AdminToolList UI 구현 |
| 9 | NodeKit에서 정책 목록 조회 + 수동 갱신 | ✅ | PolicyManagementView + GrpcPolicyBundleProvider 구현 |
| 10 | 발견된 이슈 + DagEdit 반영 사항 문서화 | ✅ | 이 문서 |

**MUST 달성 판정: 완료 (10/10)**

---

## 4. 발견된 이슈 목록

### 4-1. 검증-전송 불일치 (BF-01)
**현상**: L1 검증 통과 후 폼을 수정해도 Send 버튼이 활성 상태로 남아 검증되지 않은 값을 전송 가능  
**수정**: 폼 변경 시 `_l1Passed` 플래그와 Send 버튼을 즉시 무효화하는 이벤트 핸들러 추가

### 4-2. EnvironmentSpec 전송 유실 (BF-02)
**현상**: `ToolDefinition.EnvironmentSpec`이 `BuildRequest` 직렬화 시 포함되지 않아 NodeForge에 전달 안 됨  
**수정**: `BuildRequestFactory`에 `EnvironmentSpec` 매핑 추가 + proto field 9 연결 확인

### 4-3. 이미지 URI 포트/태그 판별 오류 (BF-03)
**현상**: `ubuntu:22.04`에서 `:22.04`가 태그가 아닌 포트로 잘못 파싱됨  
**수정**: 포트(숫자만) vs 태그 구분 로직 수정, 포트+태그 동시 케이스 처리 추가

### 4-4. digest 추출 실패 (ClusterIP 접근 불가)
**현상**: `fetchDigest()`가 레지스트리 ClusterIP(`10.96.x.x:5000`)를 직접 호출했지만, 호스트에서
ClusterIP는 접근 불가 (curl exit 28 = timeout)  
**수정**: `extractDigestFromPodLogs()` 폴백 추가 — kaniko 로그의 `Pushed ...@sha256:...` 패턴
파싱으로 digest 추출

### 4-5. nodeforge-smoke 네임스페이스 미생성
**현상**: ValidateService가 `nodeforge-smoke` 네임스페이스 존재를 가정하고 Job을 생성했지만
해당 네임스페이스가 없어 L3 dry-run 실패  
**수정**: `ensureNamespace()` 헬퍼를 ValidateService 시작 시점에 호출하도록 변경

### 4-6. L1 validator 계약 불일치 (BF-04~06)
**현상**: L1 validator가 문서화된 필수 필드 체크, Dockerfile install 버전 고정, conda pip: 혼합
해석에서 명세와 불일치  
**수정**: Bug Fix Sprint에서 각 케이스별 테스트 추가 후 수정

---

## 5. DagEdit 반영 사항

### 5-1. VirtualCanvas-Avalonia(VCA) 통합 현황

DagEdit는 이번 스프린트와 병행하는 별도 트랙이며, VCA와의 통합은 Phase별로 진행 중이다.

| Phase | 내용 | 상태 |
|-------|------|------|
| Phase 1 — Viewer | G-0(기존 캔버스 보존), H-2(뷰어 통합) | ✅ 완료 |
| Phase 2 — Hybrid | VCA의 Pinning API 확정 후 착수 | 🔴 블로킹 — VCA API 미확정 |
| Phase 3 — Full Editor | Phase 2 완료 후 착수 | 대기 중 |

### 5-2. NodeKit 결과에서 DagEdit으로 전달되어야 하는 사항

이번 스프린트에서 확정된 `RegisteredToolDefinition` 구조(CAS 파일)가 DagEdit의
`PipelineToolPalette`에서 소비될 예정이다. 아래 사항을 DagEdit 설계에 반영해야 한다:

1. **도구 팔레트 데이터 소스**: `ToolRegistryService.ListTools()` gRPC를 통해 `RegisteredToolDefinition` 목록 조회
2. **CAS 해시 기반 식별**: 도구를 `cas_hash`로 식별. 동일 내용의 도구는 동일한 CAS 해시를 가진다.
3. **이미지 URI 형식**: 항상 `image@sha256:digest` 형태 — `latest` 태그 없음
4. **I/O 명세**: `input_names`, `output_names`는 문자열 리스트. DagEdit 연결선에서 이름 기반 매핑에 사용.
5. **environment_spec**: conda/pip 환경 명세 문자열. 파이프라인 실행 환경 복원에 활용 가능.

### 5-3. STRETCH 항목 (S-1~S-4) 처리 방침

| 항목 | 내용 | 판단 |
|------|------|------|
| S-1 | 팔레트 → 캔버스 드래그&드롭 | DagEdit Phase 2(VCA 의존) 완료 후 구현 가능 |
| S-2 | 방향성 연결선 + 숫자 라벨 | 동상 |
| S-3 | 연결선 클릭 → 매핑 상세창 | 동상 |
| S-4 | 연결 규칙 검증 | DagEdit에서 이름 기반 I/O 매핑 검증으로 구현 예정 |

STRETCH 항목은 VCA Phase 2 API 확정 후 DagEdit 트랙에서 구현한다.
NodeKit 스프린트에서는 DagEdit 연동 코드를 추가하지 않는다 (역할 경계 준수).

---

## 6. 잔여 리스크 및 후속 과제

| ID | 항목 | 성격 | 후속 조치 |
|----|------|------|-----------|
| R-01 | api-protos 버전 드리프트 | 저장소 간 | `ApiProtosRoot` 환경 변수로 버전 고정 유지 |
| R-02 | NodeForge PolicyService 정책 목록 하드코딩 | 기술 부채 | DFM001~004 외 DSF/DGF 규칙 추가 시 갱신 필요 |
| R-03 | VCA Phase 2 API 미확정 | 외부 의존 | VCA 팀과 Pinning API 확정 후 DagEdit Phase 2 착수 |
| R-04 | ClusterIP 레지스트리 직접 접근 불가 (호스트 → 클러스터) | 환경 제약 | pod log 폴백 유지, 향후 port-forward 또는 NodePort 레지스트리로 전환 검토 |
| R-05 | DockGuard security/genomics 정책이 NodeForge wasm에 미포함 | 기술 부채 | PolicyService 재빌드 시 새 정책 포함한 wasm 생성 필요 |

---

## 7. 스프린트 종료 선언

2026-04-09 기준으로 NodeKit Sprint 1의 **MUST 10개 항목 전체가 완료**되었음을 선언한다.

STRETCH 항목(S-1~S-4)은 VCA 의존성으로 인해 DagEdit 별도 트랙에서 처리하며,
이번 스프린트 실패 기준에 해당하지 않는다.
