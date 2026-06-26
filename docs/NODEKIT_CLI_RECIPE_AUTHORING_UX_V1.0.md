# NodeKit CLI Recipe Authoring UX v1.0 개발 문서

문서명: `NODEKIT_CLI_RECIPE_AUTHORING_UX_V1.0.md`
상태: Development Contract — 코드 대조 반영본
기준 코드: `NodeKit-main (9).zip`
대상: `nodekit recipe create` 대화형 UX, final-validation recovery 문구, digest authoring seam
기반: 기존 repo v1.0 Draft, v0.9.2 구현, 코드 리뷰에서 확인된 실제 구현 상태
비범위: NodeVault submit, 이미지 빌드, registry push, MCP server, 필드 단위 완전 `/back` 네비게이션, DagEdit UI 통합, draft 저장/resume, 실제 Harbor/OCI registry 네트워크 연동, `--verbose` 도입

---

## 0. Implementation Status

이 문서는 설계상 존재하는 구성과 실제 코드에 존재하는 구성을 분리한다. 이후 개선안은 아래 상태표를 기준으로 "보강"인지 "신규"인지 판단한다.

| 항목 | 상태 | 근거 / 비고 |
|---|---|---|
| `AuthoringModeSelector` | 구현됨 | `src/NodeKit.Cli/AuthoringModeSelector.cs`; `recipe create` 시작 시 guided/quick/CI usage 선택 |
| 쉬운 안내 모드 `GuidedBeginner` | 구현됨 | `RecipeCreateInteractiveRunner.Run(...)`에서 `AuthoringModeSelector.Mode.GuidedBeginner`일 때 `BeginnerGuideFlow.Run(...)` 호출 |
| 빠른 설정 모드 `QuickSetup` | 구현됨 | `SelectMethod(...)` → 기존 method Q&A 경로 |
| `BeginnerGuideFlow` | 구현됨 | `src/NodeKit.Cli/BeginnerGuideFlow.cs` |
| 7-choice clue picker | 구현됨 | `[1] 도구 이름만 알고 있다`부터 `[7] 잘 모르겠다`까지 출력 |
| `RunToolNameFlow` | 구현됨 / 보강 필요 | 도구 이름을 받지만 bioconda/BioContainers URL 안내는 부족함 |
| `RunNoClueFlow` | 구현됨 / 보강 필요 | 설치 명령, 이미지 주소, 소스 URL, Dockerfile, 종료로 라우팅. 도구 이름 기반 lookup 복귀 경로는 부족함 |
| `BuildRecoveryPlan` | 구현됨 | `RecipeAuthoringSession.BuildRecoveryPlan(...)` |
| `RecipeValidationRecoveryAction` | 구현됨 | `Label`, `Kind`, `RelatedFields`, `Description`, `BeginnerHint` 구조 |
| `BeginnerHint` 출력 | 구현됨 | `RecipeCreateInteractiveRunner.RunRecoveryLoop(...)`에서 `action.BeginnerHint.Get("ko")` 출력 |
| `BuildKind` null guard | 구현됨 | `RecipeValidationPipeline.ValidateRecipe(...)`에서 `BuildKind == null` fail-fast |
| `RecipeSession.Build()` 내부 `Resolve()` 자동 호출 | 없음 / 유지해야 함 | `Build()`는 document 조립만 담당. `BuildKind` resolve는 runner가 명시 호출 |
| `nodekit validate` / `nodekit render` | 구현됨 | `CliApp.Run(...)`의 supported command |
| `submit` / `build` / `build-request submit` 명령 | 미구현 / 비범위 | 현재 CLI 계약 밖 |
| draft 저장/resume | 미구현 / 비범위 | v1.0 범위 밖 |
| 실제 Harbor/OCI digest resolver | 미구현 / v1.1 이후 | v1.0은 seam까지만 |
| 초기 주요 화면 `/back` | 구현됨 | guided/quick 선택 이후 `/back` 입력 시 시작 화면으로 복귀 |
| `--verbose` | 미구현 / v1.0 비범위 | 현재 CLI flag parsing 없음 |

검증용 명령:

```bash
find src tests -name '*BeginnerGuideFlow*' -o -name '*RecipeAuthoringSession*'
grep -R "RunNoClueFlow\|잘 모르겠다\|BeginnerGuideFlow\|BuildRecoveryPlan\|BeginnerHint" -n src tests
```

---

## 1. 목적

NodeKit CLI는 이미 `recipe create`, `validate`, `render`의 기본 경로를 갖고 있다. 특히 `recipe create`에는 쉬운 안내 모드와 빠른 설정 모드가 모두 존재한다. 그러나 유전체 연구자, 임상 연구자, 바이오인포매틱스 실무자처럼 conda/bioconda 사용 경험은 있지만 container digest, reproducibility rule, validation rule ID에는 익숙하지 않은 사용자가 혼자 recipe 작성을 완주하기에는 아직 몇 가지 회복 UX가 약하다.

v1.0의 목표는 다음이다.

```text
도메인 전문가가 외부 설명 없이도 recipe 작성 흐름에서 막힌 이유를 이해하고,
다음 행동을 알 수 있게 만든다.
```

v1.0에서 바꾸지 않는 핵심 계약은 다음이다.

```text
RecipeDocument → RecipeValidator → RecipeRenderer → ToolDefinition → legacy BuildRequest
```

즉 이 문서는 runtime submit/build 기능을 추가하는 문서가 아니다. authoring UX와 validation recovery를 개선하는 문서다.

---

## 2. 현재 CLI 계약

현재 CLI 명령 구조는 다음을 기준으로 한다.

