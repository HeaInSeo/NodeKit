# Quickstart: ToolFunctionRecipe 로컬 저작·검증·렌더링

이 문서는 구현이 끝난 뒤 "동작한다"를 사람이 직접 확인하는 절차다. 전체 구현 코드는 포함하지 않는다 — 실행 가능한 시나리오와 기대 결과만 정리한다.

## 사전 조건

- .NET 10 SDK 설치.
- 이미 확정된 ToolSpec 하나의 `toolSpecDigest`(C1)/`baseToolImageDigest`(A)(실제 NodeVault 빌드 없이도, 형식만 맞는 테스트용 문자열이면 충분 — 이 기능은 NodeVault에 실제로 조회하지 않는다). 형식은 서로 다르다: C1은 `sha256:` 없는 소문자 hex 64자리(`submit`의 typed 등록 preflight가 이 형식만 받는다), A는 `sha256:` + hex 64자리다. 식별자 표는 `docs/NODEKIT_CLI_USAGE.md` §9.
- `dotnet build src/NodeKit.Cli/NodeKit.Cli.csproj`가 경고 0개로 성공(CLAUDE.md §8).

## 시나리오 1 — User Story 1: Draft 작성 → Ready 검증 통과

```bash
cd tests/manual-scratch   # 또는 임시 디렉터리, 실제 프로젝트 소스는 건드리지 않음

dotnet run --project src/NodeKit.Cli -- function-recipe create samtools-sort.json \
  --tool-spec-digest aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa \
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

## 시나리오 3b — FR-020/FR-021/SC-005: 두 번째 이미지 빌드 제출 (P07, 2026-10-11 정정)

```bash
NODEKIT_NODEVAULT_URL=http://<nodevault>:50051 dotnet run --project src/NodeKit.Cli -- function-recipe submit samtools-sort.json
```

**기대 결과**:
- `Draft`(검증 전)이거나 현재 내용이 검증에 실패하면 exit 1 + 구체적 진단, 요청 0회. 일반 "알 수 없는 명령" usage 오류(exit 2)가 아니어야 한다.
- 유효한 `Ready`라도 현재 NodeVault가 kind 2를 받지 않으므로 exit 1 + `TOOLFUNCTION_BUILD_GATE_CLOSED` 안내로 끝난다. 요청·receipt는 없고 주소가 있어도 연결하지 않는다.
- NodeVault가 kind 2를 수용해 상류 게이트가 열린 뒤에는: 유효한 `Ready`이면 Recipe 옆 `.nodekit/receipts/<request-id>.json`(`operation_kind=toolfunction_image_build`)을 먼저 남긴 뒤 `ResolveToolSpec → SubmitToolBuild → WatchToolBuild`를 보낸다. Watch `Succeeded`에 전체 두 번째 이미지 digest B가 있으면 B/C2/build ID를 출력하고 typed 등록으로 넘어간다. 최종 exit code는 등록 결과를 따른다(성공 0, 거부·결과 불명 1, timeout 124, 취소 130, 저장 실패 2).
- `Failed`/`Interrupted`, 최종 상태 없는 종료, B 없는 `Succeeded`는 exit 1이다.
- 빌드 성공은 승인이 아니다. 게이트가 열린 뒤 full B를 확인하면 typed 등록(`RegisterToolFunction`, P08)으로 넘어가며, 서버가 돌려준 F/H/P/version을 그대로 출력·저장한다. 결과 불명 등록은 `function-recipe register-replay`로 확인한다(`contracts/cli-function-recipe-commands.md`). Recipe 파일은 어떤 필드도 변경되지 않는다.

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
- 시나리오 3b → `tests/NodeKit.Cli.Tests/ToolFunctionRecipeSubmitCommandTests.cs`(입력/Draft/invalid RPC 0회) + `tests/NodeKit.Cli.Tests/ToolFunctionSecondImageBuildP07Tests.cs`(상류 게이트 닫힘 RPC 0회, 게이트를 연 in-process fake BuildService로 S3-05-A..E, S3-04-C, S3-09)
- 시나리오 4 → `tests/NodeKit.Cli.Tests/ToolFunctionRecipeSavePathConfirmationTests.cs`
- 시나리오 5 → `tests/NodeKit.Cli.Tests/RecipeCreateCommandTests.cs`에 회귀 케이스 추가(기존 파일 수정)

`dotnet test tests/NodeKit.Tests/NodeKit.Tests.csproj && dotnet test tests/NodeKit.Cli.Tests/NodeKit.Cli.Tests.csproj`가 전부 통과하고 `dotnet build`가 경고를 늘리지 않으면 이 기능은 "동작 확인 완료"로 본다(CLAUDE.md §10 완료 보고 기준과 일치).
