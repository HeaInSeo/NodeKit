# NodeKit ↔ NodeVault 로컬 gRPC 통합 테스트 시나리오

Status: 실행 완료 (2026-07-05) — 버그 4건 발견, 전부 수정·머지 완료
Created: 2026-07-04
Updated: 2026-07-05
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

**v0.2 변경**: 1차 리뷰에서 지적된 5개 보완점(cache-hit 재현 방식, closed_network 정책
분기 vs 실제 네트워크 차단 분리, submit 전/빌드 중 실패 분리, cancel용 slow fixture,
index 확인 방법 구체화)을 코드 확인 후 반영. 리뷰가 제안한 구체적 예시 중 실제 구현과
다른 부분(SQLite 위치, ORAS insecure TLS 의미, BuildEvent 이벤트 이름)은 코드 기준으로
정정함 — 자세한 내용은 각 섹션의 "확인된 사실" 참고.

**이 문서는 실행 전 시나리오다. 실제 실행은 이 문서 확정 후 별도로 진행한다.**

## 1. 범위 — 되는 것 / 안 되는 것

### 이 로컬 테스트로 검증 가능한 것

- NodeKit CLI 플로우: 쉬운 안내 모드 / 빠른 설정 모드, 채널 확정, base image 자동 조회, 저장
- `ToolSpecRequest → ResolveToolSpec → SubmitToolBuild → WatchToolBuild` gRPC 전체 사이클
- `ResolveRecipe`의 분기: Harbor(로컬 레지스트리) cache hit / cache miss+오픈망 외부 조회
  / closed_network 정책 분기
- podbridge5를 통한 **실제** 이미지 빌드 + 로컬 레지스트리 push (mock 아님, 진짜 buildah 실행)
- NodeVault index에 `lifecycle_phase = Active`로 반영되는지 (등록 성공 여부)
- L1 정적 검증 회귀 (latest tag, digest 미고정, 버전 미고정 패키지 차단)
- 빌드 실패/취소 시 CLI 에러 표시 및 exit code (CLAUDE.md §11 히든 실패 모드 대응)

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

**주의 (확인된 사실): plain HTTP 레지스트리는 쓰지 않는다.**
`pkg/oras/referrer.go`의 `NODEVAULT_ORAS_INSECURE_TLS=true`는 인증서 검증만 스킵할 뿐
`PlainHTTP`를 세팅하지 않는다 — 즉 sori/oras referrer push 경로는 TLS 자체는 항상 요구한다.
반면 buildah(podbridge5) push 경로는 NodeVault가 노출하는 insecure 옵션이 아예 없고
`/etc/containers/registries.conf`의 `insecure = true` 항목에만 의존한다. 두 경로가
서로 다른 방식으로 "insecure"를 처리하므로, **plain HTTP로 레지스트리를 띄우면 oras
referrer push만 조용히 실패할 수 있다.** self-signed HTTPS로 띄우는 게 안전하다.

```bash
# self-signed 인증서 생성
mkdir -p /tmp/local-registry-certs
openssl req -newkey rsa:2048 -nodes -keyout /tmp/local-registry-certs/domain.key \
  -x509 -days 30 -out /tmp/local-registry-certs/domain.crt \
  -subj "/CN=localhost"

podman run -d --name local-registry -p 5000:5000 \
  -v /tmp/local-registry-certs:/certs:Z \
  -e REGISTRY_HTTP_TLS_CERTIFICATE=/certs/domain.crt \
  -e REGISTRY_HTTP_TLS_KEY=/certs/domain.key \
  docker.io/library/registry:2
```

- 익명 push 허용 (registry:2 기본 상태, htpasswd 미설정)
- buildah 쪽은 self-signed 인증서라도 검증을 스킵해야 하므로 registries.conf에 등록:
  ```
  # $HOME/.config/containers/registries.conf.d/local-registry.conf
  [[registry]]
  location = "localhost:5000"
  insecure = true
  ```
- NodeVault 쪽 oras 경로는 `NODEVAULT_ORAS_INSECURE_TLS=true`로 인증서 검증 스킵
  (TLS 연결 자체는 되므로 이 조합으로 정상 동작)

### 2.2 NodeVault 로컬 host 모드 기동

**상태 디렉터리 완전 격리 (확인된 사실)**: NodeVault의 로컬 상태는 4개 독립 경로에
분산되어 있다 — `INDEX_DIR`(카탈로그 index, JSON), `CATALOG_DIR`(tool CAS),
`DATA_CATALOG_DIR`(data CAS, `runtimeConfig`에 안 묶여 있어 별도 지정 필요),
`NODEVAULT_BUILD_STATE_DB`(build 실행 상태, SQLite). TC-2/TC-3처럼 cache
hit/miss를 비교하는 테스트가 서로 오염되지 않으려면 매 테스트런마다 이 4개를 전부
새 스크래치 경로로 잡아야 한다.