```bash
nodekit recipe create recipe.json
nodekit validate recipe.json
nodekit render recipe.json --out build-request.json
```

다음 명령은 현재 CLI 범위에 없다.

```bash
nodekit submit
nodekit build
nodekit build-request submit
```

문서와 화면 출력에서 위 명령을 다음 단계처럼 안내하면 안 된다.

`recipe create`는 완성된 recipe를 저장하는 명령이다. incomplete draft를 저장하고 나중에 이어서 작성하는 기능은 현재 없다. 따라서 v1.0 문서와 UX에서 "나중에 추가", "draft 저장 후 종료" 같은 문구를 사용하지 않는다. 그 기능은 별도의 `recipe resume` 또는 `--save-draft` 설계를 요구하므로 v1.0 비범위다.

---

## 3. UX 평가 요약

### 3.1 평가 기준

대상 사용자는 다음과 같다.

```text
bioconda/conda 명령은 어느 정도 알고 있지만,
컨테이너 digest, reproducibility rule, BuildRequest, validation rule ID에는 익숙하지 않은 도메인 전문가
```

### 3.2 현재 점수

| 관점 | 점수 | 이유 |
|---|---:|---|
| 개발자/엔지니어 기준 | 6~7/10 | 명령 구조, validation, render, test 구조는 비교적 정돈됨 |
| 도메인 전문가/초보자 기준 | 4~5/10 | digest, checksum, no-clue 회복, validation recovery 문구에서 다음 행동이 부족함 |

이 평가는 "CLI 구조 전체가 실패했다"는 뜻이 아니다. 핵심은 **사용자가 막히는 순간의 회복 UX가 아직 약하다**는 것이다.

---

## 4. 핵심 문제

### 4.1 컨테이너 이미지 digest를 어디서 구해야 하는지 모름

container image flow는 digest를 요구한다. 이 방향은 맞다. 하지만 사용자는 다음 상황에서 막힌다.

```text
quay.io/biocontainers/bwa:0.7.17--h7132678_9 는 알지만,
@sha256:... 값을 어디서 복사해야 하는지 모름
```

v1.0은 digest 요구를 완화하지 않는다. 대신 digest를 어디서 찾고 어떻게 입력해야 하는지 안내해야 한다.

### 4.2 `잘 모르겠다` flow는 존재하지만, 도구 이름 기반 lookup 안내가 부족함

현재 Beginner Guide는 `[7] 잘 모르겠다` 선택 시 `RunNoClueFlow`로 진입한다. 이 flow는 설치 명령, 컨테이너 이미지, 소스 URL, Dockerfile, 저장하지 않고 종료 경로를 제공한다. 따라서 문제는 no-clue flow의 부재가 아니다.

실제 문제는 다음이다.

```text
도메인 전문가가 가장 흔히 아는 "도구 이름"에서
bioconda 패키지 페이지 또는 BioContainers tag 페이지로 이어지는 안내가 부족하다.
```

예를 들어 사용자가 `bwa`, `samtools`, `fastqc` 정도만 알고 있을 때, CLI가 어디에서 설치 명령이나 이미지 tag를 확인할 수 있는지 안내해야 한다.

### 4.3 validation recovery 문구가 아직 내부 필드명 중심임

interactive final validation recovery 구조는 이미 있다. 그러나 기본 action 문구는 아직 다음처럼 내부 필드명을 그대로 드러내는 성격이 강하다.

```text
ImageDigest 항목 수정
Packages 항목 수정
SourceChecksum 항목 수정
```

초보 사용자에게 필요한 것은 내부 필드명보다 다음 행동이다.

```text
이미지 digest를 어디서 복사해야 하는가?
SourceChecksum은 어떤 명령으로 계산하는가?
패키지 버전은 어느 형태로 고정해야 하는가?
```

---

## 5. v1.0 설계 원칙

### 5.1 reproducibility rule은 완화하지 않는다

사용자 UX를 개선하더라도 L1 재현성 규칙은 유지한다.

```text
latest tag 허용 금지
패키지 버전 고정 유지
이미지 digest 고정 유지
source checksum 필수 유지
```

UX 개선은 bypass를 추가하는 것이 아니라, 사용자가 필요한 값을 얻고 입력할 수 있게 돕는 것이다.

### 5.2 기존 구현을 존중한다

v1.0은 이미 존재하는 구조를 재구현하지 않는다.

```text
BeginnerGuideFlow는 새로 만들지 않는다. 기존 flow를 보강한다.
BuildRecoveryPlan은 새 formatter로 대체하지 않는다. 기존 recovery action 문구를 개선한다.
```

### 5.3 interactive와 CI/개발자 출력을 분리한다

| 경로 | 주 사용자 | rule ID 정책 |
|---|---|---|
| `recipe create` interactive | 도메인 전문가/초보자 | rule ID보다 사람 말과 다음 행동 우선 |
| `validate` | 개발자/CI | rule ID와 field 유지 |
| `render` | 개발자/CI | 실패 시 rule ID와 field 유지 |

`validate`/`render`에서 rule ID를 완전히 숨기면 CI 디버깅성이 떨어진다. v1.0 P1은 interactive recovery를 우선 개선한다.

### 5.4 draft 저장은 넣지 않는다

사용자가 digest나 checksum을 모르면 다음 중 하나를 제공한다.

```text
구하는 방법을 보여준다
다시 입력하게 한다
다른 작성 방식으로 바꾼다
저장하지 않고 종료한다
```

하지만 incomplete draft 저장은 제공하지 않는다.

### 5.5 실제 네트워크 digest 조회는 v1.0 완료 조건이 아니다

