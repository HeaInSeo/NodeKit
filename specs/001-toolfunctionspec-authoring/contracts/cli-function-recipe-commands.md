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

## `nodekit function-recipe submit <path>` — 두 번째 이미지 빌드 제출 (P07, 2026-10-11 정정)

이전(2026-07-24) 계약의 "항상 차단되는 명령"은 P07 기능 PR에서 실제 BuildService client 경로로 바뀌었다(v0.9.1 S3-05, S3-09). 명령이 없거나 일반 usage 오류로 끝나면 안 된다는 SC-005의 요구("원인 불명의 오류나 무응답 없음")는 그대로 유지한다.

**입력**: `<path>`(필수, ToolFunctionRecipe JSON 파일), 선택 `--url <nodevault-url>`(없으면 `NODEKIT_NODEVAULT_URL`), `--receipt <path>`, `--connect-timeout <seconds>`, `--watch-timeout <duration>`, `--format human|jsonl`. 옵션 의미와 timeout/취소 exit code(124/125/130)는 `nodekit submit`과 같다. `--strict-reproducible`은 ToolSpec Recipe 전용이라 받지 않는다.

**동작**:
1. 파일을 읽는다. 읽기/파싱/`SchemaVersion` 누락·미지원 오류는 exit 2, RPC 0회.
2. current preflight(P06): 저장된 `State=Ready` 표지만 믿지 않는다. `Ready`가 아니면 `RECIPE_NOT_READY`, 현재 내용 full L1 위반, base image A 형식, ScriptPath source(없음/절대 경로/UTF-8 아님/빈 내용), raw_spec v1, typed 등록 전제(C1 형식·typed mapping)를 확인한다. 실패하면 진단을 stderr에 쓰고 exit 1(source 파일 입력 오류는 2), RPC 0회. `State`는 바꾸지 않는다.
3. 위 로컬 검사를 통과한 뒤에야 NodeVault 주소를 확인한다(없으면 exit 2, RPC 0회).
4. 요청 envelope(tool_name=`FunctionId`, version=`Revision`, raw_spec=`nodevault.build.raw_spec.v1` exact 4키 kind 2)와 Recipe 파일 bytes·전송한 script 내용을 `operation_kind=toolfunction_image_build` receipt에 prepared로 먼저 저장한다. 저장하지 못하면 RPC 0회, exit 2. 기본 위치는 Recipe 옆 `.nodekit/receipts/<request-id>.json`이며 ToolSpec 첫 이미지(C1/A)의 receipt와 섞이지 않는다.
5. `ResolveToolSpec → SubmitToolBuild → WatchToolBuild`를 각각 한 번 호출한다. ResolveToolSpec이 거부(예: InvalidArgument)하면 Submit/Watch를 보내지 않는다.
6. 결과:
   - Watch `Succeeded` + record `image_digest`가 `sha256:` + 16진수 64자리 → exit 0. 두 번째 이미지 B, 이번 요청 ToolSpec digest C2, build ID를 출력한다. 기반 A/C1은 그대로이며 B/C2 자리에 쓰지 않는다.
   - `Succeeded`인데 B가 없거나 형식이 다름 → `SECOND_IMAGE_DIGEST_MISSING`, exit 1, recovery uncertain. 기반 이미지 A나 legacy `DigestAcquired` 로그 digest로 대신하지 않는다.
   - `Failed`/`Interrupted` → exit 1, 성공/승인 표시 없음.
   - 최종 상태 없이 스트림 종료 → exit 1, 결과 미확인(uncertain).
7. 빌드 성공은 승인(`Approved`)이 아니다. typed 등록(`RegisterToolFunction`)은 보내지 않는다(P08). NodeKit은 이미지를 만들거나 Kubernetes를 호출하지 않는다. Recipe 파일의 `State`는 어떤 경우에도 바꾸지 않는다.

**출력**: human은 진행 이벤트와 결과를 stdout, 실패 진단을 stderr에 쓴다. `--format jsonl`은 `nodekit submit`과 같은 `nodekit.submit.v1` 레코드를 쓰며, 로컬 preflight 실패는 `error_code=PREFLIGHT_FAILED` completed 레코드 하나다. `nodekit receipt watch`도 이 receipt의 `Succeeded`에 B가 없으면 같은 `SECOND_IMAGE_DIGEST_MISSING`/exit 1로 보고한다.

## 공통 계약

- 모든 서브커맨드는 `--help`/`-h`를 인자 어느 위치에서든 인식한다(`IsHelpRequested` 관례 재사용).
- 모든 파일 I/O 오류(`IOException`)와 JSON 파싱 오류(`JsonException`)는 사용자 친화적 한국어 stderr 메시지로 감싼다(기존 `RecipeCreateCommand`/`SubmitCommand` 관례).