```bash
cd /opt/go/src/github.com/HeaInSeo/NodeVault

TESTROOT=/tmp/nodevault-local-grpc-test
rm -rf "$TESTROOT" && mkdir -p "$TESTROOT"

NODEVAULT_RUNTIME_MODE=host \
NODEVAULT_BUILD_BACKEND=local-podbridge \
NODEVAULT_REGISTRY_ADDR=localhost:5000 \
NODEVAULT_ORAS_INSECURE_TLS=true \
NODEVAULT_ADDR=:50061 \
INDEX_DIR="$TESTROOT/index" \
CATALOG_DIR="$TESTROOT/catalog" \
DATA_CATALOG_DIR="$TESTROOT/data-catalog" \
NODEVAULT_BUILD_STATE_DB="$TESTROOT/buildstate/build-state.db" \
  go run ./cmd/controlplane
```

- 포트는 `:50061`처럼 기본값(`:50051`)과 다르게 잡아서, 켜져 있을 수 있는 다른
  NodeVault 프로세스와 충돌하지 않게 한다.
- `ValidateService`(K8s 클라이언트 필요)는 host 모드에서 초기화 실패해도 경고만 찍고
  계속 진행됨 — 정상.

### 2.3 NodeKit CLI 환경변수

**확인된 사실**: `GrpcToolSpecClient`/`GrpcResolveRecipeClient` 둘 다
`GrpcChannel.ForAddress(address)`를 스킴 정규화 없이 그대로 호출한다. 스킴이 없는
`localhost:50061` 형태는 실패한다 — **반드시 `http://` 포함**.

```bash
export NODEKIT_NODEVAULT_URL=http://localhost:50061
```

### 2.4 테스트 픽스처

- 사전 검증된 recipe 샘플 (CONDA/MICROMAMBA 각 1개, PACKAGE_MIRROR 1개, BIOCONTAINER 1개)
- **cache-hit 재현 방법 (정정)**: 로컬 레지스트리에 이미지를 수동으로 미리 push하는
  방식은 **쓰지 않는다.** `pkg/build/recipe_resolve.go`의 Harbor-first 조회는
  `s.registry.ListTools`로 **NodeVault 자신의 index/카탈로그**를 조회해서 `Active`
  상태 ToolDefinition의 `EnvironmentSpec`에서 build string을 추출하는 구조다. 즉
  raw 이미지를 레지스트리에 올려놓는 것만으로는 cache hit 조건을 충족하지 못한다.
  대신: **TC-9(실제 submit 성공)를 먼저 한 번 통과시켜 해당 tool+version을 실제로
  NodeVault index에 등록한 뒤, 같은 tool+version으로 TC-3(ResolveRecipe)을 재호출**해서
  cache hit가 되는지 확인하는 순서로 구성한다. TC-9 → TC-3 순서 의존성이 생기므로
  테스트 케이스 표에 명시한다.
- cancel 테스트용 slow fixture (§3 TC-11 참고): 일반 alpine 빌드는 ~3초 만에 끝나
  취소 타이밍을 잡을 수 없다. 의도적으로 느린 Dockerfile을 별도로 준비한다.
  ```dockerfile
  FROM docker.io/library/alpine:3.20
  RUN sleep 60
  ```
- L1 실패 케이스용: `latest` 태그, digest 미고정, 버전 미고정 패키지가 섞인 잘못된 입력 세트
- **submit 전 실패 케이스용** (TC-10A): base image 자체가 존재하지 않는 recipe
  (`docker.io/library/does-not-exist-xyz:1.0`) — ResolveToolSpec 단계에서 막히는지 확인
- **빌드 중 실패 케이스용** (TC-10B): base image는 정상이지만 Dockerfile/recipe 내부
  단계가 실패하도록 구성 (`RUN false`, 존재하지 않는 conda 패키지 설치 등)

## 3. 테스트 케이스

케이스를 계층별로 재분류했다 (인프라 → ResolveRecipe → base image resolver → L1 →
submit/build/watch). 실패 시 원인 계층을 바로 특정할 수 있다.

### A. 인프라 계층

