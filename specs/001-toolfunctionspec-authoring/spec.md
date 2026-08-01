# Feature Specification: ToolFunctionSpec v0.3 Authoring Scope

**Feature Branch**: `001-toolfunctionspec-authoring`

**Created**: 2026-07-23

**Status**: Draft

**Input**: User description: "NodeKit에 ToolFunctionSpec v0.3을 적용하려고 한다. 기준 문서는 ToolFunctionSpec v0.3 Draft(Notion). 먼저 문서와 현재 NodeKit 코드를 분석해서, NodeKit이 담당해야 할 기능 범위의 명세를 작성해줘. 아직 구현하지 말고 요구사항과 제외 범위만 정리해줘."

**용어·소유권 개정 (2026-07-23)**: 최초 초안은 `ToolFunctionDraft`라는 별도 핵심 타입명을 사용했다. 이후 사용자가 명시적으로 소유권과 용어를 정리한 결정에 따라 이 문서 전체를 개정했다. 핵심 변경: (1) NodeKit의 작성 입력은 기존 ToolSpec 흐름과 동일하게 **Recipe**로 부른다 — `ToolFunctionRecipe`. (2) Recipe·빌드 요청·이미지·최종 계약을 각각 다른 이름으로 구분한다: `ToolFunctionRecipe`(NodeKit 작성 입력) → `ToolFunctionBuildRequest`(NodeVault 전달용 wire 요청) → `ToolFunctionImage`(NodeVault가 만드는 실행 이미지) → `ToolFunctionSpec`(승인된 최종 실행 계약). (3) function-image builder의 소유권은 NodeVault로 명시적으로 확정한다. "Draft"는 별도 타입이 아니라 Recipe의 lifecycle 상태(`Draft → Ready → Submitted → Built → Validated → Approved`) 중 하나로 표현한다.

## 전체 흐름 (참고, 비FR)

이번 spec(001)이 다루는 부분은 아래 흐름 전체 중 3번째 줄 하나뿐이다. 나머지는 다른 컴포넌트 소관이며, 승인된 ToolFunctionSpec 이후로는 NodeKit이 전혀 관여하지 않는 파이프라인 실행 영역으로 이어진다 — 이 spec의 경계를 명확히 하기 위해 끝까지 표기한다.

```
[자산 작성 — NodeKit/NodeVault/NodeSentinel]

1. NodeKit:    ToolSpec Recipe 작성 (nodekit recipe create)
2. NodeVault:  Recipe 검증 + ToolSpec image 빌드·확정 — 이미 구현된 기존 경로
               (ResolveToolSpec → SubmitToolBuild → WatchToolBuild)
3. NodeKit:    확정된 ToolSpec image digest를 참조해 ToolFunctionRecipe 작성   ← ★ 이번 spec(001) 범위
               (functionId, 스크립트 참조, command/포트/parameter, fixture 참조,
                예상 결과, enforced 자원, validationRequirements 선언; Draft→Ready)
4. NodeVault:  ToolFunctionBuildRequest 수신 (BuildRequest{kind=TOOLFUNCTIONSPEC})
               → ToolFunction image builder가 base ToolSpec image + nan + 스크립트 결합
               → Harbor push, provenance 기록                                  — 범위 밖 (NodeVault)
5. NodeVault:  NodeSentinel에 EnqueueValidationWork (fixture_set 식별자 전달)   — 범위 밖 (NodeVault)
6. NodeSentinel: L3(K8s dry-run)→L4(smoke)→L5-a(fixture 기반 기능 검증,
               현재는 설계 대비 미구현 상태)→L5-b(보안 스캔) 실행,
               결과를 NodeVault에 회수(gRPC 지향, 현재 REST)                    — 범위 밖 (NodeSentinel)
7. NodeVault:  결과를 ObservedToolFunctionProfile(ToolProfile)로 저장,
               검토·승인 → 최종 ToolFunctionSpec 확정 (digest, catalog/index)   — 범위 밖 (NodeVault)

[승인 이후 — 파이프라인 계획·실행, NodeKit 완전히 범위 밖]

8. NodePalette: 승인된 ToolFunctionSpec + ToolSpec + ToolProfile 요약 노출
9. DagEdit:    ToolFunctionSpec을 노드로 사용해 Pipeline Logic Spec 작성
10. Binding Resolver / Lowering (GAP, 소유자 미정): 논리 입력을 Tori FileBlock /
               Sori Data Image에 연결, Executable Run Spec으로 변환
11. JUMI:      Executable Run Spec 실행, Kubernetes Job 생성/관리
12. nan:       Pod 내부에서 실제 명령 실행 (컨테이너에 포함된 바이너리 — 그 자체가
               K8s data-plane app은 아님), output manifest 작성
13. JUMI/AH:   nan manifest 회수 → AH RegisterArtifact/ResolveHandoff로 노드 간
               중간 산출물 handoff
```

nan이 ToolFunction image에 구체적으로 어떻게 결합되는지(4번 단계의 빌드 스테이지, base image 상속 등 세부 메커니즘)는 이번 결정에서 다루지 않는 별도 결정사항이다. NodeKit의 Recipe는 이 메커니즘을 알 필요가 없다 — NodeVault의 ToolFunction image builder 내부 구현이기 때문이다.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - 새 ToolFunctionRecipe 작성 및 로컬 검증 (Priority: P1)