자동 digest 조회는 UX 효과가 크지만, Harbor, OCI registry, 인증, 네트워크 실패, NodeVault/Catalog 책임 경계가 얽힌다. v1.0에서는 seam을 먼저 만든다.

```text
v1.0:
  digest resolver 인터페이스
  Null/Fake resolver
  BeginnerGuideFlow에서 resolver 사용 가능한 구조
  자동 조회 성공/실패 UX 테스트

v1.1 이후:
  실제 OCI registry resolver
  Harbor resolver
  인증 처리
```

---

## 6. v1.0 범위와 비범위

### 6.1 v1.0 범위

| 항목 | 설명 |
|---|---|
| 기존 `RunNoClueFlow` 보강 | 도구 이름 기반 lookup 안내 추가 |
| `RunToolNameFlow` 안내 강화 | bioconda/BioContainers URL 안내 추가 |
| SourceChecksum 안내 개선 | `curl -fsSL <URL> \| sha256sum` 명령 안내 |
| 기존 recovery action 문구 개선 | `Description` / `BeginnerHint`를 사용자 행동 중심으로 개선 |
| 실제 field key 검증 | `_renderedFieldToCatalogFields`와 실제 `violation.Field` 기준으로 매핑 정리 |
| digest resolver seam 추가 | 실제 네트워크 resolver 전 구조 확보 |
| 용어 개선 | 내부 필드명을 사용자 언어로 풀어 표시 |

### 6.2 v1.0 비범위

| 항목 | 이유 |
|---|---|
| `nodekit submit` / `nodekit build` | 현재 CLI 책임 밖 |
| `nodekit build-request submit` | 현재 명령 없음 |
| draft 저장/resume | 별도 상태 모델 필요 |
| 실제 Harbor HTTP API 연동 | 인증/설정/책임 경계 확정 필요 |
| 실제 OCI registry resolver | 네트워크/인증/오프라인 정책 필요 |
| bioconda API 검색/파싱 | 외부 API 의존성 증가 |
| Docker daemon 연결 | NodeKit 책임 경계 위반 가능 |
| 필드 단위 완전 `/back` 네비게이션 | 필드 완료 상태와 list edit rollback을 포함한 별도 상태 모델 필요 |
| DagEdit/Avalonia UI 통합 | 별도 UI 트랙 |
| `--verbose` 도입 | v1.0 P1 범위 밖 |
| NodeVault gRPC submit | NodeVault Phase와 별도 조율 필요 |

---

## 7. 개선안 A — 기존 `RunNoClueFlow`에 도구 이름 기반 lookup 안내 추가

### 7.1 현재 상태

현재 `BeginnerGuideFlow`는 top-level clue picker를 제공한다.

```text
[1] 도구 이름만 알고 있다
[2] 설치 명령을 알고 있다
[3] 컨테이너 이미지 주소를 알고 있다
[4] GitHub 또는 소스코드 주소를 알고 있다
[5] Dockerfile을 가지고 있다
[6] 회사/학교 내부 저장소를 써야 한다
[7] 잘 모르겠다
```

`[7] 잘 모르겠다`는 `RunNoClueFlow`로 진입한다. 현재 `RunNoClueFlow`는 다음 경로를 제공한다.

```text
[1] 설치 명령을 입력한다
[2] 컨테이너 이미지 주소를 입력한다
[3] 소스코드 주소를 입력한다
[4] Dockerfile 경로를 입력한다
[5] 저장하지 않고 종료한다
```

따라서 v1.0에서 no-clue flow를 새로 만드는 것은 목표가 아니다. 목표는 기존 flow를 보강하는 것이다.

### 7.2 실제 UX 갭

현재 flow에는 다음 부족점이 있다.

```text
no-clue 상태에서 "도구 이름으로 찾아보기"로 돌아가는 경로가 없다.
RunToolNameFlow도 도구 이름 입력 후 bioconda/BioContainers URL을 직접 보여주지 않는다.
```

도메인 전문가가 가장 흔히 알고 있는 정보는 도구 이름이다. 따라서 도구 이름만으로 다음 확인 위치를 안내해야 한다.

```text
bioconda 패키지 페이지:
  https://anaconda.org/bioconda/<tool>

BioContainers 이미지 tag 페이지:
  https://quay.io/repository/biocontainers/<tool>?tab=tags
```

외부 API 호출은 하지 않는다. URL만 생성한다.

### 7.3 목표 UX — `RunToolNameFlow` 보강

사용자가 `[1] 도구 이름만 알고 있다`를 선택하면 현재처럼 도구 이름을 입력받되, 입력 직후 lookup 안내를 먼저 보여준다.

```text
도구 이름:
> bwa

다음 위치에서 도구를 확인해보세요.

  bioconda 패키지:
    https://anaconda.org/bioconda/bwa

  BioContainers 이미지:
    https://quay.io/repository/biocontainers/bwa?tab=tags

bioconda 페이지에서 conda install 명령어를 찾았다면 package 방식으로 진행할 수 있습니다.
BioContainers 페이지에서 이미지 주소를 찾았다면 container 방식으로 진행할 수 있습니다.
```

그 뒤 기존 선택지를 유지한다.

```text
[1] conda install 또는 micromamba install 예시를 봤다
[2] docker run 또는 컨테이너 이미지 주소를 봤다
[3] GitHub 또는 source archive 주소를 봤다
[4] Dockerfile을 받았다
[5] 회사/학교 내부 저장소에서 설치해야 한다
[6] 아무것도 모른다
```

### 7.4 목표 UX — `RunNoClueFlow` 보강

`RunNoClueFlow`에는 도구 이름 기반 lookup으로 이동하는 선택지를 추가한다.

현재:

```text
[1] 설치 명령을 입력한다
[2] 컨테이너 이미지 주소를 입력한다
[3] 소스코드 주소를 입력한다
[4] Dockerfile 경로를 입력한다
[5] 저장하지 않고 종료한다
```

목표:

```text
[1] 도구 이름으로 bioconda/BioContainers 확인 방법을 본다
[2] 설치 명령을 입력한다
[3] 컨테이너 이미지 주소를 입력한다
[4] 소스코드 주소를 입력한다
[5] Dockerfile 경로를 입력한다
[6] 저장하지 않고 종료한다
```

이때 `[1]`은 기존 `RunToolNameFlow`로 재진입하거나, 공통 helper로 lookup 안내를 출력한 뒤 package/container/source/dockerfile/mirror flow 중 하나로 이어지게 한다.

### 7.5 구현 지침

대상 파일:

```text
src/NodeKit.Cli/BeginnerGuideFlow.cs
```

기존 `BeginnerGuideFlow`, `RunToolNameFlow`, `RunNoClueFlow`를 제거하지 않는다. 기존 flow를 보강한다.

권장 helper:

```csharp
private static void PrintToolLookupGuidance(TextWriter output, string toolName)
private static string BuildBiocondaUrl(string toolName)
private static string BuildBioContainersUrl(string toolName)
```

도구 이름은 trim하고 URL path에 안전하게 들어가도록 escape한다. 빈 도구 이름은 다시 입력하게 한다.

외부 네트워크 요청은 하지 않는다.

### 7.6 테스트

대상 테스트:

```text
tests/NodeKit.Cli.Tests/BeginnerGuideFlowTests.cs
```

추가/수정 테스트:

```text
RunToolNameFlow_PrintsBiocondaAndBioContainersUrls()
RunToolNameFlow_EmptyToolName_AsksAgain()
RunNoClueFlow_KeepsExistingRouteOptions()
RunNoClueFlow_CanRouteToToolNameLookup()
RunNoClueFlow_CanContinueToInstallCommandPath()
RunNoClueFlow_CanContinueToContainerImagePath()
RunNoClueFlow_Cancel_DoesNotWriteRecipe()
```

완료 조건:

```text
기존 설치 명령 / 이미지 / 소스 / Dockerfile / 종료 경로를 깨지 않는다.
도구 이름 기반 URL 안내만 추가한다.
외부 API를 호출하지 않는다.
```

---

## 8. 개선안 B — SourceChecksum 안내 개선

### 8.1 현재 문제

source build 방식에서 `SourceChecksum`은 필수지만, 사용자가 checksum을 어떻게 계산해야 하는지 모를 수 있다.

### 8.2 목표 UX

`SourceChecksum`이 필요한 시점에 다음 안내를 제공한다.

```text
소스 코드 검증값이 필요합니다.

NodeKit은 같은 소스 코드로 다시 빌드할 수 있도록 sha256 checksum을 요구합니다.

소스 archive URL이 있다면 다음 명령으로 계산할 수 있습니다.

  curl -fsSL "<SourceUri>" | sha256sum

출력 예:
  3f2a1b9c...  -

앞의 64자리 hex 값에 sha256: prefix를 붙여 입력하세요.
예:
  sha256:3f2a1b9c...

SourceChecksum을 입력하세요:
> sha256:
```

CLI는 직접 `curl`을 실행하지 않는다. 명령만 안내한다.

### 8.3 선택지

SourceChecksum이 비어 있을 때 선택지는 다음으로 제한한다.

```text
[1] 계산 방법을 본다
[2] 직접 입력한다
[3] 다른 작성 방식으로 바꾼다
[4] 저장하지 않고 종료한다
```

다음 선택지는 제공하지 않는다.

```text
[금지] 나중에 추가한다
[금지] draft 저장 후 종료
[금지] checksum 없이 진행
```

### 8.4 구현 지침

대상 파일 후보:

```text
src/NodeKit.Cli/BeginnerGuideFlow.cs
src/NodeKit.Cli/RecipeCreateInteractiveRunner.cs
src/Authoring/Recipes/RecipeAuthoringSession.cs
```

Source build 입력 flow와 final validation recovery 둘 다에서 안내가 필요할 수 있다. 중복을 줄이려면 helper를 둘 수 있다.

```csharp
internal static class SourceChecksumGuidance
{
    public static void Print(TextWriter output, string? sourceUri);
}
```

단, helper 추가는 선택이다. 기존 recovery action의 `BeginnerHint` 문자열 개선만으로 충분하면 새 타입을 만들지 않는다.

### 8.5 테스트

```text
SourceFlow_MissingChecksum_PrintsCurlSha256sumGuidance()
BuildRecoveryPlan_ForMissingSourceChecksum_IncludesCurlSha256sumHint()
SourceFlow_MissingChecksum_DoesNotOfferDraftSave()
```

---

## 9. 개선안 C — 기존 RecoveryPlan 기반 오류 메시지 개선

### 9.1 현재 상태

interactive recipe create 경로에는 이미 validation recovery 구조가 있다.

```text
RecipeAuthoringSession.BuildRecoveryPlan(violations)
RecipeValidationRecoveryAction
RecipeValidationRecoveryPlan
RecipeCreateInteractiveRunner.RunRecoveryLoop(...)
```

`RecipeValidationRecoveryAction`은 이미 다음 정보를 담는다.

```text
Label
Kind
RelatedFields
Description
BeginnerHint
```

따라서 v1.0에서 interactive 오류 UX를 개선하기 위해 별도의 `UserFacingViolation` 또는 독립적인 `ViolationMessageFormatter`를 새로 만드는 것은 권장하지 않는다.

새 formatter를 추가하면 다음 문제가 생긴다.