| ID | 시나리오 | 기대 결과 |
|----|---------|----------|
| TC-1 | 연결 확인 | `curl -k https://localhost:5000/v2/_catalog` 200, NodeKit `nodekit ping`(또는 동등 명령)으로 NodeVault gRPC 응답 확인 |
| TC-1B | 상태 디렉터리 격리 확인 | `$TESTROOT/index`, `catalog`, `data-catalog`, `buildstate` 4개 디렉터리가 실제로 이번 실행에서 새로 생성됐는지 확인 |
| TC-1C | 로컬 레지스트리 push/pull 확인 | `podman push`로 아무 이미지나 로컬 레지스트리에 push 성공 확인 (buildah/oras 양쪽 경로 사전 검증용이 아니라 레지스트리 자체 동작 확인) |

### B. ResolveRecipe 계층

| ID | 시나리오 | 사전조건 | 기대 결과 |
|----|---------|---------|----------|
| TC-2 | 오픈망, cache miss | 신규 tool+version, 로컬 index에 미등록 | conda 외부 채널(bioconda 등) 후보 목록 표시 → 사용자 선택 → recipe.json에 full_pin 저장, 응답 `resolution_source = external_source` |
| TC-3 | 오픈망, cache hit | **TC-9를 먼저 동일 tool+version으로 성공시킨 후 재실행** | 후보 1개 자동 선택, 응답 `resolution_source = harbor_cache` — **source 필드까지 확인, "후보 1개"만 보고 통과 처리하지 않는다** |
| TC-4A | closed_network=true 정책 분기 | 외부망은 실제로 열려 있는 상태에서 `closed_network=true`만 세팅 | 외부 조회를 시도하지 않고 즉시 InvalidArgument/NotFound 안내, build_string 없이 저장 |
| TC-4B | 실제 외부망 차단 | `closed_network=false`(오픈망 모드)인데 DNS/proxy를 의도적으로 막아 외부 조회 자체를 실패시킴 | CLI가 조용히 죽지 않고 명확한 실패 메시지 표시 (연결 실패와 "정책상 차단"을 사용자가 구분할 수 있어야 함) |
| TC-13 | BIOCONTAINER variant 요청 | 의도적으로 BIOCONTAINER recipe 제출 | NodeVault가 `codes.Unimplemented`(P3, 설계상 의도) 명확히 반환, CLI가 크래시 없이 표시 |

### C. Base image resolver 계층

| ID | 시나리오 | 기대 결과 |
|----|---------|----------|
| TC-6 | 오픈망 base image digest 조회 | PublicRegistryImageDigestResolver 경로로 태그 → digest 자동 치환, 사용자 확인 프롬프트 |
| TC-7 | 폐쇄망 base image digest 조회 | 로컬 레지스트리를 Harbor 대역으로 사용해 HarborImageDigestResolver가 조회, digest 반환 |

### D. L1 validation 계층

| ID | 시나리오 | 기대 결과 |
|----|---------|----------|
| TC-8 | latest 태그 | L1에서 즉시 차단 |
| TC-8B | digest 미고정 base image | L1에서 즉시 차단 |
| TC-8C | version 미고정 package | L1에서 즉시 차단 |
| TC-8D | 잘못된 조합 반복 확인 | 세 케이스 모두 저장/제출 진행 안 됨, exit code 1 (§5 참고) |

### E. Submit/build/watch 계층

| ID | 시나리오 | 사전조건 | 기대 결과 |
|----|---------|---------|----------|
| TC-9 | submit 성공 | TC-2에서 저장된 recipe (실패하지 않는 정상 recipe) | `nodekit submit` → ResolveToolSpec 성공 → SubmitToolBuild 접수 → WatchToolBuild 스트림에서 `BuildEventKind` 순서: `JOB_CREATED → JOB_RUNNING → PUSH_SUCCEEDED → DIGEST_ACQUIRED → SUCCEEDED` (terminal은 SUCCEEDED 정확히 1회) → exit code 0 |
| TC-10A | submit 전 실패 | §2.4 "submit 전 실패 케이스용" fixture | ResolveToolSpec 단계(또는 그 이전 L1)에서 즉시 실패, **SubmitToolBuild가 호출조차 안 됨**, exit code 2 또는 1 (어느 단계에서 막히는지에 따라 다름 — 실제 실행 결과로 확정) |
| TC-10B | 빌드 중 실패 | §2.4 "빌드 중 실패 케이스용" fixture | SubmitToolBuild는 접수됨(`JOB_CREATED`까지 수신), 이후 WatchToolBuild 스트림에서 `BUILD_EVENT_KIND_FAILED` 수신, exit code 1, stderr에 실패 메시지 표시 (CLAUDE.md §11 "실패 이벤트가 조용히 사라지는" 케이스를 정확히 이 지점에서 검증) |
| TC-11 | 빌드 취소 | §2.4 slow fixture(`RUN sleep 60`)로 제출 중 Ctrl-C | **주의**: `BuildEventKind`에는 취소 전용 값이 없다 (proto 확인 완료 — LOG/JOB_CREATED/JOB_RUNNING/PUSH_SUCCEEDED/DIGEST_ACQUIRED/SUCCEEDED/FAILED뿐). 취소는 클라이언트 측 `OperationCanceledException`으로 처리되어 exit code `130` 반환. 서버 측은 buildstate SQLite의 `Status = Interrupted`로 확인 (`$TESTROOT/buildstate/build-state.db`). **가장 중요한 확인**: 취소된 빌드가 NodeVault index에 `lifecycle_phase = Active`로 절대 들어가면 안 됨 |
| TC-12 | 등록 상태 확인 | TC-9 성공 후 | `cat $TESTROOT/index/vault-index.json \| jq '.entries[] | {cas_hash, lifecycle_phase}'`로 `Active` 확인, 또는 `grpcurl -plaintext -d '{"cas_hash":"<hash>"}' localhost:50061 nodevault.v1.ToolRegistryService/GetTool` |