NodeKit 사용자(도구 작성자)는 이미 확정된 ToolSpec image digest를 기반으로, 그 위에 사용자/실행 스크립트를 얹어 만들 하나의 파이프라인 기능(ToolFunction)에 대한 Recipe를 작성한다. functionId, 스크립트 참조, 구조화된 명령, 입출력 포트, parameter, 샘플 데이터/fixture, 예상 결과, 중간 파일 정책, enforced 자원, 실행 호환성, 필요한 관찰 수준을 채운 뒤 로컬 정적 검증을 통과시켜 Recipe를 `Draft`에서 `Ready` 상태로 전이시킨다.

**Why this priority**: ToolFunctionSpec의 나머지 모든 단계(렌더링, 향후 제출, NodeVault 빌드, dry-run, 승인)는 `Ready` 상태의 Recipe가 먼저 존재해야 가능한 전제 조건이다. 이 스토리 하나만으로도 "기능 계약을 구조화된 형태로 문서화하고 명백한 오류를 조기에 잡는다"는 핵심 가치를 전달한다.

**Independent Test**: 확정된 ToolSpec image digest만 주어진 상태에서, 사용자가 처음부터 끝까지 Recipe를 작성해 `Ready` 상태에 도달할 수 있는지로 독립적으로 테스트 가능하다.

**Acceptance Scenarios**:

1. **Given** 이미 확정된 `toolSpecDigest`와 `baseToolImageDigest`, **When** 사용자가 이를 참조하여 새 ToolFunctionRecipe를 시작하면, **Then** 두 값 모두 read-only 참조로 Recipe에 고정되고 편집할 수 없으며 Recipe는 `Draft` 상태로 생성된다.
2. **Given** 작성 중인 Recipe, **When** 사용자가 명령을 `bash -c "..."`같은 단일 셸 문자열로 입력하면, **Then** 시스템은 이를 거부하고 executable/arguments 배열로 구조화하라고 안내한다.
3. **Given** 모든 필수 필드(functionId, 명령, 최소 1개 입력 포트, 최소 1개 출력 포트, 샘플 데이터/fixture 참조, enforced 자원)가 채워진 Recipe, **When** 사용자가 검증을 실행하면, **Then** 검증이 통과하고 Recipe 상태가 `Ready`로 전이된다.
4. **Given** 출력 포트 이름과 입력 포트 이름이 동일한 Recipe, **When** 검증을 실행하면, **Then** 포트 이름 충돌 오류가 표시되고 상태는 `Draft`에 머무른다.
5. **Given** enforced 자원에서 memory limit이 memory request보다 작게 입력된 Recipe, **When** 검증을 실행하면, **Then** limit이 request 이상이어야 한다는 오류가 표시된다.

---

### User Story 2 - Ready 상태 Recipe를 빌드 요청 미리보기로 렌더링 (Priority: P2)

사용자는 `Ready` 상태의 Recipe를, NodeVault가 향후 받게 될 `ToolFunctionBuildRequest`와 동일한 구조의 canonical JSON으로 렌더링하여 미리 확인한다. 이 결과물은 로컬 파일로 export될 뿐, 어디로도 전송되지 않으며 Recipe는 `Submitted` 상태로 전이되지 않는다.

**Why this priority**: 실제 제출 경로가 열리기 전에도, 작성자와 리뷰어가 "이 빌드 요청이 최종적으로 어떤 모양이 될지"를 미리 확인하고 팀 내에서 공유·검토할 수 있어야 한다. 실제 제출(이번 spec 범위 밖)보다 먼저 가치를 준다.

**Independent Test**: `Ready` 상태 Recipe 하나를 렌더링 명령에 입력했을 때, 문서화된 필드 구조를 갖춘 JSON 파일이 로컬에 생성되는지로 독립적으로 테스트 가능하다.

**Acceptance Scenarios**:

1. **Given** `Ready` 상태의 Recipe, **When** 사용자가 렌더링을 실행하면, **Then** 모든 선언 필드가 포함된 canonical JSON 파일이 로컬에 생성되고 Recipe 상태는 `Ready`로 유지된다.
2. **Given** 아직 `Draft` 상태인 Recipe, **When** 사용자가 렌더링을 시도하면, **Then** 시스템은 렌더링을 거부하고 먼저 검증을 통과해 `Ready` 상태가 되어야 한다고 안내한다.
3. **Given** 렌더링된 JSON 미리보기, **When** 사용자가 이를 NodeVault로 제출하려는 동작을 시도하면, **Then** 시스템은 실제로 전송하지 않고 "NodeVault ToolFunction 빌드 게이트가 아직 열려 있지 않다"는 명확한 안내를 표시하며 Recipe는 `Ready`에 머무른다.

---

### User Story 3 - 기존 ToolSpec 마법사에서 새 플로우로 이관 (Priority: P3)

기존 `recipe create` 마법사에서 Inputs/Outputs/Command placeholder를 입력하던 사용자가, 해당 입력 단계 대신 이 기능을 사용하라는 안내를 받는다.

**Why this priority**: 사용자 경험의 일관성을 위한 정리 작업이며, P1/P2가 실제로 동작한 이후에만 안전하게 전환할 수 있는 후행 작업이다.

**Independent Test**: `recipe create` 마법사를 처음부터 끝까지 실행했을 때 Inputs/Outputs/Command 입력 단계가 더 이상 나타나지 않고, 대신 새 플로우에 대한 안내 메시지가 표시되는지로 테스트 가능하다.

**Acceptance Scenarios**:

1. **Given** `recipe create` 마법사를 실행하는 사용자, **When** 기존에 포트/명령을 입력하던 단계에 도달하면, **Then** 해당 단계는 더 이상 입력을 요구하지 않고 새 ToolFunctionRecipe 플로우에 대한 안내로 대체되어 있다.
2. **Given** 이미 저장된 과거 `ToolDefinition`(Inputs/Outputs/Command 값을 가진), **When** 시스템이 이를 다시 불러오면, **Then** 해당 값들은 무시되거나 마이그레이션 안내와 함께 표시되며 오류 없이 처리된다.

### Edge Cases

- 존재하지 않거나 아직 확정되지 않은 `toolSpecDigest`/`baseToolImageDigest`를 참조하려고 하면 어떻게 되는가? → 참조 검증에서 명확히 거부되어야 한다.
- 명령을 raw shell 문자열로 입력하면 어떻게 되는가? → 구조화 강제 규칙에 의해 검증 단계에서 차단된다.
- 입력 포트와 출력 포트 이름이 중복되면 어떻게 되는가? → 포트 이름 유일성 규칙 위반으로 검증 실패.
- enforced 자원에서 limit이 request보다 작으면 어떻게 되는가? → 검증 실패, 구체적 필드 지목.
- `functionId`가 형식 규칙(공백/허용 문자/대소문자 규칙 등)을 어기면 어떻게 되는가? → 검증 실패, 형식 안내 메시지 표시.
- 같은 `functionId`로 여러 revision의 Recipe 파일을 동시에 만들면 어떻게 되는가? → 로컬 파일 경로/이름 충돌을 감지하고 명확히 안내한다(덮어쓰기 묵인 금지).
- 사용자가 (아직 존재하지 않는) 제출 동작을 시도하면 어떻게 되는가? → 오류 코드 노출이나 무응답 대신 "NodeVault ToolFunction 빌드 게이트 미개방"이라는 사용자 친화적 안내를 표시하며 상태 전이는 일어나지 않는다.
- 필수 포트나 자원 필드가 비어 있는 상태로 렌더링을 시도하면 어떻게 되는가? → 렌더링이 거부되고 어떤 필드가 누락됐는지 표시된다.
- 사용자가 로컬에서 직접 Recipe 상태를 `Submitted`/`Built`/`Validated`/`Approved`로 바꾸려 하면 어떻게 되는가? → 이 상태들은 NodeVault/NodeSentinel의 실제 처리 결과를 반영하는 자리이며, 이번 기능은 이 상태로의 전이 수단을 제공하지 않는다.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: 시스템은 이미 확정된 `toolSpecDigest`(빌드에 사용된 resolved ToolSpec 자체의 식별자)와 `baseToolImageDigest`(그 결과로 실제 빌드된 이미지의 식별자)를 각각 read-only 참조로 사용하여 새 ToolFunctionRecipe를 생성할 수 있어야 한다. 이 두 값은 서로 다른 식별자다 — 기존 NodeKit↔NodeVault 흐름에서 `toolSpecDigest`는 `ResolveToolSpec`/`SubmitToolBuild`가 사용하는 입력값이고, `baseToolImageDigest`는 빌드 완료 시 `WatchToolBuild`가 반환하는 `BuildEvent.image_digest`에서 얻는다.
- **FR-002**: 시스템은 유효한 `toolSpecDigest`와 `baseToolImageDigest` 참조 없이는 ToolFunctionRecipe 생성을 거부해야 한다.
- **FR-003**: 시스템은 `functionId`, `revision`, 표시 정보(이름/설명/카테고리/태그)를 사용자 입력으로 받아야 한다.
- **FR-004**: 시스템은 사용자/실행 스크립트를 로컬 파일 참조로 캡처해야 하며, 이는 기존 `ToolDefinition.Script` 필드와 별개의 개념으로 다뤄야 한다. nan을 이 스크립트에 결합하는 방식은 NodeVault ToolFunction image builder의 내부 구현이므로 Recipe는 nan 관련 필드를 요구하지 않는다.
- **FR-005**: 시스템은 명령을 executable, 순서 있는 arguments 배열, workingDirectory, environment allowlist, 성공 exit code 목록, timeout 정책으로 구조화하여 입력받아야 한다.
- **FR-006**: 시스템은 명령이 단일 raw shell 문자열로 입력되는 것을 거부해야 한다.
- **FR-007**: 시스템은 하나 이상의 named 입력 포트(데이터 종류/형식, cardinality, 필수/선택 여부, 경로 배치 규칙, companion file 선언)를 입력받아야 한다.
- **FR-008**: 시스템은 하나 이상의 named 출력 포트(형식, cardinality, 경로/glob, 완료 검증법, downstream 호환성 메모)를 입력받아야 한다.
- **FR-009**: 시스템은 dry-run에 사용할 샘플 데이터/fixture에 대한 참조(로컬 경로 또는 content digest)를 입력받아야 한다. 실제 dry-run 실행 자체는 이 기능의 범위 밖이다(NodeSentinel 소관).
- **FR-010**: 시스템은 각 출력 포트에 대한 예상 결과(기대값 또는 비교 규칙)를 선언 입력으로 받아야 한다. 실제 비교 실행과 판정은 이 기능의 범위 밖이다.
- **FR-011**: 시스템은 중간/숨은 파일 정책(ephemeral/cache/checkpoint/sidecar-output/sensitive-temp)을 파일 또는 패턴 단위로 선언받아야 한다.
- **FR-012**: 시스템은 parameter 계약(이름, 타입, 기본값, 허용 범위, 필수 여부, CLI 인자 매핑 규칙, 상호배타 조합)을 입력받아야 한다.
- **FR-013**: 시스템은 자원 계약 중 enforced tier(CPU/메모리/스토리지 request·limit, 최대 실행시간, 병렬성)만 사용자 입력으로 받아야 한다.
- **FR-014**: 시스템은 자원 observed tier와 recommended tier를 사용자가 직접 입력하지 못하도록 막아야 한다(이 값들은 ToolProfile 관찰 근거에서만 나올 수 있으며 이번 범위에서 다루지 않는다).
- **FR-015**: 시스템은 실행 환경/호환성(지원 플랫폼, writable path, network policy, root/capability 필요 여부)을 입력받아야 한다.
- **FR-016**: 시스템은 `minimumObservationLevel`과 `requiredCoverage` 플래그들로 구성된 validation requirements를 선언 입력으로 받아야 한다.
- **FR-017**: 시스템은 `Ready` 상태로 전이하기 전에 다음 규칙을 포함한 정적 검증을 수행해야 한다: `functionId` 형식, 명령 구조화 강제, 포트 이름 유일성, enforced 자원의 limit ≥ request, 필수 필드(샘플 데이터/fixture 참조 포함) 완결성.
- **FR-018**: 시스템은 검증에 실패한 Recipe를 `Ready`로 전이시키지 않고, 실패한 구체적 필드/규칙을 사용자에게 표시해야 한다.
- **FR-019**: 시스템은 `Ready` 상태의 Recipe만 canonical JSON(`ToolFunctionBuildRequest` 미리보기, NodeVault ToolFunction 빌드 계약과 동일한 wire shape)으로 렌더링/export할 수 있어야 한다. 이 wire shape는 새로운 proto 메시지를 정의하지 않고, 기존 `ToolSpecRequest`/`BuildRequest`(`kind = BUILD_KIND_TOOLFUNCTIONSPEC`)를 재사용하는 것을 전제로 설계한다.
- **FR-020**: 시스템은 이 기능의 범위 내에서 렌더링된 `ToolFunctionBuildRequest` 미리보기를 실제 gRPC로 NodeVault에 전송하지 않아야 하며, Recipe를 `Submitted` 이후 상태로 전이시키지 않아야 한다.
- **FR-021**: 시스템은 사용자가 제출을 시도하는 진입점을 노출할 경우, 실제 전송 대신 "NodeVault ToolFunction 빌드 게이트가 아직 열려 있지 않다"는 명확한 안내를 표시해야 한다.
- **FR-022**: 시스템은 ToolFunctionRecipe를 로컬 파일로 저장해야 하며, 작성과 검증에 NodeVault로의 실시간 연결을 요구하지 않아야 한다.
- **FR-023**: 시스템은 같은 `functionId`/`revision` 조합으로 기존 Recipe 파일과 충돌이 발생하면 이를 감지하고 사용자에게 명확히 알려야 하며, 묵시적으로 덮어써서는 안 된다.
- **FR-024**: 시스템은 기존 `recipe create` 마법사의 Inputs/Outputs/Command 입력 단계를 제거하고, 해당 지점에서 새 ToolFunctionRecipe 플로우로 사용자를 안내해야 한다.
- **FR-025**: 시스템은 Inputs/Outputs/Command 값을 가진 기존 저장 데이터를 다시 불러올 때 오류 없이 처리해야 한다(무시하거나 마이그레이션 안내를 표시).

