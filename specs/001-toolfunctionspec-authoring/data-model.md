# Phase 1 Data Model: ToolFunctionSpec v0.3 Authoring Scope

spec.md의 Key Entities와 Functional Requirements, research.md의 결정을 근거로 한 필드 수준 모델이다. 실제 C# 구현 시 이 문서의 이름/타입을 그대로 클래스/필드명으로 쓰는 것을 전제로 한다(POCO, `internal`, `System.Text.Json` 직렬화).

## ToolFunctionRecipe (루트 엔티티)

| 필드 | 타입 | 필수 | 근거 |
|---|---|---|---|
| `Id` | `Guid` | 자동생성 | `RecipeDocument.Id` 관례 미러 |
| `SchemaVersion` | `string` | 자동(`"draft-1"`) | `RecipeDocument.SchemaVersion` 관례 미러 |
| `State` | `ToolFunctionRecipeState` | 자동(`Draft`로 생성) | FR-017/FR-018/FR-020, research.md §1 |
| `ToolSpecDigest` | `string` | 필수, read-only(생성 후 불변) | FR-001, FR-002 |
| `BaseToolImageDigest` | `string` | 필수, read-only(생성 후 불변) | FR-001, FR-002 |
| `FunctionId` | `string` | 필수 | FR-003, 형식은 research.md §5 |
| `Revision` | `string` | 필수 | FR-003 |
| `DisplayLabel` / `DisplayDescription` / `DisplayCategory` / `DisplayTags` | `string`/`string`/`string`/`List<string>` | 선택 | FR-003 |
| `ScriptPath` | `string` | 필수 | FR-004 — 로컬 파일 경로 참조, 내용을 인라인으로 담지 않음 |
| `NanCompatibility` | `string` | 선택 | FR-005(이번 정정판에서는 spec 최신 버전 기준 명시적 FR 번호 없음 — nan 필드는 Recipe에 요구하지 않음, FR-004 후반부 참고. **주의**: 최신 spec.md는 nan 관련 필드를 Recipe에 요구하지 않는다고 명시하므로 이 필드는 포함하지 않는다) |
| `Command` | `CommandContract` | 필수 | FR-005, FR-006 |
| `InputPorts` | `List<PortContract>` (Direction=Input) | 최소 1개 | FR-007 |
| `OutputPorts` | `List<PortContract>` (Direction=Output) | 최소 1개 | FR-008 |
| `FixtureReferences` | `List<FixtureReference>` | 최소 1개 | FR-009, FR-017 |
| `ExpectedResults` | `List<ExpectedResult>` | 출력 포트당 권장 1개 | FR-010 |
| `IntermediateFilePolicies` | `List<IntermediateFilePolicyEntry>` | 선택 | FR-011 |
| `Parameters` | `List<ParameterContract>` | 선택 | FR-012 |
| `EnforcedResources` | `ResourceContract` | 필수 | FR-013, FR-017 |
| `ExecutionEnvironment` | `ExecutionEnvironmentContract` | 선택 | FR-015 |
| `ValidationRequirements` | `ValidationRequirements` | 선택 | FR-016 |
| `CreatedAt` | `DateTime` | 자동생성 | `RecipeDocument.CreatedAt` 관례 미러 |

**주의(FR-004 재확인)**: spec.md FR-004는 "nan을 이 스크립트에 결합하는 방식은 NodeVault 내부 구현이므로 Recipe는 nan 관련 필드를 요구하지 않는다"고 명시한다. 위 표의 `NanCompatibility` 행은 초기 초안에서 혼동을 막기 위해 **포함하지 않을 필드**로 명시적으로 남겨둔 것이며, 실제 구현 시 이 필드를 추가하지 않는다.

### State (lifecycle)

```
Draft ──(검증 통과, FR-017/018)──▶ Ready
Ready ──(향후 제출 API, 이번 범위 밖)──▶ Submitted ──▶ Built ──▶ Validated ──▶ Approved
```

이번 기능은 `Draft↔Ready` 전이만 실제로 구현한다(User Story 1). `Submitted` 이후 상태는 스키마에 값으로 예약되지만 이 기능이 도달시키는 수단을 제공하지 않는다(Assumptions, FR-020).

## CommandContract

| 필드 | 타입 | 필수 | 근거 |
|---|---|---|---|
| `Executable` | `string` | 필수, 공백/`|`/`;`/`>`/`<` 금지(L1-TFR-003) | FR-005, FR-006, research.md §4 |
| `Arguments` | `List<string>` | 선택(순서 유지) | FR-005 |
| `WorkingDirectory` | `string` | 선택 | FR-005 |
| `Environment` | `List<EnvironmentEntry>`(이름+출처, allowlist 성격) | 선택 | FR-005 |
| `SuccessExitCodes` | `List<int>` | 선택(기본 `[0]`) | FR-005 |
| `TimeoutPolicy` | `TimeoutPolicy`(soft/hard 초 단위) | 선택 | FR-005 |

## PortContract (Input/Output 공용, research.md §9)

| 필드 | 타입 | 필수 | 비고 |
|---|---|---|---|
| `Name` | `string` | 필수, 입출력 통틀어 유일(L1-TFR-004) | FR-007/FR-008 |
| `Direction` | `enum { Input, Output }` | 필수 | research.md §9 |
| `DataFormat` | `string` | 선택 | FR-007/FR-008 |
| `Cardinality` | `enum { Single, Multiple }` | 선택(기본 `Single`) | FR-007/FR-008 |
| `Required` | `bool` | Input에서만 의미 있음(기본 `true`) | FR-007 |
| `PathPlacementRule` | `string` | Input 전용 | FR-007 |
| `CompanionFiles` | `List<string>` | Input 전용(예: BAM+BAI) | FR-007 |
| `PathOrGlob` | `string` | Output 전용 | FR-008 |
| `CompletionCheck` | `string` | Output 전용 | FR-008 |
| `DownstreamCompatibilityNote` | `string` | Output 전용, 선택 | FR-008 |