## 4. 정리 절차 (테스트 종료 후)

```bash
# NodeVault 프로세스 종료 (Ctrl+C 또는 kill)
podman rm -f local-registry
rm -rf /tmp/local-registry-certs
rm -f "$HOME/.config/containers/registries.conf.d/local-registry.conf"
rm -rf /tmp/nodevault-local-grpc-test
# 테스트 중 생성된 태그 이미지 정리 (buildah images 확인 후 개별 rmi)
# 기존에 있던 다른 프로젝트 이미지(nan-pid1-smoke, bori-operator 등)는 건드리지 않음
```

## 5. 완료 기준 및 리포트

### CLI exit code 기준 (확인된 사실, `SubmitCommand.cs` + `SubmitCommandTests.cs`)

| 상황 | exit code |
|------|-----------|
| 성공 (`BuildEventKind.Succeeded` 수신) | 0 |
| L1 검증 실패 / 빌드 실패(`Failed` 이벤트) / 처리되지 않은 예외 | 1 |
| 사용법/인자 오류 (recipe 파일 없음, JSON 파싱 실패, URL 미설정 등) | 2 |
| 사용자 취소 (Ctrl-C, `OperationCanceledException`) | 130 (코드상 존재하나 기존 테스트에 assertion 없음 — 이번 로컬 테스트가 사실상 첫 검증) |

### push 결과는 CLI 로그만으로 판단하지 않는다

TC-9 성공 후 다음을 모두 교차 확인해야 pass로 간주한다 (하나만 보지 않는다):

```text
1. CLI 성공 이벤트 (SUCCEEDED, exit code 0)
2. 로컬 레지스트리에 실제 manifest 존재
   curl -k https://localhost:5000/v2/<repo>/tags/list
3. NodeVault index의 lifecycle_phase = Active (TC-12)
4. buildstate DB의 Status = Succeeded
```

### 리포트에 포함할 것

- TC-1~TC-13 각각 pass/fail 기록
- fail 발생 시: NodeKit 클라이언트 버그 / NodeVault 서버 버그 / 로컬 테스트 환경 구성
  문제 중 어디인지 구분
- **이 테스트 통과가 곧 Sprint 7 Task 2 / U5-2 완료를 의미하지 않음을 리포트에 명시** —
  seoy에서의 최종 수동 확인은 별도로 필요

## 6. 알려진 리스크 / 실행 시 주의사항

```text
- 이 로컬 테스트는 NodeKit ↔ NodeVault gRPC와 host-mode podbridge5 build path의
  사전 검증이다. 통과 조건은 "로컬 registry(self-signed HTTPS) + host buildah +
  NodeVault index(격리된 스크래치 경로)" 기준이며, K8s 기반 NodeSentinel 검증, Harbor
  운영 인증/웹훅/GC, seoy 네트워크 조건을 대체하지 않는다.
- cache-hit(TC-3)은 반드시 TC-9를 먼저 성공시킨 뒤 재실행하는 순서로 진행한다. 로컬
  레지스트리에 이미지를 미리 수동 push하는 방식은 실제 cache-hit 조건(NodeVault 자체
  index 조회)과 다르므로 쓰지 않는다.
- 로컬 레지스트리는 plain HTTP가 아니라 self-signed HTTPS로 띄운다. plain HTTP로
  띄우면 oras/referrer push(spec attestation)가 조용히 실패할 수 있다.
- 취소(TC-11) 검증 시 BuildEvent에 취소 전용 kind가 없다는 점을 인지하고, buildstate
  SQLite의 Interrupted 상태와 index의 lifecycle_phase 비-Active를 직접 확인한다.
```

