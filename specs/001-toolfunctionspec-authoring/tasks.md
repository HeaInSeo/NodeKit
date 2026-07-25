---

description: "Task list for ToolFunctionSpec v0.3 Authoring Scope"
---

# Tasks: ToolFunctionSpec v0.3 Authoring Scope

**Input**: Design documents from `/specs/001-toolfunctionspec-authoring/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/cli-function-recipe-commands.md, quickstart.md — 전부 로드됨

**Tests**: 포함한다 — CLAUDE.md §9(검증 책임)가 신규 기능에 대응 테스트를 요구하고, quickstart.md가 시나리오별 자동 테스트 파일을 명시적으로 지정했으며, plan.md의 Project Structure가 테스트 파일 목록을 이미 확정해뒀다.

**Organization**: User Story(P1/P2/P3) 기준으로 묶는다. 각 스토리는 독립적으로 구현·테스트 가능하다.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: 병렬 가능(다른 파일, 미완료 작업에 의존하지 않음)
- **[Story]**: 어느 User Story에 속하는지(US1/US2/US3)
- 모든 작업에 정확한 파일 경로 포함

## Path Conventions

기존 4-프로젝트 구조 그대로 사용(신규 프로젝트 없음, plan.md Project Structure 참고):
`src/Authoring/`, `src/Validation/`, `src/Grpc/`, `src/NodeKit.Cli/`, `tests/NodeKit.Tests/`, `tests/NodeKit.Cli.Tests/`

**⚠️ 반복되는 함정(plan.md §Project Structure, 실제 코드로 확인됨)**: `NodeKit.csproj`는 `src/` 아래 SDK 스타일 glob이라 새 파일이 자동 포함되지만, `NodeKit.Cli.csproj`는 `<Compile Include="..\...\" Link="...">` 개별 항목을 수동으로 추가해야 CLI가 새 코드를 인식한다(기존 53~69행대 패턴 확인함). 이 프로젝트에 새 `.cs` 파일을 추가하는 모든 작업은 이 등록을 빠뜨리기 쉬우므로, 엔티티/Validator/Renderer/CLI 파일 그룹마다 별도의 "csproj 등록" 작업을 명시적으로 둔다.

---

## Phase 1: Setup

**Purpose**: 구현 착수 전 반드시 먼저 풀어야 할 기술적 불확실성 해소(research.md §11)

- [X] T001 **기술 스파이크(선행 필수)**: `src/Authoring/Recipes/RecipeAuthoringSession.cs`(`SetField`)와 `src/Authoring/Recipes/RecipeFieldCatalog.cs`를 재확인해, `--field InputPorts[0].Name=bam` 같은 인덱스/중첩 경로를 quickstart.md가 요구하는 대로 지원할 수 있는지 결정한다. **이미 코드 조사로 확인된 사실**: 현재 메커니즘은 `RecipeFieldCatalog.FieldsFor(method)`로 등록된 평평한 필드명 카탈로그 기반이라 인덱스/중첩 경로를 전혀 지원하지 않는다. 두 가지 중 하나를 택해 T020에 반영한다 — (a) `ToolFunctionRecipe` 전용으로 `Name[Index].SubField=Value` 문법을 새로 파싱하는 별도 파서를 만든다, (b) 포트/파라미터/fixture처럼 반복 구조가 있는 필드는 `--non-interactive` 모드에서 지원하지 않고 대화형 모드 전용으로 남긴다(스칼라 필드만 `--field`로 허용). 결정 결과를 이 Phase 1 섹션(T001) 바로 아래에 `**결정**:` 줄로 한 줄 기록한다.

**결정**: (a) 채택 — `src/NodeKit.Cli/ToolFunctionRecipeFieldApplier.cs`가 `Name[Index].SubField=Value` 문법을 전용으로 파싱한다(T020에서 사용). `RecipeFieldCatalog`는 재사용하지 않는다.

**Checkpoint**: T001의 결정이 나와야 T020(비대화형 create 커맨드)를 설계할 수 있다.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: User Story 1과 2가 공통으로 참조하는 데이터 모델(data-model.md 전체) — 이게 없으면 어떤 스토리도 컴파일되지 않는다.

