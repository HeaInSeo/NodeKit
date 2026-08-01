# Phase 0 Research: ToolFunctionSpec v0.3 Authoring Scope

spec.md의 Assumptions 섹션이 이미 상위 수준 모호함(용어, 소유권, 참조 방식)을 해소했으므로, 이 문서는 스펙이 아니라 **구현 방법 결정**만 다룬다. 각 항목은 기존 NodeKit `Recipes/` 코드 조사(§Explore 결과)를 근거로 삼는다.

## 1. Lifecycle 상태 저장 방식

- **Decision**: `ToolFunctionRecipeState` enum(`Draft, Ready, Submitted, Built, Validated, Approved`)을 `ToolFunctionRecipe` POCO에 새 필드로 추가하고, JSON에 직접 직렬화(`System.Text.Json` + `JsonStringEnumConverter`)한다.
- **Rationale**: 기존 `RecipeDocument`는 이런 상태 필드가 없고, 완결성 판정은 `RecipeAuthoringSession`의 휘발성 인메모리 상태로만 존재한다(RecipeDocument.cs 조사 결과). spec.md FR-017/FR-018/FR-020이 요구하는 `Draft↔Ready` 전이와 그 이후 상태(Submitted/Built/Validated/Approved, 이번 기능은 전이 수단 미구현이지만 필드는 예약해야 함, Assumptions 참고)는 파일 자체에 영속돼야 한다 — RecipeDocument 패턴을 그대로 베끼면 안 되고 새 필드가 필요하다.
- **Alternatives considered**: (a) 상태를 파일명 접미사로 인코딩 — 파일 이름 변경마다 이력 추적이 깨져서 기각. (b) 별도 `.state` 사이드카 파일 — 파일 두 개가 따로 놀 위험(하나만 복사/이동될 수 있음)으로 기각.

## 2. Validator 아키텍처

- **Decision**: 기존 `IValidator`(하드타입 `ToolDefinition` 전용) 인터페이스를 구현하지 **않고**, `RecipeValidator`와 동일한 `static class ToolFunctionRecipeValidator { Validate(ToolFunctionRecipe) }` 패턴을 따른다. `ToolFunctionRecipeValidationPipeline`이 이 정적 메서드 하나만 호출하는 얇은 래퍼가 된다(RecipeValidationPipeline이 여러 IValidator를 조합하는 것과 달리, 이번 기능엔 "렌더된 하위 DTO"가 없으므로 2단계 조합이 필요 없다).
- **Rationale**: `IValidator.Validate(ToolDefinition)`는 `ToolDefinition`에 못박혀 있고, `ToolFunctionRecipe`는 `ToolDefinition`으로 렌더링되지 않는다(별도 wire 개념, FR-019). 인터페이스를 억지로 맞추면 불필요한 어댑터가 생긴다 — `RecipeValidator`가 이미 "여러 concern을 private 헬퍼로 나누고 공유 `List<ValidationViolation>`에 추가"하는 검증된 패턴이라 그대로 따른다.
- **Alternatives considered**: `IValidator` 제네릭화(`IValidator<T>`) — 기존 4개 validator(`RequiredFieldsValidator` 등)까지 리팩터링해야 해서 CLAUDE.md §7(무관한 리팩터 금지)에 위배, 기각.

## 3. Rule ID 네임스페이스

- **Decision**: `L1-TFR-###` 접두사를 새로 쓴다(ToolFunctionRecipe).
- **Rationale**: 기존 네임스페이스(`L1-REQ-*`, `L1-IMG-*`, `L1-RCP-*`, `L1-SRC-*`, `L1-DOCKER-*`, `L1-PKG-*`)와 충돌하면 안 되고, 조사에서 확인한 명명 충돌 위험(spec.md "명명 충돌 위험" 항목 — `RecipeVariant`류)과 같은 실수를 피하려면 새 접두사가 한눈에 구분돼야 한다.
- 규칙 목록(초안, tasks.md에서 구체화): `L1-TFR-001`(digest 참조 없음), `L1-TFR-002`(functionId 형식), `L1-TFR-003`(command executable에 공백 포함 — raw shell 문자열 붙여넣기 휴리스틱), `L1-TFR-004`(포트 이름 중복), `L1-TFR-005`(enforced resource limit < request), `L1-TFR-006`(필수 필드 누락 — 포트/자원/fixture 포함).

