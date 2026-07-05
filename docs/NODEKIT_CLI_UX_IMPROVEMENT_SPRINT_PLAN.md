# NodeKit CLI UX 개선 스프린트 계획

Status: U1-U4 완료 / U5 진행 중 (U5-2만 잔류)  
Created: 2026-06-30  
Updated: 2026-07-06  
Scope: CLI UX 품질을 상업용 수준으로 끌어올리는 4개 스프린트

이 계획은 `NODEKIT_CLI_FIRST_SPRINT_PLAN.md`의 Sprint 0-5 완료 이후 진행한다.
Phase 6(ToolSpec 경로 전환)은 2026-07-02 완료됨 — 이 문서의 UX 항목과 별도 트랙.

**추가 UX 개선 (계획 단계에서 이미 구현됨, 별도 스프린트 불필요):**

```text
✓ Inputs/Outputs 프리셋 설명 + 예시 출력
    RecipeCreateFlow.cs PromptPresetListField에서 p.Description.Get("ko") + p.Examples 출력 중.
✓ 앞으로 입력할 항목에 사용자 레이블 표시
    MethodRecommendationPresenter.cs에서 field.Label.Get("ko") 사용 중.
✓ 필드 /back 지원 + [n/total] 진행도 표시
    RecipeCreateFlow.RunFieldLoop에 history + ClearField + [x/total] 이미 구현됨.
```

---

## 진행률 보고 형식 (매 보고 시 포함)

```
═══════════════════════════════════════════════════════
 NodeKit CLI UX 개선 — 스프린트 진행률
═══════════════════════════════════════════════════════
 전체 진행률   : ██████████  22/23 (96%)
 현재 스프린트 : U5 — 문서 + 최종 검증
 U5 진행률     : ██████████  2/3  (67%)
═══════════════════════════════════════════════════════
 U1 TUI 기반 도입          [5/5]  ██████████
   ✓ U1-1  Spectre.Console 패키지 추가
   ✓ U1-2  IAnsiConsole 추상화 + RecipeCreateScreen 교체
   ✓ U1-3  3구역 레이아웃 구현 (설명 / 슬래시명령 / 입력)
   ✓ U1-4  AnsiRecipeConsoleRenderingTests (TestConsole 기반)
   ✓ U1-5  빌드 0 경고 / 전체 테스트 통과 (464 tests)
───────────────────────────────────────────────────────
 U2 통합 흐름 재설계       [6/6]  ██████████
 U3 Base image 자동 조회   [5/5]  ██████████
 U4 저장 경로 마지막 확정  [4/4]  ██████████ (U4-2 draft save 제외, 범위 밖)
 U5 문서 + 최종 검증       [2/3]  ███████░░░
   ✓ U5-1  NODEKIT_CLI_USAGE.md 업데이트 (2026-07-02)
   ○ U5-2  사용자 수동 테스트 통과 (seoy 원격 장비 필요)
   ✓ U5-3  커밋 + GitHub push
═══════════════════════════════════════════════════════
```

범례: ✓ 완료 / ○ 미완료 / → 진행중

---

## U1. TUI 기반 도입 (Spectre.Console)

**목표**: 사용자가 설명 영역 / 슬래시 명령 영역 / 입력 영역을 시각적으로 구분할 수 있도록 한다.

### U1-1. Spectre.Console 패키지 추가

- `src/NodeKit.Cli/NodeKit.Cli.csproj`에 `Spectre.Console` NuGet 추가
- 버전을 정확히 고정 (floating `*` 금지 — CLAUDE.md §3)
- `dotnet build` 경고 0 확인

### U1-2. IAnsiConsole 추상화 + RecipeCreateScreen 교체

- `RecipeCreateScreen`이 `IAnsiConsole`을 받도록 변경
- 현재 `stdout.WriteLine()` 호출을 `IAnsiConsole` API로 교체
- `ConsoleCancelKeyCancellationSource` 등 기존 인프라 유지

### U1-3. 3구역 레이아웃 구현

```
┌─ 설명 영역 ────────────────────────────────────────┐
│  [2 / 6]                                            │
│  도구 이름 — recipe에서 식별할 도구 이름입니다.       │
│     예: bwa-mem, samtools                           │
├─ 명령 힌트 ─────────────────────────────────────────┤
│  /back 이전  /review 현재값  /cancel 종료           │
├─ 입력 ──────────────────────────────────────────────┤
│  > _                                                │
└─────────────────────────────────────────────────────┘
```

- 설명 영역: 각 단계 안내 + 예시
- 명령 힌트 영역: 현재 컨텍스트에서 가능한 슬래시 명령만 표시
- 입력 영역: 시각적으로 구분된 입력 프롬프트

### U1-4. 기존 테스트 TestConsole 기반으로 전환

- `Spectre.Console.Testing.TestConsole`로 기존 `TextReader`/`TextWriter` 주입 교체
- 434개 기존 테스트 모두 통과 확인
- 새 레이아웃에 대한 스냅샷 테스트 추가

### U1-5. 빌드 + 테스트 검증