**⚠️ CRITICAL**: 이 phase 전체가 끝나야 Phase 3 이후를 시작할 수 있다.

- [X] T002 [P] `ToolFunctionRecipeState` enum(`Draft/Ready/Submitted/Built/Validated/Approved`)을 `src/Authoring/ToolFunctionRecipes/ToolFunctionRecipeState.cs`에 생성 (data-model.md State 섹션)
- [X] T003 [P] `CommandContract`(Executable/Arguments/WorkingDirectory/Environment/SuccessExitCodes/TimeoutPolicy)를 `src/Authoring/ToolFunctionRecipes/CommandContract.cs`에 생성 (data-model.md CommandContract)
- [X] T004 [P] `PortContract`(Direction discriminator, Input/Output 겸용)를 `src/Authoring/ToolFunctionRecipes/PortContract.cs`에 생성 (data-model.md PortContract, research.md §9)
- [X] T005 [P] `FixtureReference`/`ExpectedResult`를 `src/Authoring/ToolFunctionRecipes/FixtureReference.cs`/`src/Authoring/ToolFunctionRecipes/ExpectedResult.cs`에 생성 (data-model.md)
- [X] T006 [P] `IntermediateFilePolicyEntry`(PathOrPattern + Policy enum)를 `src/Authoring/ToolFunctionRecipes/IntermediateFilePolicyEntry.cs`에 생성
- [X] T007 [P] `ParameterContract`를 `src/Authoring/ToolFunctionRecipes/ParameterContract.cs`에 생성
- [X] T008 [P] `ResourceContract`(enforced tier만 — Observed/Recommended 필드는 스키마에 아예 정의하지 않아 FR-014를 타입 수준에서 강제)를 `src/Authoring/ToolFunctionRecipes/ResourceContract.cs`에 생성
- [X] T009 [P] `ExecutionEnvironmentContract`를 `src/Authoring/ToolFunctionRecipes/ExecutionEnvironmentContract.cs`에 생성
- [X] T010 [P] `ValidationRequirements`(MinimumObservationLevel enum + RequiredCoverage dictionary)를 `src/Authoring/ToolFunctionRecipes/ValidationRequirements.cs`에 생성
- [X] T011 루트 `ToolFunctionRecipe` POCO를 `src/Authoring/ToolFunctionRecipes/ToolFunctionRecipe.cs`에 생성 — Id/SchemaVersion/State/ToolSpecDigest/BaseToolImageDigest(read-only)/FunctionId/Revision/Display*/ScriptPath/Command/InputPorts/OutputPorts/FixtureReferences/ExpectedResults/IntermediateFilePolicies/Parameters/EnforcedResources/ExecutionEnvironment/ValidationRequirements/CreatedAt 전체 필드 (data-model.md 루트 표, T002-T010에 의존, `NanCompatibility` 필드는 명시적으로 포함하지 않음 — data-model.md 주의사항 참고)
- [X] T012 T002-T011에서 만든 10개 파일 전부에 대해 `<Compile Include="..\Authoring\ToolFunctionRecipes\....cs" Link="Authoring\ToolFunctionRecipes\....cs" />`를 `src/NodeKit.Cli/NodeKit.Cli.csproj`에 추가 (기존 `RecipeDocument.cs` 등록 패턴 그대로 미러)

**Checkpoint**: 데이터 모델 준비 완료 — User Story 1/2 구현 시작 가능.

---

## Phase 3: User Story 1 - 새 ToolFunctionRecipe 작성 및 로컬 검증 (Priority: P1) 🎯 MVP

**Goal**: 확정된 ToolSpec digest를 참조해 ToolFunctionRecipe를 처음부터 끝까지 작성하고, 정적 검증을 통과시켜 `Draft`→`Ready`로 전이시킨다.

**Independent Test**: 확정된 `toolSpecDigest`/`baseToolImageDigest`만 주어진 상태에서 Recipe를 끝까지 작성해 `Ready`에 도달할 수 있는지로 독립 테스트 가능(quickstart.md 시나리오 1).