## 7. 실행 결과 (2026-07-05, 2차 실행까지 반영)

이 시나리오를 heain에서 실제로 두 차례에 걸쳐 실행했다. 환경 구성 중 이슈 두 건을
만났고(둘 다 §2에 반영, 이슈 아님), TC-1~TC-13 전체를 실제 NodeVault로 실행해서
**총 6개의 실제 버그**를 발견했으며 전부 수정·머지·close 완료했다.

### 환경 구성 중 발견 (문서에 이미 반영, 별도 이슈 아님)

- 시스템 `/etc/containers/storage.conf`가 root 전용 `runroot`를 하드코딩하고 있어
  `podbridge5.NewStoreWithOptions()`(buildah CLI와 달리 rootless 자동 보정이 없음)가
  `/run/containers` mkdir로 실패함 → 사용자 레벨 `storage.conf` override로 해결.
- `netavark`가 이 장비에 설치돼 있지 않아 실제 `conda install` 등 네트워크가 필요한
  `RUN` 단계에서 buildah가 실패함 (`slirp4netns`는 이 버전이 지원 안 함) → `dnf install
  netavark` + `network_backend="netavark"` 명시로 해결.
- 로컬 registry 컨테이너 기동 시 rootless cgroup delegation 문제로 `runc create
  failed`가 발생 → `cgroup_manager = "cgroupfs"`를 `containers.conf`에 명시해 해결
  (systemd cgroup manager 대신 사용).

### 실행 결과 요약 (TC-1~TC-13 전부 실제 NodeVault로 실행 완료)

| TC | 결과 |
|---|---|
| TC-1/1B/1C (인프라) | ✓ 통과 |
| TC-2 (오픈망 cache-miss) | ✓ 통과 — external_source, 다수 후보 반환 확인 |
| TC-3 (cache-hit) | 재현 안 됨 — recipe에 build string까지 고정되지 않으면 harbor_cache 분기를 안 탐 (설계상 특성, 버그 아님) |
| TC-4A (closed_network 정책 분기) | ✓ 통과 — 외부망이 열려 있어도 정책상 즉시 차단 확인 |
| TC-4B (실제 네트워크 차단) | **버그 발견**: 진짜 네트워크 실패인데 candidates=0으로 조용히 "성공" 처리 → Issue #9 |
| TC-6 (오픈망 base image digest 조회) | **버그 발견**: `library/` 네임스페이스 미보정으로 공식 이미지 401 → Issue #7 |
| TC-7 (폐쇄망 base image digest 조회) | **버그 발견**: 개인키 없는 CA cert 로딩 시 크래시 → Issue #8 |
| TC-8/8B/8C/8D (L1 검증) | ✓ 통과 |
| TC-9 (submit 성공) | ✓ 통과 — 실제 conda install + push + index 등록 확인 |
| TC-10A (submit 전 실패) | ✓ 확인 — 실제로는 "submit 전 실패"가 없고 항상 빌드 단계에서 실패한다는 것도 규명 (L1은 형식만 검사, NodeVault는 존재 여부를 빌드 시점에 확인) |
| TC-10B (빌드 중 실패) | **버그 발견**: 실패 자체는 정상 재현되지만 CLI가 exit 0 반환 → Issue #5 |
| TC-11 (취소) | **버그 발견**: 취소가 서버 빌드를 실제로 멈추지 않음 → Issue #6 |
| TC-12 (등록 확인) | ✓ 통과 |
| TC-13 (BioContainer) | ✓ 통과 (1차 실행 픽스처가 무효해서 2차에 유효한 이미지로 재검증) |
| 대화형 `recipe create` 흐름 (Package 방식) | 시도 중 **버그 발견**: stdin EOF 시 무한 루프(300MB+ 로그) → Issue #10, #11 |
| 대화형 `recipe create` 흐름 (Mirror/PackageMirror 방식) | 시도 중 **버그 발견**: ResolveRecipe에서 처리 안 된 `RpcException`으로 CLI 전체 크래시(exit 134) → Issue #13. 수정 후 실제 NodeVault로 재검증 — 크래시 없이 저장 완료, 이어서 `nodekit submit`도 시도해 실제 conda 빌드 단계까지 도달·실패(존재하지 않는 mirror URI이므로 예상된 인프라 실패, 코드 버그 아님)까지 확인 |
| 대화형 `recipe create` 흐름 (Container/BioContainer 방식) | ✓ 통과 — 저장 완료(BuildKind=BioContainer, ImageRef+ImageDigest가 BioContainerImageUri로 정상 결합), `nodekit submit`도 시도해 실제 buildah pull 단계까지 도달·실패(가짜 digest이므로 예상된 인프라 실패, 코드 버그 아님) |
| 대화형 `recipe create` 흐름 (Source/SourceBuild 방식) | ✓ 통과 — 저장 완료, `nodekit submit`도 시도해 실제 curl 다운로드 + `sha256sum -c` 단계까지 도달·실패(가짜 checksum이므로 예상된 인프라 실패, 코드 버그 아님) |
| 대화형 `recipe create` 흐름 (Dockerfile/DockerfileFallback 방식) | ✓ 통과 — 저장 완료, `nodekit submit`으로 실제 build+push+index 등록까지 전부 성공(exit 0), NodeVault index `lifecycle_phase=Active` 교차 확인 완료 |
| Package 방식 내 Micromamba 엔진(`--engine micromamba`, quick-setup에는 경로 없음) | 시도 중 **버그 발견**: `RecipeRenderer`가 렌더하는 `micromamba install -y <pkg>`에 target 환경 지정이 없어 패키지 유효성과 무관하게 100% 빌드 실패("No target prefix specified") → Issue #14. 수정 후 실제 buildah 직접 빌드 + 로컬 NodeVault `nodekit submit` 둘 다로 재검증 — build+push+index 등록까지 전부 성공(`lifecycle_phase=Active`) 확인 |
| BeginnerGuideFlow(가이드 모드) 13개 재입력 루프 전수조사 | **버그 발견 — 12/13곳**: quick-setup과 동일한 EOF-vs-빈줄 버그가 안전한 `[Y/n]`/`[y/N]` 확인 프롬프트 2곳을 제외한 나머지 전부에 있었음(실제 루프 개수도 12개가 아니라 13개 — `while(!subflowDone)` 1개가 grep 카운트에서 누락) → Issue #12 close. 가이드 모드 진입 직후 입력을 끊으면 5초 안에 350만 줄 출력되는 것으로 재현 확인, 수정 후 동일 시나리오 4가지로 재검증 — 전부 크래시/무한루프 없이 정상 취소 |