## 4. FR-006 "raw shell 문자열 거부"의 실제 메커니즘

- **Decision**: 스키마 자체가 `CommandContract.Executable`(단일 실행파일 경로/이름)과 `Arguments`(배열)를 분리하므로, JSON을 손으로 편집해 `Executable`에 `"bash -c 'foo | bar'"` 같은 문자열을 통째로 넣는 경우만 실질적 우회 경로다. 이걸 잡기 위해 `L1-TFR-003`: `Executable`에 공백·파이프(`|`)·세미콜론(`;`)·리다이렉션(`>`, `<`) 문자가 포함되면 검증 실패로 처리한다.
- **Rationale**: 대화형 마법사(`ToolFunctionRecipeCreateFlow`)는 애초에 "명령을 한 줄로 입력하세요" 같은 단일 프롬프트를 두지 않고 Executable/Arguments를 별도로 물어보게 설계하면 상호작용 경로에서는 이 문제가 구조적으로 발생하지 않는다(spec Acceptance Scenario 2). 검증 규칙은 비대화형(`--non-interactive --field`) 경로나 손으로 수정한 JSON을 위한 방어선이다.
- **Alternatives considered**: 정규식으로 "이것이 shell 스크립트처럼 보이는지" 판정 — 오탐 위험이 커서(예: 정당한 실행파일 경로에 특수문자가 필요한 경우는 드물지만 있을 수 있음) 화이트리스트(영숫자/`-`/`_`/`/`/`.`만 허용) 방식이 더 예측 가능하므로 채택.

## 5. `functionId` 형식 규칙

- **Decision**: `\A[a-z][a-z0-9]*(\.[a-z][a-z0-9]*)*\z` (예: `samtools.sort`, `bwa.mem.paired-end`처럼 점으로 구분된 소문자 세그먼트). `RecipeValidator`가 쓰는 "`\A...\z` 앵커, `$`는 trailing `\n` 때문에 안전하지 않음" 관례를 그대로 따른다.
- **Rationale**: ToolFunctionSpec v0.3 원문 §6.1 예시(`samtools.sort`)와 일치하고, 기존 `RecipeValidator`의 정규식 앵커링 관례(코드 주석에 명시된 이유)를 재사용한다.
- **Alternatives considered**: 케밥케이스(`samtools-sort`) — 원본 문서 예시와 어긋나서 기각.

## 6. FR-023 파일 충돌 감지 — 키 선택

- **Decision**: `functionId`+`revision` 조합을 1차 충돌 키로 쓰되, 실제 감지는 "저장하려는 디렉터리 안의 기존 `*.json` 파일을 열어 `functionId`+`revision` 필드를 읽어 비교"하는 방식으로 구현한다(파일명 패턴만으로는 사용자가 임의 파일명을 줄 수 있어 불충분).
- **Rationale**: 기존 `RecipeCreateFlow.PromptSavePath`의 유일한 선례는 **파일 경로** 존재 여부만 확인한다(`File.Exists(savePath)`) — 이건 정확한 경로 재사용 시나리오만 잡고, "다른 파일명으로 저장했지만 같은 `functionId`+`revision`"인 진짜 충돌은 못 잡는다. spec FR-023의 의도(같은 functionId/revision 조합 충돌 방지)를 만족하려면 파일 경로 검사보다 한 단계 더 나아가야 한다.
- **Alternatives considered**: 인덱스 파일(`.nodekit/function-recipes-index.json`)을 별도로 유지 — 더 빠르지만 인덱스와 실제 파일이 어긋날 위험(사용자가 파일을 수동으로 옮기거나 지울 수 있음)이 있어, 이번 범위(로컬 파일 몇 개 수준)에서는 과설계로 판단해 기각. 디렉터리 스캔 방식이 CLAUDE.md §7(단순함 우선)과 더 맞는다.

