# Implementation Plan: ToolFunctionSpec v0.3 Authoring Scope

**Branch**: `001-toolfunctionspec-authoring` | **Date**: 2026-07-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-toolfunctionspec-authoring/spec.md`

## Summary

NodeKit이 로컬에서 `ToolFunctionRecipe`(기존 ToolSpec Recipe와 대응하는 새 저작 모델)를 작성·정적 검증·canonical JSON 미리보기 렌더링까지 하도록 만든다. 확정된 ToolSpec의 `toolSpecDigest`/`baseToolImageDigest`를 read-only로 참조하고, functionId/구조화된 command/입출력 포트/parameter/fixture 참조/예상 결과/중간파일 정책/enforced 자원/실행 환경/validationRequirements를 선언 입력으로 받는다. 기존 `RecipeDocument`→`RecipeValidator`→`RecipeRenderer`→`ToolSpecRawSpecFactory` 파이프라인과 동일한 구조(정적 POCO, static validator, static renderer, typed wire record)를 그대로 재사용해 새 병렬 파이프라인(`ToolFunctionRecipe`→`ToolFunctionRecipeValidator`→`ToolFunctionRecipeRenderer`→`ToolFunctionBuildRequestPreviewFactory`)을 만든다. 실제 gRPC 제출은 이번 범위에서 구현하지 않는다(NodeVault `BUILD_KIND_TOOLFUNCTIONSPEC` 게이트 미개방).

## Technical Context

**Language/Version**: C# / .NET 10.0 (기존 `NodeKit.csproj`/`NodeKit.Cli.csproj`와 동일)

**Primary Dependencies**: `System.Text.Json`(직렬화, Newtonsoft 아님), `Google.Protobuf`/`Grpc.Net.Client`(기존 wire 계약 참조용, 이번 기능은 실제 gRPC 호출 안 함), xUnit v3 + `CsCheck`(property-based) + `Spectre.Console.Testing`(테스트)

**Storage**: 로컬 파일(JSON) — NodeVault/DB 연결 없음. 기본 저장 위치는 현재 작업 디렉터리(`RecipeDocument`와 동일 관례)

**Testing**: xUnit v3. 순수 로직(POCO/validator/renderer)은 `tests/NodeKit.Tests/ToolFunctionRecipes/`, CLI 통합은 `tests/NodeKit.Cli.Tests/`

**Target Platform**: 크로스플랫폼 CLI(.NET 10, Linux/macOS/Windows) — GUI(Avalonia) 쪽은 이번 spec 범위 밖(User Story 1/2/3 전부 CLI 우선, GUI는 후속)

**Project Type**: CLI 확장 — 기존 4-프로젝트 구조(`NodeKit.csproj`(GUI), `NodeKit.Cli.csproj`(CLI), `NodeKit.Tests.csproj`, `NodeKit.Cli.Tests.csproj`) 그대로 사용, 새 프로젝트 생성 없음

**Performance Goals**: 해당 없음(로컬 CLI 도구, 실시간 처리량 요구사항 없음)

**Constraints**: NodeVault로의 실시간 연결 없이 완전히 동작해야 함(FR-022). gRPC 제출 코드 경로를 만들지 않음(FR-020) — 즉 이 기능은 네트워크 I/O가 전혀 없다.

**Scale/Scope**: 단일 사용자, 로컬 파일 1개당 ToolFunctionRecipe 1개. 대량 동시 사용자/처리량 시나리오 없음.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

`.specify/memory/constitution.md`는 채워지지 않은 템플릿이라 speckit 형식의 정식 헌법이 없다. 이 프로젝트의 실질적 거버넌스 문서는 저장소 루트의 `CLAUDE.md`이며, 아래 게이트를 그 문서 기준으로 평가한다.

| 게이트 (CLAUDE.md §1/§6) | 평가 |
|---|---|
| K8s API 호출, Job 스케줄링, 이미지 빌드 로직 추가? | 아니오 — 이 기능은 로컬 저작/검증/렌더링만 한다. |
| `RegisteredToolDefinition`/`RegisteredToolFunctionSpec` 등 최종 등록 객체 생성 로직 추가? | 아니오 — `ToolFunctionRecipe`는 초안이며 승인된 객체를 만들지 않는다(spec Key Entities). |
| 재현성 규칙(latest tag, digest, 버전 고정) 완화? | 아니오 — 참조하는 `toolSpecDigest`/`baseToolImageDigest`는 read-only이며 완화 대상이 아니다. |
| `IPolicyBundleProvider` 우회한 정책 파일 경로 하드코딩? | 해당 없음 — 이 기능은 정책 평가를 다루지 않는다. |
| DagEdit 내부 구현에 결합? | 아니오 — DagEdit은 이 spec의 Out of Scope다. |
| Catalog 서비스 우회하고 NodeVault index 직접 조회? | 아니오 — 이 기능은 NodeVault에 어떤 조회도 하지 않는다(FR-022, 실시간 연결 불필요). |
| 작은 diff, 무관한 리팩터 없음(§7)? | 계획대로면 통과 — 새 파일 추가 위주이며 기존 `Recipes/` 코드는 수정하지 않는다(단, `recipe create` 마법사의 Inputs/Outputs/Command 단계 제거는 FR-024 요구사항이므로 예외적으로 기존 코드 변경 포함). |
| 컴파일 경고 증가 없음(§8)? | Phase 1 이후 `dotnet build`로 검증 예정. |

**결과**: 게이트 위반 없음. Complexity Tracking 불필요.

## Project Structure

### Documentation (this feature)

```text
specs/001-toolfunctionspec-authoring/
├── plan.md              # 이 문서
├── research.md          # Phase 0 산출물
├── data-model.md        # Phase 1 산출물
├── quickstart.md        # Phase 1 산출물
├── contracts/           # Phase 1 산출물 — CLI 명령 계약
│   └── cli-function-recipe-commands.md
└── tasks.md              # Phase 2 산출물 (/speckit-tasks, 이 명령이 만들지 않음)
```

### Source Code (repository root)

**Structure Decision**: 기존 4-프로젝트 구조를 그대로 쓴다. `RecipeDocument`/`Recipes/` 패턴을 그대로 미러링한 새 `ToolFunctionRecipe`/`ToolFunctionRecipes/` 폴더 트리를 추가한다. `NodeKit.csproj`는 `src/` 아래 SDK 스타일 glob이라 새 파일이 자동 포함되지만, `NodeKit.Cli.csproj`는 `<Compile Include="..\...\" Link="...">` 개별 항목을 수동으로 추가해야 CLI가 새 코드를 본다(§4 조사 결과) — 이 작업 자체가 실수하기 쉬운 지점이라 tasks.md에 명시적 항목으로 남겨야 한다.