### 발견된 버그 10건(#5-#14)과 수정 커밋 — 전부 close 완료

- **Issue #5** (NodeKit `bd9786e`): `GrpcToolSpecClient.MapWatchEvent()`가 서버의
  `Status` 필드(PascalCase)를 소문자와 비교해서 매칭이 항상 실패 → 빌드 실패 시에도
  exit code 0.
- **Issue #6** (NodeKit `bd9786e`): `SubmitCommand`가 Ctrl-C 시 `CancelToolBuild`
  RPC를 아예 호출하지 않고, gRPC 스트림 취소가 `RpcException`으로 오는데
  `OperationCanceledException`만 잡고 있어서 취소 처리 자체가 도달 불가능한 코드였음.
- **Issue #7** (NodeKit `73805d4`): `PublicRegistryImageDigestResolver`가 `alpine:3.20`
  같은 네임스페이스 없는 공식 이미지명에 `library/`를 보정하지 않아 401.
- **Issue #8** (NodeKit `a938690`): `HarborImageDigestResolver.TryCreate()`가
  `X509Certificate2.CreateFromPemFile()`(개인키 필요)을 써서, 개인키 없는 정상적인
  CA 신뢰 전용 인증서로 크래시.
- **Issue #9** (NodeVault `605a98d` + NodeKit `1749a58`): `queryAnacondaOrg`가 채널
  조회 실패(진짜 네트워크 에러)와 404(없음)를 구분 안 해서, 전체 채널 연결 불가
  상황에서도 candidates=0으로 조용히 "성공" 처리됨 → NodeVault는 전체 실패 시
  `Unavailable` 에러 반환하도록, NodeKit은 candidates=0인 패키지에 경고 메시지를
  출력하도록 각각 수정.
- **Issue #10** (NodeKit `f1b5b37`): `MethodRecommendationPresenter.Present()`의
  `while(true)` 루프가 stdin EOF와 "빈 줄 입력"을 구분 못 해서 유효한 선택을
  영원히 못 받으면 무한 재입력 루프(CPU 100%, 수백MB 로그)에 빠짐.