### Tests for User Story 1

- [X] T013 [P] [US1] `ToolFunctionRecipeValidator` 규칙 테스트를 `tests/NodeKit.Tests/ToolFunctionRecipes/ToolFunctionRecipeValidatorTests.cs`에 작성 — `L1-TFR-001`(digest 참조 없음) ~ `L1-TFR-006`(필수 필드 누락) 전부, 특히 `L1-TFR-003`(raw shell 문자열 거부, quickstart 시나리오 2) 케이스 포함
- [X] T014 [P] [US1] `ToolFunctionRecipeCreateCommand` 통합 테스트를 `tests/NodeKit.Cli.Tests/ToolFunctionRecipeCreateCommandTests.cs`에 작성 — 대화형/비대화형(`--field`) 양쪽, quickstart 시나리오 1의 정확한 `--field` 조합으로 `Draft` 파일 생성 확인
- [X] T015 [P] [US1] 파일 충돌 감지(FR-023) 테스트를 `tests/NodeKit.Cli.Tests/ToolFunctionRecipeSavePathConfirmationTests.cs`에 작성 — 같은 `functionId`+`revision`, 다른 파일명으로 재생성 시 비대화형 exit 1 + 명확한 메시지, 묵시적 덮어쓰기 없음 확인(quickstart 시나리오 4, 기존 `SavePathConfirmationTests.cs` 패턴 미러)
- [X] T016 [P] [US1] `ToolFunctionRecipeValidationPipeline`의 `function-recipe validate` 파일 왕복(round-trip) 테스트를 `tests/NodeKit.Tests/ToolFunctionRecipes/ToolFunctionRecipeValidationPipelineTests.cs`에 작성 — 검증 통과 시 파일의 `state`가 실제로 `Draft`→`Ready`로 디스크에 갱신되는지, 실패 시 파일이 `Draft`로 그대로 유지되고 위반 목록(RuleId+필드+메시지)이 출력되는지 확인(quickstart 시나리오 1의 두 번째 명령, FR-018, contracts §validate — `/speckit-analyze` 리뷰에서 quickstart.md가 이 파일을 명시적으로 지정했음에도 기존에는 대응 작업이 없었던 커버리지 공백을 메움)

### Implementation for User Story 1

- [X] T017 [US1] `ToolFunctionRecipeValidator` static class를 `src/Validation/ToolFunctionRecipes/ToolFunctionRecipeValidator.cs`에 구현 — `L1-TFR-001`~`L1-TFR-006` 전 규칙(research.md §2-6, data-model.md 검증 규칙 표), `RecipeValidator`와 동일하게 `IValidator` 인터페이스 구현하지 않음(research.md §2 결정)
- [X] T018 [US1] `ToolFunctionRecipeValidationPipeline`을 `src/Validation/ToolFunctionRecipes/ToolFunctionRecipeValidationPipeline.cs`에 구현 — `ToolFunctionRecipeValidator.Validate(...)` 하나만 호출하는 얇은 래퍼(research.md §2, T017에 의존)
- [X] T019 [US1] 대화형 마법사 `ToolFunctionRecipeCreateFlow`를 `src/NodeKit.Cli/ToolFunctionRecipeCreateFlow.cs`에 구현 — functionId→revision→표시정보→스크립트 경로→command(개별 프롬프트)→입력 포트(반복)→출력 포트(반복)→fixture(반복)→예상 결과→중간파일 정책(선택)→parameter(선택)→enforced 자원→실행 환경(선택)→validationRequirements(선택) 순서(contracts §create, `RecipeCreateFlow.cs` 패턴 미러)
- [X] T020 [US1] `ToolFunctionRecipeCreateCommand`를 `src/NodeKit.Cli/ToolFunctionRecipeCreateCommand.cs`에 구현 — `--tool-spec-digest`/`--base-tool-image-digest`(필수) 검사, `--non-interactive`+`--field`(T001 스파이크 결정 반영), 저장 직전 FR-023 충돌 검사, `Draft` 상태로 저장(contracts §create, T001·T019에 의존)
- [X] T021 [US1] `CliApp.cs`에 `"function-recipe"` 최상위 verb를 추가하고 `create`/`validate` 서브커맨드를 라우팅 — 기존 `"recipe" => RunRecipe(...)` 케이스 옆에 추가, `TopLevelUsage` 문자열도 갱신(src/NodeKit.Cli/CliApp.cs 64-71행 스위치 패턴 미러)
- [X] T022 [US1] T017-T020에서 만든 4개 파일에 대해 `<Compile Include>` 항목을 `src/NodeKit.Cli/NodeKit.Cli.csproj`에 추가(T012와 동일 패턴)

