# NodeKit ↔ NodeVault 로컬 gRPC 통합 테스트 시나리오

Status: Draft (실행 전)
Created: 2026-07-04
Scope: heain 개발 장비에서 seoy/K8s 없이 `nodekit submit` 전체 경로를 검증

## 0. 배경 및 목적

Sprint 7 Task 2 / U5-2("seoy 원격 장비 nodekit submit 수동 테스트")는 seoy 장비 가용성에
의존하는 외부 병목이다. 조사 결과 다음이 확인되어, seoy/K8s 없이도 `nodekit submit`의
핵심 경로(gRPC 프로토콜, ResolveRecipe 분기, 실제 이미지 빌드+push+등록)를 heain에서
직접 재현할 수 있다는 결론에 도달했다:

- Harbor push는 NodeSentinel이 아니라 NodeVault(podbridge5)의 책임이며, L2 build 단계에서
  in-process로 수행된다.
- Harbor 주소는 하드코딩이 아니라 `NODEVAULT_REGISTRY_ADDR` 환경변수로 override 가능하고,
  push 경로 자체는 표준 OCI Distribution v2 API만 사용한다(Harbor 전용 API 의존 없음).
- Harbor webhook reconcile은 NodeVault index의 `integrity_health`(모니터링 전용 축)만
  갱신하며, `nodekit submit` 성공 여부를 결정하는 `lifecycle_phase`는 `SubmitToolBuild`
  호출이 직접 세팅한다 — webhook/reconcile 없이도 submit 성공 여부 검증에 지장 없음.
- heain 커널에서 rootless overlay buildah 빌드가 실제로 동작함을 확인함
  (`buildah bud --storage-driver overlay`, native overlay 드라이버, ~3초, 성공).

**이 문서는 실행 전 시나리오 초안이다. 실제 실행은 이 문서 리뷰 후 별도로 진행한다.**

## 1. 범위 — 되는 것 / 안 되는 것

### 이 로컬 테스트로 검증 가능한 것

- NodeKit CLI 플로우: 쉬운 안내 모드 / 빠른 설정 모드, 채널 확정, base image 자동 조회, 저장
- `ToolSpecRequest → ResolveToolSpec → SubmitToolBuild → WatchToolBuild` gRPC 전체 사이클
- `ResolveRecipe`의 3가지 분기: Harbor(로컬 레지스트리) cache hit / cache miss+오픈망 외부 조회
  / cache miss+폐쇄망 차단
- podbridge5를 통한 **실제** 이미지 빌드 + 로컬 레지스트리 push (mock 아님, 진짜 buildah 실행)
- NodeVault index에 `lifecycle_phase = Active`로 반영되는지 (등록 성공 여부)
- L1 정적 검증 회귀 (latest tag, digest 미고정, 버전 미고정 패키지 차단)
- 빌드 실패/취소 시 CLI 에러 표시 (CLAUDE.md §11 히든 실패 모드 대응)

### 이 로컬 테스트로 검증 불가능한 것 (여전히 seoy/K8s 필요)

- L3 dry-run / L4 smoke-run / L5-a Profiler / L5-b Security Scan — 전부 NodeSentinel +
  K8s Job 의존
- Harbor 실제 웹훅 수신, retention policy, GC 동작
- Cilium 네트워킹, 실제 프로덕션 Harbor의 인증/robot account 체계
- `AdminToolList`/`AdminDataList` — 별도 Catalog 서비스 REST API 경유라 이 로컬 구성 범위 밖
  (NodeVault index 직접 조회로 대체 확인)

**결론적으로 이 테스트는 Sprint 7 Task 2 / U5-2의 최종 완료 기준을 대체하지 않는다.**
seoy 사인오프 전 사전 검증(조기 버그 발견) 목적이며, 통과해도 U5-2는 별도로 seoy에서
최종 확인해야 한다.

## 2. 사전 준비

### 2.1 로컬 OCI 레지스트리 (Harbor 대역)

```bash
podman run -d --name local-registry -p 5000:5000 docker.io/library/registry:2
```

- 익명 push 허용 (registry:2 기본 상태, htpasswd 미설정).
- HTTP(TLS 없음) — buildah/podbridge5가 insecure registry로 인식하도록
  `$HOME/.config/containers/registries.conf.d/local-registry.conf`에 등록 필요:
  ```
  [[registry]]
  location = "localhost:5000"
  insecure = true
  ```

### 2.2 NodeVault 로컬 host 모드 기동

```bash
cd /opt/go/src/github.com/HeaInSeo/NodeVault
NODEVAULT_RUNTIME_MODE=host \
NODEVAULT_BUILD_BACKEND=local-podbridge \
NODEVAULT_REGISTRY_ADDR=localhost:5000 \
NODEVAULT_ORAS_INSECURE_TLS=true \
NODEVAULT_ADDR=:50061 \
  go run ./cmd/controlplane
```

- 포트는 `:50061`처럼 기본값(`:50051`)과 다르게 잡아서, 혹시 켜져 있을 수 있는
  다른 NodeVault 프로세스와 충돌하지 않게 한다.
- `ValidateService`(K8s 클라이언트 필요)는 host 모드에서 초기화 실패해도 경고만 찍고
  계속 진행됨 — 정상.