- **Issue #11** (NodeKit `49250e6`): #10과 동일한 근본 원인이 `PromptChannelEntry`,
  `PromptStringListField` 두 곳에도 있었음 — 필수 리스트 필드(Channels 등)가 값을
  하나도 못 채운 채 stdin이 EOF에 도달하면 동일하게 무한 루프. 처음에는 두 지점
  모두 `ReadLineOrCancel()`로 일괄 치환했으나 기존 테스트 24개가 깨짐(같은 함수가
  옵션 필드에도 재사용되고, 그 테스트들은 EOF를 "남은 옵션 필드는 기본값으로 넘어감"
  신호로 의도적으로 사용하고 있었음) → raw EOF(`null`)와 실제 빈 줄을 구분해서
  `CompleteListField`가 실패했을 때만 취소로 승격시키는 정밀 패턴으로 재수정.
- **Issue #12** (NodeKit `9766494`, close 완료): `PromptScalarField`/`PromptChoiceField`도
  같은 계열 버그가 있어 `0c17984`로 수정했지만(`PromptChoiceField`는 현재 카탈로그에
  도달 가능한 Required choice 필드가 없어서 방어적 수정일 뿐 — 테스트 없음),
  `BeginnerGuideFlow.cs`는 별도 전수조사 항목으로 열어 두었었다. 전수조사 결과 실제
  루프는 12개가 아니라 **13개**였다(`while(true)` 12개 + `while(!subflowDone)` 1개 —
  후자는 grep 패턴에 안 걸려서 원래 카운트에서 누락됨). 그중 확인된 두 개의 `[Y/n]`/
  `[y/N]` 확인 프롬프트(digest 사용 확인, Dockerfile 경고 확인 — 둘 다 blank/EOF가
  이미 명시적 기본값으로 처리됨)만 안전했고, **나머지 12곳은 전부 같은 EOF 버그를
  가지고 있었다**(그중 하나는 while 루프 자체가 아니라 루프 안에 끼어 있는 별도 read
  지점 — "직접 입력" 선택 후 ImageDigest를 물어보는 부분). 실제로 재현: 가이드 모드
  진입 직후 입력을 끊으면 5초 안에 350만 줄이 출력됨. `ReadTrimmedLineOrNull` 헬퍼로
  동일한 정밀 패턴(진짜 EOF만 취소로 승격, 실제 빈 Enter는 기존처럼 재입력 유도)을
  12곳 전부에 적용, 기존 테스트 157개 그대로 통과 확인 후 회귀 테스트 7개 추가.
  실제 CLI로 4개 경로(도구 이름/설치 명령/source checksum/container 개별 digest 입력)
  재검증해서 전부 크래시 없이 정상 취소되는 것까지 확인.
- **Issue #13** (NodeKit `70048ff`): `IResolveRecipeClient.ResolveAsync()`가
  `package_mirror_uri`를 받을 파라미터 자체가 없어서, Mirror 방식 recipe는 항상 빈
  URI로 ResolveRecipe를 호출 → NodeVault가 필연적으로 `InvalidArgument`로 거부.
  `RecipeCreateFlow.Execute()`가 이 호출을 try/catch로 감싸지도 않아서 처리 안 된
  `RpcException`으로 CLI 프로세스 전체가 크래시(exit 134, raw stack trace)했음.
  인터페이스에 `packageMirrorUri` 파라미터를 추가해 실제 값을 전달하도록 고치고,
  `RpcException`을 잡아 NotFound 분기처럼 경고만 출력하고 계속 진행하도록 방어
  코드도 추가. 수정 후 실제 로컬 NodeVault로 재검증: 더 이상 크래시하지 않고
  ResolveRecipe가 정상적으로 `not_found`를 반환하며(관리자 사전 등록 필요, 설계상
  의도된 동작), recipe가 정상 저장됨을 확인.
- **Issue #14** (NodeKit `927b047`): `RecipeRenderer.RenderInstallerFamily()`가
  micromamba 엔진에도 conda와 동일하게 `"micromamba install -y <packages>"`만
  렌더링하고 target 환경(`-n base`)을 지정하지 않음. `mambaorg/micromamba`
  이미지는 conda-forge/miniforge 계열과 달리 plain `RUN` 단계에서 환경이 자동
  activate되지 않아서, 패키지가 실제로 존재하고 버전이 정확해도 **항상**
  "No target prefix specified"로 빌드가 실패했다. `installer == "micromamba"`일
  때만 `-n base`를 추가하도록 수정 — `PackageVersionValidator`가 이미
  `-n`/`--name`을 "다음 토큰을 소비하는 옵션"으로 처리하고 있어서 L1 false
  positive는 없음. 실제 buildah 직접 빌드로 원인 재현(`-n base` 추가 전/후 비교)한
  뒤, 로컬 NodeVault `nodekit submit`으로 build+push+index 등록까지 전부 성공하는
  것까지 재검증.