## 7. CLI 명령 패밀리 이름

- **Decision**: `nodekit function-recipe create|validate|render` (spec.md Assumptions가 이미 예시로 제시한 이름을 채택 확정).
- **Rationale**: 기존 `nodekit recipe create` / `nodekit validate` / `nodekit render` 명명 관례와 일관되고, `recipe`(ToolSpec 축)와 `function-recipe`(ToolFunctionSpec 축)가 시각적으로 구분된다.
- **Alternatives considered**: `nodekit tool-function create` — "Recipe"라는 핵심 용어(사용자가 이번 세션에서 직접 확정한 용어, 대화 기록 참고)가 이름에서 사라져서 기각.

## 8. `render`가 실제로 만드는 JSON의 범위

- **Decision**: `render`는 `ToolFunctionRecipe` 전체를 하나의 canonical JSON으로 출력하되, 문서 상단에 두 그룹으로 주석/필드 그룹핑을 명시한다 — (a) 오늘 실제 `BuildRequest`(`kind=BUILD_KIND_TOOLFUNCTIONSPEC`) proto 필드와 1:1 매핑되는 부분(`base_image_digest`↔`baseToolImageDigest`, `script`↔스크립트 참조), (b) 아직 어떤 실제 wire 메시지에도 속하지 않는 나머지(command/포트/parameter/fixture/자원/환경/validationRequirements) — `TOOLFUNCTIONSPEC_OPERABLE_DESIGN_v0.1.md` §3.1이 이걸 "2단계 wire" 구조(1단계=BuildRequest 재사용, 2단계=아직 없는 등록 RPC)로 이미 정리해뒀다.
- **Rationale**: spec FR-019가 "canonical JSON은 NodeVault ToolFunction 빌드 계약과 동일한 wire shape"라고 했지만, 실제로는 그런 단일 wire shape가 아직 존재하지 않는다(등록 RPC 미정, NodeVault issue #19). 렌더링 결과를 "미래에 어떻게 나뉠지 이미 알고 있는 미리보기"로 명확히 표시해야 사용자가 오해하지 않는다.
- **Alternatives considered**: 1단계 필드만 렌더링하고 나머지는 별도 파일 — 사용자가 계약 전체를 한눈에 보고 싶어할 것(spec SC-003, "필드 라벨만으로 전체 계약 구성요소를 식별")이므로 기각, 하나의 파일에 그룹 주석으로 구분하는 쪽을 택함.

## 9. 포트 모델 — 단일 타입 vs Input/Output 분리

- **Decision**: 공통 `PortContract` 하나에 `Direction`(`Input`/`Output`) discriminator를 두고, Input 전용 필드(companion file, 경로 배치 규칙)와 Output 전용 필드(glob, 완료 검증법, downstream 호환성 메모)는 nullable로 공존시킨다. `ToolFunctionRecipe.InputPorts`/`OutputPorts`는 각각 `List<PortContract>`이지만 같은 타입을 쓴다.
- **Rationale**: spec Key Entities가 "Port (Input/Output)"을 이미 하나의 개념으로 서술하고 있고, 공통 필드(이름, 데이터 형식, cardinality, 필수 여부)가 대부분이라 완전히 분리하면 중복 코드가 생긴다.
- **Alternatives considered**: `InputPort`/`OutputPort` 완전 분리 타입 — 필드 재사용성이 떨어지고, 두 리스트를 순회하며 "이름 중복 검사"(FR-007/FR-008, Edge Case)를 할 때 타입이 다르면 코드가 두 배가 돼서 기각.

## 10. `submit` 서브커맨드 — 최초 계획 오류 정정 (2026-07-24)

- **잘못된 초기 결정**: contracts/cli-function-recipe-commands.md 최초 초안은 "`submit` 서브커맨드 자체를 안 만들고 일반 usage 오류로 충분하다"고 YAGNI 논리로 판단했다.
- **정정**: FR-021, User Story 2 Acceptance Scenario 3, SC-005("모든 경우 100%에 명확한 안내, 무응답 없음")를 다시 대조한 결과 이는 요구사항 누락이었다. 일반 usage 오류(exit 2, "알 수 없는 명령")는 SC-005가 명시적으로 금지하는 "원인 불명의 오류"에 해당한다.
- **결론**: `nodekit function-recipe submit <path>`를 실제로 구현하되, `State`와 무관하게 항상 게이트 미개방 메시지로 실패(exit 1)하도록 만든다. contracts/cli-function-recipe-commands.md·plan.md·quickstart.md 전부 이 정정을 반영했다.
- **교훈**: "이 기능은 범위 밖이니 진입점 자체를 안 만든다"는 결정과 "제출을 시도했을 때 특정 안내 메시지를 표시해야 한다"는 요구사항은 다른 것이다 — 전자만 보고 후자를 놓치기 쉽다. Out of Scope 섹션과 FR/Acceptance Scenario를 항상 같이 대조해야 한다.

## 11. 미해결 구현 위험 (tasks.md/구현 단계에서 확인 필요)

- **`--field` 비대화형 문법의 중첩·인덱스 필드 지원 여부**: quickstart.md는 `--field InputPorts[0].Name=bam` 같은 문법을 전제하지만, 기존 `RecipeDocument`의 `--field` 파서(`RecipeCreateCommand`/`CliOptionParser`)가 이런 인덱스·중첩 경로 문법을 실제로 지원하는지 이번 조사로 확인하지 못했다. 기존 `RecipeDocument`는 대부분 평평한 스칼라/문자열 리스트 필드만 다뤄 이 정도 복잡도의 선례가 없을 수 있다. **구현 시 가장 먼저 검증해야 할 항목** — 만약 기존 파서가 이 문법을 지원하지 않으면, (a) 파서를 확장하거나 (b) 포트/파라미터처럼 복잡한 반복 구조는 비대화형 모드에서 아예 지원하지 않고 대화형 모드 전용으로 남기는 것 중 하나를 다시 결정해야 한다.
- **FR-025(과거 데이터 로드) — 확인 결과 위험 아님**: `System.Text.Json`은 기본적으로 알 수 없는 JSON 속성을 조용히 무시하고 예외를 던지지 않는다(`UnmappedMemberHandling.Disallow`를 명시적으로 켜지 않는 한). 따라서 `RecipeDocument.Inputs`/`Outputs`/`Command` 필드를 이번 작업에서 남겨두든 나중에 제거하든, 과거 파일을 다시 불러올 때 자동으로 FR-025의 "무시" 조건을 만족한다 — 별도 마이그레이션 코드가 필요 없다.

## 12. NEEDS CLARIFICATION 잔여 여부

없음 — 위 결정들로 Technical Context의 모든 항목이 확정됐다. spec.md 자체에 남은 미해결 사항(NodeVault issue #19, nan 결합 메커니즘 등)은 이 기능의 구현 방법이 아니라 상위 컴포넌트 조율 사안이므로 Phase 0 연구 대상이 아니다(spec.md "의존성 및 위험" 섹션에 이미 기록됨). §11의 `--field` 파서 위험은 NEEDS CLARIFICATION이 아니라 구현 착수 시 첫 번째로 검증할 기술 스파이크로 tasks.md에 명시적 선행 작업으로 남긴다.
