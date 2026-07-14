# NodeKit Legacy-First Sprint Plan

Status: Sprint 6 완료 / Sprint 7 진행 중(Task 1 완료, Task 2 대기) / R18-R21(§13) 완료 / R22-B/C/D(§13) 완료 / 적대적 리뷰 Major-1(#41) 완료, wizard 통합(#42) 완료  
Created: 2026-06-17  
Updated: 2026-07-14  
Scope: NodeKit work — Sprint 0-6 완료, Sprint 7(Post-Migration Hardening) 진행 중,
§13 Live Recipe Reproducibility Improvement(R18-R21) 완료, R22-B/C/D(SourceBuild
구조화 intent authoring 모델 + 2-stage 렌더링 + RuntimeProfile hygiene advisor) 완료,
NodeKit↔NodeVault 적대적 리뷰 Major-1(#41) 및 그 후속 SourceStructured
wizard 통합(#42) 완료

## 0. Resume Note For Agents

Read this document first when resuming NodeKit work.

**Phase 6 완료 (2026-07-02)**: NodeVault Phase 1 gate가 열렸고 NodeKit CLI는 이미
ToolSpec 경로(`ResolveToolSpec → SubmitToolBuild → WatchToolBuild`)로 전환 완료.
`--legacy` 플래그와 `BuildAndRegister` 경로는 CLI에서 제거됨.
`IBuildClient` / `GrpcBuildClient`는 Avalonia(NodeKit.csproj)에만 남아 있음 — Sprint 7 대상.

**§13 완료 (2026-07-09)**: seoy-libvirt-cilium 실 K8s 클러스터 라이브
테스트(2026-07-08)에서 나온 NodeKit 쪽 개선 항목 R18-R21 전부 구현·테스트·
커밋 완료 — 각 스프린트의 Progress 블록 참조. NodeVault 쪽 개선 항목(P0-P3)은
별도 개발 에이전트가 독립 진행 중이라 이 세션에서 다루지 않는다
([[project_nodevault_parallel_agent]] 메모리 참조).

**R22-B 완료 (2026-07-13, 커밋 `d4b1656`)**: `RecipeBuildKind
.SourceBuildStructured` + `RecipeMethodId.SourceStructured`(대화형
wizard 미배선, `--non-interactive --method source-structured`로만
접근), `BuildProfile`/`RuntimeProfile`/`RuntimeDependencies` 필드,
`SourceBuildProfileCatalog`(generic/minimal 프로필, 둘 다 `buildah run`
으로 실제 도구 존재를 로컬에서 확인한 뒤 digest pinning), `RecipeValidator`
새 규칙(L1-RCP-017/018), `RecipeRenderer.RenderSourceBuildStructured`
(§11 Phase B 임시 단일 스테이지 placeholder — #38이 진짜 2-stage로
교체 예정). 상세는 §13 R22-B Progress 블록 참조. Issue #37 close.

**R22-C 완료 (2026-07-13, 커밋 `3117f8a`)**: `RecipeRenderer
.RenderSourceBuildStructured`가 R22-B의 단일 스테이지 placeholder를
실제 2-stage Dockerfile("FROM ... AS builder" → "FROM <runtime>" +
`COPY --from=builder /nodekit/output/ /`)로 교체. 로컬 `buildah bud`로
실제 빌드해 최종 이미지에 curl/wget/git/gcc가 없고(builder 스테이지에는
있음을 대조 확인) 설치한 바이너리가 정상 동작하며 `USER=1000`임을
실증. 상세는 §13 R22-C Progress 블록 참조. Issue #38 close.

**R22-D 진행 대기**: 설계는 2026-07-12에 확정됐고(Issue #36 close),
§13에 Sprint R22-D(#39) 형식으로 Goal/Context/Tasks/Done-when이
등록되어 있음. 상세는 §13 R22-D 섹션과
`docs/NODEKIT_SOURCEBUILD_STRUCTURED_INTENT_DESIGN.md` 참조.
**R22-D 구현 착수 전 반드시 다시 읽을 것 — 놓치기 쉬운 함정:**

```text
R22-B/C 구현만으로 SourceBuild 보안 문제가 "완전히 해결됐다"고 하지
말 것 — NodeVault Sprint 9(2026-07-13 완료)가 최종 스테이지 RUN의
risky tool은 정적으로 거부하지만, base image에 이미 포함된 도구는
Sprint 10(미구현)이 필요하다. "서버 쪽 강제가 전혀 없다"는 표현은
더 이상 정확하지 않음 — §13 하단 체크리스트 항목 1(2026-07-13
갱신), 설계 문서 §2.6 Q5/§8(2026-07-13 갱신) 참조.
```

**적대적 리뷰 Major-1 완료 (2026-07-13/14, Issue #41 close, 커밋
`2e9fdf7`/`f99508a`/`c9944f4`/`c144425`)**: `docs/NODEKIT_NODEVAULT_
ADVERSARIAL_REVIEW_BRIEFING.md` 기반 별도 에이전트 리뷰에서 나온
Major-1 지적(legacy SourceBuild vs NodeVault Sprint 9 정책 충돌) 6개
항목 전부 대응 — `SourceBuildBaseImageAdvisor` 무조건 경고,
proto 재벤더링(`allow_runtime_tools`는 의도적으로 미노출), R18 digest
fallback을 `BuildEvent.image_digest` 등 새 필드 소비로 교체, 문서
표현 narrowing. 이어서 **SourceStructured wizard 통합 완료 (2026-07-14,
Issue #42 close, 커밋 `301ba4d`)**: 대화형 wizard 옵션 6번으로 추가,
`HasSourceArchiveAndChecksum` 신호가 이제 legacy Source 대신
SourceStructured를 추천(legacy는 수동 선택 "4"로 계속 접근 가능).
쉬운 안내 모드(BeginnerGuideFlow)는 의도적으로 범위 밖(#42 참조).

**R22-D 완료 (2026-07-14, Issue #39 close, 커밋 `bf3caca`)**:
`RuntimeProfileHygieneAdvisor` 신설 — SourceBuildStructured의
RuntimeProfile이 advanced일 때만, RuntimeProfileImage가 이름 기준으로
risky(curl 전용/buildpack-deps 등)하면 non-blocking 경고. 상세는 §13
R22-D Progress 블록 참조. **R22(SourceBuild 구조화 intent) 이니셔티브
전체(R22-B/C/D) 완료.**

현재 NodeKit 초점:

```text
R22-B/C/D 완료, 적대적 리뷰 Major-1(#41)/wizard 통합(#42) 완료 →
  다음 후보: Sprint 7 Task 1(Avalonia GUI ToolSpec 마이그레이션) 또는
  U5-2(seoy 수동 테스트) — 사용자 확인 필요
Sprint 7: Avalonia GUI ToolSpec 마이그레이션
+ U5-2: seoy 원격 장비 nodekit submit 수동 테스트 (seoy 준비 후)
  — 2026-07-05: seoy 없이 heain 로컬 사전 검증 완료(TC-1~TC-13 전체), 버그 6건
    발견·수정·close (docs/NODEKIT_LOCAL_GRPC_TEST_SCENARIO.md §7 참조).
    seoy 본 테스트는 아직 미완료.
+ CI 그린 유지
```

## 1. Current Boundary

NodeKit currently owns:

```text
- user-facing authoring in the existing C# / Avalonia app
- local L1 validation before submission
- BuildRequest creation
- BuildAndRegister gRPC client behavior
- local policy/check feedback quality
```

NodeKit does not yet own:

```text
- ToolSpecRequest production submission
- ResolveToolSpec production integration
- SubmitToolBuild integration
- canonical digest calculation
- ResolvedToolSpec generation
- image build
- certification / promotion
- NodeVault Index mutation
```

Future direction remains CLI-first and reactive, but implementation waits for the platform phase that makes the new API real.

## 2. Reactive Implementation Policy

Reactive means event/state oriented C# implementation, not the React JavaScript framework.

When new NodeKit application state is introduced, prefer the same library direction used by DagEdit:

```text
- ReactiveUI
- System.Reactive
- DynamicData where collection change streams are useful
```

For the current legacy phase, use this carefully:

```text
- Do not rewrite the UI just to introduce reactive libraries.
- Prefer focused state objects around validation, submission progress, and result reporting.
- Keep pure validation and serialization code plain and deterministic.
- Do not duplicate validation rules between UI handlers and service classes.
```

## 3. CI, Lint, And Coverage Gate

NodeKit should follow the DagEdit operational bar:

```text
All GitHub Actions CI must be green.
Lint/analyzer checks must be green.
No warning regression is allowed.
NuGet dependency graph must be locked and reproducible.
Known vulnerable packages fail CI.
Security workflows must cover dependency review and CodeQL.
Coverage must not fall below the committed baseline gate.
```

Coverage policy for this phase:

```text
- L1 validation, BuildRequest mapping, and gRPC client mapping should target 90%+ focused coverage.
- Existing UI rendering code is not the first coverage target.
- Overall repository coverage can be lower while legacy UI remains, but changed non-UI logic must be covered.
- Coverage must not decrease for the touched validation/mapping/client areas after the baseline is established.
```

Use Microsoft.Testing.Platform coverage through the xUnit v3 test project:

```bash
dotnet test --solution NodeKit.sln --coverage --coverage-output coverage.cobertura.xml --coverage-output-format cobertura
```

## 4. Sprint Schedule

### Sprint 0. Baseline And Guardrails

Goal:

```text
Make the current legacy path measurable and keep it green.
```

Tasks:

```text
1. Confirm current BuildRequest / BuildAndRegister path compiles and tests pass.
2. Add or repair GitHub Actions verify workflow if missing.
3. Establish local commands for restore, build, test, coverage.
4. Document that ToolSpecRequest migration is blocked on NodeVault Phase 1.
5. Identify current analyzer warnings and decide a no-regression approach.
```

Done when:

```text
- dotnet build NodeKit.sln succeeds.
- dotnet test NodeKit.sln succeeds.
- Coverage artifact is generated.
- Sprint document clearly blocks new ToolSpecRequest path until NodeVault Phase 1.
```

### Sprint 1. L1 Validation Hardening

Goal:

```text
Improve user-side validation without changing the NodeVault API boundary.
```

Tasks:

```text
1. Inventory current L1 validation rules.
2. Add missing required-field validation for existing ToolDefinition inputs.
3. Add validation around Dockerfile/build context fields currently mapped into BuildRequest.
4. Ensure validation messages are actionable and stable.
5. Add focused tests for pass/fail validation cases.
```

Done when:

```text
- Invalid authoring input is rejected before BuildAndRegister.
- Tests cover validation success and failure cases.
- No production ToolSpecRequest path exists.
```

Progress:

```text
- 2026-06-18: validation execution/state moved from MainWindow code-behind into
  UI/ViewModels/ValidationViewModel using ReactiveUI ReactiveObject.
- MainWindow still owns legacy form collection and visual panel updates; new
  validation state should continue moving into reactive ViewModel code.
- 2026-06-18: RequiredFieldsValidator now rejects missing version, missing I/O
  role/format values, invalid I/O shape/class values, and empty command entries
  before legacy BuildAndRegister submission.
- 2026-06-18: DockerfileStructureValidator added for first-instruction FROM,
  dangling line continuation, COPY/ADD arity, build context escape, and remote
  ADD checks before legacy BuildAndRegister submission.
```

### Sprint 2. BuildRequest Mapping Quality

Goal:

```text
Make ToolDefinition -> BuildRequest mapping explicit and well tested.
```

Tasks:

```text
1. Review BuildRequestFactory mapping field by field.
2. Add tests for every field NodeVault currently expects.
3. Confirm environment/spec/context fields are not silently dropped.
4. Keep proto compatibility with the current legacy NodeVault endpoint.
```

Done when:

```text
- BuildRequestFactory has focused coverage.
- Serialization/mapping regressions fail tests.
- BuildAndRegister remains the only production build submission path.
```

### Sprint 3. gRPC Client Resilience

Goal:

```text
Improve BuildAndRegister client behavior and diagnostics.
```

Tasks:

```text
1. Review GrpcBuildClient timeout/cancellation behavior.
2. Improve error messages for connection, auth, and stream failures.
3. Add tests around mapping from gRPC stream events to NodeKit state.
4. Keep live NodeVault integration opt-in.
```

Done when:

```text
- Cancellation and failure paths are deterministic.
- Local tests do not require a live NodeVault.
- Remote NodeVault assumptions are not hard-coded.
```

### Sprint 4. Remote Environment Readiness

Goal:

```text
Prepare for NodeVault running as a remote Kubernetes data-plane app.
```

Tasks:

```text
1. Document that live connection details must be discovered from ~/.config/infra-lab.
2. Add opt-in integration-test configuration.
3. Do not assume NodeVault is localhost.
4. Do not require Kubernetes API access from NodeKit.
5. Treat NodeVault and related platform services as remote-infrastructure-tested by default.
```

Done when:

```text
- Live integration is configurable and skipped by default.
- NodeKit remains only a gRPC client.
```

### Sprint 5. Legacy UX Quality

Goal:

```text
Improve the existing authoring experience without changing the protocol.
```

Tasks:

```text
1. Surface L1 validation results clearly.
2. Prevent submission while local validation is failing.
3. Improve build event/progress rendering.
4. Keep UI changes small and covered where logic is extracted.
```

Done when:

```text
- The current UI sends fewer malformed BuildRequests.
- Build progress/error output is easier to diagnose.
```

### Sprint 6. ToolSpecRequest Migration Gate ✓ (2026-07-02 완료)

Goal:

```text
Start migration only after NodeVault Phase 1 is actually complete.
```

Entry criteria — 모두 충족됨 (2026-07-02):

```text
✓ NodeVault has canonical ResolveToolSpec implementation.
✓ NodeVault has SubmitToolBuild API.
✓ PLATFORM_SCHEDULE.md Phase 6 has begun.
✓ NodeVault proto/API is stable enough to vendor (protos/ 디렉터리에 벤더링됨).
```

Tasks — 모두 완료:

```text
✓ 1. Vendor the stable NodeVault proto. (protos/nodevault/v1/nodevault.proto)
✓ 2. Add ToolSpecRequest authoring models. (GrpcToolSpecClient, IToolSpecBuildClient)
✓ 3. Add CLI-first path. (nodekit submit → ResolveToolSpec → SubmitToolBuild → WatchToolBuild)
✓ 4. Add ResolveToolSpec client. (GrpcToolSpecClient.ResolveAndBuildAsync Step 1)
✓ 5. Add SubmitToolBuild client. (GrpcToolSpecClient.ResolveAndBuildAsync Step 2)
✓ 6. Remove legacy BuildRequest path from CLI. (--legacy 플래그 + BuildAndRegister 제거)
```

### Sprint 7. Post-Migration Hardening (진행 중)

Goal:

```text
Make the new path reliable enough to replace all legacy usage.
```

Tasks:

```text
✓ 1. Avalonia GUI(NodeKit.csproj)를 IBuildClient/GrpcBuildClient에서 GrpcToolSpecClient로 전환.
     IBuildClient / GrpcBuildClient는 완전히 제거됨.
○ 2. U5-2: seoy 원격 장비에서 nodekit submit 수동 테스트 통과.
✓ 3. NodeVault 측 BuildAndRegister RPC deprecated 표시 (NodeVault 담당, 확인 완료).
```

Done when:

```text
- Avalonia GUI도 ToolSpec 경로로 전환 완료.
- CLI end-to-end 수동 테스트 통과 (seoy 장비).
- IBuildClient / GrpcBuildClient 완전 제거 또는 명시적 ADR 후 유지 결정.
```

**Progress (Task 1 완료, 2026-07-14, 커밋 `f7f34b1`/`9ccda57`, Issue #44):**

```text
완료:
- GrpcToolSpecClient/IToolSpecBuildClient를 src/NodeKit.Cli/(NodeKit.Cli
  네임스페이스, Avalonia 프로젝트에서 안 보임 — NodeKit.csproj가
  src/NodeKit.Cli/**를 명시적으로 제외)에서 src/Grpc/(NodeKit.Grpc
  네임스페이스, CLI/GUI 공유 위치)로 이동.
- SubmitCommand의 private BuildRawSpec을 공유 ToolSpecRawSpecFactory로
  추출 — raw_spec JSON 형태는 CLI/GUI 두 제출 경로가 반드시 동일해야
  하는 wire 계약이라 중복 시 조용히 갈라질 위험이 있었음.
- UI/ViewModels/BuildSubmissionViewModel.cs(신규, ReactiveObject) —
  GrpcToolSpecClient 수명, in-flight build ID 추적, best-effort 서버
  취소를 전담. MainWindow 코드-비하인드에 직접 추가하지 않고
  ViewModel로 분리한 이유: 이 저장소의 기존 방향(DagEdit 스타일
  ReactiveUI/System.Reactive, 코드-비하인드 확장 금지)과
  ReactiveArchitectureGuardTests의 줄 수 baseline(667줄) — 코드-비하인드에
  직접 추가했다면 746줄로 baseline을 초과했을 것, 분리 후 660줄.
- 단순 줄 수 회피가 아니라 실제로 필요한 분리였음: WatchToolBuild는
  관찰용 스트림이라, legacy BuildAndRegister(취소 = 곧 서버 빌드 중단)와
  달리 클라이언트 취소만으로는 서버 빌드가 멈추지 않는다. 새 빌드가
  이전 빌드를 대체하거나 창을 닫을 때 CancelBuildAsync를 best-effort로
  호출하도록 새로 추가(CLI의 SubmitCommand.CancelServerBuildBestEffort와
  동일 패턴).
- HandleBuildEvent가 WatchToolBuild 고유 필드(ImageRef/ImageDigest)도
  표시하도록 확장 — CLI의 R18 digest fallback 교체와 같은 이유(legacy
  DigestAcquired/Digest는 WatchToolBuild에서 절대 안 옴).
- IBuildClient/GrpcBuildClient 완전 제거(그 테스트 2개 파일도 함께 제거) —
  ADR 없이 제거 쪽으로 결정, CLI가 Phase 6에서 이미 같은 선택을 한 전례를
  따름.
- 612/612 통과(옵트인 NodeVault 통합 테스트 1개 스킵), 0 warnings,
  dotnet format 클린.

**알려진 한계**: 이 환경에는 디스플레이 서버가 없어(XOpenDisplay 실패)
실제 GUI를 실행/스모크테스트하지 못했고, 이 저장소에는 headless Avalonia
테스트 하네스도 없다. 빌드 경고 0개/테스트 전체 통과/모든 변경 지점 수동
코드 리뷰로 대신 검증함 — 실제 GUI 라이브 검증은 아직 안 된 상태로 남아
있음. Issue #44에 기록.
```

**Progress (Task 2 / U5-2 사전 검증, 2026-07-05, 2차 실행까지 반영):**

```text
seoy 장비 없이 heain에서 nodekit submit 전체 경로(gRPC 프로토콜, 실제 podbridge5
rootless build, base image digest 조회, ResolveRecipe 정책 분기, 취소)를 로컬
NodeVault + 로컬 OCI 레지스트리로 사전 검증함 — TC-1~TC-13 전부 실행 완료.
상세 시나리오/실행 결과는 docs/NODEKIT_LOCAL_GRPC_TEST_SCENARIO.md §7 참조.

발견 및 수정된 버그 6건 (GitHub Issue #5-#10 전부 close):
- #5 nodekit submit이 빌드 실패 시에도 exit code 0 반환 (NodeKit bd9786e)
- #6 Ctrl-C 취소가 서버 빌드를 실제로 멈추지 않음 (NodeKit bd9786e)
- #7 library/ 네임스페이스 미보정으로 공식 Docker Hub 이미지 401 (NodeKit 73805d4)
- #8 개인키 없는 CA cert 로딩 시 크래시 (NodeKit a938690)
- #9 ResolveRecipe가 실제 네트워크 실패를 candidates=0으로 조용히 성공 처리
  (NodeVault 605a98d + NodeKit 1749a58)
- #10 recipe create가 stdin EOF 시 무한 루프(CPU 100%, 수백MB 로그) (NodeKit f1b5b37)

#5/#6/#9는 처음엔 NodeVault 근본 수정이 필요할 것으로 예상했으나, 조사 결과
CancelToolBuild/WatchToolBuild 메커니즘 자체는 이미 정상이었고 (#5/#6은 NodeKit의
매핑/호출 누락), #9만 실제로 NodeVault 쪽 원인이 있었다.

부가 성과: opt-in 통합 테스트의 vacuous pass 문제를 발견해 Assert.Skip()으로 수정하고,
in-process fake gRPC 서버(GrpcServices=Both + ASP.NET Core TestServer)를 구축해
seoy 없이도 매 테스트 실행마다 자동으로 이 gRPC 경로의 wire-level 회귀를 잡도록
개선함 (commit 461e963).

**주의**: 이 사전 검증은 seoy 실제 장비 수동 테스트(U5-2 본 항목)를 대체하지 않는다.
K8s 기반 NodeSentinel 검증(L3/L4/L5), 실제 Harbor 인증/웹훅/GC, seoy 네트워크
조건은 여전히 seoy에서 별도 확인 필요 — Task 2는 여전히 미완료(○) 상태로 유지.
```

## 5. Immediate First Slice

Start here:

```text
1. Do not add new ToolSpecRequest production code.
2. Verify existing BuildRequest / BuildAndRegister build and tests.
3. Add or repair CI workflow.
4. Improve L1 validation tests.
5. Improve BuildRequestFactory tests.
6. Add coverage collection and document the baseline.
```

## 6. Baseline Snapshot

Captured: 2026-07-02 (업데이트)

NodeVault observation:

```text
- NodeVault Phase 1 완료: ResolveToolSpec / SubmitToolBuild / WatchToolBuild API 사용 가능.
- BuildAndRegister RPC는 NodeVault에 남아 있지만 NodeKit CLI에서는 제거됨.
- proto는 protos/nodevault/v1/nodevault.proto 로 벤더링됨 (git tracked).
- NodeKit CLI는 GrpcToolSpecClient 경유 3단계 경로만 사용.
```

Local CI-equivalent commands:

```bash
dotnet restore NodeKit.sln --locked-mode
./scripts/ci-audit-packages.sh
dotnet format NodeKit.sln --no-restore --verify-no-changes --verbosity minimal
dotnet build NodeKit.sln --no-restore --configuration Release /p:TreatWarningsAsErrors=true /p:EnforceCodeStyleInBuild=true
dotnet test --solution NodeKit.sln --no-build --configuration Release --results-directory TestResults --coverage --coverage-output coverage.cobertura.xml --coverage-output-format cobertura
./scripts/ci-check-coverage.sh
```

Latest local result (2026-07-02):

```text
- Locked restore: pass
- Package audit: pass
- Format: pass (dotnet format --verify-no-changes exit 0)
- Build: pass, 0 warnings, 0 errors
- Tests: pass, 461 passed, 0 failed, 0 skipped
- Coverage threshold: pass
- Coverage artifact generated under TestResults/coverage.cobertura.xml
```

CI workflow:

```text
.github/workflows/verify.yml runs locked restore, NuGet package audit, format,
warnings-as-errors build, coverage test, and coverage threshold on main branch
pushes / pull requests.
.github/workflows/dependency-review.yml blocks vulnerable or denied-license
dependency changes on pull requests.
.github/workflows/codeql.yml runs CodeQL C# security-and-quality analysis on
pushes, pull requests, and a weekly schedule.
```

## 7. Non-Goals (불변 / 영구)

NodeVault Phase 1 완료 후에도 NodeKit이 절대 하지 않는 것:

```text
- No local authoritative canonical digest calculation.
- No NodeKit image build logic (docker/buildah/buildkit 실행 금지).
- No Kubernetes API calls from NodeKit.
- No rootless/Buildah handling in NodeKit.
- No NodeVault index/catalog mutation from NodeKit.
```

Phase 1 이전 non-goal이었으나 현재 완료된 항목 (참고용):

```text
✓ ToolSpecRequest CLI 경로 — 완료 (GrpcToolSpecClient)
✓ ResolveToolSpec 클라이언트 경로 — 완료
✓ SubmitToolBuild 클라이언트 경로 — 완료
```

## 8. Handoff Note

The correct instruction for a NodeKit agent is:

```text
NodeKit CLI는 Phase 6 완료 (2026-07-02)로 ToolSpec 경로로 전환됨.
BuildAndRegister / legacy 경로는 CLI에서 제거됨.
다음 단계: Avalonia GUI(NodeKit.csproj)의 IBuildClient → GrpcToolSpecClient 전환 (Sprint 7).
재현성 규칙(latest 태그 금지, digest 필수, 패키지 버전 고정)은 여전히 불변.
NodeVault 경계(이미지 빌드, K8s API, 인덱스 뮤테이션)도 여전히 불변.
```

## 9. Recipe Authoring Boundary And CLI Command Naming (2026-06-21 design intent)

A first-pass draft filling in the `raw_spec` schema per variant, the concrete
CLI command interface, and the `src/NodeKit.Cli/` scope left open below is in
[`docs/NODEKIT_CLI_RECIPE_SPEC_DRAFT.md`](NODEKIT_CLI_RECIPE_SPEC_DRAFT.md) —
marked DRAFT, pending review, not yet adopted.

"NodeKit does not build images" does not mean "NodeKit does not choose what
recipe a tool uses." This section records a design decision about what NodeKit
is allowed to own. It does not move the Sprint 6 entry criteria in Section 4.

Boundary, restated:

```text
NodeKit owns:
- recipe variant selection (the user's choice of how a tool gets built)
- collecting the inputs that variant needs (e.g. a Dockerfile, a conda env spec)
- normalizing/validating that input as a raw_spec payload
- rendering a ToolSpecRequest / BuildRequest from it
- L1 validation
- exporting the rendered request to a file
- submitting the request once Sprint 6 entry criteria are met

NodeKit does not own:
- executing docker / buildah / buildkit build
- pushing to a registry
- recording a canonical image digest
- ResolvedToolSpec generation
- NodeVault index/catalog mutation
```

This matches the live NodeVault contract: `ToolSpecRequest.raw_spec`
(`protos/nodevault/v1/nodevault.proto`) is an opaque string. NodeVault's
`pkg/resolve` only computes digests from it and recognizes
`base_image`/`base_image_uri`/`image_uri` keys for pin checking. No
recipe-variant schema exists on the NodeVault side — defining the variant list
is NodeKit authoring-layer scope.

Recipe variants under consideration:

```text
1. conda / bioconda / conda-forge
2. micromamba
3. existing BioContainer
4. source build
5. local package mirror
6. Dockerfile fallback
```

Command naming, chosen to avoid implying NodeKit executes builds or owns
NodeVault-side resolve:

```text
nodekit recipe select   - choose a recipe variant
nodekit spec render     - render a ToolSpecRequest / BuildRequest from collected input
nodekit request export  - write the rendered request to a file (no network call)
nodekit validate        - run L1 validation
nodekit submit          - submit to NodeVault (Sprint 6 gate only)
```

Avoid `nodekit build`: it reads as "NodeKit builds the image locally," which is
the one thing this layer must never do. If a `build` alias ships later for UX
reasons, its documented meaning must be "submit a build request," not "build
locally."

What can start now vs. what stays gated:

```text
Can start now (touches nothing in NodeVault):
- recipe variant selection UX/CLI scaffolding
- collecting Dockerfile / conda spec / etc. input per variant
- L1 validation of that input
- request export to a local file

Stays gated behind Sprint 6 entry criteria (Section 4):
- nodekit submit, or any network call that sends ToolSpecRequest to NodeVault
- any client-side ResolveToolSpec or SubmitToolBuild call
```

### 9.1 한국어 요약

"NodeKit이 이미지를 직접 빌드하지 않는다"는 말이 "NodeKit이 빌드 레시피 선택도
하지 않는다"는 뜻은 아니다. 이 섹션은 NodeKit이 가질 수 있는 권한 범위에 대한
설계 결정을 기록한 것이며, 4번 섹션의 Sprint 6 진입 조건 자체를 바꾸지 않는다.

경계 재정리:

```text
NodeKit이 담당하는 영역:
- recipe variant 선택 (사용자가 어떤 방식으로 도구를 빌드할지 고르는 것)
- 해당 variant에 필요한 입력 수집 (예: Dockerfile, conda env spec)
- 그 입력을 raw_spec payload로 정규화/검증
- 이를 바탕으로 ToolSpecRequest / BuildRequest 렌더링
- L1 validation
- 렌더링된 request를 파일로 export
- Sprint 6 진입 조건이 충족된 후 request 제출(submit)

NodeKit이 담당하지 않는 영역:
- docker / buildah / buildkit build 실행
- registry push
- canonical image digest 기록
- ResolvedToolSpec 생성
- NodeVault index/catalog 변경
```

이는 실제 NodeVault 계약과 일치한다: `ToolSpecRequest.raw_spec`
(`protos/nodevault/v1/nodevault.proto`)은 opaque한 문자열이고, NodeVault의
`pkg/resolve`는 거기서 digest만 계산하며 `base_image`/`base_image_uri`/
`image_uri` 키만 pin 여부 확인용으로 인식한다. NodeVault 쪽에는 recipe-variant
스키마가 존재하지 않으므로, variant 목록을 정의하는 것은 NodeKit authoring
레이어의 범위다.

검토 중인 recipe variant:

```text
1. conda / bioconda / conda-forge
2. micromamba
3. existing BioContainer
4. source build
5. local package mirror
6. Dockerfile fallback
```

명령 이름 (NodeKit이 빌드를 실행하거나 NodeVault의 resolve 권한을 가진 것처럼
보이지 않도록 선택):

```text
nodekit recipe select   - recipe variant 선택
nodekit spec render     - 수집한 입력으로 ToolSpecRequest / BuildRequest 렌더링
nodekit request export  - 렌더링된 request를 파일로 저장 (네트워크 호출 없음)
nodekit validate        - L1 검증 실행
nodekit submit          - NodeVault에 제출 (Sprint 6 게이트 이후에만)
```

`nodekit build`는 피한다: "NodeKit이 로컬에서 이미지를 빌드한다"로 읽혀, 이 계층이
절대 해서는 안 되는 바로 그 일을 암시하기 때문이다. 추후 UX상 `build`라는
별칭(alias)을 쓰더라도, 그 의미는 "빌드 request를 제출한다"여야 하며 "로컬에서
빌드한다"가 되어서는 안 된다.

지금 시작 가능한 것 vs. 게이트가 풀려야 하는 것:

```text
지금 시작 가능 (NodeVault를 전혀 건드리지 않음):
- recipe variant 선택 UX/CLI 골격
- variant별 Dockerfile / conda spec 등 입력 수집
- 그 입력에 대한 L1 검증
- request를 로컬 파일로 export

Sprint 6 진입 조건(4번 섹션)이 충족되기 전까지 보류:
- nodekit submit, 또는 ToolSpecRequest를 NodeVault로 보내는 모든 네트워크 호출
- 클라이언트 측 ResolveToolSpec / SubmitToolBuild 호출
```

## 10. Recipe Authoring Session Implementation Sprints (2026-06-24)

Design source:
[`docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md`](NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md)
v0.8 (§32 implementation order, §33 v1 scope, §19.3
authoring-requirement-vs-L1-policy boundary).

This track is the "Can start now" half of Section 9 — it does not move the
Sprint 6 entry criteria and does not touch NodeVault. It is independent of
the BuildRequest/ToolSpecRequest migration gate.

Today's shipped state, for calibration: `src/Authoring/Recipes/` already has
`RecipeDocument`, `RecipeRenderer`, `RecipeVariant`; `src/Validation/Recipes/`
already has `RecipeValidator`; `src/NodeKit.Cli/CliApp.cs` already has
`recipe validate` / `recipe render`. None of `RecipeMethodId`,
`RecipeFieldRequirement`, `RecipeFieldCatalog`, `RecipeMethodCatalog`,
`RecipeMethodRecommender`, `RecipeBuildKindResolver`, or
`RecipeAuthoringSession` exist yet — Sprints R0-R7 are additive on top of the
existing legacy `validate`/`render` path, not a replacement of it.

### Sprint R0. RecipeVariant -> RecipeBuildKind Rename

Goal:

```text
Apply the already-decided rename in code before any new type is added.
```

Tasks:

```text
1. Rename RecipeVariant to RecipeBuildKind across src/Authoring/Recipes,
   src/Validation/Recipes, and all referencing tests.
2. Touch nothing else in the same commit.
```

Done when:

```text
- Build succeeds with 0 new warnings.
- Existing tests pass unchanged (mechanical rename; no new tests required
  per Section 9 of CLAUDE.md's validation-responsibility table).
```

### Sprint R1. Method And Field-Requirement Type Skeleton

Goal:

```text
Add the two foundational enums the rest of the design depends on.
```

Tasks:

```text
1. Add RecipeMethodId (Container, Package, Mirror, Source, Dockerfile).
2. Add RecipeFieldRequirement (Required, Defaulted, Optional, Recommended).
```

Done when:

```text
- Both enums compile; neither is referenced by existing code yet.
- No behavior change to the legacy validate/render path.
```

### Sprint R2. Field Catalog And Shared Validation Pipeline

Goal:

```text
Establish the field-requirement data model and a single validation entry
point shared by validate, render, and the future recipe create.
```

Tasks:

```text
1. Implement RecipeFieldCatalog keyed by RecipeMethodId + RecipeFieldRequirement.
2. Implement the field composition contract: common scalar fields + method
   fields + Inputs/Outputs (Inputs/Outputs are Required for every method).
3. Extract RecipeValidationPipeline as the single L1 gate shared by
   validate/render today and recipe create later.
4. Apply the v0.8 rule directly: any field CLAUDE.md Section 3 blocks at L1
   (unpinned tag, missing digest, unpinned package version) must be
   RecipeFieldRequirement.Required, never Recommended/Optional.
```

Done when:

```text
- RecipeFieldCatalog tests cover FieldsFor ordering and the Inputs/Outputs
  always-Required invariant.
- Existing validate/render tests still pass after the pipeline extraction.
```

### Sprint R3. Method Recommendation Engine

Goal:

```text
Implement the beginner-facing method recommender.
```

Tasks:

```text
1. Implement RecipeMethodCatalog (per-method description/prep/warning text).
2. Implement RecipeMethodQuestionCatalog (fixed question order).
3. Implement RecipeMethodRecommender: the IsRestrictedNetwork top-level gate,
   the priority table, and the Answer tri-state (Yes/No/Unknown) where
   Unknown is neither evidence for nor against a method.
```

Done when:

```text
- All recommender test scenarios from the design's test plan pass, including
  internal-network Yes/Unknown/No combinations and Unknown-heavy answers.
```

### Sprint R4. Build-Kind Resolution, Presets, Normalization

Goal:

```text
Wire method selection to the existing RecipeBuildKind model and add the
input/output convenience layer.
```

Tasks:

```text
1. Implement RecipeBuildKindResolver.Resolve(RecipeMethodId, RecipeDocument),
   guarded to throw if called before Defaulted fields are applied (e.g.
   PackageEngine empty for RecipeMethodId.Package).
2. Implement InputOutputPresetCatalog (FASTQ/BAM/VCF presets).
3. Implement format/role/channel normalization (.gz suffix detection,
   snake_case, choice-first selection).
```

Done when:

```text
- Resolver tests cover both the pre-Build() guard violation and the
  Package -> Conda/Micromamba branch.
- Preset/normalization tests match the design's worked examples.
```

### Sprint R5. RecipeAuthoringSession Core

Goal:

```text
Implement the authoring session state machine and its Snapshot/Build/
ValidateDraft contracts.
```

Tasks:

```text
1. Implement RecipeAuthoringSession: SelectMethod, NextField, SetField,
   AppendListItem, CompleteListField, SkipOptionalField, IsComplete.
2. Implement Snapshot() (works on incomplete sessions, never applies
   Defaulted values, never validates) and Build() (requires IsComplete,
   throws otherwise, sole place Defaulted values are applied).
3. Implement ValidateDraft() as authoring-level only: it must never call
   RecipeBuildKindResolver, RecipeRenderer, or the L1 validator chain.
```

Done when:

```text
- A guard test proves Build() before IsComplete throws.
- A guard test proves ValidateDraft() does not invoke the resolver, renderer,
  or L1 chain even when the draft happens to be field-complete.
- A test proves the Section 19.3 invariant end to end: a recipe that
  RecipeAuthoringSession produces does not fail nodekit validate afterward
  (in particular for the Required ImageDigest field, see the v0.8 patch).
```

### Sprint R6. ChangeMethod, Recovery, List Editing

Goal:

```text
Implement the parts of the session that handle revision and recovery, not
just forward progress.
```

Tasks:

```text
1. Implement PreviewMethodChange / ChangeMethod: atomically discard
   incompatible method-specific fields, reset method-specific session
   metadata (DockerfileWarningAccepted, ImageTagWarningShown/Accepted), and
   mark shared Inputs/Outputs fields invalidated rather than discarding them.
2. Implement the invalidated-field lifecycle (mark on ChangeMethod, clear on
   re-confirm / edit / preset-reselect / discard).
3. Implement RecipeValidationRecoveryPlan / RecoveryActionKind, built from
   final-validation failures while keeping the session alive.
4. Implement per-item list field editing (edit/add/delete on an existing
   list field, not just append).
```

Done when:

```text
- ChangeMethod tests cover field discard, metadata reset, and the
  invalidated (not discarded) treatment of shared fields.
- RecoveryPlan tests cover at least one Required-field failure and one L1
  policy failure (e.g. unpinned digest) producing distinct recovery actions.
```

### Sprint R7. CLI Wiring, Non-Interactive Mode, Golden Tests

Goal:

```text
Connect the session to a real CLI command and lock in the beginner scenarios.
```

Tasks:

```text
1. Wire nodekit recipe create to RecipeAuthoringSession (interactive wizard).
2. Add non-interactive flags (--method, --engine,
   --accept-dockerfile-warning, etc.) covering the same field set.
3. Add golden transcript tests for the design's beginner scenarios.
4. Keep nodekit validate / nodekit render as-is; recipe create only adds an
   authoring front end that ends in the same RecipeValidationPipeline call.
```

Done when:

```text
- Interactive and non-interactive paths produce identical RecipeDocument
  output for the same logical answers.
- Golden transcript tests pass and are reviewed as fixtures, not snapshots
  that auto-update.
```

### 10.1 Sequencing Note

```text
R0 and R1 are mechanical and can be one sitting each.
R2 is required before R3-R6 (field catalog underlies everything).
R3 and R4 can be reordered relative to each other but both must precede R5.
R5 is the highest-risk sprint: the Snapshot/Build/ValidateDraft contract is
where a quiet regression would silently reopen the ImageDigest-class
conflict this plan exists to prevent (see Section 19.3 of the design doc).
R6 depends on R5. R7 depends on everything above being stable; it is an
integration sprint, not a place to discover new design questions.
There is little parallelism available — the dependency chain is mostly linear.
```

### 10.2 한국어 요약

```text
이 트랙(R0-R7)은 9번 섹션의 "지금 시작 가능" 절반에 해당하며, Sprint 6
진입 조건을 옮기지 않고 NodeVault를 건드리지 않는다.

R0: RecipeVariant -> RecipeBuildKind 리네임 (기계적, 단독 커밋)
R1: RecipeMethodId / RecipeFieldRequirement 타입 골격
R2: RecipeFieldCatalog + RecipeValidationPipeline 공유화
    (CLAUDE.md 3번 섹션 L1 하드룰 대상 필드는 전부 Required로)
R3: RecipeMethodCatalog / RecipeMethodQuestionCatalog / RecipeMethodRecommender
R4: RecipeBuildKindResolver + InputOutputPresetCatalog + 정규화
R5: RecipeAuthoringSession 본체 (Snapshot/Build/ValidateDraft 계약) —
    위험도 가장 높음. ImageDigest류 충돌이 조용히 재발하지 않는지
    end-to-end로 검증해야 함 (설계 문서 19.3절).
R6: ChangeMethod / invalidated field / RecoveryPlan / 리스트 편집
R7: CLI 와이어링(recipe create) + non-interactive 모드 + golden transcript 테스트

순서는 거의 선형이다: R0,R1 -> R2 -> (R3,R4 순서 교환 가능) -> R5 -> R6 -> R7.
```

## 11. Recipe Authoring UX v0.9.2 Implementation Sprints (2026-06-25)

Design source:
[`docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_V0.9.2.md`](NODEKIT_CLI_RECIPE_AUTHORING_UX_V0.9.2.md)
("Freeze Candidate"). This document supersedes the v0.8 beginner design doc
cited in Section 10 for `recipe create`'s first-entry UX. Section 10's R0-R7
sprints are complete and shipped (`recipe create` today drives
`RecipeAuthoringSession` / `RecipeFieldCatalog` / `RecipeMethodRecommender`
end to end; documented in `docs/NODEKIT_CLI_USAGE.md`). This track extends
that base — it does not redo it.

This track stays inside the same boundary as Sections 9/10: no NodeVault
submission, no image build, no MCP server implementation. It changes only
the `recipe create` entry UX, escape-hatch commands, Ctrl+C handling, and
non-interactive documentation accuracy.

### Sprint R8. Naming And Terminology Alignment

Goal:

```text
Make the v0.9.2 design doc's terminology match the shipped code before any
new component is built on top of it.
```

Tasks:

```text
1. In NODEKIT_CLI_RECIPE_AUTHORING_UX_V0.9.2.md, replace RecipeDraft /
   RecipeSession / RecipeCreateSession references with the actual class name
   RecipeAuthoringSession (Sections 4.5, 24.1, 24.2).
2. Update Section 23.3 / 29.4 from hedged "Command may or may not be a list
   field" language to a confirmed statement: Command is
   RecipeFieldType.StringList in RecipeFieldCatalog today, so
   IsListType(Command) == true.
3. No production code changes in this sprint; documentation only.
```

Done when:

```text
- grep for "RecipeDraft|RecipeCreateSession" in the v0.9.2 doc returns no
  hits.
- Section 23.3 states the Command list-type fact directly, not as an open
  question.
```

Progress:

```text
- 2026-06-25: Completed. All RecipeDraft / RecipeSession / RecipeCreateSession
  references in NODEKIT_CLI_RECIPE_AUTHORING_UX_V0.9.2.md replaced with
  RecipeAuthoringSession (Sections 4.5, 5, 10.4, 10.6, 24.1, 24.2). Confirmed
  via src/Authoring/Recipes/RecipeFieldCatalog.cs that Command is
  RecipeFieldType.StringList (Optional); updated Sections 20.1, 23.3, 29.4,
  the implementation checklist (Section 32), and the conclusion (Section 33)
  from hedged language to a confirmed statement, and added Command to the
  v0.9.2-guaranteed repeatable list-field set alongside Packages, Channels,
  SourceBuildCommands, BuildDependencies. No production code touched.
```

### Sprint R9. Phase 1-A — Prompt Command Escape Hatches

Goal:

```text
Let users exit or inspect an in-progress recipe create session at any major
prompt, without touching method-recommendation or field-catalog logic.
```

Tasks:

```text
1. Implement PromptCommandHandler recognizing /help, /review, /cancel,
   /quit, /exit at every major input prompt (existing /change-method stays
   as-is; do not change its behavior in this sprint).
2. Implement /review: render current RecipeAuthoringSession field state,
   explicitly marking unset fields as not-yet-entered.
3. Implement /cancel (and /quit, /exit as aliases): confirm, then exit
   without writing a file.
4. Add RecipeCreateResultKind.Cancelled (or RecipeCancelledException — pick
   one per Section 18.4 of the design doc) and map it to process exit
   code 130.
```

Done when:

```text
- Cancelling at any prompt produces no recipe.json on disk.
- Exit code is 130 on cancellation, distinct from the existing 0/1/2 codes.
- /review output matches the design doc's "아직 입력 안 함" convention for
  unset fields.
- Existing recipe create interactive tests still pass unchanged.
```

Progress:

```text
- 2026-06-25: Completed. Added /review, /cancel, /quit, /exit checks to the
  same four prompt-loop insertion points where /help and /change-method
  already lived in RecipeCreateInteractiveRunner.cs (PromptScalarField,
  PromptChoiceField, PromptStringListField, PromptPresetListField) — not to
  the recommender Q&A or method-selection prompts, consistent with this
  sprint's "without touching method-recommendation... logic" goal.
  Cancellation is represented by a new RecipeCreateCancelledException
  (src/NodeKit.Cli/RecipeCreateCancelledException.cs), chosen over
  RecipeCreateResultKind.Cancelled because every prompt-loop method here is
  void — an exception propagates cancellation to Run's single catch site
  without threading a new result type through every call site. Run() catches
  it, prints the Section 17.3 cancellation message, and returns 130 (no
  stack trace, no recipe.json write). /review renders RecipeAuthoringSession.
  Snapshot() against RecipeFieldCatalog.FieldsFor(method), marking any field
  absent from Snapshot().Values as "아직 입력 안 함" per Section 17.4. Added 5
  tests to RecipeCreateInteractiveTests.cs (/review display, /cancel
  declined-continues, /cancel + /quit + /exit confirmed-exits-130). Full
  suite: 319/319 passing (36 NodeKit.Cli.Tests + 283 NodeKit.Tests), 0 build
  warnings.
```

### Sprint R10. Phase 1-B — Ctrl+C Signal Cancellation

Goal:

```text
Map process-level Ctrl+C onto the same cancellation result as /cancel,
through a testable abstraction (no real signal in unit tests).
```

Tasks:

```text
1. Introduce an IConsoleCancellation (or equivalent) seam so
   Console.CancelKeyPress is not called directly from logic under test.
2. Wire Console.CancelKeyPress -> cancellation flag/CancellationToken ->
   the same Cancelled result path Sprint R9 introduced.
3. Suppress stack traces on Ctrl+C; print the same cancellation message as
   /cancel.
4. Add tests that inject a fake cancellation source and assert: no file
   written, exit code 130, no stack trace, message matches /cancel's.
```

Done when:

```text
- Ctrl+C and /cancel produce identical observable outcomes (file absence,
  exit code, message) under test.
- No new compiler warnings (CLAUDE.md Section 8).
```

Progress:

```text
- 2026-06-25: Completed. Added IRecipeCreateCancellationSource
  (src/NodeKit.Cli/IRecipeCreateCancellationSource.cs), a single-member
  `bool IsCancellationRequested { get; }` seam per Section 18.5, and its
  production implementation ConsoleCancelKeyCancellationSource
  (src/NodeKit.Cli/ConsoleCancelKeyCancellationSource.cs), which subscribes
  to Console.CancelKeyPress, sets e.Cancel = true so the process survives
  the signal, and latches a volatile flag. RecipeCreateCancelledException
  from Sprint R9 is reused (not replaced) for the Ctrl+C path, keeping a
  single cancellation representation. RecipeCreateInteractiveRunner.Run was
  split into the public 5-arg overload (constructs a real
  ConsoleCancelKeyCancellationSource) and an internal 6-arg overload taking
  IRecipeCreateCancellationSource, reachable from tests via
  InternalsVisibleTo. The cancellation parameter was threaded through
  RunFieldLoop, PromptField, RunRecoveryLoop, ReEditField, and a check
  (`if (cancellation.IsCancellationRequested) throw new
  RecipeCreateCancelledException();`) was added at the top of the same four
  prompt-loop insertion points Sprint R9 used (PromptScalarField,
  PromptChoiceField, PromptStringListField, PromptPresetListField) — not at
  every stdin read site, per CLAUDE.md Section 7 and consistent with R9's
  own scope decision. Known limitation: Ctrl+C during the recommender Q&A,
  method selection, dockerfile-warning confirmation, /change-method's
  internal reads, or the Inputs/Outputs list-review/edit sub-flow is not yet
  covered — those sites would need the same threading if this gap matters
  in practice. Added 2 tests to RecipeCreateInteractiveTests.cs using a fake
  SequencedCancellationSource (returns false for N checks, then true): one
  asserting Ctrl+C-equivalent cancellation produces exit code 130, no file
  written, no stack trace, and the exact /cancel message text; one asserting
  /cancel and simulated-Ctrl+C produce an identical exit code and final
  message lines. Full suite: 321/321 passing (38 NodeKit.Cli.Tests + 283
  NodeKit.Tests), 0 build warnings.
```

### Sprint R11. Phase 2 — Authoring Mode Selector + Fast Questionnaire Enrichment

Goal:

```text
Add the up-front mode choice and enrich the existing 6-question recommender
flow with the explanation/example/impact text the design doc specifies,
without changing recommendation logic.
```

Tasks:

```text
1. Implement AuthoringModeSelector: the three-choice entry screen (쉬운 안내
   모드 / 빠른 설정 모드 / 스크립트-CI 모드 사용법 보기) from Section 7.
   The third choice prints usage and exits without starting recipe create.
2. Wrap each of the existing 6 questions (RecipeMethodQuestionCatalog) with
   the per-question explanation/example/impact text from Sections
   15.3-15.8. Do not change RecipeMethodRecommender's decision logic.
3. Implement MethodRecommendationPresenter: the post-recommendation summary
   screen (Section 16) showing recommended method, reason, upcoming fields,
   and warnings, with a reject-and-pick-manually path.
```

Done when:

```text
- Existing RecipeMethodRecommender tests pass unchanged (logic untouched).
- New presenter/selector tests cover: mode selection, the CI-mode early
  exit, and the recommendation-reject-then-manual-select path.
```

Progress:

```text
- 2026-06-26: Completed. Added AuthoringModeSelector
  (src/NodeKit.Cli/AuthoringModeSelector.cs) — Section 7 entry screen
  with choices [1] 쉬운 안내 모드 / [2] 빠른 설정 모드 / [3] 스크립트/CI 모드.
  Mode 3 prints --non-interactive usage and returns exit code 0 without
  entering the Q&A. Mode 1 is currently a placeholder (routes to the same
  Q&A flow as mode 2 with a "아직 준비 중" notice; actual BeginnerGuideFlow
  is deferred to Sprint R13). Added RecipeMethodQuestionDetail record +
  RecipeMethodQuestionDetailCatalog (src/NodeKit.Cli/) with the
  Section 15.3-15.8 per-question meaning/example/y-effect/n-effect/
  enter-effect text for all 6 questions. Updated AskRecommenderQuestions to
  print the Section 15.2 quick-setup intro screen and enriched question
  detail before each answer prompt (prompt text changed from "[y/n/u]" to
  "선택 [y/n/Enter]:", behavior unchanged). Added MethodRecommendationPresenter
  (src/NodeKit.Cli/MethodRecommendationPresenter.cs) — Section 16 screen
  showing recommended method, reason, effects, upcoming fields (from
  RecipeFieldCatalog.FieldsFor), and cautions; accept [Y/n] path or reject
  → fixed 1-5 manual method menu; internally loops until valid input.
  Extracted TryParseMethodSelection from RecipeCreateInteractiveRunner
  (private) into MethodRecommendationPresenter (internal static), shared
  with TryHandleChangeMethod. Removed DisplayRecommendation, PromptMethodChoice,
  TryParseMethodSelection from RecipeCreateInteractiveRunner. SelectMethod
  outer while-loop removed (presenter handles its own looping).
  All 18 existing interactive test transcripts updated to prepend "2"
  (quick-setup mode selection). Note: "앞으로 입력할 항목" in the presenter
  shows catalog field.Name strings (e.g. "ImageRef") rather than the
  design doc's Section 16 example text ("BaseImage") — this reflects the
  pre-existing naming difference in RecipeFieldCatalog and is not renamed
  here per CLAUDE.md §7. Added 3 new tests: CI-mode early exit (exit 0,
  no Q&A consumed, --non-interactive usage shown), guided-beginner fallback
  (notice printed, Q&A continues, file saved), recommendation-reject→manual
  select (picks [1] container, saves BioContainer recipe). Full suite:
  324/324 passing (41 NodeKit.Cli.Tests + 283 NodeKit.Tests), 0 build
  warnings. RecipeMethodRecommender tests unchanged and passing.
```

### Sprint R12. Phase 3a — InstallCommandParser Spike

Goal:

```text
Resolve the highest-uncertainty contract in the whole design — the
install-command parser's Parsed/PartiallyParsed/Failed boundary — against
real-world install command strings before BeginnerGuideFlow is built on top
of it.
```

Tasks:

```text
1. Build a fixture table of real install commands (conda, micromamba, pip,
   curl|bash, git clone && make, and at least 10 more representative
   bioinformatics-tool install patterns) with their expected
   Parsed/PartiallyParsed/Failed classification per Section 9.2.
2. Implement InstallCommandParser against that fixture table only — no CLI
   wiring yet.
3. Treat any fixture that the parser cannot classify as expected as a design
   gap to resolve before Sprint R13, not a bug to patch silently.
```

Done when:

```text
- InstallCommandParser passes its fixture table.
- Any fixture-table gaps found are written back into the v0.9.2 design doc
  Section 9.2/9.3 as resolved open questions, or explicitly deferred with a
  noted reason.
```

**Progress (Sprint R12 완료):**

```text
구현 완료. 빌드 경고 0건, 359/359 테스트 통과.

신규 파일:
  src/Authoring/Recipes/InstallCommandParseStatus.cs  (internal enum)
  src/Authoring/Recipes/InstallCommandParseResult.cs  (internal sealed record)
  src/Authoring/Recipes/InstallCommandParser.cs       (internal static class)
  tests/NodeKit.Tests/Recipes/InstallCommandParserTests.cs
    → 26케이스: Parsed 8, ParsedWithWarning 3, PartiallyParsed 3,
      CondaCreate 2, Failed 14 + OriginalCommand 보존 2건 + 기타 2건

설계 갭 해결 및 문서화 (Section 9.4 추가):
  - mamba → Failed (지원 엔진 외)
  - conda install 채널 미지정 → PartiallyParsed (Missing=[Channels]);
    묵시적 defaults 채널 불삽입 (재현성 원칙)
  - conda create → PartiallyParsed + 의미론 경고
  - 래핑 명령 → Failed (첫 토큰 기준)
  - public → internal 선언 (CA1515; InternalsVisibleTo로 테스트 접근)

커밋: (다음 커밋에 포함)
```

### Sprint R13. Phase 3b — Beginner Guide Flow + Image Reference Normalizer

Goal:

```text
Implement the clue-based entry flow for users who start with no method
decided, reusing InstallCommandParser from R12 and generalizing the
ImageRef/ImageDigest composition that already exists narrowly in
RecipeAuthoringSession.Build() for the Container method.
```

Tasks:

```text
1. Implement BeginnerGuideFlow: the first-question clue picker (Section 8.2)
   and per-clue sub-flows (install command, container image, source/GitHub,
   Dockerfile, internal mirror, tool-name-only) per Sections 9-14.
2. Implement ImageReferenceNormalizer as a generalization of the existing
   BaseImage+ImageDigest composition in RecipeAuthoringSession.Build():
   accept ImageRef with or without an embedded digest plus an optional
   separate ImageDigest, detect conflicts (Section 10.5), and run
   immediately before RecipeDocument construction (Section 10.4) — not
   inside prompt-layer string handling.
3. Implement the "아무것도 모름" (no clues at all) safe-exit path (Section 14)
   that explains the minimum required clue set instead of forcing the
   wizard forward.
```

Done when:

```text
- ImageRef-with-digest, ImageRef-without-digest+separate-ImageDigest, and
  conflicting-digest cases each produce the documented canonical URI or the
  documented conflict-resolution prompt.
- A test proves RecipeValidator/RecipeRenderer/L1 validators only ever see
  an already-normalized digest-pinned URI, never a raw split ImageRef.
- The "아무것도 모름" path exits without writing a file and without forcing
  a method choice.
```

**Progress (Sprint R13 완료):**

```text
완료:
- ImageReferenceNormalizeStatus / ImageReferenceNormalizeResult / ImageReferenceNormalizer
  (src/Authoring/Recipes/)
  • Normalize(imageRef, imageDigest): Normalized / MissingDigest / DigestConflict 세 결과
  • 임베디드 @sha256: 파싱, sha256: 접두어 자동 추가, 빈/공백 digest 처리

- BeginnerGuideFlow (src/NodeKit.Cli/BeginnerGuideFlow.cs)
  • 7-choice 단서 픽커 (Section 8.2)
  • 설치 명령 서브플로 (Section 9): InstallCommandParser 재사용,
    Parsed/PartiallyParsed/Failed 처리
  • 컨테이너 이미지 서브플로 (Section 10): ImageReferenceNormalizer 적용,
    MissingDigest 시 [1-4] 선택 프롬프트
  • 소스/GitHub 서브플로 (Section 11)
  • Dockerfile 서브플로 (Section 12): 기본-N 경고 + 확인
  • 미러 서브플로 (Section 13)
  • 아무것도 모름 서브플로 (Section 14): 최소 단서 안내 후 파일 저장 없이 exit 0

- RecipeCreateInteractiveRunner: mode==GuidedBeginner → BeginnerGuideFlow.Run() 호출,
  null 반환 시 "단서가 부족합니다." 출력 후 exit 0

- NodeKit.Cli.csproj: InstallCommandParser+ImageReferenceNormalizer 관련
  신규 파일 6개 소스 링크 추가

테스트:
- ImageReferenceNormalizerTests.cs: 12 unit + 2 integration (NodeKit.Tests)
  • Session_SplitImageRefDigest_BuildProducesCanonicalBioContainerUri
  • Session_SplitImageRefDigest_L1ValidationPasses_NoDigestViolation
    (BuildKind = RecipeBuildKindResolver.Resolve() 포함)
- BeginnerGuideFlowTests.cs: ~15 transcript 기반 테스트 (NodeKit.Cli.Tests)
- RecipeCreateInteractiveTests.cs: GuidedBeginner 모드 transcript 갱신

최종 테스트 결과: NodeKit.Tests 332/332, NodeKit.Cli.Tests 57/57 (0 failed)

설계 비고:
- DigestConflict 는 BeginnerGuideFlow 내에서 도달 불가
  (임베디드 digest → 즉시 Normalized; SeparateDigest 분기는 MissingDigest에서만
  진입 → 별도 digest 제공 시 항상 Normalized). 단위 테스트로만 커버.
```

### Sprint R14. Phase 4 — Non-Interactive Contract Alignment

Goal:

```text
Bring docs/NODEKIT_CLI_USAGE.md and the v0.9.2 doc's non-interactive
examples in line with RecipeCreateCommand's actual parsing behavior — this
is a documentation/test-confirmation sprint, not new parsing logic.
```

Tasks:

```text
1. Add a regression test asserting --field splits only on the first '=' and
   preserves embedded '=' in the value (e.g. Packages=bwa=0.7.17=h5bf99c6_8).
2. Add a regression test asserting repeated --field Name=Value for a
   StringList field (Packages, Channels, SourceBuildCommands,
   BuildDependencies, and Command since R8 confirmed it is StringList)
   accumulates rather than overwrites.
3. Update non-interactive examples in both the v0.9.2 doc and
   docs/NODEKIT_CLI_USAGE.md to use --field ToolVersion=... (internal field
   name), not --field Version=....
```

Done when:

```text
- New regression tests pass against the existing RecipeCreateCommand parser
  with no production code changes (Section 23 of the design doc frames this
  as confirming existing behavior, not building new behavior).
- No non-interactive example in either doc uses the Version alias.
```

**Progress (Sprint R14 완료):**

```text
완료:
- RecipeCreateCommandTests.cs 에 regression 테스트 2건 추가 (production code 변경 없음)
  • Field_EmbeddedEquals_PreservedAsValue_NotSplitAgain
    TrySplitOnce(IndexOf('=')) 가 Packages=bwa=0.7.17=h5bf99c6_8 에서
    이름=Packages, 값=bwa=0.7.17=h5bf99c6_8 으로 분리하는 동작 검증
  • Field_StringList_RepeatedField_AccumulatesAllValues
    --field Packages=pkg1 --field Packages=pkg2 (및 Channels) 반복 시
    덮어쓰지 않고 누적하는 동작 검증

문서 점검:
  - NODEKIT_CLI_USAGE.md: --field Version= 잔재 없음 (ToolVersion만 사용)
  - UX v0.9.2 doc: --field Version= 예시 없음
    (1767번 줄 "공식 문법이 아니다" 설명은 그대로 유지)

최종 테스트 결과: NodeKit.Cli.Tests 59/59 (2 추가), 0 failed. 빌드 경고 0.
```

### Sprint R15. Documentation Update

Goal:

```text
Restructure docs/NODEKIT_CLI_USAGE.md's recipe create section to match the
shipped v0.9.2 UX, per Section 28 of the design doc.
```

Tasks:

```text
1. Restructure the recipe create section into the 2-1..2-9 subsection layout
   from Section 28 (mode selection, easy guide mode, fast setup mode,
   common fields, per-method fields, Inputs/Outputs, recovery,
   non-interactive, escape hatches/review/change-method).
2. Document /back as explicitly out of scope for v0.9.2 with the reason
   from Section 17.2, not silently absent.
3. Document the digest-required-by-default container flow and the
   Dockerfile-method default-N warning from Sections 10.2/12.2.
```

Done when:

```text
- docs/NODEKIT_CLI_USAGE.md's recipe create section reflects the v0.9.2
  flows a reader would actually see, not the pre-v0.9.2 wizard.
- No stale field-name examples (Section 19.2's Version/ToolVersion
  distinction is reflected).
```

**Progress (Sprint R15 완료):**

```text
완료:
- NODEKIT_CLI_USAGE.md 섹션 2 전면 재구성 (2-1~2-5 → 2-1~2-9)
  2-1: 진행 방식 선택 (AuthoringModeSelector 화면 예시 포함)
  2-2: 쉬운 안내 모드
       · 7단서 picker 표 및 화면 예시
       · container digest 필수 플로우 (MissingDigest → [1-4])
       · Dockerfile default-N 경고 (y/N 화면)
       · 아무것도모름 안전종료 설명
  2-3: 빠른 설정 모드 (기존 6문항 Q&A 보존)
  2-4: 공통 필드 입력
  2-5: method별 필드 입력 (container ImageRef+digest 설명 개선)
  2-6: Inputs/Outputs 입력 (구 2-3 내용)
  2-7: recovery (구 2-4 내용)
  2-8: non-interactive
       · --field 첫 번째 = 구분자 규칙 명시
       · 목록 필드 반복 누적 명시
       · 예시 --input/--output 프리셋 id 형식으로 정정
  2-9: 중간에 나가기 / review / method 변경
       · /help, /review, /change-method, /cancel, /quit, /exit 표 형식
       · /back 명시적 범위 밖 기록 (Section 17.2 이유: 단방향 루프,
         method 변경 후 필드 무효화, Inputs/Outputs 이전 단계 의미 모호)

Version/ToolVersion 확인: 두 문서 모두 --field Version= 잔재 없음 (R14 확인 재확인)

변경: docs/NODEKIT_CLI_USAGE.md (+164 -68)
```

### 11.1 Sequencing Note

```text
R8 is mechanical documentation cleanup and can run first, independent of
everything else.
R9 and R10 are independent of method-recommendation/beginner-flow logic and
can ship before R11-R13; R10 depends on R9's Cancelled result type existing.
R11 only touches existing recommender presentation, not its logic; it can
run in parallel with R9/R10 if capacity allows, but has no hard dependency
on them.
R12 must precede R13 — R13 reuses InstallCommandParser and would otherwise
bake an unvalidated parser contract into BeginnerGuideFlow.
R13 is the highest-risk sprint in this track, matching the design doc's own
Phase 3 risk ordering (Section 25).
R14 only needs RecipeCreateCommand as it exists today; it has no dependency
on R9-R13 and could run anytime, but is sequenced last among code sprints
so its doc updates land alongside R15 in one documentation pass.
R15 depends on R9-R14 being functionally complete, since it documents their
combined behavior.
```

### 11.2 한국어 요약

```text
이 트랙은 v0.9.2 설계 문서(Freeze Candidate)를 구현하는 sprint 시퀀스다.
10번 섹션의 R0-R7(recipe create 기본 구현)은 이미 완료되어 있고, 이 트랙은
그 위에 진입 UX/이스케이프 핫치/Ctrl+C/non-interactive 문서 정합성을 추가한다.
NodeVault 제출, 이미지 빌드, MCP server 구현은 여전히 범위 밖이다.

R8:  문서 용어 정리 (RecipeDraft 등 -> RecipeAuthoringSession, Command는
     StringList로 확정 — 기계적, 코드 변경 없음, 가장 먼저 처리)
R9:  Phase 1-A — /help, /review, /cancel, /quit, /exit + 종료 코드 130
R10: Phase 1-B — Ctrl+C를 같은 취소 결과로 매핑 (R9의 Cancelled 결과 타입 의존)
R11: Phase 2 — AuthoringModeSelector + 기존 6문항 설명 보강 + 추천 결과 화면
     (추천 로직 자체는 변경 없음, R9/R10과 병행 가능)
R12: Phase 3a — InstallCommandParser 스파이크 (실제 설치 명령 fixture 테이블로
     Parsed/PartiallyParsed/Failed 계약을 먼저 검증, CLI 연결 없음)
R13: Phase 3b — BeginnerGuideFlow + ImageReferenceNormalizer
     (R12 의존, 이 트랙에서 위험도 가장 높음)
R14: Phase 4 — non-interactive 파싱 계약 회귀 테스트 + 문서 예시 정정
     (--field 첫 '=' 기준, 리스트 필드 반복 누적, ToolVersion 표기)
R15: 사용 가이드(NODEKIT_CLI_USAGE.md) recipe create 섹션 재구성
     (R9-R14 완료 후, 마지막)

순서: R8 단독 선행 가능 -> (R9 -> R10) 및 R11은 서로 독립적으로 병행 가능 ->
R12 -> R13 -> R14 -> R15 -> R16.
```

## 12. ResolveRecipe Client Seam (2026-06-28)

Design source: `docs/PLATFORM_MASTER_DESIGN.md` §4.9 / §6,
`NodeVault/docs/PLATFORM_SCHEDULE.md` 병렬 트랙 D.

이 트랙은 NodeVault `ResolveRecipe` RPC가 proto에 추가되기 전에 NodeKit의
UX 계층을 먼저 구현한다. proto 없이도 인터페이스 seam + Null 구현으로 전체
흐름을 선제적으로 완성하고, proto가 준비되면 `GrpcResolveRecipeClient`만
플러그인한다.

### Sprint R16. ResolveRecipe Seam + Candidate Selection UX

Goal:

```text
Build the NodeKit side of the ResolveRecipe pre-build step: define the
interface, provide a no-op null implementation, implement the candidate
selection UI, and wire it between L1 validation and recipe save.
```

Tasks:

```text
1. Define IResolveRecipeClient + result model types
   (ResolveRecipeResult, PackageResolution, BuildStringCandidate,
   RecipeResolutionSource enum).
2. Implement NullResolveRecipeClient (returns Unsupported → step skipped).
3. Implement PackageCandidatePresenter: show numbered candidate list for
   multi-candidate packages; auto-select for single-candidate packages;
   ApplySelections replaces version-only pins with full_pin strings.
4. Wire into RecipeCreateInteractiveRunner.Run(): after L1 validation
   passes and before SaveDocument, call IResolveRecipeClient.ResolveAsync
   for Package-method recipes and present any returned candidates.
5. Show polite guidance when resolution_source == NotFound (closed network
   without Harbor pre-registration).
```

Done when:

```text
- Build: 0 warnings, 0 errors.
- PackageCandidatePresenterTests: single-candidate auto-select, multi-candidate
  prompt+pick, Enter=first, /cancel throws, invalid-then-valid reprompt,
  ApplySelections replaces pin correctly.
- NullResolveRecipeClient test: returns Unsupported.
- All existing interactive tests still pass unchanged (Null client means
  the resolve step is a no-op today).
```

**Progress (Sprint R16 완료, 2026-06-28):**

```text
완료:
- src/NodeKit.Cli/IResolveRecipeClient.cs
    IResolveRecipeClient interface, ResolveRecipeResult, PackageResolution,
    BuildStringCandidate, RecipeResolutionSource enum
- src/NodeKit.Cli/NullResolveRecipeClient.cs
    Singleton no-op; returns Unsupported
- src/NodeKit.Cli/PackageCandidatePresenter.cs
    Present(): auto-select for 1 candidate; numbered prompt for N>1;
    /cancel|/quit|/exit → RecipeCreateCancelledException; invalid input → reprompt
    ApplySelections(): replaces version-only pin with full_pin by package name
- RecipeCreateInteractiveRunner.Run(): optional IResolveRecipeClient param
    (default = NullResolveRecipeClient); resolve step inserted after validation,
    before SaveDocument. NotFound path prints폐쇄망 guidance without blocking.
- tests/NodeKit.Cli.Tests/PackageCandidatePresenterTests.cs: 9 tests
    (auto-select, pick-second, Enter=first, cancel-throws, invalid-then-valid,
    ApplySelections-replaces, ApplySelections-empty, ApplySelections-full-pin,
    NullClient-returns-unsupported)

최종 결과: 336 NodeKit.Tests + 92 NodeKit.Cli.Tests = 428 / 428 통과, 0 warnings.
```

### Sprint R17. GrpcResolveRecipeClient (NodeVault proto 준비 후)

Goal:

```text
Implement the real gRPC client once NodeVault adds ResolveRecipe to the proto.
```

Entry criteria:

```text
- NodeVault has added ResolveRecipe RPC + ResolveRecipeRequest /
  ResolveRecipeResponse / PackageResolution / BuildStringCandidate messages
  to protos/nodevault/v1/nodevault.proto.
- Vendored proto in NodeKit is updated.
```

Tasks:

```text
1. Generate C# gRPC stubs from the updated proto.
2. Implement GrpcResolveRecipeClient: call NodeVault ResolveRecipe, map
   the response to NodeKit's ResolveRecipeResult model.
3. Wire GrpcResolveRecipeClient into RecipeCreateInteractiveRunner.Run()
   when NODEKIT_NODEVAULT_URL env var is set (same pattern as HarborImageDigestResolver).
4. Add integration tests (opt-in, skipped without live NodeVault).
```

Done when:

```text
- bwa=0.7.17 + Harbor cache hit → one candidate auto-selected, full_pin
  written to recipe.json.
- bwa=0.7.17 + Harbor miss + open network → candidate list shown to user.
- bwa=0.7.17 + Harbor miss + closed network → NotFound guidance shown,
  recipe saved without build_string (NodeVault will resolve at submit time).
```

**Progress (Sprint R17 완료, 2026-06-30):**

```text
완료 (커밋 0180674):
- src/NodeKit.Cli/GrpcResolveRecipeClient.cs
    TryCreate(): NODEKIT_NODEVAULT_URL 게이팅 (HarborImageDigestResolver와 동일 패턴)
    BuildService.BuildServiceClient.ResolveRecipeAsync 호출, PackageSpec/RecipeVariant/
    ResolveRecipeResponse → ResolveRecipeResult 매핑. GrpcChannel 소유, IDisposable.
- RecipeCreateInteractiveRunner.cs: resolve client fallback chain에 연결
    (test override → StubResolveRecipeClient → GrpcResolveRecipeClient → NullResolveRecipeClient)
- protos/nodevault/v1/nodevault.proto: NodeVault 원본과 diff 결과 동일, 재벤더링 불필요
    (ResolveRecipeRequest/ResolveRecipeResponse/PackageResolution/BuildStringCandidate 이미 반영됨)
- tests/NodeKit.Cli.Tests/GrpcResolveRecipeClientIntegrationTests.cs
    NODEKIT_NODEVAULT_URL 미설정 시 skip되는 opt-in 통합 테스트

NodeVault 측 pkg/build/recipe_resolve.go: Harbor 우선 조회 + conda/micromamba 외부 조회
(Anaconda.org API) + 폐쇄망 차단 모두 실구현, TestResolveRecipe_* 7/7 통과 확인.
BIOCONTAINER variant만 codes.Unimplemented로 명시적 미구현(P3, 의도된 설계).

최종 결과: PackageCandidatePresenterTests 9개 + GrpcResolveRecipeClientIntegrationTests
+ RecipeCreateInteractiveTests(FixedResolveRecipeClient) 모두 통과.
```

## 13. Live Recipe Reproducibility Improvement (2026-07-08)

배경: seoy-libvirt-cilium 실 K8s 클러스터에 배포된 NodeVault로 라이브
end-to-end 테스트 2회 실행. 핵심 경로(DockerfileFallback/BioContainer/
Conda/Micromamba/PackageMirror/SourceBuild-적절한-base)는 전부 통과,
NodeVault 최종 게이트가 conda 버전-only pin을 실제로 거부하는 것도 확인.
전체 보고서: `docs/NODEKIT_LOCAL_GRPC_TEST_SCENARIO.md`(로컬), NodeVault
저장소 `docs/NODEKIT_LIVE_RECIPE_REPRO_TEST_2026-07-08.md` /
`docs/NODEKIT_LIVE_RECIPE_EXTENDED_TEST_2026-07-08.md`(읽기 전용 참고).

NodeKit 쪽 개선안 원문: `docs/NODEVAULT_LIVE_RECIPE_REPRO_IMPROVEMENT_NODEKIT.md`.
NodeVault 쪽 개선안(P0 RegistryConfig/Harbor CA trust, P1 build_events,
P2 SourceBuild final image 정책, P3 pinning_status)은 별도 개발 에이전트가
독립적으로 진행 중 — NodeKit 세션은 그 항목을 구현하거나 추적하지 않는다.

우선순위는 NodeVault 의존성 유무로 정렬했다: R18(NodeVault 값 없어도
지금 바로 유용)이 가장 먼저고, R19~R21은 recipe authoring 쪽이라
NodeVault와 무관하게 진행 가능하지만 서로 영향을 주므로 이 순서로 묶는다.

### Sprint R18. Digest Observability Fallback (submit/watch)

Goal:

```text
nodekit submit이 성공(Succeeded)했는데 이미지 digest를 한 번도 못 받았으면
"NodeVault 인덱스를 직접 확인하라"는 명시적 안내를 출력한다.
```

Context:

```text
BuildEvent proto(nodevault.proto:206-213)에는 이미 digest 필드가 있고,
SubmitCommand.PrintEvent(230-255)도 BuildEventKind.DigestAcquired를
받으면 이미 digest를 출력하도록 되어 있다 — 코드는 이미 있다.
문제는 라이브 테스트(F-04, extended test F-03)에서 확인된 대로 NodeVault의
WatchToolBuild가 지금 DigestAcquired 이벤트를 실제로 보내지 않는다는
것이다(NodeVault 쪽 P1 과제, 다른 에이전트 담당). NodeKit이 당장 할 수
있는 건 "못 받았다"는 사실을 조용히 넘어가지 않고 명시적으로 알리는 것.
```

Tasks:

```text
1. SubmitAsync에서 DigestAcquired 이벤트 수신 여부를 추적하는 플래그 추가.
2. Succeeded 반환 직전, 플래그가 false면 stdout에 안내 메시지 출력:
   "이미지 digest가 서버에서 제공되지 않았습니다 — NodeVault 인덱스에서
   직접 확인하세요 (build ID: ...)."
3. NodeVault가 나중에 DigestAcquired를 실제로 보내기 시작하면 이 안내는
   자동으로 나오지 않게 된다(플래그가 true가 되므로) — 별도 정리 불필요.
```

Done when:

```text
- Build: 0 warnings, 0 errors.
- SubmitCommandTests: Succeeded without prior DigestAcquired → stdout에
  안내 메시지 포함.
- SubmitCommandTests: Succeeded with prior DigestAcquired → 안내 메시지
  없음(기존 동작 유지).
- 기존 SubmitCommandTests 전부 그대로 통과.
```

**Progress (Sprint R18 완료, 2026-07-09, 커밋 `c23e026`):**

```text
완료: SubmitCommand.SubmitAsync에 digestReceived 플래그 추가, Succeeded
반환 직전 플래그 false면 안내 메시지 출력. 회귀 테스트 2개 추가
(Submit_BuildSucceeded_WithoutDigestAcquired_PrintsFallbackNotice,
Submit_BuildSucceeded_WithDigestAcquired_DoesNotPrintFallbackNotice).
전체 테스트 543개 통과, 0 warnings.
```

### Sprint R19. Conda/Micromamba Pin-Mode UX

Goal:

```text
NodeKit이 authoring 시점에 name=version(버전-only) pin을 계속 허용하면서도,
NodeVault가 최종적으로 거부할 것이라는 사실을 제출 전에 알려준다.
```

Context:

```text
라이브 테스트 n03에서 확인: NodeKit L1은 bwa=0.7.17을 통과시키지만
NodeVault 최종 게이트는 "package pin bwa=0.7.17 must include
name=version=build"로 거부한다. 지금은 submit까지 가야만 이 불일치를
알 수 있다.
```

Tasks:

```text
1. Packages 필드 각 항목에 대해 "=" 개수로 pin 상태를 분류하는 순수
   함수 추가: FullPin(2개 "="), VersionOnly(1개 "="), Malformed(0개).
   (L1-RCP-011 allowlist 통과 이후에만 의미 있음 — 형식 자체가 틀리면
   기존 규칙이 이미 막는다.)
2. --strict-reproducible 플래그(non-interactive/submit 공통): VersionOnly
   pin이 하나라도 있으면 submit 이전에 명확한 메시지와 함께 차단
   (L1-RCP-016, 새 규칙).
3. 대화형 Package method 흐름에서 VersionOnly pin을 확정할 때 "NodeVault가
   최종 제출 시 거부할 수 있습니다" 경고를 표시(차단하지는 않음 — 로컬
   allowlist 통과 + Recommended 수준 유지, 사용자가 감수하고 진행 가능).
```

Done when:

```text
- Build: 0 warnings, 0 errors.
- RecipeValidatorTests: --strict-reproducible 미설정 시 VersionOnly pin
  통과(기존 동작), 설정 시 L1-RCP-016으로 차단.
- 대화형 테스트: VersionOnly pin 확정 시 경고 문구 출력, 저장은 계속 진행.
- 실제 CLI로 bwa=0.7.17 + --strict-reproducible 재현: 차단 확인.
```

**Progress (Sprint R19 완료, 2026-07-09, 커밋 `8d12cd4`, `2fca7aa`):**

```text
완료:
- src/Validation/Recipes/PackagePinClassifier.cs (신규): "=" 개수로
  FullPin/VersionOnly/Malformed 분류.
- RecipeValidator.Validate(recipe, strictReproducible)/
  RecipeValidationPipeline.ValidateRecipe(recipe, strictReproducible):
  Conda/Micromamba/PackageMirror에 L1-RCP-016 추가(strictReproducible일 때만).
- CliApp(validate/render)/SubmitCommand: --strict-reproducible 플래그 파싱
  + threading (CliApp.HasStrictReproducibleFlag).
- RecipeCreateFlow.PromptStringListField: Packages 필드에서 VersionOnly pin
  확정 시 non-blocking 경고 출력(stdout).
- docs/NODEKIT_CLI_USAGE.md §3-5에 --strict-reproducible 반영.

실제 CLI로 bwa=0.7.17 재현: --strict-reproducible 없이 exit 0,
--strict-reproducible과 함께 L1-RCP-016으로 exit 1 확인.
회귀 테스트 13개 추가(PackagePinClassifierTests 5, RecipeValidatorTests 4,
CliAppTests 2, SubmitCommandTests 1, 대화형 테스트 1).
최종 결과: 565 / 565 통과(스킵 2 제외), 0 warnings.
```

### Sprint R20. SourceBuild Risky Base-Image Warnings

Goal:

```text
SourceBuild의 BaseImage가 fetch 도구(curl/tar/sha256sum)를 갖췄는지 불확실할
때, 그리고 curlimages/curl처럼 최종 런타임 이미지로는 부적절해 보이는
이미지를 선택했을 때 경고한다.
```

Context:

```text
라이브 테스트 F-01: alpine/miniforge3은 curl이 없어 빌드 실패, curlimages/curl은
성공했지만 "production-grade final runtime base 아님". 개선안 문서가 제안하는
진짜 해법(multi-stage fetch/builder/final 3단계 recipe 구조)은 RecipeDocument에
새 필드(FetcherImage/BuilderImage 등)를 추가하고 RecipeRenderer.RenderSourceBuild를
다시 설계해야 하는 훨씬 큰 작업이라 별도 스프린트(R22, 아래 미착수 항목)로
미룬다. 이번 스프린트는 문서 자체의 "Engineering Opinion"(자동 설치보다
검증/UX 명확성이 먼저)을 따라 경고만 추가한다 — BaseImageEngineMismatchChecker와
같은 패턴(휴리스틱, non-blocking).
```

Tasks:

```text
1. SourceBuildBaseImageAdvisor(가칭): BaseImage 이름에 알려진 fetch-friendly
   이미지 패턴(curl/wget/git 등을 이름에 포함)이 없으면 "curl/tar/sha256sum이
   없을 수 있습니다" 경고, curlimages/curl류 fetch-전용 이미지 패턴이면
   "fetch 단계용 이미지로 보입니다 — 최종 실행 이미지로는 권장하지 않습니다"
   경고. 둘 다 non-blocking(경고만, 차단 아님).
2. RecipeCreateCommand.RunNonInteractive와 대화형 SourceBuild BaseImage 확정
   시점에 배선 (BaseImageEngineMismatchChecker 호출 패턴과 동일한 위치).
```

Done when:

```text
- Build: 0 warnings, 0 errors.
- SourceBuildBaseImageAdvisorTests: alpine/미확인 이미지 → 경고, curlimages/curl
  → 다른 문구의 경고, condaforge/miniforge3(패키지 매니저 있음, fetch 도구
  불확실) → 경고.
- 실제 CLI로 SourceBuild + alpine base 재현: 경고 출력 확인(차단 아님).
```

**Progress (Sprint R20 완료, 2026-07-09, 커밋 `82aacff`):**

```text
완료: SourceBuildBaseImageAdvisor(신규, non-blocking 휴리스틱) — 알려진
fetch-전용 이미지 패턴(curlimages/curl, busybox, alpine/curl)만 경고.
RecipeCreateFlow(대화형)/RecipeCreateCommand(non-interactive) 양쪽에
BaseImageEngineMismatchChecker와 동일한 위치로 배선.

**계획 대비 축소**: 원래 "Done when"에 있던 "condaforge/miniforge3처럼
fetch 도구 존재 여부가 불확실한 이미지도 경고"는 구현하지 않았다 — "이
이미지에 curl이 있을지 없을지"를 이름만으로 판단하는 건 오탐(false
positive)이 훨씬 잦은 신뢰도 낮은 휴리스틱이라, 확실한 패턴(알려진
fetch-전용 이미지)만 남기고 범위를 좁혔다. 실제 테스트도
Describe_SourceBuildWithOrdinaryImage_ReturnsNull로 condaforge/miniforge3이
경고 없이 통과함을 명시적으로 확인한다.

실제 CLI로 curlimages/curl base 재현: 경고 출력 확인(차단 아님).
회귀 테스트 5개 추가. 최종 결과: 560 / 560 통과(스킵 2 제외), 0 warnings.
```

### Sprint R21. BuildDependencies Actionability Warning

Goal:

```text
BuildDependencies가 채워져 있는데 렌더러가 실제로 아무것도 하지 않는다는
사실을 조용히 넘어가지 않고 사용자에게 알린다.
```

Context:

```text
확장 라이브 테스트 F-04/#10: BuildDependencies는 recipe 표면에 존재하지만
RecipeRenderer가 전혀 사용하지 않는다. 개선안 문서의 보수적 정책 1번
("Treat BuildDependencies as build-stage-only metadata")과 2번("Warn if
the current renderer cannot install or place them")을 그대로 따른다 —
자동 설치 로직은 pin/snapshot 정책 없이 추가하면 새로운 재현성 문제를
만든다는 문서 자체의 경고를 존중해 이번 스프린트에서는 구현하지 않는다.
```

Tasks:

```text
1. RecipeValidator.ValidateSourceBuild에 경고성 규칙 추가: BuildDependencies가
   비어있지 않으면 "이 목록은 현재 자동 설치되지 않습니다 — BaseImage에
   이미 포함되어 있는지 직접 확인하세요"를 non-blocking 안내로 표시
   (Recommended 필드이므로 여전히 차단 대상 아님, 표시 방식만 명확화).
2. NODEKIT_CLI_USAGE.md의 BuildDependencies 설명에 이 제약을 명시.
```

Done when:

```text
- Build: 0 warnings, 0 errors.
- RecipeValidatorTests: BuildDependencies 비어있지 않을 때 안내 메시지
  포함, IsValid는 여전히 true(다른 위반 없다면).
- 문서 업데이트 확인.
```

**Progress (Sprint R21 완료, 2026-07-09, 커밋 `2c42c0d`):**

```text
완료: BuildDependenciesAdvisor(신규, R20과 동일한 non-blocking advisor
패턴) 추가, RecipeCreateFlow/RecipeCreateCommand 양쪽에 배선.

**계획과 다르게 구현**: 원래 Tasks에는 "RecipeValidator.ValidateSourceBuild에
경고성 규칙 추가"라고 되어 있었지만, RecipeValidator가 반환하는
ValidationResult.IsValid는 violations.Count == 0으로만 결정되어 "경고성
violation"이라는 개념 자체가 없다 — RecipeValidator에 추가하면 자동으로
차단(validate/render/submit 전부 exit 1)이 된다. 그래서 R20의
SourceBuildBaseImageAdvisor와 동일한 별도 advisor 클래스 패턴으로
구현을 바꿨다 — ValidationResult와 완전히 분리된, 순수 안내용 stderr/stdout
출력.

실제 CLI로 BuildDependencies=zlib1g-dev 재현: 경고 출력 + exit 0 확인.
회귀 테스트 4개 추가. 최종 결과: 564 / 564 통과(스킵 2 제외), 0 warnings.
NODEKIT_CLI_USAGE.md 갱신은 별도로 하지 않음 — BuildDependencies는 원래
그 문서에 섹션이 없었고, advisor 메시지 자체가 실행 시점에 충분히
명확해 별도 문서화가 급하지 않다고 판단.
```

### Sprint R22-B. SourceBuild 구조화 Intent — Authoring 모델 (Issue #37)

Goal:

```text
새 RecipeBuildKind.SourceBuildStructured와 관련 필드를 authoring 모델에
도입한다. 기존 RecipeBuildKind.SourceBuild(legacy)는 절대 변경하지 않는다.
NodeVault 쪽 코드/API는 전혀 건드리지 않는다.
```

Context:

```text
설계: docs/NODEKIT_SOURCEBUILD_STRUCTURED_INTENT_DESIGN.md §5, §10
D-1/D-2/D-8. 스테이지 개수는 2026-07-13 사용자 확인으로 2-profile
(Build/Runtime)로 확정(§10 D-2b) — fetch/builder/final 3-stage가
아니다. Issue #36 설계 검토에서 분리된 첫 구현 단계.
```

Tasks:

```text
1. RecipeBuildKind.SourceBuildStructured 추가.
2. RecipeDocument에 BuildProfile / BuildProfileImage / RuntimeProfile /
   RuntimeProfileImage / RuntimeDependencies 필드 추가.
3. RecipeFieldCatalog에 새 kind 전용 필드 목록 추가 — BuildProfile/
   RuntimeProfile은 큐레이션된 Choice 선택지 + "advanced: custom image"
   override(BaseImageField() 패턴 재사용).
4. BuildProfile/RuntimeProfile의 실제 이미지→digest 매핑표 작성(초기
   큐레이션 — BaseImageCatalog의 기존 후보 재사용 가능 여부부터 검토).
5. RecipeBuildKindResolver.Resolve에 RecipeMethodId.Source →
   SourceBuildStructured 분기 조건 추가(사용자가 구조화 방식을 선택했을
   때만 — 기존 legacy 매핑은 그대로 유지).
6. RecipeValidator.Validate에 새 kind 분기 추가 — BuildProfileImage/
   RuntimeProfileImage 둘 다 digest pinning 검증, 기존 L1-RCP-015류
   (개행 차단) SourceBuildCommands에 재적용.
```

Done when:

```text
- Build: 0 warnings, 0 errors.
- 새 kind로 non-interactive recipe create → nodekit validate 통과.
- 기존 RecipeBuildKind.SourceBuild(legacy) 관련 테스트 82줄/11개 파일
  전부 무변경 통과(회귀 없음).
- BuildProfile/RuntimeProfile 이미지 매핑표가 실제로 채워짐 — 빈 상태로
  이 스프린트를 완료 처리하지 않는다(아래 체크리스트 항목 2 참조).
```

**Progress (Sprint R22-B 완료, 2026-07-13, 커밋 `d4b1656`):**

```text
완료:
- RecipeBuildKind.SourceBuildStructured, RecipeMethodId.SourceStructured
  (--method source-structured, 대화형 wizard에는 미배선 — 의도된 설계).
- RecipeDocument: BuildProfile/BuildProfileImage/RuntimeProfile/
  RuntimeProfileImage/RuntimeDependencies 필드 + Normalize() 반영.
- SourceBuildProfileCatalog(신규, 2개 파일로 분리: Entry record + Catalog):
  Build profile "generic"(buildpack-deps:bookworm), Runtime profile
  "minimal"(debian:bookworm-slim) — 둘 다 buildah run으로 curl/tar/
  sha256sum(generic)과 sh/bash(minimal) 실제 존재를 로컬에서 직접 확인한
  뒤 digest pinning(2026-07-13). 기존 BaseImageCatalog의 condaforge/
  miniforge3 후보는 curl이 없어 재사용 불가함을 재확인.
- RecipeFieldCatalog: SourceStructured 필드 목록(9개) + BuildProfileChoices/
  RuntimeProfileChoices 헬퍼("advanced" 이스케이프 해치 포함).
- RecipeValidator: ValidateSourceBuild를 ValidateSourceFetchFields로 추출해
  legacy SourceBuild와 SourceBuildStructured가 공유(legacy 동작 무변경,
  기존 테스트로 확인). 새 규칙 L1-RCP-017(BuildProfile)/L1-RCP-018
  (RuntimeProfile) 추가.
- RecipeRenderer.RenderSourceBuildStructured: §11 Phase B 임시 단일
  스테이지 placeholder(주석으로 명시 — #38이 진짜 2-stage로 교체 예정).
  RecipeValidationPipeline이 nodekit validate에서도 항상 Render를
  호출하므로(#32 교훈) 이게 없으면 crash한다 — 원래 계획엔 없었지만
  구조적으로 필요해서 이번 스프린트 범위에 포함.
- RecipeCreateCommand: --method source-structured 매핑 +
  "source-build-structured"(내부 이름) 오타 가드.

실제 CLI로 전체 경로(create --non-interactive --method source-structured
→ validate → render) 재현 확인, 큐레이션 매핑이 실제 이미지 참조로
렌더링됨을 확인. hand-authored JSON으로 authoring session을 우회한
경로(L1-RCP-017)도 별도로 재현 확인(#32 패턴과 동일하게 crash 없이
정상 violation).

회귀 테스트 22개 추가(SourceBuildProfileCatalogTests 5,
RecipeFieldCatalogTests 3, RecipeBuildKindResolverTests 1,
RecipeMethodCatalogTests 갱신 1, RecipeValidatorTests 9,
RecipeRendererTests 3, RecipeCreateCommandTests 2 — 정확한 개수는 커밋
참조). 최종 결과: 587 / 587 통과(스킵 2 제외), 0 warnings.
```

### Sprint R22-C. SourceBuild 구조화 Intent — RecipeRenderer 멀티스테이지 렌더링 (Issue #38)

Goal:

```text
RecipeRenderer가 R22-B의 구조화된 intent로부터 client-side에서 완성된
2-stage Dockerfile 문자열을 합성한다. BuildRequest/raw_spec 와이어
프로토콜은 전혀 바꾸지 않는다 — 기존 dockerfile_content 필드에 그대로
담아 보낸다.
```

Context:

```text
설계: 설계 문서 §2.6 Q1/Q5, §5, §7, §10 D-3/D-6/D-7. 선행: R22-B(#37).
NodeVault가 dockerfile_content를 다시 쓰지 않고(§2.1), 멀티스테이지
Dockerfile의 모든 FROM을 스테이지별로 digest pinning 검증한다는 것을
실제 테스트(TestValidateBuildRequest_RejectsEveryUnpinnedStage)로 이미
확인했으므로, NodeVault 코드 변경 없이 이 스프린트만으로 기존 API 위에서
동작한다.
```

Tasks:

```text
1. RecipeRenderer.RenderSourceBuildStructured 신규 메서드 — 기존
   RenderSourceBuild(legacy 전용)는 그대로 둔다. Render()의 switch에
   새 분기 추가.
2. 2-stage 템플릿(설계 문서 §5): builder 스테이지(BuildProfileImage +
   fetch+checksum+extract+SourceBuildCommands) → runtime 스테이지
   (RuntimeProfileImage + COPY --from=builder 고정 export root +
   USER 1000).
3. ENTRYPOINT는 추가하지 않는다(D-6 — Script/Command가 이미 그 역할).
   USER는 runtime 스테이지에만 부여한다(D-7).
4. 산출물 export는 고정 export root 관례로 처리 — 신규 사용자 필드
   추가하지 않음. SourceBuildCommands 도움말/예시 문구에 관례를
   문서화(예: "빌드 결과물을 /nodekit/output/ 아래에 설치하세요").
```

Done when:

```text
- Build: 0 warnings, 0 errors.
- RecipeRendererTests: 새 렌더 메서드가 유효한 2-stage Dockerfile 문자열을
  만드는지(FROM 2개, 둘 다 digest pinned, USER는 마지막 스테이지에만,
  ENTRYPOINT 없음) 확인.
- nodekit render로 생성한 build-request.json의 dockerfile_content가
  실제로 유효한 멀티스테이지 Dockerfile.
- (가능하면) 실 seoy 클러스터 라이브 테스트로 alpine 등 fetch 도구 없는
  이미지를 BuildProfileImage로 선택해도 빌드 성공, 최종 이미지에 curl이
  없음을 확인.
- 기존 RenderSourceBuild(legacy) 무변경 확인.
- 이 스프린트가 "SourceBuild 보안 문제를 해결했다"는 게 아니라는 한계를
  CLI 도움말/릴리스 노트/커밋 메시지 중 최소 하나에 명시(아래 체크리스트
  항목 1 참조 — NodeVault 서버 쪽 강제는 별도 upstream 작업).
```

**Progress (Sprint R22-C 완료, 2026-07-13, 커밋 `3117f8a`):**

```text
완료:
- RecipeRenderer.RenderSourceBuildStructured: R22-B의 단일 스테이지
  placeholder를 실제 2-stage 템플릿으로 교체. builder 스테이지
  (BuildProfileImage, "AS builder") → fetch+checksum(sha256sum -c)+
  extract+mkdir -p /nodekit/output+SourceBuildCommands, runtime 스테이지
  (RuntimeProfileImage) → COPY --from=builder /nodekit/output/ /,
  USER 1000. ENTRYPOINT 없음(D-6), USER는 runtime 스테이지에만(D-7).
  ExportRoot 상수를 SA1201 때문에 클래스 최상단으로 이동.
- RecipeFieldCatalog: SourceBuildCommands 도움말/예시를
  "/nodekit/output/ 아래에만 설치 — 그 경로만 런타임 이미지로 복사됨"으로
  갱신(예시: "make install DESTDIR=/nodekit/output").
- RecipeRendererTests: 느슨한 Contains 기반 검증 2개를 엄격한 검증으로
  교체 — FROM 2개 존재, 첫 FROM에 " AS builder" 접미사, COPY
  --from=builder 존재, mkdir -p 존재, ENTRYPOINT 부재, USER 문자열
  위치가 두 번째 FROM 이후(문자열 인덱스 비교)임을 확인. R22-B 시점
  테스트는 2-stage 출력에도 우연히 통과할 만큼 느슨해서 실제 보안
  속성(빌드 도구가 새지 않음)을 검증하지 못했음을 인지하고 자발적으로
  강화함.

**실 인프라 검증(seoy 클러스터가 아니라 로컬 buildah bud — Done-when의
"가능하면" 조건 충족, 유닛 테스트만으로 끝내지 않음):**
- `nodekit recipe create --non-interactive --method source-structured`
  → `nodekit validate`(OK) → `nodekit render --out -`로 실제
  build-request.json의 dockerfile_content 추출.
- `buildah bud --network host`로 추출한 Dockerfile을 실제 2-stage
  빌드(로컬 HTTP 서버로 source tarball 제공, `RUN curl` 단계가 호스트
  네트워크로 fetch).
- 최종 이미지(runtime 스테이지)에서 `command -v curl/wget/git/gcc`
  전부 부재 확인, 설치한 바이너리(`/usr/local/bin/hello`) 정상 실행
  확인, `buildah inspect`로 `Config.User == "1000"` 확인.
- 대조 검증: `--target builder`로 builder 스테이지만 별도로 빌드해
  그 안에는 curl이 실제로 존재함을 확인 — runtime 이미지의 curl 부재가
  우연이 아니라 스테이지 분리 때문임을 증명.
- 기존 RenderSourceBuild(legacy) 무변경 — 회귀 테스트 전부 그대로 통과.

최종 결과: 587 / 587 통과(스킵 2 제외), 0 warnings, dotnet format 클린.

⚠ 미해결 그대로 남음(당시 기준, §13 체크리스트 항목 1과 동일): 이번
스프린트는 client-side 렌더링만 다룬다. NodeVault가 dockerfile_content를
그대로 받아들이고(재작성 없음) 런타임 스테이지 내용을 서버에서 강제하지
않으므로, authoring 시점에 "안전한 recipe.json"을 만들 수는 있어도
누군가 손으로 dockerfile_content를 다시 조작해 제출하는 경로를 막지는
못한다. NodeVault Sprint 9/10(당시 unimplemented)이 이 gap의 담당.

**이후 갱신(2026-07-13 같은 날 늦게, 적대적 리뷰 Major-1/Issue #41)**:
NodeVault Sprint 9가 실제로 머지되어(`645c594`) 위 문단이 stale해짐 —
최종 스테이지 RUN의 정적 검사는 이제 서버 쪽에 존재한다("dockerfile_content를
손으로 조작해도 아무도 안 막는다"는 더 이상 사실이 아님). Sprint
10(base image에 이미 포함된 도구 탐지)만 여전히 미구현. 최신 상태는
§13 하단 체크리스트 항목 1과 설계 문서 §2.6 Q5(2026-07-13 갱신) 참조.
```

### Sprint R22-D. SourceBuild 구조화 Intent — RuntimeProfile hygiene advisor (Issue #39)

Goal:

```text
새 kind의 RuntimeProfile/RuntimeProfileImage(최종 스테이지)만 대상으로
하는 non-blocking 휴리스틱 경고를 추가한다. 기존 SourceBuildBaseImageAdvisor는
legacy SourceBuild kind 전용으로 범위를 명확히 한다.
```

Context:

```text
설계: 설계 문서 §10 D-5. 선행: R22-B(#37), R22-C(#38) — RuntimeProfile
필드와 렌더링 로직이 있어야 검사 대상이 생긴다.
```

Tasks:

```text
1. 신규 RuntimeProfileHygieneAdvisor(가칭, src/NodeKit.Cli/) —
   SourceBuildBaseImageAdvisor와 같은 패턴(internal static class,
   Describe(...) → string? null-or-warning).
2. SourceBuildBaseImageAdvisor 클래스 doc 주석 갱신 — "legacy
   RecipeBuildKind.SourceBuild 전용" 명시.
3. RecipeCreateFlow.cs / RecipeCreateCommand.cs에 새 advisor 배선(R20/R21과
   동일한 위치·패턴).
4. 이미지 이름 문자열만으로 내부 도구 존재를 확정하지 않는다 — 경고
   문구에 "추정"임을 표시하고, 실제 검사(NodeVault Sprint 9/10, 이
   저장소 범위 밖)와 명확히 구분.
```

Done when:

```text
- Build: 0 warnings, 0 errors.
- 신규 advisor 단위 테스트 + RecipeCreateCommandTests: 새 kind + risky
  RuntimeProfileImage 조합에서 경고 출력, 저장은 계속 진행(non-blocking).
- SourceBuildBaseImageAdvisorTests 기존 테스트(legacy kind 대상) 전부
  무변경 통과.
- 실제 CLI로 RuntimeProfileImage에 fetch 전용 이미지를 넣었을 때 경고
  출력 확인(차단 아님).
```

**Progress (Sprint R22-D 완료, 2026-07-14, 커밋 `bf3caca`):**

```text
완료:
- RuntimeProfileHygieneAdvisor(신규, src/NodeKit.Cli/): SourceBuildBaseImageAdvisor와
  동일 패턴(internal static class, Describe(...) → string? null-or-warning).
  RecipeBuildKind.SourceBuildStructured + RuntimeProfile == "advanced" +
  RuntimeProfileImage가 curlimages/curl, busybox, alpine/curl, buildpack-deps
  중 하나와 이름이 겹칠 때만 경고 — 큐레이션된 프로필(generic/minimal)은
  이미 buildah로 검증됐으므로 advanced 이스케이프 해치에만 적용.
  경고 문구에 "추정"임을 명시하고 실제 검증(NodeVault Sprint 9 완료/
  Sprint 10 미구현)과 분리 — 이미지 이름만으로 콘텐츠를 확정하지 않음.
- SourceBuildBaseImageAdvisor doc 주석 갱신 — legacy SourceBuild 전용임을
  명시, RuntimeProfileHygieneAdvisor와 체크리스트를 공유하지 않는 이유
  설명(legacy는 BaseImage가 fetch+runtime 역할을 겸하는 게 설계 결함
  그 자체이므로).
- RecipeCreateFlow.cs(interactive) / RecipeCreateCommand.cs(non-interactive)
  양쪽에 동일 위치·패턴으로 배선(R20/R21과 동일).
- 신규 테스트: RuntimeProfileHygieneAdvisorTests 6개(advanced+risky 경고,
  advanced+buildpack-deps 경고, advanced+ordinary null, curated+risky도
  null, non-SourceBuildStructured null, null image null) +
  RecipeCreateCommandTests 1개(advanced RuntimeProfile + fetch 전용
  이미지 → stderr 경고 + exit 0 + 파일 저장 확인).
- 실제 CLI로 advanced+curlimages/curl(경고 발생, exit 0) 및
  minimal(경고 없음, exit 0) 둘 다 직접 실행 확인.
- SourceStructured가 이제 wizard 통합됐다는 사실을 반영해 관련 테스트
  주석의 stale 문구("대화형 wizard에서는 아직 제공하지 않습니다")도
  같이 정리(#42 완료 반영).

623/623 통과(스킵 2), 0 warnings, dotnet format clean. Issue #39 close.
```

### 참고 (R22와 무관, 별도 트리거) — 완료

```text
- NodeVault가 P1(build_events/digest 노출)을 실제로 배포하면 R18의
  fallback 안내를 실제 digest 표시로 승격.
  → 완료(2026-07-13). NodeVault 커밋 `03f5025`("Sprint 7 P1a")가
    BuildEvent에 image_ref/image_digest/spec_referrer_digest/
    integrity_health 필드를 추가했고, NodeKit이 vendored proto를
    재벤더링(`protos/nodevault/v1/nodevault.proto`, NodeVault 원본과
    diff 결과 동일)한 뒤 실제로 소비하도록 배선함:
    `Grpc.BuildEvent`에 4개 필드 추가, `GrpcToolSpecClient.MapWatchEvent`
    에서 매핑, `SubmitCommand.SubmitAsync`가 Succeeded 시 ImageDigest가
    있으면 "이미지 digest: <ref>@<digest>"를 출력하고 legacy fallback
    안내는 건너뜀 — 기존 DigestAcquired/Digest 경로(legacy
    BuildAndRegister 전용)는 손대지 않고 safety-net으로 유지.
    `allow_runtime_tools`/`allow_runtime_tools_reason`(같은 커밋에서
    추가된 다른 필드, Sprint 9 관련)은 의도적으로 UI/CLI에 배선하지
    않음 — R22의 취지(risky tool을 최종 이미지에서 빼는 것)와 반대
    방향의 탈출구이기 때문. 적대적 리뷰 Major-1(Issue #41) 항목 3/4
    완료.
```

**⚠ R22 구현 시 절대 놓치면 안 되는 것 (2026-07-12 사용자 지시로 명시적
추적 요청받음 — 각 스프린트의 "Done when"에도 반영되어 있지만 여기서도
중복 기록):**

```text
1. (구현 완료, 2026-07-13 — 문구는 2026-07-13에 한 번 더 좁혀 갱신됨,
   적대적 리뷰 Major-1/Issue #41) R22-C(#38)에서 실제 2-stage
   렌더링을 구현하고 로컬 buildah bud로 curl 미유출을 실증했다.
   같은 날 NodeVault Sprint 9 P2a(커밋 `645c594`)가 실제로 머지되어
   최종 스테이지 RUN의 risky tool을 서버 쪽에서 정적으로 거부하기
   시작했다 — "NodeVault 서버 쪽 강제가 전혀 없다"는 이전 문구는 더
   이상 정확하지 않다. 여전히 정확한 것: base image에 이미 포함된
   risky tool은 Sprint 10(post-build 이미지 스캔, 미구현)이 있어야
   탐지되므로, client-side 렌더링 + Sprint 9만으로 "SourceBuild 보안
   문제가 완전히 해결됐다"고 발표/기록하지는 말 것. R22-D(#39) 착수
   시에도, 그리고 NodeVault Sprint 10 완료 여부를 확인하기 전까지는
   "base image 콘텐츠까지는 아직 서버가 못 본다"는 좁혀진 문구를
   유지할 것.
2. (완료, 2026-07-13) R22-B(#37)에서 BuildProfile/RuntimeProfile의 실제
   이미지→digest 매핑표를 채웠다 — SourceBuildProfileCatalog(generic/
   minimal), 둘 다 buildah run으로 실제 도구 존재를 확인한 뒤 pinning.
   R22-B Progress 블록 참조.
```
