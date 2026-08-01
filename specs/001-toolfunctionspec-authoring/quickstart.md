# Quickstart: ToolFunctionRecipe 로컬 저작·검증·렌더링

이 문서는 구현이 끝난 뒤 "동작한다"를 사람이 직접 확인하는 절차다. 전체 구현 코드는 포함하지 않는다 — 실행 가능한 시나리오와 기대 결과만 정리한다.

## 사전 조건

- .NET 10 SDK 설치.
- 이미 확정된 ToolSpec 하나의 `toolSpecDigest`/`baseToolImageDigest`(실제 NodeVault 빌드 없이도, 형식만 맞는 테스트용 sha256 문자열이면 충분 — 이 기능은 NodeVault에 실제로 조회하지 않는다).
- `dotnet build src/NodeKit.Cli/NodeKit.Cli.csproj`가 경고 0개로 성공(CLAUDE.md §8).

## 시나리오 1 — User Story 1: Draft 작성 → Ready 검증 통과

```bash
cd tests/manual-scratch   # 또는 임시 디렉터리, 실제 프로젝트 소스는 건드리지 않음

dotnet run --project src/NodeKit.Cli -- function-recipe create samtools-sort.json \
  --tool-spec-digest sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa \
  --base-tool-image-digest sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb \
  --non-interactive \
  --field FunctionId=samtools.sort \
  --field Revision=v1 \
  --field ScriptPath=./sort.sh \
  --field Command.Executable=samtools \
  --field Command.Arguments=sort \
  --field Command.Arguments=-@4 \
  --field InputPorts[0].Name=bam \
  --field InputPorts[0].Required=true \
  --field OutputPorts[0].Name=sortedBam \
  --field FixtureReferences[0].LocalPath=./fixtures/small.bam \
  --field EnforcedResources.CpuRequest=500m \
  --field EnforcedResources.CpuLimit=2000m \
  --field EnforcedResources.MemoryRequest=256Mi \
  --field EnforcedResources.MemoryLimit=1Gi
```

**기대 결과**: exit code 0, `samtools-sort.json` 생성, 파일 내용의 `state`가 `"Draft"`.

```bash
dotnet run --project src/NodeKit.Cli -- function-recipe validate samtools-sort.json
```

**기대 결과**: exit code 0, stdout에 검증 통과 메시지, `samtools-sort.json`의 `state`가 `"Ready"`로 갱신됨.

## 시나리오 2 — Acceptance Scenario: raw shell 문자열 거부

시나리오 1과 동일하되 `--field Command.Executable="bash -c 'samtools sort'"`로 바꿔 실행.

**기대 결과**: `create` 또는 `validate` 단계에서 `L1-TFR-003` 위반으로 exit 1, stderr에 "executable에 공백/셸 메타문자를 포함할 수 없습니다. arguments 배열로 분리하세요" 계열 메시지.

## 시나리오 3 — User Story 2: Ready 상태를 canonical JSON으로 렌더링

```bash
dotnet run --project src/NodeKit.Cli -- function-recipe render samtools-sort.json --out preview.json --pretty
```

**기대 결과**: exit code 0, `preview.json` 생성. 내용에 `stage1`(kind=2, base_image_digest, script) 그룹과 `stage2`(command/inputPorts/outputPorts/...) 그룹이 구분되어 있음. `samtools-sort.json`의 `state`는 `"Ready"`로 그대로 유지(변경 없음).

같은 명령을 `state: "Draft"`인 파일에 실행하면 exit 1 + "먼저 검증을 통과해야 합니다" 메시지.

## 시나리오 3b — FR-021/SC-005: 제출 시도는 항상 차단

```bash
dotnet run --project src/NodeKit.Cli -- function-recipe submit samtools-sort.json
```

**기대 결과**: `samtools-sort.json`이 `Ready`(시나리오 1 이후)든 `Draft`(검증 전)든 **State와 무관하게 항상** exit 1 + "NodeVault ToolFunction 빌드 게이트가 아직 열려 있지 않습니다" 계열의 구체적 안내. 일반 "알 수 없는 명령" usage 오류(exit 2)가 아니어야 한다 — 이게 실수하기 가장 쉬운 지점이다(이전 계획 초안에서 실제로 이 요구사항을 빠뜨렸던 이력 참고). 파일은 어떤 필드도 변경되지 않는다(네트워크 호출 없음, `State`는 `Submitted`로 전이하지 않음).

## 시나리오 4 — FR-023: 파일 충돌 감지

시나리오 1을 같은 디렉터리에서 다른 파일명(`samtools-sort-2.json`)으로, 그러나 같은 `FunctionId`/`Revision`(`samtools.sort`/`v1`)으로 다시 실행.

**기대 결과**: 비대화형 모드는 exit 1 + "동일한 functionId/revision(`samtools.sort`/`v1`)을 가진 기존 파일(`samtools-sort.json`)이 있습니다" 메시지, 새 파일을 만들지 않음(묵시적 덮어쓰기 없음).

## 시나리오 5 — User Story 3: 기존 `recipe create` 마법사 확인

```bash
dotnet run --project src/NodeKit.Cli -- recipe create --non-interactive --method dockerfile ...
```

**기대 결과**: 기존에 Inputs/Outputs/Command를 물어보던 프롬프트/필드가 더 이상 나타나지 않고, 대신 "포트/명령 설정은 `nodekit function-recipe create`를 사용하세요" 같은 안내가 표시됨(대화형 모드에서 확인). 과거에 Inputs/Outputs/Command 값이 채워진 `ToolDefinition` JSON을 다시 불러와도(`nodekit validate old-recipe.json`) 오류 없이 처리됨(FR-025).

## 자동화 테스트로의 매핑

위 시나리오는 각각 다음 자동 테스트에 대응한다(구현 시 tasks.md에서 구체 파일명 확정):
- 시나리오 1 → `tests/NodeKit.Tests/ToolFunctionRecipes/ToolFunctionRecipeValidationPipelineTests.cs` + `tests/NodeKit.Cli.Tests/ToolFunctionRecipeCreateCommandTests.cs`
- 시나리오 2 → `tests/NodeKit.Tests/ToolFunctionRecipes/ToolFunctionRecipeValidatorTests.cs`(`L1-TFR-003` 케이스)
- 시나리오 3 → `tests/NodeKit.Cli.Tests/ToolFunctionRecipeCreateCommandTests.cs`(render 하위 케이스) 또는 별도 `ToolFunctionRecipeRenderCommandTests.cs`
- 시나리오 3b → `tests/NodeKit.Cli.Tests/ToolFunctionRecipeSubmitCommandTests.cs` — `Draft`/`Ready` 두 State 모두에서 exit 1 + 게이트 메시지 확인하는 테스트 최소 2개(SC-005 "모든 경우 100%"를 부분적으로나마 회귀 방지)
- 시나리오 4 → `tests/NodeKit.Cli.Tests/ToolFunctionRecipeSavePathConfirmationTests.cs`
- 시나리오 5 → `tests/NodeKit.Cli.Tests/RecipeCreateCommandTests.cs`에 회귀 케이스 추가(기존 파일 수정)

`dotnet test tests/NodeKit.Tests/NodeKit.Tests.csproj && dotnet test tests/NodeKit.Cli.Tests/NodeKit.Cli.Tests.csproj`가 전부 통과하고 `dotnet build`가 경고를 늘리지 않으면 이 기능은 "동작 확인 완료"로 본다(CLAUDE.md §10 완료 보고 기준과 일치).