**Checkpoint**: User Story 1 독립적으로 완전히 동작 — `function-recipe create` → `function-recipe validate` → `Ready` 전이까지 테스트 가능.

---

## Phase 4: User Story 2 - Ready 상태 Recipe를 빌드 요청 미리보기로 렌더링 (Priority: P2)

**Goal**: `Ready` 상태 Recipe를 `ToolFunctionBuildRequest`와 동일한 구조의 canonical JSON으로 렌더링하고, 제출 시도는 항상 게이트 미개방으로 차단한다.

**Independent Test**: `Ready` 상태 Recipe 하나를 렌더링 명령에 입력했을 때 문서화된 필드 구조의 JSON 파일이 로컬에 생성되는지로 독립 테스트 가능(quickstart.md 시나리오 3). `submit` 차단은 Recipe 상태와 무관하게 항상 성립하므로 US1 완료 여부와 독립적으로도 테스트 가능(quickstart 시나리오 3b).

### Tests for User Story 2

- [X] T023 [P] [US2] `ToolFunctionBuildRequestPreviewFactory` 렌더링 테스트를 `tests/NodeKit.Tests/ToolFunctionRecipes/ToolFunctionBuildRequestPreviewFactoryTests.cs`에 작성 — stage1(kind=2/base_image_digest/script)·stage2(command/포트/parameter/fixture/자원/환경/validationRequirements) 그룹이 시각적으로 구분되는지 확인(data-model.md Renderer 섹션, research.md §8)
- [X] T024 [P] [US2] `ToolFunctionRecipeSubmitCommand` 테스트를 `tests/NodeKit.Cli.Tests/ToolFunctionRecipeSubmitCommandTests.cs`에 작성 — `Draft`/`Ready` 두 State 모두에서 exit 1 + 게이트 미개방 메시지 확인(SC-005 "100%" 요건의 회귀 방지 최소 2케이스, quickstart 시나리오 3b)

### Implementation for User Story 2

- [X] T025 [US2] `ToolFunctionBuildRequestPreviewFactory`를 `src/Grpc/ToolFunctionBuildRequestPreviewFactory.cs`에 구현 — `ToolSpecRawSpecFactory`와 동일하게 `record`+`[JsonPropertyName]`으로 필드명 고정, stage1/stage2 최상위 키로 분리 렌더링(data-model.md Renderer 섹션, research.md §8, 실제 gRPC 전송 코드 없음 — FR-020)
- [X] T026 [US2] `nodekit function-recipe render <path> --out <out.json> [--pretty]` 커맨드 핸들러를 `src/NodeKit.Cli/ToolFunctionRecipeRenderCommand.cs`에 구현 — `Ready` 아니면 거부(exit 1 + 안내), `Ready`면 렌더링, `State` 변경 없음(contracts §render, T025에 의존)
- [X] T027 [US2] `ToolFunctionRecipeSubmitCommand`를 `src/NodeKit.Cli/ToolFunctionRecipeSubmitCommand.cs`에 구현 — 파일 읽기 성공 시 `State`·내용과 무관하게 항상 "NodeVault ToolFunction 빌드 게이트가 아직 열려 있지 않습니다(issue #19)" 안내 후 exit 1, 네트워크 호출 없음, `State` 변경 없음(contracts §submit, 2026-07-24 정정 사항 그대로 구현)
- [X] T028 [US2] `CliApp.cs`의 `"function-recipe"` 라우팅에 `render`/`submit` 서브커맨드 추가(T021에 의존, 같은 switch 블록 수정이므로 T021 완료 후 진행)
- [X] T029 [US2] T025/T026/T027에서 만든 파일에 대해 `<Compile Include>` 항목을 `src/NodeKit.Cli/NodeKit.Cli.csproj`에 추가

