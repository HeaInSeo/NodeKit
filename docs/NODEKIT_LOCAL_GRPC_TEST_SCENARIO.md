# NodeKit ↔ NodeVault 로컬 gRPC 통합 테스트 시나리오

Status: Draft v0.2 (실행 전, 1차 리뷰 반영 완료)
Created: 2026-07-04
Updated: 2026-07-04
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