### Key Entities *(include if feature involves data)*

- **ToolFunctionRecipe** (NodeKit): NodeKit에서 사용자가 작성하는 빌드·검증 입력. 기존 `RecipeDocument`(ToolSpec Recipe)와 개념적으로 대응하는 새로운 저작 모델이지만, `RecipeBuildKind`(Conda/Micromamba/Container 등)와 같은 "빌드 방식 선택"이 없다는 점에서 구조가 다르므로 별도 모델로 취급한다. functionId, revision, 참조 `toolSpecDigest`/`baseToolImageDigest`, 스크립트 참조, 명령/포트/parameter/fixture/예상 결과/자원/환경/validation requirements를 담으며, `Draft → Ready → Submitted → Built → Validated → Approved` lifecycle 상태를 가진다. 아직 최종 ToolFunctionSpec이 아니다.
- **ToolFunctionBuildRequest** (NodeKit → NodeVault, wire 개념): `Ready` 상태의 ToolFunctionRecipe를 NodeVault에 전달하기 위한 요청 형태. 이번 기능은 이 payload를 렌더링/미리보기만 하고 실제로 전송하지 않는다.
- **ToolFunctionImage** (NodeVault, 참조): NodeVault가 기반 ToolSpec image를 확장하여 생성하는 실행 이미지. NodeKit은 만들지 않는다.
- **ToolFunctionSpec** (NodeVault, 참조): 이미지 빌드, dry-run, 관찰, 검토와 승인을 통과한 최종 실행 계약. NodeKit은 이 객체를 만들지 않으며 이번 기능에서 조회하지도 않는다.
- **CommandContract**: executable, 순서 있는 arguments, workingDirectory, environment allowlist, 성공 exit code, timeout 정책으로 구성된 구조화된 실행 명령.
- **Port (Input/Output)**: 파이프라인 노드 간 데이터 연결을 위한 이름 있는 자리. 데이터 형식, cardinality, 필수 여부, companion file, 경로 배치 규칙 등을 갖는다.
- **FixtureReference / ExpectedResult**: dry-run에 사용할 샘플 데이터/fixture 참조와, 각 출력 포트에 대한 기대값 또는 비교 규칙. 실제 dry-run과 비교 실행은 NodeSentinel 소관이며 이 기능은 선언만 담당한다.
- **ParameterContract**: 파일이 아닌 값에 대한 계약. 타입, 기본값, 허용 범위, 필수 여부, CLI 인자 매핑, 상호배타 조합을 표현한다.
- **IntermediateFilePolicy**: 중간/숨은 파일의 보존 정책 분류(ephemeral/cache/checkpoint/sidecar-output/sensitive-temp).
- **ResourceContract (enforced tier)**: 운영 정책이 실제로 적용할 CPU/메모리/스토리지 request·limit, 최대 실행시간, 병렬성. observed/recommended tier는 이 기능에서 다루지 않는다.
- **ValidationRequirements**: 승인에 필요한 관찰 수준(`minimumObservationLevel`)과 커버리지 항목(`requiredCoverage`) 선언. 특정 관찰 기술을 지정하지 않는다.
- **`toolSpecDigest` / `baseToolImageDigest` 참조 (read-only)**: 기존 NodeKit ToolSpec 산출물에 대한 두 개의 서로 다른 링크 — `toolSpecDigest`는 resolved ToolSpec 자체의 식별자(`ResolveToolSpec`/`SubmitToolBuild` 입력), `baseToolImageDigest`는 그 빌드 결과로 실제 생성된 이미지의 식별자(`BuildEvent.image_digest`). ToolFunctionSpec v0.3 §6.1 데이터 모델의 필드명과 일치시켰다. 이 기능에서 생성하거나 수정하지 않는다.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 확정된 ToolSpec image digest가 주어졌을 때, 사용자는 완전한 ToolFunctionRecipe를 작성해 `Ready` 상태에 도달하는 과정을 15분 이내에 마칠 수 있다.
- **SC-002**: 구조화되지 않은(raw shell) 명령 입력은 100%의 경우 검증 단계에서 차단되고, 실패 원인이 함께 표시된다.
- **SC-003**: 렌더링된 `ToolFunctionBuildRequest` 미리보기를 확인하는 사용자 중 90% 이상이 추가 설명 없이 필드 라벨만으로 최종 계약의 각 구성요소(명령/포트/자원/환경/fixture)가 무엇을 의미하는지 식별할 수 있다.
- **SC-004**: 기존 `recipe create` 마법사에서 새 플로우로 넘어가는 과정에서, 과거 Inputs/Outputs/Command 데이터로 인해 마법사가 중단되거나 오류를 내는 사례가 0건이다.
- **SC-005**: 사용자가 제출을 시도하는 모든 경우(100%)에 명확한 "NodeVault ToolFunction 빌드 게이트 미개방" 안내를 받으며, 원인 불명의 오류나 무응답은 발생하지 않는다.