- `dotnet build` 0 경고 / 0 에러
- `dotnet test` 전체 통과

**완료 기준**: 실제 터미널에서 3구역이 시각적으로 구분되고, 모든 테스트 통과.

---

## U2. 통합 흐름 재설계

**목표**: 쉬운 안내 모드와 빠른 설정 모드가 동일한 공통 흐름을 공유한다. 각 단계가 두 모드 모두에 존재한다.

### 공통 흐름 (두 모드 모두 이 순서를 따른다)

```
[단계 1] 모드 선택
[단계 2] 방식 결정 (클루 선택 / Q&A)
[단계 3] 채널 확정 (package/mirror 방식)   ← 빠른 설정에 추가
[단계 4] Base image 선택 + digest 조회     ← 신규 (U3에서 구현)
[단계 5] 나머지 필드 입력 (RunFieldLoop)
[단계 6] 최종 검증 + recovery
[단계 7] 빌드 문자열 선택 (ResolveRecipe)
[단계 8] 포트 설정
[단계 9] 저장 경로 확정 + 저장             ← U4에서 구현
```

### U2-1. 공통 흐름 클래스 신설

- `RecipeCreateFlow` (신규): 단계 1-9를 순서대로 실행하는 오케스트레이터
- `RecipeCreateInteractiveRunner`는 진입점만 유지, 흐름 로직은 `RecipeCreateFlow`로 이동

### U2-2. BeginnerGuideFlow 역할 축소

- 역할: 클루 선택 + 초기값 추출 (단계 2)만 담당
- 채널 확정(단계 3), base image 선택(단계 4)은 공통 흐름으로 이동
- 쉬운 안내의 "친절한 설명"은 각 단계의 verbose 모드로 표현

### U2-3. 빠른 설정에 채널 확정 단계 추가

- Q&A 6문항 후 채널이 확정되지 않은 경우 채널 입력 요청
- 쉬운 안내와 동일한 채널 확정 로직 공유

### U2-4. RunFieldLoop에서 ImageRef 처리 분리

- `ImageRef` 필드를 `RunFieldLoop`의 단순 텍스트 입력에서 제거
- 단계 4(Base image 선택)에서 처리

### U2-5. 테스트 업데이트

- 두 모드 모두 채널 확정 단계를 거치는 테스트
- 공통 흐름 단계 순서 검증 테스트

### U2-6. 빌드 + 테스트 검증

**완료 기준**: 쉬운 안내와 빠른 설정 모두 채널 확정 단계를 포함하고, 동일한 흐름을 따른다.

---

## U3. Base Image 선택 + Digest 자동 조회

**목표**: 사용자가 64자 sha256을 손으로 타이핑하지 않아도 된다.

### U3-1. BaseImageSelectionStep 인터페이스 + 공통 UI

- 채널 정보를 받아 base image 후보 목록과 digest를 반환하는 인터페이스
- 단일 후보: 자동 선택 후 확인 요청
- 복수 후보: 번호 선택 UI

### U3-2. PublicRegistryImageDigestResolver (오픈망)

- 채널(bioconda, conda-forge 등) 기반으로 공개 레지스트리 질의
- bioconda → quay.io/biocontainers, conda-forge → Docker Hub / ghcr.io
- digest 후보 목록 반환

### U3-3. HarborImageDigestResolver 통합 (폐쇄망)

- 기존 `HarborImageDigestResolver` 유지
- `BaseImageSelectionStep`에서 Harbor / 공개 레지스트리를 환경변수 기준으로 자동 선택

### U3-4. Stub + 테스트 추가

- `NODEKIT_BASE_IMAGE_STUB=1` 환경변수로 stub 동작
- 오픈망 / 폐쇄망 / stub 세 가지 경로 테스트

### U3-5. 빌드 + 테스트 검증

**완료 기준**: 오픈망과 폐쇄망 모두에서 사용자가 번호 선택만으로 base image + digest가 확정된다.

---

## U4. 저장 경로 마지막 확정

**목표**: CLI 시작 시 파일 경로를 요구하지 않는다. 마법사 완료 후 경로를 확정한다.

### U4-1. Run() 시그니처 변경

- `string outPath` → `string? outPathHint`
- 힌트가 파일 경로면 마지막에 해당 경로로 저장
- 힌트가 디렉터리 또는 null이면 마지막 단계에서 확정

### U4-2. 임시 파일 draft 저장

- 마법사 진행 중 `~/.nodekit/drafts/{sessionId}.json`에 자동 저장
- `/cancel` 또는 비정상 종료 시 draft 파일 유지 (추후 resume 가능성 열어둠)
- 정상 완료 시 최종 경로로 이동 후 draft 삭제

### U4-3. 저장 경로 확정 UI (단계 9)

```
저장 위치를 확인하세요.

기본 경로: ./samtools-1.17.json
다른 경로를 입력하거나 Enter로 기본 경로를 사용:
> _
```

- 기본값: `./{ToolName}-{ToolVersion}.json`
- 사용자가 다른 경로 입력 가능
- 경로 충돌(파일 이미 존재) 시 덮어쓸지 확인