## FixtureReference / ExpectedResult

| 필드 | 타입 | 필수 | 비고 |
|---|---|---|---|
| `FixtureReference.LocalPath` | `string` | LocalPath/ContentDigest 중 하나 필수 | FR-009 |
| `FixtureReference.ContentDigest` | `string` | 위와 동일 | FR-009 |
| `ExpectedResult.OutputPortName` | `string` | 필수, 존재하는 `OutputPorts` 이름을 참조 | FR-010 |
| `ExpectedResult.ExpectedValueOrRule` | `string` | 필수 | FR-010 |

## IntermediateFilePolicyEntry

| 필드 | 타입 | 필수 |
|---|---|---|
| `PathOrPattern` | `string` | 필수 |
| `Policy` | `enum { Ephemeral, Cache, Checkpoint, SidecarOutput, SensitiveTemp }` | 필수 |

## ParameterContract

| 필드 | 타입 | 필수 |
|---|---|---|
| `Name` | `string` | 필수 |
| `Type` | `enum { String, Integer, Number, Boolean, Enum }` | 필수 |
| `DefaultValue` | `string` | 선택 |
| `AllowedRange` | `string`(문자열로 인코딩, 타입별 해석) | 선택 |
| `Required` | `bool` | 필수 |
| `CliArgumentMapping` | `string` | 선택 |
| `MutuallyExclusiveGroup` | `string` | 선택 |

## ResourceContract (enforced tier만, FR-013/FR-014)

| 필드 | 타입 | 필수 |
|---|---|---|
| `CpuRequest` / `CpuLimit` | `string`(K8s 스타일, 예: `"500m"`) | 필수 |
| `MemoryRequest` / `MemoryLimit` | `string`(예: `"256Mi"`) | 필수 |
| `StorageRequest` / `StorageLimit` | `string` | 선택 |
| `MaxExecutionTimeSeconds` | `int` | 선택 |
| `Parallelism` | `int` | 선택 |

검증 규칙(L1-TFR-005): `MemoryLimit ≥ MemoryRequest`, `CpuLimit ≥ CpuRequest`(단위 정규화 후 비교).

**명시적으로 포함하지 않는 필드**: `Observed*`/`Recommended*` 계열 자원 값(FR-014) — 이 타입에 그런 필드 자체를 두지 않아 스키마 수준에서 원천 차단한다(런타임 검증이 아니라 타입에 필드가 없으므로 애초에 입력 불가).

## ExecutionEnvironmentContract

| 필드 | 타입 | 필수 |
|---|---|---|
| `SupportedPlatforms` | `List<string>`(예: `"linux/amd64"`) | 선택 |
| `WritablePaths` | `List<string>` | 선택 |
| `NetworkPolicy` | `string` | 선택 |
| `RequiresRoot` | `bool` | 선택(기본 `false`) |
| `RequiredCapabilities` | `List<string>` | 선택 |

## ValidationRequirements

| 필드 | 타입 | 필수 |
|---|---|---|
| `MinimumObservationLevel` | `enum { Basic, Enhanced, Full }` | 선택 |
| `RequiredCoverage` | `Dictionary<string,bool>`(예: `resourceSamples`, `processEvents`, `fileEvents`, `networkEvents`) | 선택 |

## 검증 규칙 요약 (research.md §3 근거)

| Rule ID | 대상 | 조건 |
|---|---|---|
| `L1-TFR-001` | `ToolSpecDigest`/`BaseToolImageDigest` | 비어있으면 실패 |
| `L1-TFR-002` | `FunctionId` | research.md §5 정규식 불일치 시 실패 |
| `L1-TFR-003` | `Command.Executable` | 공백/`|`/`;`/`>`/`<` 포함 시 실패 |
| `L1-TFR-004` | `InputPorts`+`OutputPorts` | `Name` 중복(입출력 통틀어) 시 실패 |
| `L1-TFR-005` | `EnforcedResources` | `*Limit < *Request` 시 실패 |
| `L1-TFR-006` | 전체 | 필수 필드(functionId/command/포트 최소 1개씩/fixture 최소 1개/enforced resource) 누락 시 실패 |

## Renderer 산출물 — `ToolFunctionBuildRequestPreview`

`ToolFunctionRecipe`(Ready 상태)를 canonical JSON으로 렌더링한 결과. 두 그룹으로 나뉜다(research.md §8):

- **Stage-1 그룹**(실제 `BuildRequest` proto와 매핑): `kind`(상수 `2`, `BUILD_KIND_TOOLFUNCTIONSPEC`), `base_image_digest`(← `BaseToolImageDigest`), `script`(← `ScriptPath`가 가리키는 파일 내용 또는 참조).
- **Stage-2 그룹**(아직 실제 wire 메시지 없음, 미리보기 전용): `command`, `inputPorts`, `outputPorts`, `parameters`, `fixtureReferences`, `expectedResults`, `intermediateFilePolicies`, `enforcedResources`, `executionEnvironment`, `validationRequirements`.

`ToolFunctionBuildRequestPreviewFactory`는 `ToolSpecRawSpecFactory`와 동일하게 `record` + `[JsonPropertyName]`으로 필드명을 고정하지만, 두 그룹을 JSON에서도 시각적으로 구분되는 최상위 키(`stage1`/`stage2` 또는 주석 헤더)로 나눠 렌더링해 "이게 오늘 실제로 보낼 수 있는 부분과 아직 아닌 부분"을 사용자가 혼동하지 않게 한다.
