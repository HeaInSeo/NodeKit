# NodeKit ↔ NodeVault seoy Smoke Fixtures

Status: fixtures created and locally verified (buildah/render/validate); never run against
live seoy NodeVault
Created: 2026-07-15
Scope: three fixed recipe.json fixtures + exact commands, so future reviews of the
ToolSpec submit path / Sprint 9 policy / digest observability don't have to improvise a
new recipe each time

## 0. 배경

두 번째 적대적 리뷰(2026-07-14/15)의 요청 사항 중 하나: "structured SourceBuild 성공
recipe / legacy SourceBuild 거부 recipe / digest·referrer 확인 recipe, 이 3개를 테스트
fixture 또는 문서화된 smoke 절차로 고정하면 다음 리뷰가 빨라진다." 이 문서와
`docs/fixtures/seoy-smoke/*.json`이 그 고정본이다.

**세 fixture 모두 실제 데이터를 쓴다** — 가짜 checksum이나 존재하지 않는 이미지가
아니라 실제로 fetch/pull 가능한 소스/이미지다. fixture 1(`structured-sourcebuild-success.json`)은
로컬 `buildah bud`로 실제 2-stage 빌드까지 실행해 bwa 바이너리가 정상적으로
컴파일·실행되는 것까지 확인했다(§1 참조). 세 fixture 전부 `nodekit validate`를
통과하는 것도 확인했다. **다만 실제 seoy NodeVault에 제출해본 적은 없다** —
그 부분이 이 문서가 아직 못 채운 gap이고, 다음 seoy 세션에서 채워야 한다.

## 1. Fixture 1 — structured-sourcebuild-success.json

**목적**: `RecipeBuildKind.SourceBuildStructured`(R22-B/C/D)로 만든 2-stage recipe가
NodeVault Sprint 9 정책을 통과하고 실제로 빌드 성공하는지 확인.

**내용**: bwa 0.7.17을 실제 GitHub 소스(`https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz`,
실제 sha256 checksum)에서 받아 큐레이션된 `generic`(buildpack-deps:bookworm) 빌드
프로필로 컴파일하고, `minimal`(debian:bookworm-slim) 런타임 프로필로 복사한다.

**로컬 검증 완료(2026-07-15)**:
```bash
nodekit validate docs/fixtures/seoy-smoke/structured-sourcebuild-success.json   # OK
nodekit render docs/fixtures/seoy-smoke/structured-sourcebuild-success.json --out -
# 렌더링된 Dockerfile을 실제로 buildah bud로 빌드 — 성공 확인:
#   - builder 스테이지에서 bwa가 실제로 컴파일됨(GCC 10+ 환경에서 bwa 0.7.17
#     Makefile이 기본으로 실패하는 것까지 발견 — SourceBuildCommands에
#     CFLAGS="...-fcommon"을 추가해서 우회. 이 값이 이미 fixture에 반영되어 있음)
#   - 최종 이미지에서 curl/gcc 부재 확인(command -v curl/gcc 둘 다 실패)
#   - /usr/local/bin/bwa 실행 시 정상적으로 usage 배너 출력 확인
```

**seoy에서 확인할 것**:
```bash
NODEKIT_NODEVAULT_URL=http://100.123.80.48:50051 \
  nodekit submit docs/fixtures/seoy-smoke/structured-sourcebuild-success.json
```
- exit code 0
- `[성공]` 이벤트까지 도달
- NodeVault Sprint 9 정책(최종 스테이지 RUN risky-tool 정적 검사)에 걸리지 않고
  통과하는지 — 최종 스테이지에 RUN이 없는 2-stage 구조이므로 통과해야 정상이다.
- §3(digest/referrer)도 같이 확인 가능(아무 성공 빌드로나 확인되므로 이 fixture로도
  가능 — 굳이 fixture 3을 따로 안 써도 됨. fixture 3은 소스 빌드 없이 더 빠르게
  같은 걸 확인하고 싶을 때용).

## 2. Fixture 2 — legacy-sourcebuild-rejected.json