```text
interactive recovery 경로가 두 개로 갈라진다.
기존 BuildRecoveryPlan의 BeginnerHint/Description과 중복된다.
Field 기반 recovery mapping과 RuleId 기반 formatter가 충돌할 수 있다.
```

### 9.2 v1.0 목표

v1.0에서는 기존 RecoveryPlan 구조를 유지하면서 다음을 개선한다.

```text
BuildRecoveryPlan이 생성하는 action의 Label을 더 사용자 친화적으로 만든다.
Description에 왜 필요한지 설명을 추가한다.
BeginnerHint에 다음 행동을 명확히 넣는다.
RelatedFields를 유지해 기존 field 기반 recovery를 깨지 않는다.
```

즉 interactive 경로의 개선 방향은 다음이다.

```text
새 recovery system 추가 X
기존 RecoveryPlan 문구 개선 O
```

### 9.3 Field 기반 매핑 유지

현재 recovery mapping은 rule ID가 아니라 `violation.Field`와 catalog field를 중심으로 동작한다.

현재 `_renderedFieldToCatalogFields` key는 다음과 같다.

| rendered / violation field | catalog field |
|---|---|
| `Name` | `ToolName` |
| `Version` | `ToolVersion` |
| `Script` | `Script` |
| `ImageUri` | `ImageRef`, `ImageDigest` |
| `BioContainerImageUri` | `ImageRef`, `ImageDigest` |
| `BaseImage` | `ImageRef` |
| `Packages` | `Packages` |
| `Channels` | `Channels` |
| `PackageMirrorUri` | `MirrorUri` |
| `SourceUri` | `SourceUri` |
| `SourceChecksum` | `SourceChecksum` |
| `SourceBuildCommands` | `SourceBuildCommands` |
| `DockerfileContent` | `DockerfileContent` |
| `DockerfilePath` | `DockerfilePath` |
| `Command` | `Command` |

특수 처리:

```text
Inputs / Outputs → ReviewSectionAction
unknown field 또는 field 없음 → ShowExplanationOnlyAction
```

구현 전 반드시 실제 `violation.Field` 값이 위 key와 일치하는지 확인한다. 문서상의 후보 이름이 실제 field key와 다르면 recovery mapping이 걸리지 않는다.

### 9.4 주요 recovery 문구 목표

#### 이미지 digest 없음

대상 field:

```text
ImageUri
BioContainerImageUri
ImageDigest
ImageRef
```

주의:

```text
ImageUri / BioContainerImageUri는 rendered field일 수 있고,
ImageDigest / ImageRef는 catalog field다.
BuildRecoveryPlan의 primary mapping은 현재 rendered field key를 기준으로 한다.
```

목표 Label:

```text
이미지 digest 입력하기
```

목표 Description:

```text
컨테이너 이미지가 나중에 바뀌지 않도록 @sha256:... digest가 필요합니다.
```

목표 BeginnerHint:

```text
Quay 또는 Harbor의 tag 상세 화면에서 sha256 digest를 복사하세요.
이미지 주소가 ubuntu:22.04처럼 tag만 있으면 나중에 다른 이미지로 바뀔 수 있습니다.
```

구현 방식:

```text
현재 EditRelatedFieldsAction(ImageRef, ImageDigest)이 너무 일반적인 문구를 낸다면,
fields 조합이 ImageRef + ImageDigest인 경우 전용 action 문구를 사용한다.
```

#### SourceChecksum 없음

대상 field:

```text
SourceChecksum
SourceUri
```

목표 Label:

```text
소스 코드 검증값 입력하기
```

목표 Description:

```text
source build는 같은 소스 코드로 다시 빌드할 수 있도록 sha256 checksum이 필요합니다.
```

목표 BeginnerHint:

```text
archive URL이 있다면 다음 명령으로 계산할 수 있습니다.

  curl -fsSL "<SourceUri>" | sha256sum

출력된 64자리 hex 값 앞에 sha256:을 붙여 입력하세요.
```

#### 패키지 버전 미고정

대상 field:

```text
Packages
Channels
```

기존 문서에서 후보로 언급된 `PackageSpecs`, `InstallCommand`, `PackageName`, `PackageVersion`은 현재 field mapping key가 아니다. 구현 시 해당 이름으로 새 매핑을 만들지 않는다.

목표 Label:

```text
패키지 버전 고정하기
```

목표 Description:

```text
패키지는 이름만이 아니라 버전까지 고정해야 재현 가능한 recipe가 됩니다.
```

목표 BeginnerHint:

```text
예: bwa=0.7.17 또는 가능하면 bwa=0.7.17=h7132678_9처럼 build string까지 포함하세요.
bioconda 페이지에서 정확한 버전과 build string을 확인할 수 있습니다.
```

### 9.5 validate/render 출력 정책

`nodekit validate`와 `nodekit render`는 interactive beginner flow가 아니다. 개발자나 CI에서 사용할 수 있으므로 rule ID와 field를 유지한다.

현재 출력 형식:

```text
<RuleId> (<Field>): <Message>
```

v1.0 P1에서는 `validate`/`render` 출력 개선을 필수로 하지 않는다. interactive recovery 문구 개선을 우선한다.

비대화형 출력에 사용자 친화 요약 prefix를 추가하는 작업은 P2로 둔다. 단, 이 경우에도 interactive recovery의 primary path를 대체하는 formatter를 만들지 않는다. 필요하다면 validate/render 전용의 작은 rule-ID summary table을 별도 P2 작업으로 설계한다.

`--verbose` 플래그는 현재 CLI에 없으므로 v1.0 범위에 넣지 않는다.

### 9.6 구현 지침

대상 파일:

```text
src/Authoring/Recipes/RecipeAuthoringSession.cs
src/NodeKit.Cli/RecipeCreateInteractiveRunner.cs
src/NodeKit.Cli/CliApp.cs
```

주의:

```text
기존 BuildRecoveryPlan 구조를 제거하지 않는다.
RecipeValidationRecoveryAction을 대체하지 않는다.
interactive 경로에 별도 UserFacingViolation 타입을 만들지 않는다.
RuleId 기반 formatter를 interactive primary path로 만들지 않는다.
```

필요하다면 `BuildRecoveryPlan` 내부 action 생성 helper를 보강한다.

예:

```csharp
private static RecipeValidationRecoveryAction BuildImageDigestRecoveryAction(...)
private static RecipeValidationRecoveryAction BuildSourceChecksumRecoveryAction(...)
private static RecipeValidationRecoveryAction BuildPackageVersionRecoveryAction(...)
```

### 9.7 테스트

대상 테스트:

```text
tests/NodeKit.Tests/Recipes/RecipeAuthoringSessionTests.cs
tests/NodeKit.Cli.Tests/RecipeCreateInteractiveTests.cs
tests/NodeKit.Cli.Tests/CliAppTests.cs
```

추가/수정 테스트:

```text
BuildRecoveryPlan_ForMissingImageDigest_IncludesBeginnerHint()
BuildRecoveryPlan_ForMissingSourceChecksum_IncludesCurlSha256sumHint()
BuildRecoveryPlan_ForUnpinnedPackage_IncludesBiocondaVersionHint()
RunRecoveryLoop_PrintsBeginnerHint()
CliApp_Validate_KeepsRuleIdInOutput()
CliApp_Render_KeepsRuleIdInOutput()
```

완료 조건:

```text
interactive recovery는 기존 RecoveryPlan을 사용한다.
BeginnerHint/Description이 초보자에게 다음 행동을 알려준다.
validate/render는 rule ID를 계속 출력한다.
새로운 parallel recovery formatter를 만들지 않는다.
```

---

## 10. 개선안 D — digest resolver seam 추가

### 10.1 현재 문제

사용자에게 digest를 직접 입력하라고만 하면 container 방식의 완주율이 낮다.

하지만 실제 registry 조회를 v1.0에 넣으면 범위가 커진다. Harbor인지 Quay인지, 인증은 어떻게 할지, NodeVault/Catalog를 거쳐야 하는지 결정이 필요하다.

### 10.2 v1.0 목표

v1.0에서는 실제 네트워크 resolver 구현을 완료 조건으로 삼지 않는다. 대신 다음을 구현한다.

```text
IImageDigestResolver 인터페이스
ImageDigestResolutionResult 결과 타입
NullImageDigestResolver
FakeImageDigestResolver 또는 테스트 double
BeginnerGuideFlow에서 resolver를 호출할 수 있는 구조
```

### 10.3 인터페이스

단순히 `string?`을 반환하면 실패 이유를 잃는다. 결과 타입을 사용한다.

```csharp
internal enum ImageDigestResolutionStatus
{
    Resolved,
    NotFound,
    AuthenticationRequired,
    NetworkUnavailable,
    InvalidReference,
    Unsupported
}

internal sealed record ImageDigestResolutionResult(
    ImageDigestResolutionStatus Status,
    string? Digest,
    string? Message)
{
    public static ImageDigestResolutionResult Resolved(string digest) =>
        new(ImageDigestResolutionStatus.Resolved, digest, null);

    public static ImageDigestResolutionResult Unsupported(string? message = null) =>
        new(ImageDigestResolutionStatus.Unsupported, null, message);
}

internal interface IImageDigestResolver
{
    Task<ImageDigestResolutionResult> ResolveAsync(
        string imageUri,
        CancellationToken cancellationToken);
}
```

### 10.4 BeginnerGuideFlow 통합 방식

현재 `BeginnerGuideFlow`는 `internal static class`다. 따라서 생성자 주입을 전제로 하지 않는다.

v1.0에서는 작은 변경을 우선한다.

```csharp
BeginnerGuideFlow.Run(
    RecipeAuthoringSession session,
    TextReader stdin,
    TextWriter stdout,
    IRecipeCreateCancellationSource cancellation,
    IImageDigestResolver digestResolver)
```

또는 overload를 둔다.

```csharp
public static RecipeMethodId? Run(...)
{
    return Run(..., NullImageDigestResolver.Instance);
}
```

내부 private flow 메서드 시그니처에 resolver와 cancellation token이 필요한 경우 일괄 전파한다.

### 10.5 Null resolver 동작

`NullImageDigestResolver`는 항상 `Unsupported`를 반환한다.

```text
자동 digest 조회를 사용할 수 없습니다.
이미지 registry에서 digest를 복사해 입력하세요.
```

이 경우 기존 수동 입력 경로로 이어진다.

### 10.6 자동 조회 성공 UX

Fake resolver나 향후 실제 resolver가 digest를 반환하면 다음처럼 묻는다.

```text
이미지 digest를 확인했습니다.

  sha256:3f2a1b9c...

이 digest를 사용할까요? [Y/n]
```

사용자가 거부하면 수동 입력으로 이동한다.

### 10.7 자동 조회 실패 UX

| 상태 | 메시지 |
|---|---|
| `NotFound` | 이미지를 찾을 수 없습니다. 이미지 이름과 tag를 확인하세요. |
| `AuthenticationRequired` | registry 인증이 필요합니다. 현재 CLI는 인증 조회를 지원하지 않습니다. |
| `NetworkUnavailable` | 네트워크 연결을 확인할 수 없습니다. 수동으로 digest를 입력하세요. |
| `InvalidReference` | 이미지 주소 형식이 올바르지 않습니다. |
| `Unsupported` | 현재 환경에서는 자동 조회를 사용할 수 없습니다. |

