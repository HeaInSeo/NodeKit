# CLI Contract: `nodekit function-recipe *`

기존 `nodekit recipe create` / `nodekit validate` / `nodekit render` 명령 계약(문서: `docs/NODEKIT_CLI_USAGE.md`)과 동일한 관례를 따른다. exit code 체계도 동일하게 재사용한다: `0` 성공, `1` 검증/비즈니스 실패, `2` 사용법/인자 오류.

## `nodekit function-recipe create [<path>]`

**입력**
- `--tool-spec-digest <digest>` (필수) — FR-001/FR-002
- `--base-tool-image-digest <digest>` (필수) — FR-001/FR-002
- `--non-interactive` (선택) — 지정하면 대화형 마법사 대신 `--field Name=Value`(반복 가능, 리스트 필드는 여러 번) 조합으로 생성
- `[<path>]` (선택) — 저장 경로. 생략 시 대화형 모드에서만 프롬프트로 결정, 비대화형 모드에서는 필수(exit 2)

**동작**
1. `--tool-spec-digest`/`--base-tool-image-digest` 형식 검사(빈 문자열 거부) — 실패 시 exit 2.
2. 대화형 모드: `ToolFunctionRecipeCreateFlow`가 functionId → revision → 표시정보 → 스크립트 경로 → command(executable/arguments/workingDir/env/exitCodes/timeout 각각 별도 프롬프트, 단일 통합 입력 없음) → 입력 포트(반복) → 출력 포트(반복) → fixture 참조(반복) → 예상 결과(출력 포트당) → 중간파일 정책(선택, 반복) → parameter(선택, 반복) → enforced 자원 → 실행 환경(선택) → validationRequirements(선택) 순으로 진행.
3. 저장 직전 파일 충돌 검사(research.md §6): 저장 대상 디렉터리의 기존 `*.json` 중 같은 `functionId`+`revision`을 가진 파일이 있으면 대화형 모드는 확인 프롬프트, 비대화형 모드는 exit 1 + 명확한 오류 메시지(묵시적 덮어쓰기 금지, FR-023).
4. 저장 성공 시 `Draft` 상태로 파일에 기록, exit 0.

**출력**: 성공 시 저장된 파일 경로를 stdout에 1줄 출력. 실패 시 stderr에 원인 메시지, 대화형 모드는 계속 재시도 가능.

## `nodekit function-recipe validate <path>`

**입력**: `<path>`(필수, ToolFunctionRecipe JSON 파일)

**동작**: 파일을 읽어 `ToolFunctionRecipe.Normalize()` 후 `ToolFunctionRecipeValidationPipeline.Validate(...)` 실행(FR-017).
- 통과: 파일의 `State`를 `Ready`로 갱신해 저장(FR-018), stdout에 "검증 통과" 메시지 + exit 0.
- 실패: 파일은 변경하지 않음(`Draft` 유지), 위반 사항 목록(RuleId + 필드 + 메시지)을 stderr에 출력, exit 1.

## `nodekit function-recipe render <path> --out <out.json> [--pretty]`

**입력**: `<path>`(필수), `--out <out.json>`(필수), `--pretty`(선택, JSON indent)

**동작**(FR-019, FR-020, FR-021):
1. 파일의 `State`가 `Ready`가 아니면 렌더링 거부, stderr에 "먼저 검증을 통과해 Ready 상태가 되어야 합니다" 안내, exit 1(Acceptance Scenario User Story 2 #2).
2. `Ready`이면 `ToolFunctionBuildRequestPreviewFactory`로 stage-1/stage-2 그룹이 구분된 canonical JSON을 `--out` 경로에 기록, exit 0. `State`는 변경하지 않음(`Ready` 유지, `Submitted`로 전이하지 않음 — FR-020).
3. 이 명령은 어떤 네트워크 호출도 하지 않는다. `--submit` 같은 플래그를 이 명령에 추가하지 않는다.

## `nodekit function-recipe submit <path>` — 항상 차단되는 명령 (2026-07-24 정정)

**이전 초안은 이 명령 자체를 안 만들기로 했었으나, spec.md FR-021 / User Story 2 Acceptance Scenario 3 / SC-005를 다시 대조한 결과 오판이었다.** 세 요구사항 모두 "제출을 시도하면 **구체적이고 친절한 NodeVault 게이트 안내**를 받는다"를 명시적으로 요구하고, SC-005는 "원인 불명의 오류나 무응답은 발생하지 않는다"까지 못박는다. 명령 자체를 없애고 일반 usage 오류(exit 2, "알 수 없는 명령")로 대체하면 정확히 이 요구를 위반한다 — YAGNI가 아니라 요구사항 누락이었다.

**입력**: `<path>`(필수, ToolFunctionRecipe JSON 파일)

**동작**:
1. 파일을 읽는다(존재하지 않거나 파싱 실패 시 기존 파일 I/O 오류 처리 관례를 그대로 따름).
2. 파일 내용이나 `State`와 무관하게 — `Draft`든 `Ready`든 — **항상** stdout에 "NodeVault ToolFunction 빌드 게이트가 아직 열려 있지 않습니다. 이 기능은 issue #19가 해결된 이후 지원됩니다" 계열의 친절한 안내를 출력하고 exit 1로 종료한다.
3. 어떤 네트워크 호출도 하지 않는다. `State`를 변경하지 않는다(`Submitted`로 전이하지 않음, FR-020).
4. `render`와 달리 `Ready` 여부를 검사하지 않는다 — 게이트가 닫혀 있다는 사실은 Recipe의 완결성과 무관하게 항상 참이므로, 사용자가 `Draft` 상태에서 실수로 `submit`을 시도해도 똑같이 안내해야 한다(SC-005의 "모든 경우 100%" 요건).

**출력**: exit 1(성공이 아니므로 실제 제출은 절대 일어나지 않는다는 신호로), stdout 또는 stderr 중 하나에 게이트 미개방 메시지 — 어느 스트림을 쓸지는 구현 시 기존 관례(`SubmitCommand`가 실패 메시지를 stderr로 보내는 패턴)를 따른다.

## 공통 계약

- 모든 서브커맨드는 `--help`/`-h`를 인자 어느 위치에서든 인식한다(`IsHelpRequested` 관례 재사용).
- 모든 파일 I/O 오류(`IOException`)와 JSON 파싱 오류(`JsonException`)는 사용자 친화적 한국어 stderr 메시지로 감싼다(기존 `RecipeCreateCommand`/`SubmitCommand` 관례).