```text
src/
├── Authoring/
│   ├── Recipes/                       # 기존, 변경 없음
│   └── ToolFunctionRecipes/           # 신규
│       ├── ToolFunctionRecipe.cs              # 최상위 POCO (Key Entities 전체 필드)
│       ├── ToolFunctionRecipeState.cs         # enum: Draft/Ready/Submitted/Built/Validated/Approved
│       ├── CommandContract.cs
│       ├── PortContract.cs                    # Input/Output 공용, IsOutput 또는 별도 InputPort/OutputPort
│       ├── ParameterContract.cs
│       ├── IntermediateFilePolicyEntry.cs
│       ├── ResourceContract.cs                 # enforced tier만
│       ├── ValidationRequirements.cs
│       └── FixtureReference.cs / ExpectedResult.cs
├── Validation/
│   ├── Recipes/                       # 기존, 변경 없음
│   └── ToolFunctionRecipes/           # 신규
│       ├── ToolFunctionRecipeValidator.cs      # static, L1-TFR-* 규칙 (RecipeValidator 패턴 미러)
│       └── ToolFunctionRecipeValidationPipeline.cs
├── Grpc/
│   └── ToolFunctionBuildRequestPreviewFactory.cs   # 신규 — canonical JSON 렌더링 (wire 재사용, 실제 전송 없음)
└── NodeKit.Cli/
    ├── CliApp.cs                       # 기존 파일 수정 — "function-recipe" 최상위 verb 추가 (create/validate/render/submit)
    ├── ToolFunctionRecipeCreateCommand.cs   # 신규
    ├── ToolFunctionRecipeCreateFlow.cs      # 신규 (대화형 마법사, RecipeCreateFlow 패턴 미러)
    ├── ToolFunctionRecipeSubmitCommand.cs   # 신규 — 항상 게이트 미개방 메시지로 차단(FR-021, contracts/ 참고)
    ├── ToolFunctionRecipeRenderCommand.cs   # 신규 — `Ready` 상태만 렌더링 허용(FR-019/FR-020, contracts §render)
    └── RecipeCreateFlow.cs             # 기존 파일 수정 — FR-024 (Inputs/Outputs/Command 단계 제거+안내로 대체)

tests/
├── NodeKit.Tests/
│   └── ToolFunctionRecipes/            # 신규 — Validator/Renderer/Pipeline 단위 테스트(Pipeline 테스트는 `ToolFunctionRecipeValidationPipelineTests.cs`, quickstart.md 시나리오 1의 상태 왕복 검증)
└── NodeKit.Cli.Tests/
    ├── ToolFunctionRecipeCreateCommandTests.cs   # 신규
    ├── ToolFunctionRecipeSubmitCommandTests.cs   # 신규 — SC-005: 모든 State에서 100% 차단 확인
    └── ToolFunctionRecipeSavePathConfirmationTests.cs  # 신규 (SavePathConfirmationTests.cs 패턴 미러)
```

새 라이브러리 프로젝트나 별도 repo를 만들지 않는다 — 기존 구조 재사용이 CLAUDE.md §7(작은 diff)과 일치한다.

## Constitution Check — Post-Design 재점검

Phase 1(data-model.md/contracts/quickstart.md) 완료 후 위 게이트를 다시 확인했다. 설계 산출물 중 게이트를 새로 위협하는 항목 없음:

- `ResourceContract`(data-model.md)에 observed/recommended 필드를 **아예 정의하지 않아** FR-014를 스키마 수준에서 강제 — 재현성/범위 위반 없음.
- `ToolFunctionBuildRequestPreviewFactory`(stage-1/stage-2 그룹 렌더링)는 실제 전송 코드를 포함하지 않는다 — FR-020/FR-021, gRPC 클라이언트 미추가 확인.
- `nodekit function-recipe submit`(2026-07-24 정정, research.md §10)은 실제로 만들되 항상 게이트 미개방으로 실패하며 네트워크 호출이 전혀 없다 — FR-020/FR-021 위반 아님, 오히려 이 명령이 없으면 FR-021/SC-005를 위반하게 됨을 재확인했다.
- 유일한 기존 코드 변경은 `CliApp.cs`(새 verb 등록)와 `RecipeCreateFlow.cs`(FR-024, Inputs/Outputs/Command 단계 제거)뿐이며 둘 다 spec 요구사항에 직접 근거함 — CLAUDE.md §7 위반 아님.

**결과**: 위반 없음, Complexity Tracking 불필요(변경 없음). 단, 초기 Post-Design 재점검 시 `submit` 서브커맨드 관련 요구사항(FR-021)을 한 차례 놓쳤다가 사용자 지적으로 발견·수정했다 — research.md §10/§11에 기록.