### 10.8 비범위

다음 구현체는 v1.0 완료 조건이 아니다.

```text
HarborImageDigestResolver
SkopeoImageDigestResolver
Quay API resolver
Docker daemon resolver
```

문서에는 향후 후보로만 둔다.

### 10.9 테스트

```text
ContainerImageFlow_WhenResolverReturnsDigest_AsksToUseDigest()
ContainerImageFlow_WhenResolverUnsupported_FallsBackToManualDigestInput()
ContainerImageFlow_WhenResolverFails_PrintsHumanReadableReason()
ContainerImageFlow_WhenUserRejectsResolvedDigest_AsksManualDigest()
```

---

## 11. 용어 개선

사용자 화면에서는 내부 필드명을 그대로 노출하지 않는다. 단, 내부 모델명은 변경하지 않는다. 변경 대상은 prompt, guide, validation display 문자열이다.

| 내부 용어 | 사용자 화면 목표 표현 | 상태 |
|---|---|---|
| `ImageRef` | 컨테이너 이미지 주소 | 일부 적용, 보강 필요 |
| `ImageDigest` | 이미지 digest / 이미지 고정 코드 | 일부 적용, 보강 필요 |
| `BioContainerImageUri` | digest 포함 컨테이너 이미지 주소 | recovery 문구 보강 필요 |
| `SourceChecksum` | 소스 코드 검증값 / sha256 checksum | 보강 필요 |
| `Packages` | 패키지 목록 | 적용됨, 버전 고정 안내 보강 필요 |
| `Channels` | 채널 목록 | 적용됨 |
| `PackageMirrorUri` / `MirrorUri` | 내부 미러 주소 | 적용됨 |
| `DockerfileContent` | Dockerfile 내용 | 적용됨 |
| `BuildKind` | 사용자에게 직접 노출하지 않음 | 유지 |
| `L1-IMG-004` 등 rule ID | interactive에서는 기본 숨김, validate/render에서는 유지 | 유지 |

참고: 내부 필드명 `Script`는 legacy `BuildRequest` 호환을 위해 유지하지만, 사용자
화면에서는 "기본 실행 명령"으로 표현한다. NodeVault의 장기 toolspec/toolprofile
방향에서는 실행 정보가 `runtime.command`, `runnerScriptDigest`, observed I/O
profile로 분리된다.

---

## 12. BuildKind 계약 현황

`BuildKind == null` 문제는 이미 처리되어 있다.

`RecipeValidationPipeline.ValidateRecipe()`는 `BuildKind`가 없는 문서를 받으면 `InvalidOperationException`으로 fail-fast 해야 한다.

이 계약은 유지한다.

```text
Session / Builder:
  필드 수집과 RecipeDocument 조립

RecipeBuildKindResolver:
  BuildKind 확정

RecipeValidationPipeline:
  이미 BuildKind가 확정된 RecipeDocument 검증
```

`RecipeAuthoringSession.Build()` 내부에서 `RecipeBuildKindResolver.Resolve()`를 자동 호출하지 않는다.

추가 검토 사항:

```text
nodekit validate/render가 사용자가 손으로 작성한 BuildKind 없는 JSON을 읽었을 때
InvalidOperationException stack trace처럼 보이지 않도록 CLI boundary에서 메시지를 정리할 수 있다.
```

이 항목은 P2로 둔다.

---

## 13. 구현 우선순위

### P0 — 문서 정정

```text
Implementation Status 표 추가
submit/build-request submit 안내 제거
recipe validate/render 명령어를 현재 CLI 구조에 맞게 수정
나중에 추가/draft 저장 문구 제거
RunNoClueFlow를 "신규"가 아니라 "기존 구현 보강"으로 명시
Harbor resolver를 v1.0 완료 조건에서 제거
테스트 개수 고정값 제거
ViolationMessageFormatter / UserFacingViolation 신규 제안 제거
```

### P1 — 기존 구조 보강

```text
RunToolNameFlow에 bioconda/BioContainers URL 안내 추가
RunNoClueFlow에 도구 이름 lookup 경로 추가
SourceChecksum 계산 안내 보강
기존 BuildRecoveryPlan의 Description/BeginnerHint 개선
validate/render는 rule ID 유지
```

이 단계만 해도 초보자 UX 점수는 4점에서 6점대까지 올라갈 수 있다.

### P2 — digest resolver seam 및 비대화형 출력 개선

```text
IImageDigestResolver 추가
ImageDigestResolutionResult 추가
Null/Fake resolver 추가
BeginnerGuideFlow 통합
자동 조회 성공/실패 테스트
validate/render 전용 사용자 친화 prefix 검토
BuildKind null 예외의 CLI boundary 메시지 정리 검토
```

실제 네트워크 조회 없이도 구조를 먼저 안전하게 만든다.

### P3 — 실제 registry 연동

```text
OCI registry resolver
Harbor resolver
skopeo resolver
인증 설정
오프라인/내부망 정책
```

P3는 v1.0 완료 조건이 아니라 v1.1 이후 검토 항목이다.

---

## 14. 변경 파일 목록

예상 변경 파일은 다음과 같다.