## Assumptions

- ToolFunctionRecipe는 `RecipeDocument`와 동일하게 로컬 파일로 저장되며, 작성 및 검증 과정에서 NodeVault와의 실시간 연결을 필요로 하지 않는다.
- 이 기능을 위한 새 CLI 명령 그룹(예: `nodekit function-recipe create|validate|render`)은 기존 `nodekit recipe`/`submit` 명령 구조를 따른다는 것을 전제로 하며, 정확한 명명과 세부 UX는 이후 계획 단계에서 확정한다.
- 사용자/실행 스크립트는 Recipe 안에 직접 내용을 입력하는 대신 로컬 파일 경로로 참조되며, 이는 기존 Dockerfile 내용/환경 spec을 파일에서 읽어오는 현재 패턴과 일치한다.
- 기존 `ToolDefinition.Inputs`/`Outputs`/`Command` 필드와 이를 채우던 `recipe create` 마법사 단계는 이 기능으로 대체되며, 사용자와 이미 이 방향으로 확정했다.
- 이 기능이 참조하는 ToolSpec image는 이미 기존 ToolSpec Recipe → NodeVault 빌드 경로를 통해 확정되었다고 가정하며, 이 기능은 NodeVault에 대해 digest를 재검증하는 새로운 gRPC 읽기 경로를 추가하지 않는다.
- 참조 데이터(DataDefinition) 쪽의 ToolFunctionSpec 대응 개념은 이 기능의 범위에 포함하지 않는다. ToolFunctionSpec v0.3 문서 자체가 Tool 전용으로 설계되어 있다.
- Recipe의 `Submitted`/`Built`/`Validated`/`Approved` 상태는 NodeVault/NodeSentinel의 실제 처리 결과를 반영하기 위한 자리로 스키마에 예약하되, 이번 기능은 이 상태들로의 실제 전이 수단(제출 API 연동)을 구현하지 않는다.

### 의존성 및 위험 (Dependencies & Risks)