**목적**: legacy `RecipeBuildKind.SourceBuild`(단일 스테이지)가 NodeVault Sprint 9
정책에 실제로 걸려 거부되는지 확인 — 적대적 리뷰 Major-1(#41)의 핵심 주장을
seoy에서 실증.

**내용**: fixture 1과 동일한 bwa 0.7.17 소스/checksum이지만, 단일 스테이지
(`BaseImage`가 fetch와 최종 실행 역할을 겸함)로 렌더링된다 — `RUN curl ... && ... &&
make ...`가 전부 하나의(유일한) 스테이지에 있다.

**로컬 검증 완료(2026-07-15)**:
```bash
nodekit validate docs/fixtures/seoy-smoke/legacy-sourcebuild-rejected.json   # OK
# NodeKit L1 자체는 통과한다 — 이건 의도된 것이다(L1은 "항상 틀린 것"만 막고,
# NodeVault 서버 정책 위반 여부는 서버가 최종 판단). stderr에
# SourceBuildBaseImageAdvisor 경고("...거부될 가능성이 매우 높습니다...")가
# 뜨는 것도 이미 확인함(§13 R22-D, #41 Phase 1).
```

**seoy에서 확인할 것**:
```bash
NODEKIT_NODEVAULT_URL=http://100.123.80.48:50051 \
  nodekit submit docs/fixtures/seoy-smoke/legacy-sourcebuild-rejected.json
```
- **거부되어야 정상** — NodeVault의 `ValidateBuildRequest`(Sprint 9 P2a)가 최종
  (유일한) 스테이지의 `RUN`에서 `curl`과 `make`를 발견하고 reject해야 한다.
- 거부 메시지에 `curl` 또는 `make`가 risky tool로 언급되는지, `allow_runtime_tools`
  안내가 포함되는지 확인.
- **만약 이게 거부되지 않고 성공한다면 — NodeVault Sprint 9 정책에 회귀가 생긴
  것이거나, NodeKit 쪽 이해가 틀렸다는 뜻이다. 즉시 이슈로 기록할 것.**

## 3. Fixture 3 — digest-referrer-check.json

**목적**: `WatchToolBuild`가 실제로 `image_ref`/`image_digest`/`spec_referrer_digest`/
`integrity_health`(NodeVault Sprint 7 P1a, NodeKit이 R18 fallback을 대체하며 소비하기
시작한 필드들)를 채워서 돌려주는지 확인 — 가장 빠르게(빌드 없이 이미 존재하는
이미지를 그대로 등록) 확인하기 위한 최소 fixture.

**내용**: `RecipeBuildKind.Container` — 실제 존재하는 공개 이미지
(`docker.io/library/alpine:3.19`, 실제 digest 고정)를 그대로 등록한다. 소스 빌드가
없으므로 fixture 1보다 훨씬 빠르게 끝난다.

**로컬 검증 완료(2026-07-15)**:
```bash
nodekit validate docs/fixtures/seoy-smoke/digest-referrer-check.json   # OK
# 렌더링 결과: "FROM docker.io/library/alpine:3.19@sha256:6baf43...\n" — buildah pull로
# 이 digest가 실제로 존재/pull 가능함을 확인함.
```

**seoy에서 확인할 것**:
```bash
NODEKIT_NODEVAULT_URL=http://100.123.80.48:50051 \
  nodekit submit docs/fixtures/seoy-smoke/digest-referrer-check.json
```
- exit code 0, `[성공]` 도달.
- stdout에 `이미지 digest: <ref>@<digest>` 형태로 표시되는지 확인(SubmitCommand의
  R18 fallback 대체 로직 — `BuildEvent.ImageDigest`가 실제로 채워져서 오는지가
  핵심). 만약 이 줄이 안 뜨고 예전처럼 "이미지 digest가 서버에서 제공되지
  않았습니다" fallback 안내가 뜬다면, NodeVault의 `WatchToolBuild`가 아직
  `image_digest`를 안 채우고 있다는 뜻 — 그 자체로 중요한 발견이니 이슈로 기록.
- (선택) NodeVault index를 직접 조회해 `spec_referrer_digest`/`integrity_health`도
  값이 채워졌는지 확인.

## 4. 세 fixture 실행 순서 제안

1. Fixture 3 (가장 빠름) — 연결/인증/digest observability 먼저 확인.
2. Fixture 2 — Sprint 9 정책이 실제로 살아있는지 확인(거부 확인).
3. Fixture 1 — 전체 파이프라인(2-stage 빌드 + 통과 + digest observability)을
   한 번에 확인.

## 5. 이 문서가 대체하지 않는 것

`docs/NODEKIT_CLI_FIRST_SPRINT_PLAN.md` Sprint 7 Task 2 / U5-2(seoy 원격 장비
`nodekit submit` 수동 테스트, 쉬운 안내/빠른 설정 모드 × 오픈망/폐쇄망 전체 조합)의
최종 완료 기준을 대체하지 않는다. 이 문서는 딱 "R22 + Sprint 9 정책 + digest
observability"라는 좁은 범위를 빠르게 재확인하기 위한 것이고, U5-2는 여전히 별도로
완료해야 한다.