### 2.3 NodeKit CLI 환경변수

```bash
export NODEKIT_NODEVAULT_URL=http://localhost:50061
```

### 2.4 테스트 픽스처

- 사전 검증된 recipe 샘플 (CONDA/MICROMAMBA 각 1개, PACKAGE_MIRROR 1개, BIOCONTAINER 1개)
- cache-hit 시나리오용: 특정 tool+version 이미지를 미리 로컬 레지스트리에 push해서
  Harbor 캐시 명중을 인위적으로 재현
- L1 실패 케이스용: `latest` 태그, digest 미고정, 버전 미고정 패키지가 섞인 잘못된 입력 세트

## 3. 테스트 케이스

| ID | 시나리오 | 사전조건 | 기대 결과 | 검증 계층 |
|----|---------|---------|----------|----------|
| TC-1 | 인프라 기동 확인 | 2.1~2.3 완료 | 로컬 레지스트리 `curl localhost:5000/v2/_catalog` 200, NodeVault `nodekit ping` 또는 PingService 성공 | 인프라 |
| TC-2 | 쉬운 안내 모드, 오픈망, cache miss | 로컬 레지스트리에 대상 tool+version 이미지 없음 | conda 외부 채널(bioconda 등) 후보 목록 표시 → 사용자 선택 → recipe.json에 full_pin 저장 | CLI UX + ResolveRecipe(external_source) |
| TC-3 | 쉬운 안내 모드, 오픈망, cache hit | 2.4에서 동일 tool+version 이미지 사전 push | 후보 1개 자동 선택, build_string 자동 반영 | ResolveRecipe(harbor_cache) |
| TC-4 | 쉬운 안내 모드, 폐쇄망, cache miss | `closed_network=true` 강제 | NotFound 안내 문구 표시, build_string 없이 저장 (제출 시 NodeVault가 나중에 재확인) | ResolveRecipe(closed_network 차단) |
| TC-5 | 빠른 설정 모드 반복 | TC-2~4 동일 조합 | 동일 결과 (모드 차이만, 로직 동일 확인) | CLI UX |
| TC-6 | base image digest 오픈망 자동 조회 | PublicRegistryImageDigestResolver 경로 | 태그 → digest 자동 치환, 사용자에게 확인 프롬프트 | BaseImageSelectionStep |
| TC-7 | base image digest 폐쇄망 자동 조회 | 로컬 레지스트리를 Harbor 대역으로 사용 | HarborImageDigestResolver가 로컬 레지스트리 조회, digest 반환 | BaseImageSelectionStep |
| TC-8 | L1 회귀 — latest 태그 | 잘못된 입력(`latest`, digest 없음, 버전 미고정 패키지) | 전부 L1에서 즉시 차단, 저장/제출 진행 안 됨 | L1 정적 검증 |
| TC-9 | 실제 submit 성공 경로 | TC-2 또는 TC-3에서 저장된 recipe | `nodekit submit` → ToolSpecRequest 생성 → ResolveToolSpec 성공 → SubmitToolBuild 접수 → WatchToolBuild 스트림 → 실제 podbridge5 빌드 → 로컬 레지스트리 push 성공 → CLI에 성공 이벤트 표시 | 전체 경로(빌드 포함) |
| TC-10 | 빌드 실패 경로 | 의도적으로 존재하지 않는 base image 등 실패 유도 | WatchToolBuild가 실패 BuildEvent 스트림, CLI가 에러를 명확히 표시 (조용히 삼키지 않음) | 히든 실패 모드 (CLAUDE.md §11) |
| TC-11 | 빌드 취소 | TC-9 진행 중 `CancelToolBuild` 트리거 | 빌드 중단, 리소스 정리, CLI에 취소 상태 표시 | CancelToolBuild |
| TC-12 | 등록 상태 확인 | TC-9 성공 후 | NodeVault index에서 해당 tool의 `lifecycle_phase = Active` 확인 (index 직접 조회, Catalog REST 아님) | index/lifecycle_phase |
| TC-13 (참고) | BIOCONTAINER variant 요청 | 의도적으로 BIOCONTAINER recipe 제출 | NodeVault가 `codes.Unimplemented`(P3, 설계상 의도) 명확히 반환, CLI가 이를 크래시 없이 표시 | ResolveRecipe 회귀 |

## 4. 정리 절차 (테스트 종료 후)

```bash
# NodeVault 프로세스 종료 (Ctrl+C 또는 kill)
podman rm -f local-registry
rm -f "$HOME/.config/containers/registries.conf.d/local-registry.conf"
# 테스트 중 생성된 태그 이미지 정리 (buildah images 확인 후 개별 rmi)
# 기존에 있던 다른 프로젝트 이미지(nan-pid1-smoke, bori-operator 등)는 건드리지 않음
```

## 5. 완료 기준 및 리포트

- TC-1~TC-13 각각 pass/fail 기록
- fail 발생 시: 원인이 NodeKit 클라이언트 버그인지, NodeVault 서버 버그인지, 로컬 테스트
  환경 구성 문제인지 구분해서 기록
- 이 테스트 통과가 곧 Sprint 7 Task 2 / U5-2 완료를 의미하지 않음을 리포트에 명시 —
  seoy에서의 최종 수동 확인은 별도로 필요