- **function-image builder 소유권 확정 (2026-07-23)**: 이전에 "ToolFunctionSpec v0.3/ToolProfile v0.3 두 문서 모두 function-image builder의 명시적 소유자가 없다"는 공백을 발견했었는데, 사용자가 이 소유권을 **NodeVault**로 명시적으로 확정했다 — admission, 기반 ToolSpec image digest 검증, 빌드 정책·재현성 검증, nan과 스크립트 결합, 빌드 오케스트레이션, Harbor push, provenance 기록, lifecycle 관리 전부 NodeVault 책임. 실제 빌드 실행기가 NodeVault 프로세스 내부인지 별도 worker/builder service인지는 결정되지 않았지만, 그 선택은 NodeKit spec에 영향을 주지 않는다. 이 결정은 NodeKit 쪽에서 내린 것이며, NodeVault issue #19(아래)에 이 방향을 반영하는 후속 조율이 필요하다.
- **NodeVault 측 등록 경로는 여전히 공식적으로 미확정 (issue [#19](https://github.com/HeaInSeo/NodeVault/issues/19))**: 위 소유권 확정은 이번 대화에서 NodeKit이 채택한 방향이며, NodeVault 저장소의 issue #19("ToolFunctionSpec metadata 등록 경로 설계/구현")는 여전히 OPEN 상태이고 완료 판정 항목이 전부 미체크다. NodeVault 쪽이 이 방향을 공식적으로 받아들였는지 별도 확인이 필요하다. FR-019(canonical JSON 렌더링)는 이 조율이 끝나기 전까지 **best-effort 미리보기**로 취급해야 하며, NodeVault 결정이 바뀌면 wire shape도 함께 바뀔 수 있다.
- **FR-019 wire 재사용 가정이 실제 proto로 재확인됨**: `protos/nodevault/v1/nodevault.proto`에 별도 "ToolFunctionBuildRequest" 메시지는 없고, 기존 `BuildRequest`(`kind = BUILD_KIND_TOOLFUNCTIONSPEC`, 이미 예약된 `base_image_digest`(17)/`script`(6) 필드 포함)를 그대로 재사용하는 구조로 이미 모델링되어 있다 — FR-019의 가정이 맞다. 다만 이 discriminated-union 방식이 이번 결정(NodeVault가 ToolFunction image builder 전체를 소유)과 완전히 합의된 것인지는 issue #19 해결 시 재확인이 필요하다.
- **NodeVault→NodeSentinel 결과 회수 경로의 실제 이름**: NodeVault가 dry-run 결과를 받는 지점은 gRPC `ValidationResultService.SubmitToolCheckRecord`/`SubmitToolScanRecord`로 proto에 정의돼 있다. 현재 NodeSentinel 코드(`pkg/vaultclient/client.go`)는 REST(`POST /v1/validation/check-records`, `POST /v1/validation/scan-records`)로 이를 호출하고 있지만, **사용자 확인에 따르면 내부 방향은 gRPC로 수렴할 예정**이다 — NodeVault issue [#33](https://github.com/HeaInSeo/NodeVault/issues/33)("[Catalog] gRPC 정본 계약 도입 및 REST 호환 계층으로 점진 전환")이 이 "gRPC 정본 + REST는 과도기 호환 계층" 패턴을 NodeVault 서비스 전반의 방향으로 이미 등록해뒀다(해당 이슈 자체는 Catalog 범위지만 동일 아키텍처 패턴 — gRPC service와 REST handler가 같은 application service를 호출 — 을 참고 근거로 인용). 또한 NodeSentinel→NodeVault 이 호출은 NodeKit이 외부에서 붙는 `ResolveToolSpec`/`SubmitToolBuild`류 gRPC와 달리 **Kubernetes 클러스터 내부 CNI 네트워크를 통한 서비스 간 통신**이라는 점도 확인했다 — 전송 계층이 다르므로 이번 spec의 FR-020(외부 제출 안 함)과는 별개 관심사다. 이번 spec은 이 경로에 직접 관여하지 않지만, 후속 spec(ToolProfile 관리자 화면 등)이 이 handoff를 참조할 때는 gRPC 쪽을 정본으로 삼고 REST는 마이그레이션 대상으로 취급해야 한다.
- **NodeSentinel의 실제 구현이 설계 문서보다 크게 뒤처져 있음** — FR-009/FR-010의 target-state 성격을 강화하는 근거: NodeSentinel 저장소(`/opt/go/src/github.com/HeaInSeo/NodeSentinel`)의 dry-run 관찰 단계(L5-a)는 설계 문서(`docs/NODESENTINEL_VALIDATION_FLOW_SPEC_v0.1.md`)와 달리 `pkg/worker/l5a.go`에서 `l5aCommand = "/bin/sh -c true"`로 하드코딩되어 있고, fixture 로딩·per-port IO 관찰·CPU/메모리/디스크 관찰·declared-vs-observed comparator 로직이 전혀 구현되어 있지 않다(관찰 항목은 command/exitCode/duration뿐). 즉 FR-009(fixture 참조)와 FR-010(예상 결과 선언)은 지금 시점에는 **받아줄 소비자가 사실상 없는 target-state 선언**이다 — 문서화된 설계는 유효하지만 실제 배선까지는 NodeSentinel 쪽에 상당한 후속 구현이 필요하다. NodeKit 쪽 FR 자체는 이 격차와 무관하게 유효하다(선언 필드를 만드는 것 자체가 이번 spec의 목적이므로).
- **NodeSentinel은 Recipe류 문서를 직접 소비하지 않음**: 설계 문서에 따르면 NodeSentinel은 이미지 참조 + `fixture_set` 식별자(NodeVault가 관리하는 이름, fixture 원본 내용이 아님) + `requested_actions`만 받는다. 즉 FR-009/FR-010에서 선언하는 fixture 참조와 예상 결과는 NodeSentinel에 직접 전달되는 게 아니라, NodeVault의 fixture-set 등록 메커니즘을 거쳐야 한다 — 이 데이터 흐름은 향후 `ToolFunctionBuildRequest` 제출 경로를 설계할 때 반영해야 할 세부사항으로 남겨둔다.
- **사용자 제안 용어를 기존 문서와 대조해 정정한 부분 (2026-07-23)**: 사용자의 §1 흐름 설명은 "확정된 ToolSpec image digest"를 단일 참조처럼 서술했지만, 원본 ToolFunctionSpec v0.3 §6.1 데이터 모델과 NodeVault 실제 proto를 대조한 결과 이는 서로 다른 두 식별자다 — `toolSpecDigest`(resolved ToolSpec 자체 식별자, `ResolveToolSpec`/`SubmitToolBuild`가 사용)와 `baseToolImageDigest`(그 빌드로 실제 생성된 이미지 식별자, `WatchToolBuild`의 `BuildEvent.image_digest`에서 얻음). 이 spec은 원래 초안(용어 개정 전)에서 이미 두 필드를 구분하고 있었으므로, 이번 개정에서 하나로 뭉뚱그려졌던 FR-001/FR-002/Key Entities를 다시 두 필드로 분리했다. 나머지 사용자 제안 용어(`ToolFunctionRecipe`/`ToolFunctionBuildRequest`/`ToolFunctionImage`/`ToolFunctionSpec`, lifecycle 상태명)는 기존 저장소 문서와 대조해도 특별한 충돌이 없었다 — `ToolFunctionBuildRequest`가 실제로는 새 proto 메시지가 아니라는 점만 FR-019에 이미 명시해 반영했다.
- **명명 충돌 위험**: NodeVault proto에는 이미 무관한 `enum RecipeVariant`(`RECIPE_VARIANT_CONDA/MICROMAMBA/PACKAGE_MIRROR/BIOCONTAINER`, `ResolveRecipe`용)가 존재하고, NodeKit 자체도 `RecipeVariant`(현재 코드) / `RecipeBuildKind`(설계 문서 `docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_TERMINOLOGY_PATCH.md`가 제안한 리네임, 아직 코드에는 미반영)를 쓰고 있어 "Variant" 계열 이름이 이미 두 번 겹친다. `ToolFunctionRecipe`에 내부 discriminator enum이 필요해지면 bare `Variant`/`RecipeVariant`는 피하고 더 구체적인 이름을 쓴다.
- **nan 결합 메커니즘은 이번 결정에서 의도적으로 유보, 단 3개 저장소에서 동일한 미해결 질문이 교차 확인됨**: nan이 ToolFunction image에 정확히 어떤 방식(base image 상속 vs 빌드 스테이지 주입 등)으로 결합되는지는 이번 소유권/용어 정리에서 다루지 않는 별도 결정사항이다. 이 질문은 서로 다른 3개 저장소 문서에서 독립적으로 "NodeVault 또는 NodeKit 중 누가 base image를 패키징하는가"로 미확정 상태였다: `node-artifact-runtime`(nan) 자체 문서("may be handled by NodeVault or NodeKit"), NodeVault `PLATFORM_SCHEDULE.md`(미결정 사항 표), JUMI `docs/JUMI_NODE_RUNTIME_BASE_IMAGE_PLAN.md`(2026-05-16, §4/§7 — "base image packaging과 publish는 장기적으로 NodeKit 또는 NodeVault 계열 repo가 맡는 것이 맞다"). 이번 결정으로 **ToolFunction image builder 전체(nan 결합 포함)가 NodeVault 소관**으로 확정되었으므로, 이 세 문서가 공통으로 남겨둔 공백을 NodeKit 쪽에서는 닫힌 것으로 취급한다 — 다만 NodeVault·JUMI 트래커 자체에는 아직 반영되지 않았을 수 있다. NodeKit의 Recipe는 이 메커니즘을 몰라도 되도록 설계했다(FR-004).
- **JUMI/AH와 nan의 실제 관계 (참고, NodeKit 범위 밖)**: `JUMI_NODE_RUNTIME_BASE_IMAGE_PLAN.md` §4와 `JUMI_AH_NAN_INTEGRATION_REVIEW.md` 가드레일에 따르면 nan은 output manifest만 작성하고 AH에 직접 등록하지 않는다 — JUMI가 manifest를 회수해 AH `RegisterArtifact`/`ResolveHandoff`를 호출한다("nan은 AH에 직접 RegisterArtifact하지 않는다", "nan은 Kubernetes API를 호출하지 않는다"). 이는 승인된 ToolFunctionSpec이 실제 파이프라인에서 실행되는 단계이며, 이번 spec의 Out of Scope에 있는 "JUMI 실행, AH artifact handoff, nan Pod 내부 실행"과 일치한다 — 새로 확인했을 뿐 범위 판단은 바뀌지 않는다.
- **플랫폼 전체 아키텍처 지도로 NodeKit 경계 재확인, 이후 정본 문서로 승격됨**: 최초에는 2026-07-21 설계 대화로 전달받은 텍스트(파일 미보관)를 근거로 삼았으나, 이후 `platform-docs/PLATFORM_CANONICAL_DESIGN_v1.0.md`(2026-07-23~24, NodeKit·NodeVault·NodeSentinel·NodePalette·Sori·Tori·artifact-handoff·nan·JUMI·spawner·dag-go·bori·kube-slint 13개 저장소의 **현재** docs/코드를 직접 재조사)로 대체·승격됐다 — 이 문서가 이제 컴포넌트 간 경계 판단의 정본이다. §3(컴포넌트별 소유·비소유)이 NodeKit 책임을 "ToolDefinition/ToolFunctionRecipe 저작, L1 검증, gRPC 클라이언트"로 한정하고, 승인된 ToolFunctionSpec 이후의 흐름(§6) — DagEdit의 Pipeline Logic Spec 저장, Binding Resolver/Lowering(소유자 미정 GAP), JUMI 실행, AH의 아티팩트 handoff, nan의 Pod 내부 실행 — 은 전부 다른 컴포넌트 또는 아직 소유자가 없는 GAP이라고 명시한다. 이번 spec의 범위 판단과 일치하며, 향후 spec에서도 이 경계를 넘지 않아야 한다. 이 정본 문서의 §8은 컴포넌트 간 미해결 충돌(문서뿐 아니라 실제 코드로 검증)도 함께 추적한다 — NodeKit 작업과 직접 관련된 것은 nan 결합 메커니즘 미확정(§4.3, 위 항목과 동일 결론) 정도이고 나머지(attemptID 발급 주체, dag-go 소비자, bori 관리 범위)는 NodeKit spec에 영향 없다.
- **"ToolProfile v0.3 Draft" 문서 확인 완료**: 정식 스키마명은 `ObservedToolFunctionProfile`(별칭 ToolProfile), NodeVault `application/vnd.nodevault.toolprofile.v1+json` OCI referrer로 저장되며 NodeSentinel이 dry-run 관찰·제출을 담당한다. §9(사용자 노출 원칙)이 **"NodeKit 관리자 화면: Candidate·실패·무효 Profile을 포함한 전체 이력, 재검증, 승인·무효화 기능을 제공한다"**고 NodeKit 몫을 구체적으로 지정하고 있다. 이는 이전에 "declared/observed/proposed 리뷰 UI"로 뭉뚱그려 제외했던 범위보다 더 명확한 후속 기능 정의이지만, 여전히 NodeVault 쪽 read/write API가 없어야 구현 가능하므로 **이번 spec에서는 계속 제외**하고, 다음 후속 spec의 출발점으로 이 §9 정의를 남겨둔다.
- **소스 문서 자체의 결함**: 원본 ToolProfile v0.3 Draft에 헤더 번호 중복("## 6." 두 번 사용) 등 편집 결함이 있고, ToolFunctionSpec v0.3과 ToolProfile v0.3이 동일한 미해결 질문(nan 결합 방식)을 서로 참조 없이 중복 등록하고 있었다. 이번 소유권 결정으로 nan 결합 방식에 대한 실무적 답은 "NodeVault 내부, NodeKit 무관"으로 좁혀졌지만, 원본 Notion 문서들 자체의 정정은 이 spec의 범위가 아니다.

### 명시적 제외 범위 (Out of Scope)

다음 항목은 이 기능의 범위에서 명시적으로 제외한다(다른 컴포넌트 소관이거나 선행 조건이 아직 없기 때문):

- NodeVault의 ToolFunction image builder(admission, 기반 digest 검증, 빌드 정책·재현성 검증, nan+스크립트 결합, 빌드 오케스트레이션, Harbor push, provenance, lifecycle 관리) — NodeVault 소관.
- `BUILD_KIND_TOOLFUNCTIONSPEC` 빌드 게이트 해제 — NodeVault 소관.
- 실제 `ToolFunctionBuildRequest` gRPC 제출 경로(`SubmitToolBuild` 등 기존 RPC 재사용 여부 포함) — 게이트 개방 후 별도 spec.
- dry-run 실행과 관찰(NodeSentinel) — NodeSentinel 소관.
- declared/observed/proposed 차이를 보여주는 리뷰 UI, ToolProfile v0.3 §9가 정의하는 "NodeKit 관리자 화면"(Candidate/실패/무효 Profile 이력, 재검증 트리거, 승인·무효화)과 이를 위한 ToolProfile evidence 읽기/쓰기 API — 후속 기능으로 분리.
- canonicalization, `toolFunctionSpecDigest` 계산, 승인 상태 전이 강제, 불변 저장, catalog/index 갱신 — NodeVault 소관.
- NodePalette에서의 노출 — 별도 애플리케이션(NodePalette) 소관.
- DagEdit의 Pipeline Logic Spec 저장, Binding Resolver(Tori/Sori 연결), Lowering(Executable Run Spec 생성), JUMI 실행, AH artifact handoff, nan Pod 내부 실행 — 각각 별도 컴포넌트 소관이다. 이 중 Binding Resolver는 `PLATFORM_CANONICAL_DESIGN_v1.0.md` §6-1(2026-07-24 정정)에 따르면 완전한 GAP은 아니다 — artifact-handoff의 `ResolveBinding` 메커니즘 자체는 이미 설계·구현되어 있고(`node_local`/`http` 백엔드는 라이브 스모크 검증까지 완료), 실제로 없는 건 그 Source Registry에 꽂을 Tori-FileBlock/Sori-DataImage 타입 백엔드뿐이다. Lowering(authored 의도 → Executable Run Spec 변환 알고리즘)과 파이프라인 저장(Pipeline Store 실제 구현)은 여전히 소유자·구현이 없는 순수 GAP이다. 어느 경우든 NodeKit 범위 밖이라는 결론은 동일하다.
- 자원 계약의 observed/recommended tier 계산 — ToolProfile 통계 근거가 필요하며 이 기능의 범위 밖.
- nan을 ToolFunction image에 실제로 결합하는 세부 빌드 메커니즘 — NodeVault 내부 구현이며, 이번 소유권/용어 정리와 별개로 유보된 결정사항.
- Kubernetes JobSpec/스케줄러 연동, 관찰 기술(eBPF 등)의 구체적 구현 — 다른 컴포넌트 소관.