**Checkpoint**: User Story 1과 2 모두 독립적으로 동작.

---

## Phase 5: User Story 3 - 기존 ToolSpec 마법사에서 새 플로우로 이관 (Priority: P3)

**Goal**: 기존 `recipe create` 마법사의 Inputs/Outputs/Command 단계를 제거하고 새 플로우 안내로 대체한다.

**Independent Test**: `recipe create` 마법사를 처음부터 끝까지 실행했을 때 해당 입력 단계가 더 이상 나타나지 않고 안내 메시지가 표시되는지로 테스트 가능(quickstart.md 시나리오 5).

### Tests for User Story 3

- [X] T030 [P] [US3] `tests/NodeKit.Cli.Tests/RecipeCreateCommandTests.cs`에 회귀 케이스 추가 — (a) Inputs/Outputs/Command 단계가 더 이상 프롬프트를 띄우지 않고 안내 메시지를 표시하는지, (b) 과거 Inputs/Outputs/Command 값을 가진 `ToolDefinition` JSON을 `nodekit validate`로 다시 불러와도 오류 없이 처리되는지(FR-025 — research.md §11에서 `System.Text.Json`이 미매핑 속성을 기본적으로 무시함을 이미 확인했으므로 코드 변경 없이도 성립해야 함을 회귀 테스트로 고정)

### Implementation for User Story 3

- [X] T031 [US3] `src/NodeKit.Cli/RecipeCreateFlow.cs`에서 Inputs/Outputs/Command 입력 단계를 제거하고, 해당 지점에서 "`nodekit function-recipe create`를 사용하세요" 안내로 대체(FR-024, 기존 파일 수정 — CLAUDE.md §7상 이 spec 요구사항에 직접 근거하므로 예외적으로 허용된 변경)

**Checkpoint**: User Story 1·2·3 전부 독립적으로 동작.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: 완료 보고 전 CLAUDE.md §10 기준 충족 확인

- [X] T032 [P] `CliApp.cs`의 `TopLevelUsage` 문자열에 `function-recipe` 명령 사용법 라인이 실제로 반영됐는지 확인·보완(T021에서 빠뜨리기 쉬움)
- [X] T033 [P] `dotnet build`를 실행해 경고 0개(CLAUDE.md §8, 기존 대비 증가 없음) 확인
- [X] T034 `dotnet test tests/NodeKit.Tests/NodeKit.Tests.csproj && dotnet test tests/NodeKit.Cli.Tests/NodeKit.Cli.Tests.csproj` 전체 통과 확인
- [X] T035 quickstart.md 시나리오 1~5 전부 수동 실행해 기대 결과와 일치하는지 확인(CLAUDE.md §10 완료 보고 기준)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: 의존성 없음 — 즉시 시작. **Phase 3(US1)를 막는 유일한 게이트**(T001 결정이 T020 설계에 필요).
- **Foundational (Phase 2)**: Setup과 독립적으로 병렬 착수 가능(T002-T010 데이터 모델 자체는 T001 결정과 무관). 단 Phase 3/4 착수 전 Phase 2 전체(T002-T012) 완료 필요.
- **User Story 1 (Phase 3)**: Foundational 완료 + T001 결정 필요.
- **User Story 2 (Phase 4)**: Foundational 완료 후 착수 가능. `render`(T025-T026)는 US1과 독립적으로 구현 가능하나, **의미 있게 테스트하려면** US1이 만든 `Ready` 상태 파일이 필요(다만 `submit` T027은 완전히 독립적 — 아무 파일이나 항상 차단). T028(CliApp.cs 라우팅)만 T021과 같은 파일을 다시 수정하므로 순서 필요.
- **User Story 3 (Phase 5)**: Foundational과도 독립적 — 다른 스토리의 신규 타입을 전혀 참조하지 않는다. 이론상 가장 먼저도 할 수 있으나, spec.md가 "P1/P2가 실제로 동작한 이후에만 안전하게 전환할 수 있는 후행 작업"이라고 명시해뒀으므로 우선순위대로 마지막에 배치.
- **Polish (Phase 6)**: 원하는 모든 User Story 완료 후.