### U4-4. 테스트 + 빌드 검증

**완료 기준**: `nodekit recipe create`를 경로 없이 실행 가능. 마지막 단계에서 경로 확정 후 저장.

---

## U5. 문서 + 최종 검증

### U5-1. NODEKIT_CLI_USAGE.md 전면 업데이트

- 새 3구역 TUI 화면 트랜스크립트
- 통합 흐름 단계 1-9 설명
- 오픈망 / 폐쇄망 base image 조회 설명
- 저장 경로 확정 단계 설명
- 시나리오 A / B / C 트랜스크립트 전면 재작성

### U5-2. 사용자 수동 테스트 통과

- 쉬운 안내 모드: 설치 명령 → 채널 확정 → base image 자동 조회 → 저장
- 빠른 설정 모드: Q&A → 채널 확정 → base image 자동 조회 → 저장
- 오픈망 / 폐쇄망 각각 확인

**Progress (사전 검증, 2026-07-05, 2차 실행까지 반영):** seoy 없이 heain 로컬
NodeVault + 로컬 레지스트리로 위 흐름(채널 확정, 오픈망/폐쇄망 base image 조회,
ResolveRecipe 정책 분기, 저장, 실제 submit, 취소)을 TC-1~TC-13 전체로 사전
검증함. 이 과정에서 버그 6건을 발견해 전부 수정·close함:
- #5 빌드 실패 시에도 exit code 0 (NodeKit `bd9786e`)
- #6 취소가 서버 빌드를 실제로 안 멈춤 (NodeKit `bd9786e`)
- #7 공식 이미지명 401 (NodeKit `73805d4`)
- #8 CA cert 로딩 크래시 (NodeKit `a938690`)
- #9 네트워크 실패를 조용히 성공 처리 (NodeVault `605a98d` + NodeKit `1749a58`)
- #10 recipe create stdin EOF 무한 루프 (NodeKit `f1b5b37`)

상세는 docs/NODEKIT_LOCAL_GRPC_TEST_SCENARIO.md §7 참조. **여전히 seoy 원격
장비에서의 실제 수동 테스트가 남아있어 이 항목은 미완료(○) 상태를 유지한다.**

### U5-3. 커밋 + GitHub push

**완료 기준**: 사용자 수동 테스트 통과 + 문서 업데이트 + push 완료.

---

## 태스크 전체 목록 (23개)

| ID | 스프린트 | 설명 | 상태 |
|---|---|---|---|
| U1-1 | U1 | Spectre.Console 패키지 추가 | ✓ |
| U1-2 | U1 | IAnsiConsole 추상화 + RecipeCreateScreen 교체 | ✓ |
| U1-3 | U1 | 3구역 레이아웃 구현 | ✓ |
| U1-4 | U1 | AnsiRecipeConsoleRenderingTests (TestConsole 기반) | ✓ |
| U1-5 | U1 | 빌드 0 경고 / 전체 테스트 통과 | ✓ |
| U2-1 | U2 | RecipeCreateFlow 신설 | ✓ |
| U2-2 | U2 | BeginnerGuideFlow 역할 축소 | ✓ |
| U2-3 | U2 | 빠른 설정에 채널 확정 단계 추가 | ✓ |
| U2-4 | U2 | RunFieldLoop에서 ImageRef 처리 분리 (BaseImageSelectionStep) | ✓ |
| U2-5 | U2 | 테스트 업데이트 | ✓ |
| U2-6 | U2 | 빌드 + 테스트 검증 | ✓ |
| U3-1 | U3 | BaseImageSelectionStep 인터페이스 + UI | ✓ |
| U3-2 | U3 | PublicRegistryImageDigestResolver (오픈망) | ✓ |
| U3-3 | U3 | HarborImageDigestResolver 통합 (폐쇄망) | ✓ |
| U3-4 | U3 | StubImageDigestResolver + 테스트 | ✓ |
| U3-5 | U3 | 빌드 + 테스트 검증 | ✓ |
| U4-1 | U4 | outPathHint 시그니처 변경 | ✓ |
| U4-2 | U4 | 임시 파일 draft 저장 | — (범위 조정: 제거) |
| U4-3 | U4 | 저장 경로 확정 UI (PromptSavePath) | ✓ |
| U4-4 | U4 | SavePathConfirmationTests + 빌드 검증 | ✓ |
| U5-1 | U5 | NODEKIT_CLI_USAGE.md 업데이트 | ✓ |
| U5-2 | U5 | 사용자 수동 테스트 통과 | ○ (seoy 원격 장비 필요) |
| U5-3 | U5 | 커밋 + GitHub push | ○ |

---

## 순서 의존성

```
U1 (TUI 기반) → U2 (통합 흐름) → U3 (Base image 조회) → U4 (저장 경로) → U5 (문서)
```

U2는 U1 완료 후 시작. U3는 U2의 단계 4 자리(BaseImageSelectionStep)가 확정된 후 구현.
U4는 U2 완료 후 독립적으로 진행 가능. U5는 U1-U4 모두 완료 후.