**#5/#6/#9는 처음엔 NodeVault 근본 수정이 필요할 것으로 예상했으나, 조사 결과
NodeVault의 `CancelToolBuild`/`WatchToolBuild` 메커니즘 자체는 이미 정상이었고
(#5/#6은 NodeKit의 매핑/호출 누락 버그), #9만 실제로 NodeVault 쪽 원인이 있었다.
#13, #14도 마찬가지로 NodeKit 쪽 버그였다 — NodeVault의 `resolvePackageMirror()`와
빌드 실행 자체는 처음부터 올바르게 동작하고 있었다.**

### 후속 개선: 테스트 스위트 신뢰성

버그 발견 과정에서 기존 opt-in 통합 테스트(`GrpcBuildClientIntegrationTests`,
`GrpcResolveRecipeClientIntegrationTests`)가 env var 미설정 시 조용히 통과 처리되는
것을 확인 — 즉 원래 이 gRPC 경로의 회귀는 유닛 테스트로 전혀 못 잡는 구조였다.
`Assert.Skip()`으로 정직하게 Skipped 표시하도록 고치고, in-process fake gRPC
서버(`GrpcServices=Both` 코드젠 + ASP.NET Core TestServer)를 새로 구축해서
seoy/NodeVault 없이 매 실행마다 자동으로 wire-level 회귀를 잡는 테스트 7개를
추가했다 (commit `461e963`). #5 버그를 코드에서 잠깐 되돌려 이 새 테스트들이
정확히 잡아내는 것도 직접 확인함.

### 이 실행이 완료한 것 / 완료하지 않은 것

- **완료**: Sprint 7 Task 2 / U5-2 이전 사전 검증. TC-1~TC-13 전체, quick-setup
  경로의 **5개 recipe method 전부**(Package/Conda, Mirror/PackageMirror,
  Container/BioContainer, Source/SourceBuild, Dockerfile/DockerfileFallback),
  **Package 방식 내 Micromamba 엔진**, 그리고 **BeginnerGuideFlow(가이드 모드)의
  13개 재입력 루프 전수조사**까지 seoy 없이 완료해서 버그 10건을 찾아 전부 수정했다.
  `nodekit submit`의 happy path, 실패, 취소, base-image-resolve(오픈망/폐쇄망),
  ResolveRecipe 정책 분기(closed_network)와 네트워크 실패 처리까지 검증됨.
  Dockerfile 방식과 Micromamba 엔진은 실제 build+push+index 등록까지
  성공(exit 0)까지 확인했고, 나머지는 의도적으로 존재하지 않는
  이미지/checksum/mirror URI를 써서 빌드 단계까지는 정상 도달한 뒤 예상대로
  실패(코드 버그 아닌 인프라 성격의 실패)하는 것까지 확인. Micromamba 엔진은
  quick-setup 대화형 경로가 아예 없어서(현재는 `--engine micromamba` CLI 플래그
  또는 BeginnerGuideFlow 전용) `--non-interactive` 경로로 검증했고, 이 과정에서
  **모든 Micromamba recipe가 100% 빌드 실패하는 실제 버그(#14)**를 찾아 수정했다 —
  "Conda 엔진과 동일한 경로라 위험이 낮다"는 이전 판단은 틀렸었다. BeginnerGuideFlow
  전수조사(#12)에서는 실제 루프가 12개가 아니라 13개였고(grep 패턴에 안 걸리는
  `while(!subflowDone)` 1개 누락), 그중 안전한 `[Y/n]`/`[y/N]` 확인 프롬프트 2곳을
  제외한 **12곳 전부**가 같은 EOF 버그를 가지고 있었다 — 즉 quick-setup 모드보다
  가이드 모드 쪽이 실질적으로 훨씬 더 취약했다.
- **완료 아님**: seoy 실제 장비에서의 최종 수동 확인(U5-2)은 여전히 별도로 필요하다.
  이 로컬 실행은 K8s 기반 NodeSentinel 검증, 실제 Harbor 인증/웹훅/GC, seoy 네트워크
  조건을 대체하지 않는다 (§1에 이미 명시). Micromamba 엔진의 quick-setup 대화형 진입
  경로 자체가 없다는 점은 별도 UX 공백으로 남아 있다(코드 버그는 아님, 설계 범위 밖).
  Issue #1("쉬운 안내 모드 진입점만 있고 BeginnerGuideFlow 미구현")은 이번 전수조사로
  BeginnerGuideFlow가 실제로 완전히 구현되어 있음이 확인되어 문서가 stale해 보이지만,
  이 이슈를 닫는 것은 이번 작업 범위 밖이라 손대지 않았다.