### Parallel Opportunities

- Phase 2의 T002-T010(엔티티 9종)은 전부 `[P]` — 서로 다른 파일, 상호 의존 없음.
- Phase 3의 T013-T016(테스트 4종)는 `[P]`.
- Phase 4의 T023-T024(테스트 2종)는 `[P]`.
- Phase 6의 T032/T033는 `[P]`.
- **csproj 등록 작업(T012, T022, T029)은 각각 직전 파일 생성 작업들에 의존하므로 `[P]` 아님** — 같은 파일(`NodeKit.Cli.csproj`)을 순차 수정.
- **`CliApp.cs` 라우팅 작업(T021, T028)도 같은 파일의 같은 switch 블록을 다루므로 순차 진행**(T028은 T021 이후).

---

## Parallel Example: Phase 2 (Foundational)

```bash
# T002-T010을 동시에 진행 가능(서로 다른 파일):
Task: "ToolFunctionRecipeState enum in src/Authoring/ToolFunctionRecipes/ToolFunctionRecipeState.cs"
Task: "CommandContract in src/Authoring/ToolFunctionRecipes/CommandContract.cs"
Task: "PortContract in src/Authoring/ToolFunctionRecipes/PortContract.cs"
Task: "FixtureReference/ExpectedResult in src/Authoring/ToolFunctionRecipes/{FixtureReference,ExpectedResult}.cs"
Task: "IntermediateFilePolicyEntry in src/Authoring/ToolFunctionRecipes/IntermediateFilePolicyEntry.cs"
Task: "ParameterContract in src/Authoring/ToolFunctionRecipes/ParameterContract.cs"
Task: "ResourceContract in src/Authoring/ToolFunctionRecipes/ResourceContract.cs"
Task: "ExecutionEnvironmentContract in src/Authoring/ToolFunctionRecipes/ExecutionEnvironmentContract.cs"
Task: "ValidationRequirements in src/Authoring/ToolFunctionRecipes/ValidationRequirements.cs"
# T011(루트 POCO)은 위 9개가 전부 끝난 뒤 진행
```

---

## Implementation Strategy

### MVP First (User Story 1만)

1. Phase 1(T001) 완료 — `--field` 문법 결정
2. Phase 2(T002-T012) 완료 — 데이터 모델 전체, CLI 프로젝트에 등록
3. Phase 3(T013-T022) 완료 — `function-recipe create`/`validate`가 끝까지 동작
4. **중단하고 검증**: quickstart.md 시나리오 1·2·4를 수동 실행해 US1이 독립적으로 동작하는지 확인
5. 이 시점에서 이미 "기능 계약을 구조화된 형태로 문서화하고 명백한 오류를 조기에 잡는다"는 핵심 가치 전달 완료(spec.md User Story 1 "Why this priority")

### Incremental Delivery

1. Setup + Foundational → 기반 완료
2. User Story 1 추가 → 독립 검증 → MVP
3. User Story 2 추가 → 독립 검증(렌더링 + 제출 차단)
4. User Story 3 추가 → 독립 검증(기존 마법사 정리)
5. Polish(Phase 6)로 마무리

---

## Notes

- `[P]` 작업 = 서로 다른 파일, 의존성 없음
- `[Story]` 라벨은 US1/US2/US3 추적용, Setup/Foundational/Polish에는 라벨 없음
- 각 User Story는 독립적으로 완료·테스트 가능해야 함
- 작업 또는 논리적 그룹 단위로 커밋
- 체크포인트에서 멈춰 해당 스토리를 독립적으로 검증할 것
- 회피할 것: 모호한 작업, 같은 파일 충돌(특히 `CliApp.cs`/`NodeKit.Cli.csproj` — 이번 feature에서 반복적으로 여러 작업이 같은 파일을 건드림), 스토리 간 독립성을 깨는 교차 의존