| 파일 | 변경 내용 |
|---|---|
| `docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_V1.0.md` | 본 문서 내용으로 업데이트 |
| `src/NodeKit.Cli/BeginnerGuideFlow.cs` | 기존 `RunToolNameFlow` / `RunNoClueFlow`에 도구 이름 기반 lookup 안내 추가 |
| `src/Authoring/Recipes/RecipeAuthoringSession.cs` | `BuildRecoveryPlan` action의 `Description` / `BeginnerHint` 문구 개선 |
| `src/NodeKit.Cli/RecipeCreateInteractiveRunner.cs` | 필요 시 `RunRecoveryLoop`의 hint 출력 방식 보강 |
| `src/NodeKit.Cli/CliApp.cs` | v1.0에서는 rule ID 유지. validate/render 출력 개선은 P2 |
| `src/NodeKit.Cli/IImageDigestResolver.cs` | digest resolver seam 추가 |
| `src/NodeKit.Cli/NullImageDigestResolver.cs` | 기본 unsupported resolver |
| `tests/NodeKit.Cli.Tests/BeginnerGuideFlowTests.cs` | no-clue lookup 안내 테스트, tool-name URL 안내 테스트 |
| `tests/NodeKit.Tests/Recipes/RecipeAuthoringSessionTests.cs` | recovery action hint 테스트 |
| `tests/NodeKit.Cli.Tests/RecipeCreateInteractiveTests.cs` | recovery hint 출력 테스트 |

명시적으로 추가하지 않을 파일:

```text
src/NodeKit.Cli/ViolationMessageFormatter.cs
tests/NodeKit.Cli.Tests/ViolationMessageFormatterTests.cs
```

---

## 15. 완료 조건

### 15.1 문서 완료 조건

```text
현재 CLI 명령어와 문서 예시가 일치한다.
submit/build 명령을 안내하지 않는다.
draft 저장/resume을 v1.0 범위로 암시하지 않는다.
Harbor/OCI 실제 resolver를 v1.0 필수로 두지 않는다.
interactive와 validate/render의 rule ID 정책을 구분한다.
BeginnerGuideFlow / RunNoClueFlow의 실제 구현 상태를 정확히 반영한다.
RecoveryPlan이 이미 있다는 사실을 반영한다.
```

### 15.2 코드 완료 조건

```text
RunToolNameFlow가 도구 이름 기반 bioconda/BioContainers URL을 보여준다.
RunNoClueFlow가 도구 이름 lookup 경로를 제공한다.
SourceChecksum이 필요할 때 curl + sha256sum 안내를 보여준다.
interactive validation recovery가 기존 RecoveryPlan을 사용한다.
BuildRecoveryPlan의 Description/BeginnerHint가 다음 행동을 포함한다.
validate/render는 rule ID와 field를 계속 출력한다.
IImageDigestResolver seam이 존재한다.
Null/Fake resolver 기반 테스트가 존재한다.
RecipeSession.Build()가 Resolve()를 몰래 호출하지 않는다.
BuildKind null guard는 유지된다.
```

### 15.3 테스트 완료 조건

정확한 테스트 개수를 완료 조건으로 두지 않는다. 테스트 수는 계속 변하기 때문이다.

대신 다음을 완료 조건으로 둔다.

```text
dotnet test 전체 통과
새 UX 경로 테스트 추가
기존 ImageReferenceNormalizer DigestConflict 테스트 유지
BuildKind null guard 테스트 유지
빌드 warning 0 유지
```

---

## 16. 구현자가 주의할 점

다음 변경은 하지 않는다.

```text
nodekit submit 명령 추가 금지
nodekit build-request submit 문서화 금지
checksum 없이 진행하는 bypass 추가 금지
digest 없이 container recipe 통과 금지
Build() 내부에서 Resolve() 자동 호출 금지
Harbor 직접 연동을 v1.0 필수 구현으로 확장 금지
rule ID를 validate/render에서 완전히 제거 금지
ViolationMessageFormatter / UserFacingViolation으로 interactive recovery를 병렬화 금지
기존 BeginnerGuideFlow를 제거하고 새 flow로 갈아엎기 금지
```

다음 변경은 허용된다.

```text
RunToolNameFlow에 URL 안내 추가
RunNoClueFlow에 tool-name lookup 경로 추가
interactive 화면에서 rule ID보다 사람 말 우선
validate/render에서 rule ID 유지
Null resolver로 자동 조회 불가 안내
Fake resolver로 자동 조회 UX 테스트
SourceChecksum 계산 명령 안내
BuildRecoveryPlan action 문구 개선
```

---

## 17. 기대 효과

v1.0을 이 범위로 구현하면 UX 점수는 다음 정도까지 올라갈 수 있다.

| 항목 | 현재 | v1.0 후 |
|---|---:|---:|
| 단서 picker | 7 | 8 |
| digest 획득 | 3 | 5~6 |
| no-clue recovery | 5 | 7 |
| 오류 recovery 문구 | 3 | 7 |
| 전체 파이프라인 맥락 | 5 | 6 |
| 초보자 전체 UX | 4~5 | 6~7 |

실제 registry digest 자동 조회까지 들어가면 7점 이상으로 올라갈 수 있지만, 그건 v1.1에서 다루는 것이 안전하다.

---

## 18. 다음 단계

실행 계획은 `docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_V1.0_SPRINT_PLAN.md`를 기준으로 한다.

1. 이 문서를 기준으로 P0 문서 정정을 커밋한다.
2. `RunToolNameFlow`와 `RunNoClueFlow`의 tool-name lookup 안내를 구현한다.
3. 기존 `BuildRecoveryPlan`의 `Description` / `BeginnerHint` 문구를 개선한다.
4. 실제 field key 기준으로 recovery 매핑 테스트를 추가한다.
5. SourceChecksum 안내를 source flow와 recovery hint에 반영한다.
6. P2 digest resolver seam을 구현한다.
7. 구현 후 문서와 코드가 다시 어긋나지 않는지 리뷰한다.
