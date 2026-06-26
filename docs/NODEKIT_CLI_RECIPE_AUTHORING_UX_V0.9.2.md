# NodeKit CLI Recipe Authoring UX v0.9.2 설계 문서

문서명: `NODEKIT_CLI_RECIPE_AUTHORING_UX_V0.9.2.md`
상태: Freeze Candidate
대상: `src/NodeKit.Cli/`의 `nodekit recipe create` 대화형 UX 개선
상위 범위: NodeKit CLI authoring UX
하위 범위: recipe draft, method selection, validation, legacy BuildRequest export
비범위: NodeVault submit, 이미지 빌드, registry push, MCP server 구현

---

## 1. 목적

이 문서는 `nodekit recipe create`의 초기 사용자 경험을 개선하기 위한 v0.9.2 설계 문서다.

현재 CLI는 recipe를 생성하고, 검증하고, legacy `BuildRequest` JSON으로 렌더링하는 데 집중한다. 이 범위는 유지한다.

현재 핵심 흐름은 다음과 같다.

```text
RecipeDocument
→ RecipeValidator
→ RecipeRenderer
→ ToolDefinition
→ L1 validator chain
→ legacy BuildRequest JSON
```

v0.9.2의 목표는 이 핵심 경로를 바꾸는 것이 아니다.

v0.9.2의 목표는 다음 여섯 가지다.

1. `recipe create`의 첫 진입 UX를 사용자 수준에 맞게 분리한다.
2. 아무것도 모르는 사용자도 최소한의 단서로 시작할 수 있는 “쉬운 안내 모드”를 제공한다.
3. 기존 6문항 Q&A 방식은 “빠른 설정 모드”로 유지하되, 질문의 의미·예시·선택 영향·후속 필드를 보강한다.
4. 모든 주요 입력 단계에서 `/cancel`, `/review`, `/change-method` 같은 escape hatch를 제공해 사용자가 중간에 갇히지 않게 한다.
5. 구현 시 모호해질 수 있는 계약을 명확히 한다.
6. 문서의 사용자-facing 라벨과 실제 내부 필드명을 명확히 분리한다.

v0.9.2에서 특히 닫는 구현 계약은 다음과 같다.

* Ctrl+C 처리 경로
* install command parser 반환 계약
* ImageRef/ImageDigest 합성 규칙
* ImageReferenceNormalizer 실행 위치
* `/change-method` 무효화 필드 계산 규칙
* non-interactive `--field` 파싱 규칙
* `Version` 라벨과 `ToolVersion` 내부 필드명 구분
* list field 반복 입력 계약

---

## 2. 버전 결정

이 문서는 `v0.9.2`로 정의한다.

이유는 다음과 같다.

1. 이전 초안은 `v0.7`이었지만, repository 안에는 이미 v0.8 계열의 beginner UX 문서가 존재한다.
2. 새 문서를 v0.7로 두면 문서 버전이 역전되어 구현자가 어느 문서가 최신인지 혼동할 수 있다.
3. v0.9는 초기 진입 모드 분리와 escape hatch 확장을 정의했다.
4. v0.9.1은 구현 계약을 더 정확히 닫았다.
5. v0.9.2는 v0.9.1 리뷰에서 발견된 마지막 불일치 가능성을 정리한다.
6. 따라서 v0.9.2는 구현 착수 직전 freeze candidate 문서다.

---

## 3. 현재 문제

현재 `nodekit recipe create`는 시작 단계에서 다음 6개의 질문을 묻는다.

```text
내부망/폐쇄망 환경인가요?
내부 package mirror URI를 아시나요?
기존 컨테이너 이미지 URI가 있나요?
public channel에 패키지가 있나요?
source URL과 checksum이 있나요?
기존 Dockerfile이 있나요?
```

이 질문들은 내부 method 추천 로직에는 적합하다. 그러나 처음 사용하는 사람에게는 어렵다.

초보 사용자는 다음 개념을 모를 수 있다.

* 내부망/폐쇄망
* package mirror
* image URI
* public channel
* source checksum
* Dockerfile
* digest
* conda channel
* BioContainer
* L1 validator

즉, 현재 CLI는 사용자가 처음부터 다음을 판단할 수 있다고 가정한다.

```text
내가 가진 도구가 container 방식인지,
package 방식인지,
mirror 방식인지,
source build 방식인지,
dockerfile fallback 방식인지
알고 있다.
```

하지만 실제 초보 사용자는 보통 이렇게 생각한다.

```text
나는 그냥 bwa 같은 도구를 recipe로 만들고 싶다.
설치 명령도 잘 모르고, Docker 이미지가 뭔지도 잘 모르겠다.
일단 어디서부터 시작해야 하는지 알고 싶다.
```

따라서 현재 UX의 문제는 기능 부족이 아니라 **첫 질문의 관점이 사용자 관점이 아니라 구현자 관점**이라는 점이다.

---

## 4. 설계 원칙

### 4.1 사용자가 가진 단서에서 시작한다

초기 질문은 “어떤 method를 쓸 것인가?”가 아니라 “지금 무엇을 알고 있는가?”에서 시작해야 한다.

사용자가 알고 있을 수 있는 단서는 다음과 같다.

* 도구 이름
* conda/micromamba 설치 명령
* 컨테이너 이미지 주소
* GitHub 또는 source archive 주소
* Dockerfile 경로
* 내부 저장소 주소
* 아무것도 모름

CLI는 이 단서를 바탕으로 내부 method를 추천한다.

---

### 4.2 모르는 것을 정상 경로로 인정한다

`잘 모르겠다`는 실패가 아니다. 정상 선택지다.

다만 v0.9.2 CLI는 외부 인터넷 검색, NodeVault 조회, BioContainer 자동 검색을 수행하지 않는다. 따라서 recipe 생성을 완료하려면 최소한 다음 중 하나의 단서는 필요하다.

* 설치 명령
* 컨테이너 이미지 주소
* 소스코드 주소
* Dockerfile
* 내부 package mirror 주소

도구 이름만 알고 있고 그 외 단서가 전혀 없다면, CLI는 recipe 생성을 억지로 계속하지 않는다. 대신 어떤 정보가 필요한지 설명하고 안전하게 종료하거나 다시 선택하게 한다.

---

### 4.3 선택의 영향을 즉시 설명한다

각 선택지는 이름만 보여주면 안 된다.

반드시 다음 정보를 함께 제공해야 한다.

1. 이 선택지가 의미하는 것
2. 실제 예시
3. 이 선택을 하면 내부적으로 어떤 method가 선택되는지
4. 이후 입력해야 하는 필드
5. 나중에 어떤 검증 실패가 발생할 수 있는지
6. 잘못 선택했을 때 되돌아갈 수 있는 방법

---

### 4.4 기존 engine을 재사용한다

v0.9.2는 CLI 전체를 다시 작성하지 않는다.

기존 핵심 구성은 유지한다.

```text
RecipeDocument
RecipeValidator
RecipeRenderer
BuildRequestFactory
L1 validators
Input/Output presets
Final recovery flow
```

변경 대상은 주로 `recipe create`의 초기 UX, prompt command handling, method 추천 presentation layer다.

---

### 4.5 두 모드는 같은 내부 engine으로 수렴한다

v0.9.2는 두 개의 interactive entry mode를 제공한다.

1. 쉬운 안내 모드
2. 빠른 설정 모드

하지만 두 모드는 내부적으로 같은 `RecipeAuthoringSession`, `RecipeValidator`, `RecipeRenderer`로 수렴해야 한다.

```text
쉬운 안내 모드
→ 사용자가 가진 단서 기반 method 결정
→ RecipeAuthoringSession
→ 공통 필드 입력
→ method별 필드 입력
→ Inputs/Outputs
→ validate
→ recovery
→ recipe.json 저장

빠른 설정 모드
→ 기존 6문항 기반 method 추천
→ RecipeAuthoringSession
→ 공통 필드 입력
→ method별 필드 입력
→ Inputs/Outputs
→ validate
→ recovery
→ recipe.json 저장
```

즉, 사용자-facing UX만 다르고, recipe 생성과 검증 경로는 같다.

---

### 4.6 문서 예시와 실제 코드 동작의 일치

v0.9.2에서는 문서 예시가 실제 코드 동작과 어긋나지 않도록 다음 계약을 명시한다.

1. `/change-method`에서 무효화되는 필드는 하드코딩하지 않는다.
2. 설치 명령 파서는 `Parsed`, `PartiallyParsed`, `Failed`의 3상태를 반환한다.
3. `ImageRef`와 `ImageDigest`를 따로 입력받는 경우 canonical image URI 합성 규칙을 따른다.
4. non-interactive `--field`는 첫 번째 `=`만 key/value 구분자로 사용한다.
5. 리스트 필드는 같은 `--field`를 반복해서 누적한다.
6. 사용자-facing `Version` 라벨은 내부 필드명 `ToolVersion`에 매핑된다.

---

## 5. 필드명 규칙: 사용자-facing 라벨과 내부 필드명

v0.9.2에서는 사용자-facing 라벨과 내부 필드명을 구분한다.

가장 중요한 예는 버전 필드다.

```text
사용자-facing 라벨:
  Version

내부 필드명:
  ToolVersion
```

따라서 프롬프트에서는 사용자가 이해하기 쉽게 `Version`이라고 보여줄 수 있다.

하지만 다음 영역에서는 반드시 내부 필드명 `ToolVersion`을 사용한다.

* `RecipeFieldCatalog`
* `RecipeAuthoringSession`
* non-interactive `--field`
* `/change-method` field set 차집합 계산
* validation/recovery field reference
* serialized `RecipeDocument` 생성 전 내부 상태
* 테스트 코드

문서에서 필드 집합, internal contract, CLI option 예시를 말할 때는 `ToolVersion`을 사용한다.

사용자에게 보여주는 설명 화면에서는 다음처럼 병기한다.

```text
Version

의미:
  도구 버전입니다.

내부 필드명:
  ToolVersion

예:
  0.7.17
  1.20
  0.12.1
```

---

## 6. 사용자-facing 모드 이름

사용자에게는 “초보자 모드”, “전문가 모드”라는 이름을 직접 노출하지 않는다.

대신 다음 이름을 사용한다.

| 내부 개념            | 사용자-facing 이름 | 설명                          |
| ---------------- | ------------- | --------------------------- |
| beginner mode    | 쉬운 안내 모드      | 도구 이름만 알아도 시작할 수 있는 안내형 UX  |
| experienced mode | 빠른 설정 모드      | 기존 6문항 Q&A 기반의 빠른 method 추천 |
| non-interactive  | 스크립트/CI 모드    | 프롬프트 없는 자동 생성               |

이유:

* “초보자”라는 표현은 사용자에게 부담을 줄 수 있다.
* “쉬운 안내 모드”는 기능 중심의 표현이다.
* “빠른 설정 모드”는 경험 있는 사용자가 빠르게 진행할 수 있음을 표현한다.

---

## 7. 전체 진입 화면

`nodekit recipe create recipe.json` 실행 시 다음 화면을 보여준다.

```text
NodeKit recipe create

이 명령은 실행 도구를 컨테이너 recipe로 만드는 마법사입니다.
처음이라도 괜찮습니다. 모르는 항목은 "잘 모르겠다"를 선택할 수 있습니다.

언제든 사용할 수 있는 명령:
  /help           지금 질문 도움말 보기
  /review         지금까지 입력한 내용 보기
  /change-method  작성 방식 다시 선택하기
  /cancel         저장하지 않고 종료하기
  /quit           /cancel과 동일
  /exit           /cancel과 동일

진행 방식을 선택하세요.

[1] 쉬운 안내 모드
    도구 이름만 알아도 시작할 수 있습니다.
    설치 명령, 이미지 주소, GitHub 주소 등을 예시와 함께 하나씩 확인합니다.
    처음 사용하는 사람에게 추천합니다.

[2] 빠른 설정 모드
    내부망, mirror, public channel, source checksum, Dockerfile 여부를 알고 있는 경우 사용합니다.
    기존 Q&A 방식과 비슷하지만 각 선택의 영향과 예시를 함께 보여줍니다.

[3] 스크립트/CI 모드 사용법 보기
    프롬프트 없이 한 줄 명령으로 recipe를 만들 때 사용합니다.

선택:
```

`[3] 스크립트/CI 모드 사용법 보기`는 recipe 생성을 시작하지 않는다. 사용법 예시를 출력한 뒤 종료한다.

---

## 8. 쉬운 안내 모드

### 8.1 목표

쉬운 안내 모드는 사용자가 아무것도 모르는 상황에서도 시작할 수 있게 한다.

다만 v0.9.2 CLI는 외부 검색을 하지 않으므로, recipe를 완성하려면 최소한 하나의 구체적 단서가 필요하다는 한계를 명확히 설명해야 한다.

---

### 8.2 첫 질문

```text
쉬운 안내 모드

정확히 몰라도 괜찮습니다.
알고 있는 것만 선택하세요.

무엇을 알고 있나요?

[1] 도구 이름만 알고 있다
    예: bwa, samtools, fastqc

[2] 설치 명령을 알고 있다
    예: conda install -c bioconda bwa=0.7.17
        micromamba install -c bioconda samtools=1.20

[3] 컨테이너 이미지 주소를 알고 있다
    예: quay.io/biocontainers/bwa:0.7.17--h7132678_9
        ghcr.io/example/tool:1.0.0@sha256:...

[4] GitHub 또는 소스코드 주소를 알고 있다
    예: https://github.com/lh3/bwa
        https://example.org/tool-1.0.0.tar.gz

[5] Dockerfile을 가지고 있다
    예: ./Dockerfile

[6] 회사/학교 내부 저장소를 써야 한다
    예: https://mirror.company.local/conda

[7] 잘 모르겠다

선택:
```

---

### 8.3 선택지와 내부 method 매핑

| 사용자 선택                  | 내부 method  | 동작                         |
| ----------------------- | ---------- | -------------------------- |
| 도구 이름만 알고 있다            | unresolved | 추가 질문으로 이동                 |
| 설치 명령을 알고 있다            | package    | conda/micromamba 기반 recipe |
| 컨테이너 이미지 주소를 알고 있다      | container  | 기존 이미지 사용                  |
| GitHub/source 주소를 알고 있다 | source     | source build               |
| Dockerfile을 가지고 있다      | dockerfile | Dockerfile fallback        |
| 내부 저장소를 써야 한다           | mirror     | package mirror             |
| 잘 모르겠다                  | unresolved | 최소 필요 단서 안내                |

---

## 9. 설치 명령 기반 쉬운 안내 흐름

### 9.1 설치 명령 입력

사용자가 `[2] 설치 명령을 알고 있다`를 선택하면 다음을 묻는다.

```text
설치 명령을 입력해 주세요.

예:
  conda install -c bioconda bwa=0.7.17
  micromamba install -c bioconda samtools=1.20

이 값을 사용하면:
  - package 방식 recipe를 만듭니다.
  - 이후 Packages, Channels, BaseImage를 입력하게 됩니다.
  - 패키지 버전이 고정되어 있지 않으면 validate에서 실패할 수 있습니다.

설치 명령:
```

입력 예:

```text
conda install -c bioconda bwa=0.7.17
```

CLI는 best-effort로 다음 값을 추출한다.

```text
PackageEngine: conda
Channels: bioconda
Packages: bwa=0.7.17
```

---

### 9.2 InstallCommandParser 반환 계약

설치 명령 파서는 성공/실패 boolean을 반환하지 않는다.

다음 3상태 결과를 반환한다.

```text
Parsed
PartiallyParsed
Failed
```

#### Parsed

`Parsed`는 package 방식 recipe를 진행하는 데 필요한 핵심 값을 충분히 추출한 상태다.

예:

```bash
conda install -c bioconda bwa=0.7.17
```

반환 예:

```text
Status: Parsed
PackageEngine: conda
Channels:
  - bioconda
Packages:
  - bwa=0.7.17
Warnings:
  - package build string is not pinned
```

`Parsed`라도 warning이 있을 수 있다. 예를 들어 `bwa=0.7.17`은 버전은 고정했지만 build string까지 고정하지 않았을 수 있다.

---

#### PartiallyParsed

`PartiallyParsed`는 일부 값은 추출했지만 필수 값 일부가 비어 있는 상태다.

예:

```bash
conda install bwa
```

반환 예:

```text
Status: PartiallyParsed
PackageEngine: conda
Channels: []
Packages:
  - bwa
Missing:
  - Channels
Warnings:
  - package version is not pinned
```

이 경우 실패로 종료하지 않는다.

다음 화면을 보여준다.

```text
설치 명령을 일부 이해했습니다.

이해한 값:
  PackageEngine: conda
  Packages:
    - bwa

추가로 필요한 값:
  Channels

주의:
  패키지 버전이 고정되어 있지 않습니다.
  나중에 validate에서 실패할 수 있습니다.

선택:
[1] 이해한 값을 사용하고 부족한 값을 직접 입력한다
[2] 설치 명령을 다시 입력한다
[3] 다른 작성 방식을 선택한다
[4] 취소한다

선택:
```

---

#### Failed

`Failed`는 설치 명령을 package 방식으로 해석하기 어려운 상태다.

실패 가능 예:

```bash
pip install some-tool
```

```bash
curl -L https://example.org/tool.sh | bash
```

```bash
git clone https://github.com/example/tool && make
```

이 경우 오류로 종료하지 않는다.

다음 화면을 보여준다.

```text
설치 명령을 자동으로 이해하지 못했습니다.

괜찮습니다. 필요한 값을 하나씩 입력하면 됩니다.

이 방식으로 계속하면:
  - package 방식 recipe를 만듭니다.
  - PackageEngine, Channels, Packages를 직접 입력합니다.

선택:
[1] package 방식으로 계속한다
[2] 설치 명령을 다시 입력한다
[3] 다른 작성 방식을 선택한다
[4] 취소한다

선택:
```

---

### 9.3 parser 구현 원칙

`InstallCommandParser`는 NodeKit Core에 위치하는 것을 권장한다.

권장 타입:

```csharp
public enum InstallCommandParseStatus
{
    Parsed,
    PartiallyParsed,
    Failed
}

public sealed record InstallCommandParseResult(
    InstallCommandParseStatus Status,
    string? PackageEngine,
    IReadOnlyList<string> Channels,
    IReadOnlyList<string> Packages,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Warnings,
    string? OriginalCommand
);
```

원칙:

1. parser는 recipe를 생성하지 않는다.
2. parser는 사용자의 명령을 best-effort로 구조화할 뿐이다.
3. parser는 값이 부족해도 가능한 만큼 반환한다.
4. parser 실패는 CLI 실패가 아니다.
5. parser 결과는 반드시 사용자에게 보여주고 확인을 받는다.
6. parser가 추출한 값도 최종 validator를 통과해야 한다.

### 9.4 구현 결정사항 (Sprint R12)

R12 spike 구현 과정에서 설계 문서에 명시되지 않았던 경계 케이스에 대해 다음 결정을 내렸다.

#### 지원 엔진

지원 엔진: `conda`, `micromamba`. `mamba`는 지원하지 않는다 → `Failed`.
사용자는 `conda install` 또는 `micromamba install`로 다시 입력한다.

#### 채널 없는 conda install

`conda install bwa` (채널 미지정) → `PartiallyParsed`, `Missing=[Channels]`.
conda의 묵시적 `defaults` 채널을 자동 삽입하지 않는다.
CLAUDE.md §3 재현성 원칙: 채널을 명시하지 않으면 빌드 환경이 달라질 수 있으므로 Missing 처리한다.

#### conda create

`conda create` subcommand → `PartiallyParsed` + 의미론 경고.
환경 생성 명령은 recipe 생성에 직접 대응하지 않으므로 `conda install`을 권장하는 경고를 추가한다.

#### 래핑된 명령

`/bin/bash -c "conda install ..."` 등 래핑 형식 → `Failed`.
첫 토큰이 지원 엔진 목록에 없으므로 자동으로 Failed 처리된다.

#### 접근 수준

설계 문서 예시 코드는 `public`으로 표기되어 있으나, NodeKit.csproj에 `EnforceCodeStyleInBuild=true`가 설정되어 있어 CA1515 경고가 발생한다.
실제 구현에서는 `internal`로 선언하며, `InternalsVisibleTo("NodeKit.Tests")`를 통해 테스트에서 접근한다.

---

## 10. 컨테이너 이미지 기반 쉬운 안내 흐름

### 10.1 이미지 주소 입력

사용자가 `[3] 컨테이너 이미지 주소를 알고 있다`를 선택하면 다음을 묻는다.

```text
컨테이너 이미지 주소를 입력해 주세요.

예:
  quay.io/biocontainers/bwa:0.7.17--h7132678_9@sha256:...
  ghcr.io/example/tool:1.0.0@sha256:...

이 값을 사용하면:
  - container 방식 recipe를 만듭니다.
  - 이미 만들어진 이미지를 그대로 사용합니다.
  - digest가 없으면 재현성을 보장할 수 없어 validate에서 실패합니다.

이미지 주소:
```

---

### 10.2 digest 없는 이미지 처리

digest가 없는 이미지는 기본 진행을 허용하지 않는다.

나쁜 예:

```text
quay.io/biocontainers/bwa:0.7.17--h7132678_9
```

좋은 예:

```text
quay.io/biocontainers/bwa:0.7.17--h7132678_9@sha256:0123456789abcdef...
```

digest가 없으면 다음 화면을 보여준다.

```text
입력한 이미지 주소에는 digest가 없습니다.

현재 값:
  quay.io/biocontainers/bwa:0.7.17--h7132678_9

NodeKit은 재현성을 위해 digest 고정을 요구합니다.
tag는 나중에 같은 이름으로 다른 이미지가 될 수 있습니다.

선택:
[1] digest가 포함된 이미지 주소를 다시 입력한다
[2] ImageDigest를 따로 입력한다
[3] 다른 작성 방식으로 바꾼다
[4] 취소한다

선택:
```

기본값은 없다. 사용자가 명시적으로 선택해야 한다.

---

### 10.3 ImageRef / ImageDigest canonicalization 계약

container 방식에서는 사용자가 다음 두 형태 중 하나로 값을 제공할 수 있다.

#### 형태 A: ImageRef에 digest 포함

```text
ImageRef:
  quay.io/biocontainers/bwa:0.7.17--h7132678_9@sha256:aaa...
ImageDigest:
  비어 있음 또는 sha256:aaa...
```

#### 형태 B: ImageRef와 ImageDigest를 분리 입력

```text
ImageRef:
  quay.io/biocontainers/bwa:0.7.17--h7132678_9

ImageDigest:
  sha256:aaa...
```

최종 canonical image URI는 다음 형태여야 한다.

```text
quay.io/biocontainers/bwa:0.7.17--h7132678_9@sha256:aaa...
```

---

### 10.4 ImageReferenceNormalizer 실행 위치

v0.9.2에서는 ImageReferenceNormalizer의 실행 위치를 다음으로 고정한다.

```text
RecipeAuthoringSession 내부:
  ImageRef = digest 없는 repository:tag
  ImageDigest = sha256 digest

RecipeDocument 생성 직전:
  ImageReferenceNormalizer 실행
  canonical image URI 생성
  ImageRef/ImageUri 계열 필드를 canonical URI로 정규화

RecipeValidator / RecipeRenderer / L1 validator:
  digest 포함 canonical URI를 입력으로 받음
```

즉, prompt layer는 사용자가 입력한 값을 수집하고 충돌을 설명한다.
하지만 canonical URI 최종 합성은 prompt 문자열 처리로 하지 않는다.

정규화는 `RecipeDocument` 생성 직전에 수행한다.

이유:

1. 사용자 입력 상태와 canonical 상태를 분리할 수 있다.
2. prompt layer가 image URI 규칙을 직접 구현하지 않아도 된다.
3. validator와 renderer는 이미 정규화된 digest 포함 URI를 받을 수 있다.
4. L1 validator 입력 형태가 안정된다.
5. legacy `BuildRequest` export 경로와 잘 맞는다.

권장 구조:

```csharp
public sealed record NormalizedImageReference(
    string RepositoryAndTag,
    string Digest,
    string CanonicalUri
);
```

예:

```text
RepositoryAndTag:
  quay.io/biocontainers/bwa:0.7.17--h7132678_9

Digest:
  sha256:aaa...

CanonicalUri:
  quay.io/biocontainers/bwa:0.7.17--h7132678_9@sha256:aaa...
```

---

### 10.5 digest 충돌 처리

`DigestConflict`는 `ImageReferenceNormalizer`가 반환하는 상태로, embedded digest와 별도 digest가 모두 제공되었을 때 둘이 다른 경우에 발생한다.

**이 상태는 Beginner Guide wizard의 일반 대화형 흐름에서 발생하지 않는다.**

BeginnerGuideFlow의 컨테이너 서브플로는 embedded digest와 별도 digest를 동시에 충돌하게 입력하는 경로를 제공하지 않으며, 그 UX 분기는 제거되었다(R13 확인 → Issue #3 → 수정 완료).

#### DigestConflict가 적용되는 경로

`DigestConflict`는 다음 경로에서만 의미가 있다:

- non-interactive 모드에서 `--field ImageRef=repo:tag@sha256:aaa` 와 `--field ImageDigest=sha256:bbb` 를 동시에 지정한 경우
- scripted usage 또는 direct `SetField` / API 호출로 embedded digest가 있는 ref와 별도 digest를 동시에 전달한 경우

이 경우 `ImageReferenceNormalizer.Normalize()` 가 `DigestConflict` 를 반환하고, 호출자는 충돌을 명시적으로 해결해야 한다.

#### 충돌 해결 원칙

둘이 같으면 허용한다.

```text
ImageRef:   repo/tool:1.0@sha256:aaa
ImageDigest: sha256:aaa
결과:       repo/tool:1.0@sha256:aaa
```

둘이 다르면 충돌 상태 그대로 저장하지 않는다. 호출자가 어느 digest를 사용할지 명시적으로 결정해야 한다.

---

### 10.6 container 방식 필드 저장 원칙

RecipeAuthoringSession 내부에서는 다음 구조를 권장한다.

```text
ImageRef: digest 없는 repository:tag
ImageDigest: sha256 digest
CanonicalImageUri: computed before RecipeDocument creation
```

이유:

1. 사용자가 입력한 tag와 digest를 분리해서 검증하기 쉽다.
2. `ImageDigest` 필드의 의미가 명확하다.
3. conflict detection이 쉽다.
4. `RecipeDocument` 생성 직전에 canonical URI를 만들기 쉽다.

`RecipeDocument`, `RecipeValidator`, `RecipeRenderer`, L1 validator는 digest 포함 canonical URI를 받는 것으로 고정한다.

---

## 11. GitHub 또는 소스코드 주소 기반 쉬운 안내 흐름

### 11.1 소스코드 주소 입력

```text
소스코드 주소를 입력해 주세요.

예:
  https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz
  https://example.org/tool-1.0.0.tar.gz

이 값을 사용하면:
  - source 방식 recipe를 만듭니다.
  - 이후 SourceUri, SourceChecksum, SourceBuildCommands를 입력하게 됩니다.
  - checksum이 없으면 같은 소스인지 확인할 수 없어 validate에서 실패합니다.

소스코드 주소:
```

---

### 11.2 SourceChecksum 설명

```text
SourceChecksum은 다운로드한 소스 파일이 정확히 같은 파일인지 확인하기 위한 sha256 값입니다.

예:
  sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef

이 값이 없으면 source build 방식은 재현성을 보장할 수 없어 validate에서 실패합니다.

SourceChecksum:
```

checksum이 없으면 다음 선택지를 제공한다.

```text
SourceChecksum이 없으면 source 방식 recipe를 완성할 수 없습니다.

선택:
[1] checksum을 입력한다
[2] source 주소를 다시 입력한다
[3] 다른 작성 방식으로 바꾼다
[4] 취소한다

선택:
```

v0.9.2에서는 checksum 자동 계산을 하지 않는다.

---

## 12. Dockerfile 기반 쉬운 안내 흐름

### 12.1 Dockerfile 경로 입력

```text
Dockerfile 경로를 입력해 주세요.

예:
  ./Dockerfile

주의:
  Dockerfile 방식은 가장 자유롭지만 NodeKit이 자동으로 보장해주는 부분이 가장 적습니다.
  FROM 이미지가 digest로 고정되어 있지 않거나 latest 태그를 사용하면 validate에서 실패합니다.
  처음 사용하는 경우에는 package 또는 container 방식이 더 쉽습니다.

Dockerfile 경로:
```

---

### 12.2 Dockerfile fallback 경고

Dockerfile 선택 시 반드시 한 번 더 확인한다.

```text
Dockerfile fallback 방식을 선택했습니다.

이 방식은 다음 책임이 사용자에게 있습니다.
  - Dockerfile의 모든 FROM 이미지 digest 고정
  - latest 태그 사용 금지
  - 외부 다운로드 URL의 재현성 관리
  - Dockerfile의 첫 FROM과 BaseImage 일치

처음 사용하는 경우에는 package 또는 container 방식을 먼저 고려하는 것을 권장합니다.

계속 진행할까요? [y/N]
```

기본값은 `N`이다.

---

## 13. 내부 저장소 기반 쉬운 안내 흐름

```text
내부 저장소 주소를 입력해 주세요.

예:
  https://mirror.company.local/conda
  https://packages.school.local/conda

이 값을 사용하면:
  - mirror 방식 recipe를 만듭니다.
  - 이후 PackageMirrorUri를 입력하게 됩니다.
  - 다른 사용자가 같은 recipe를 실행하려면 동일한 내부 저장소에 접근할 수 있어야 합니다.

내부 저장소 주소:
```

---

## 14. 도구 이름만 알고 있는 경우

```text
도구 이름을 입력해 주세요.

예:
  bwa
  samtools
  fastqc

도구 이름:
```

이후 다음 질문으로 이어진다.

```text
이 도구를 설치하거나 실행하는 예시를 본 적 있나요?

[1] conda install 또는 micromamba install 예시를 봤다
[2] docker run 또는 컨테이너 이미지 주소를 봤다
[3] GitHub 또는 source archive 주소를 봤다
[4] Dockerfile을 받았다
[5] 회사/학교 내부 저장소에서 설치해야 한다
[6] 아무것도 모른다

선택:
```

`[6] 아무것도 모른다`를 선택하면 다음처럼 안내한다.

```text
아직 recipe를 완성하기 위한 단서가 부족합니다.

NodeKit v0.9.2 CLI는 외부 검색이나 NodeVault 조회를 하지 않습니다.
따라서 recipe 생성을 완료하려면 최소한 다음 중 하나가 필요합니다.

  - conda/micromamba 설치 명령
  - 컨테이너 이미지 주소
  - 소스코드 주소와 checksum
  - Dockerfile
  - 내부 package mirror 주소

선택:
[1] 설치 명령을 입력한다
[2] 컨테이너 이미지 주소를 입력한다
[3] 소스코드 주소를 입력한다
[4] Dockerfile 경로를 입력한다
[5] 저장하지 않고 종료한다

선택:
```

---

## 15. 빠른 설정 모드

### 15.1 목표

빠른 설정 모드는 기존 6문항 Q&A 방식을 유지한다.

다만 각 질문에 다음 정보를 보강한다.

* 질문의 의미
* 실제 예시
* `y`를 선택했을 때의 영향
* `n`을 선택했을 때의 영향
* `Enter`를 선택했을 때의 처리
* 이후 입력해야 할 필드
* 추천 method에 미치는 영향

---

### 15.2 빠른 설정 모드 시작 화면

```text
빠른 설정 모드

이 모드는 도구의 배포 방식이나 빌드 방식을 어느 정도 알고 있는 사용자를 위한 모드입니다.

각 질문에는 y/n/Enter로 답할 수 있습니다.

  y      예
  n      아니오
  Enter  잘 모르겠음

잘못 선택해도 괜찮습니다.
입력 중 언제든지 /change-method로 작성 방식을 다시 선택할 수 있습니다.
저장하지 않고 종료하려면 /cancel을 입력하세요.
```

---

### 15.3 질문 1: 내부망/폐쇄망 환경

```text
Q1. 내부망/폐쇄망 환경인가요?

의미:
  현재 환경에서 public 인터넷으로 Docker 이미지나 conda 패키지를 받을 수 없는지 묻는 질문입니다.

예:
  - 회사/학교 내부망에서만 패키지를 받을 수 있음
  - 외부 인터넷 접근이 차단됨
  - 내부 mirror나 사내 registry만 사용해야 함

y를 선택하면:
  - mirror 방식이 우선 후보가 됩니다.
  - 내부 package mirror URI를 물어봅니다.
  - public channel 기반 package 방식은 뒤로 밀립니다.

n을 선택하면:
  - public channel, container, source 방식이 일반 후보로 유지됩니다.

Enter를 누르면:
  - unknown으로 처리합니다.
  - 이후 답변을 바탕으로 보수적으로 추천합니다.

선택 [y/n/Enter]:
```

---

### 15.4 질문 2: 내부 package mirror URI

```text
Q2. 내부 package mirror URI를 알고 있나요?

의미:
  회사/학교/기관에서 제공하는 내부 conda 또는 pip 저장소 주소를 알고 있는지 묻는 질문입니다.

예:
  https://mirror.company.local/conda
  https://packages.school.local/conda

y를 선택하면:
  - mirror 방식을 추천할 수 있습니다.
  - 이후 PackageMirrorUri를 입력하게 됩니다.

n을 선택하면:
  - mirror 방식 추천 우선순위가 낮아집니다.

Enter를 누르면:
  - mirror 방식은 보수적으로 뒤로 밀립니다.

선택 [y/n/Enter]:
```

---

### 15.5 질문 3: 기존 컨테이너 이미지 주소

```text
Q3. 기존 컨테이너 이미지 주소를 알고 있나요?

의미:
  이미 실행 가능한 Docker/OCI 이미지 주소를 알고 있는지 묻는 질문입니다.

예:
  quay.io/biocontainers/bwa:0.7.17--h7132678_9@sha256:...
  ghcr.io/example/tool:1.0.0@sha256:...

y를 선택하면:
  - container 방식이 강한 후보가 됩니다.
  - 이후 ImageRef와 ImageDigest를 입력하게 됩니다.
  - digest가 없으면 최종 검증에서 실패합니다.

n을 선택하면:
  - package, source, dockerfile 방식이 더 적합할 수 있습니다.

Enter를 누르면:
  - 다른 답변을 바탕으로 추천합니다.

선택 [y/n/Enter]:
```

---

### 15.6 질문 4: public channel 패키지

```text
Q4. public channel에 패키지가 있나요?

의미:
  bioconda, conda-forge 같은 공개 conda channel에서 설치할 수 있는지 묻는 질문입니다.

예:
  conda install -c bioconda bwa=0.7.17
  conda install -c conda-forge python=3.11

y를 선택하면:
  - package 방식을 추천할 수 있습니다.
  - 이후 Packages, Channels, BaseImage를 입력하게 됩니다.
  - 패키지 버전이 고정되어 있지 않으면 validate에서 실패할 수 있습니다.

n을 선택하면:
  - source, container, dockerfile, mirror 방식이 더 적합할 수 있습니다.

Enter를 누르면:
  - package 방식은 가능 후보로 유지하되 확신도는 낮게 둡니다.

선택 [y/n/Enter]:
```

---

### 15.7 질문 5: source URL과 checksum

```text
Q5. source URL과 checksum이 있나요?

의미:
  소스코드 archive를 직접 받아 빌드할 수 있고, 그 파일의 sha256 checksum을 알고 있는지 묻는 질문입니다.

예:
  SourceUri:
    https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz

  SourceChecksum:
    sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef

y를 선택하면:
  - source 방식이 후보가 됩니다.
  - 이후 SourceUri, SourceChecksum, SourceBuildCommands를 입력하게 됩니다.
  - checksum이 없으면 validate에서 실패합니다.

n을 선택하면:
  - source 방식 추천 우선순위가 낮아집니다.

Enter를 누르면:
  - source 방식은 보수적으로 뒤로 밀립니다.

선택 [y/n/Enter]:
```

---

### 15.8 질문 6: 기존 Dockerfile

```text
Q6. 기존 Dockerfile이 있나요?

의미:
  이미 작성된 Dockerfile 파일이 있는지 묻는 질문입니다.

예:
  ./Dockerfile

y를 선택하면:
  - dockerfile 방식을 선택할 수 있습니다.
  - 이후 DockerfilePath 또는 DockerfileContent를 입력하게 됩니다.
  - Dockerfile의 첫 FROM과 BaseImage가 정확히 같아야 합니다.
  - 모든 FROM 이미지는 latest 태그 없이 digest로 고정되어야 합니다.

주의:
  Dockerfile 방식은 가장 자유롭지만 재현성 책임이 가장 큽니다.
  처음 사용하는 경우에는 package 또는 container 방식이 더 쉽습니다.

n을 선택하면:
  - dockerfile 방식 추천 우선순위가 낮아집니다.

Enter를 누르면:
  - dockerfile 방식은 보수적으로 뒤로 밀립니다.

선택 [y/n/Enter]:
```

---

## 16. method 추천 결과 화면

쉬운 안내 모드와 빠른 설정 모드 모두 method가 정해진 뒤에는 추천 결과를 보여준다.

예: package 추천

```text
추천 작성 방식: package

이유:
  conda install 명령을 입력했기 때문에 package 방식이 가장 적합합니다.

이 방식으로 만들면:
  - conda/micromamba 기반 이미지 recipe를 생성합니다.
  - 패키지와 채널 정보를 recipe에 기록합니다.
  - 나중에 legacy BuildRequest로 render할 수 있습니다.

앞으로 입력할 항목:
  - ToolName
  - ToolVersion
  - Script
  - BaseImage
  - Packages
  - Channels
  - Inputs
  - Outputs

주의:
  - BaseImage는 digest로 고정되어야 합니다.
  - Packages는 버전이 고정되어야 합니다.
  - Inputs와 Outputs는 최소 1개 이상 필요합니다.

이 방식으로 진행할까요? [Y/n]
```

사용자-facing 설명에서는 `ToolVersion`을 다음처럼 표시할 수 있다.

```text
Version (internal: ToolVersion)
```

사용자가 `n`을 선택하면 method 선택 화면으로 이동한다.

```text
다른 작성 방식을 선택하세요.

[1] container
    기존 컨테이너 이미지를 그대로 사용합니다.

[2] package
    conda/micromamba 패키지를 설치합니다.

[3] mirror
    내부 package mirror에서 패키지를 설치합니다.

[4] source
    소스코드를 직접 받아 빌드합니다.

[5] dockerfile
    기존 Dockerfile을 사용합니다. 재현성 책임이 가장 큽니다.

선택:
```

---

## 17. 공통 escape hatch

### 17.1 v0.9.2 필수 명령

모든 주요 입력 프롬프트에서 다음 명령을 지원한다.

```text
/help           지금 질문의 설명, 예시, 필수 여부를 다시 보여준다.
/review         지금까지 입력한 내용을 요약해서 보여준다.
/change-method  작성 방식을 다시 선택한다.
/cancel         recipe 생성을 취소하고 종료한다.
/quit           /cancel과 동일하다.
/exit           /cancel과 동일하다.
```

---

### 17.2 v0.9.2 비범위: /back

`/back`은 사용자 경험상 유용하지만 구현 복잡도가 높다.

이유:

* 현재 필드 루프가 단방향일 수 있다.
* method 변경 후 일부 필드는 무효화될 수 있다.
* Inputs/Outputs 목록 편집 중 “이전 단계”의 의미가 모호하다.
* recovery 화면에서 되돌리기 의미가 복잡하다.

따라서 `/back`은 v0.9.2 필수 기능에서 제외한다.

UI에서는 다음처럼 안내한다.

```text
/back은 아직 지원하지 않습니다.
이전 값을 고치려면 /review로 현재 값을 확인하거나,
/change-method 또는 최종 recovery 화면에서 수정하세요.
```

향후 제한적으로 구현할 경우 다음 원칙을 따른다.

```text
/back은 simple scalar field 입력 단계에서만 best-effort로 지원한다.
Inputs/Outputs 편집, recovery, method 변경 직후에는 지원하지 않는다.
```

---

### 17.3 /cancel, /quit, /exit 동작

`/cancel`, `/quit`, `/exit`는 동일하게 동작한다.

```text
recipe 생성을 중단하려고 합니다.

[1] 저장하지 않고 종료
[2] 계속 작성

선택:
```

취소 시 출력:

```text
recipe 생성을 취소했습니다.
파일은 저장되지 않았습니다.
```

종료 코드는 `130`을 사용한다.

---

### 17.4 /review 동작

`/review`는 현재까지 입력한 값을 보여준다.

예:

```text
현재까지 입력한 내용:

진행 모드:
  쉬운 안내 모드

작성 방식:
  package

공통 필드:
  ToolName: bwa
  Version: 0.7.17
  Internal field: ToolVersion
  Script: run.sh

package 필드:
  BaseImage: 아직 입력 안 함
  Packages:
    - bwa=0.7.17
  Channels:
    - bioconda

Inputs:
  아직 입력 안 함

Outputs:
  아직 입력 안 함

Enter를 누르면 계속합니다.
```

아직 입력하지 않은 값은 명확히 `아직 입력 안 함`으로 표시한다.

---

### 17.5 /change-method 동작

`/change-method`는 작성 방식을 다시 선택하게 한다.

원칙:

1. 공통 필드는 최대한 유지한다.
2. 기존 method에만 유효한 필드는 새 method에서 무효화될 수 있다.
3. 무효화되는 필드는 사용자에게 보여준다.
4. 사용자가 승인해야 method 변경을 적용한다.
5. 무효화 필드 목록은 하드코딩하지 않는다.

예:

```text
작성 방식을 package에서 source로 바꾸려고 합니다.

유지되는 값:
  ToolName: bwa
  ToolVersion: 0.7.17
  Script: run.sh
  Inputs: 1개
  Outputs: 1개

무효화되는 값:
  Packages
  Channels
  PackageEngine

새로 입력해야 하는 값:
  SourceUri
  SourceChecksum
  SourceBuildCommands

계속할까요? [y/N]
```

기본값은 `N`이다.

---

### 17.6 /change-method 무효화 필드 계산 계약

무효화되는 필드는 문서나 코드에 하드코딩하지 않는다.

다음 방식으로 계산한다.

```text
oldMethodFields = FieldsFor(oldMethod)
newMethodFields = FieldsFor(newMethod)
commonFields = CommonRecipeFields

invalidatedFields =
  oldMethodFields - newMethodFields - commonFields

newlyRequiredFields =
  RequiredFieldsFor(newMethod) - ExistingValidFields
```

공통 필드는 method 변경 시 유지 대상이다.

공통 필드:

```text
ToolName
ToolVersion
Script
Inputs
Outputs
DisplayLabel
DisplayDescription
DisplayCategory
DisplayTags
```

주의:

```text
사용자-facing 라벨은 Version일 수 있지만,
field set 계산에서는 반드시 ToolVersion을 사용한다.
```

method-specific 필드는 method 변경 시 새 method의 필드 집합에 포함되지 않으면 무효화된다.

예:

```text
oldMethod: package
newMethod: source

oldMethodFields:
  BaseImage
  Packages
  Channels
  PackageEngine

newMethodFields:
  BaseImage
  SourceUri
  SourceChecksum
  SourceBuildCommands
  BuildDependencies

commonFields:
  ToolName
  ToolVersion
  Script
  Inputs
  Outputs

invalidatedFields:
  Packages
  Channels
  PackageEngine

preserved method-specific field:
  BaseImage
```

`BaseImage`처럼 두 method가 공유하는 method-specific field는 유지할 수 있다. 단, 새 method의 validation에서도 유효해야 한다.

---

## 18. Ctrl+C 처리 계약

### 18.1 Ctrl+C는 command 입력이 아니라 process signal이다

`/cancel`은 사용자가 문자열을 입력하는 command다.

반면 Ctrl+C는 process signal이다.

따라서 Ctrl+C 처리는 `/cancel`과 같은 UX 결과를 내야 하지만, 구현 경로는 별도로 정의해야 한다.

---

### 18.2 Phase 1 분리

v0.9.2 Phase 1은 두 단계로 나눈다.

```text
Phase 1-A: prompt command 기반 취소
  - /cancel
  - /quit
  - /exit
  - /review
  - exit code 130

Phase 1-B: process signal 기반 취소
  - Ctrl+C
  - Console.CancelKeyPress
  - stack trace 방지
  - Program.Main 또는 CliApp.Run까지 취소 상태 전달
  - exit code 130
```

Phase 1-A는 PromptCommandHandler에서 처리한다.

Phase 1-B는 `Console.CancelKeyPress` 또는 equivalent abstraction에서 처리한다.

---

### 18.3 Ctrl+C 기대 동작

Ctrl+C를 누르면 다음을 만족해야 한다.

1. stack trace를 출력하지 않는다.
2. partial recipe 파일을 쓰지 않는다.
3. 사용자가 취소했다는 메시지를 출력한다.
4. exit code 130을 반환한다.
5. 테스트 가능한 방식으로 구현한다.

출력 예:

```text
^C

recipe 생성을 취소했습니다.
파일은 저장되지 않았습니다.
```

---

### 18.4 권장 구현 경로

권장 흐름:

```text
Console.CancelKeyPress
→ e.Cancel = true
→ CancellationToken 또는 cancellation flag 설정
→ 현재 prompt loop가 cancellation 상태 확인
→ RecipeCancelledResult 또는 RecipeCancelException 발생
→ CliApp.Run 또는 Program.Main에서 catch
→ stderr/stdout에 취소 메시지 출력
→ exit code 130 반환
```

권장 타입:

```csharp
public sealed class RecipeCancelledException : Exception
{
    public RecipeCancelledException()
        : base("Recipe creation was cancelled by the user.")
    {
    }
}
```

또는 exception을 쓰지 않는다면:

```csharp
public enum RecipeCreateResultKind
{
    Success,
    ValidationFailed,
    UsageError,
    Cancelled
}
```

두 방식 중 하나를 선택한다.

원칙:

```text
취소는 오류가 아니다.
취소는 정상적인 사용자 의사결정 결과다.
```

---

### 18.5 테스트 고려

테스트에서는 실제 Ctrl+C를 보내기 어렵다.

따라서 다음 중 하나의 abstraction을 둔다.

```text
IConsoleCancellation
IInterruptSignal
ICancellationSource
```

테스트에서는 fake cancellation source를 주입해 Ctrl+C와 동일한 경로를 검증한다.

테스트해야 할 것:

1. Ctrl+C 발생 시 recipe 파일이 생성되지 않는다.
2. exit code가 130이다.
3. stack trace가 출력되지 않는다.
4. 취소 메시지가 출력된다.
5. `/cancel`과 Ctrl+C의 최종 결과가 일관된다.

---

## 19. 공통 필드 입력 UX

method가 정해진 뒤 공통 필드를 입력한다.

공통 필드:

* ToolName
* ToolVersion
* Script
* Inputs
* Outputs

각 필드는 다음 정보를 함께 보여준다.

* 의미
* 예시
* 나중에 영향
* 필수 여부
* 내부 필드명

---

### 19.1 ToolName

```text
ToolName

의미:
  recipe가 식별할 도구 이름입니다.

예:
  bwa
  samtools
  fastqc

나중에 영향:
  - render 결과의 ToolName에 들어갑니다.
  - NodeVault나 UI에서 도구를 구분하는 이름으로 사용될 수 있습니다.

필수 여부:
  필수

ToolName:
```

---

### 19.2 Version / ToolVersion

```text
Version

내부 필드명:
  ToolVersion

의미:
  도구 버전입니다.

예:
  0.7.17
  1.20
  0.12.1

나중에 영향:
  - recipe와 BuildRequest의 버전 정보로 사용됩니다.
  - 재현성과 추적성을 위해 필요합니다.

필수 여부:
  필수

Version:
```

저장되는 내부 필드명은 `ToolVersion`이다.

non-interactive 모드에서는 반드시 다음처럼 사용한다.

```bash
--field ToolVersion=0.7.17
```

`--field Version=0.7.17`은 v0.9.2의 공식 문법이 아니다.

하위 호환 목적으로 alias를 허용할지는 별도 결정 사항이다. 허용하더라도 내부적으로는 즉시 `ToolVersion`으로 정규화해야 한다.

---

### 19.3 Script

```text
Script

의미:
  컨테이너 안에서 실행할 스크립트 또는 명령입니다.

예:
  run.sh
  bwa mem
  python main.py

나중에 영향:
  - 실제 실행 시 어떤 명령을 호출할지 결정합니다.
  - 입력/출력 파일 경로와 맞지 않으면 실행 단계에서 실패할 수 있습니다.

필수 여부:
  필수

Script:
```

---

## 20. method별 필드 입력 UX

### 20.1 container

필수 필드:

* ImageRef
* ImageDigest

선택 필드:

* Command

`Command`는 `RecipeFieldCatalog`에서 `RecipeFieldType.StringList`로 정의되어 있다. 따라서 반복 입력을 허용한다.

#### ImageRef

```text
ImageRef

의미:
  사용할 컨테이너 이미지 주소입니다.

예:
  quay.io/biocontainers/bwa:0.7.17--h7132678_9@sha256:...

나중에 영향:
  - 이 이미지를 기반으로 ToolDefinition.ImageUri가 만들어집니다.
  - digest가 없으면 재현성을 보장할 수 없습니다.

필수 여부:
  필수

ImageRef:
```

#### ImageDigest

```text
ImageDigest

의미:
  이미지가 정확히 어떤 버전의 바이너리인지 고정하기 위한 sha256 digest입니다.

예:
  sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef

나중에 영향:
  - 같은 tag라도 이미지 내용이 바뀌는 문제를 막습니다.
  - 비어 있으면 최종 검증에서 실패합니다.

필수 여부:
  필수

ImageDigest:
```

#### container field validation

container 필드 입력 후에는 `ImageReferenceNormalizer`를 통해 다음을 확인한다.

1. ImageRef에 digest가 있는가?
2. ImageDigest가 별도로 있는가?
3. 둘이 충돌하는가?
4. canonical URI를 만들 수 있는가?

canonical URI를 만들 수 없으면 다음 단계로 넘어가지 않는다.

---

### 20.2 package

필수 필드:

* BaseImage
* Packages
* Channels

선택 필드:

* PackageEngine

#### BaseImage

```text
BaseImage

의미:
  conda/micromamba를 실행할 기반 컨테이너 이미지입니다.

예:
  condaforge/miniforge3:24.3.0-0@sha256:...

나중에 영향:
  - Dockerfile의 FROM 이미지로 사용됩니다.
  - digest가 없으면 validate에서 실패합니다.

필수 여부:
  필수

BaseImage:
```

#### Packages

```text
Packages

의미:
  설치할 패키지 목록입니다.

예:
  bwa=0.7.17=h5bf99c6_8
  samtools=1.20=h50ea8bc_0

나중에 영향:
  - 패키지 버전이 고정되어야 재현성이 생깁니다.
  - 버전이 없으면 validate에서 실패할 수 있습니다.

필수 여부:
  최소 1개 필수

패키지를 하나씩 입력하세요.
빈 줄을 입력하면 종료합니다.
```

#### Channels

```text
Channels

의미:
  패키지를 받을 conda channel입니다.

예:
  bioconda
  conda-forge

나중에 영향:
  - 같은 패키지 이름이라도 channel에 따라 결과가 달라질 수 있습니다.
  - recipe에 명시적으로 기록됩니다.

필수 여부:
  최소 1개 필수

채널을 하나씩 입력하세요.
빈 줄을 입력하면 종료합니다.
```

---

### 20.3 mirror

필수 필드:

* BaseImage
* PackageMirrorUri
* Packages

선택 필드:

* MirrorKind

#### PackageMirrorUri

```text
PackageMirrorUri

의미:
  내부 package mirror 주소입니다.

예:
  https://mirror.company.local/conda

나중에 영향:
  - recipe를 실행하려는 환경이 이 주소에 접근할 수 있어야 합니다.
  - 외부 사용자에게는 같은 recipe가 동작하지 않을 수 있습니다.

필수 여부:
  필수

PackageMirrorUri:
```

---

### 20.4 source

필수 필드:

* BaseImage
* SourceUri
* SourceChecksum
* SourceBuildCommands

권장 필드:

* BuildDependencies

#### SourceUri

```text
SourceUri

의미:
  소스코드 archive 또는 release 파일 주소입니다.

예:
  https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz

나중에 영향:
  - 이 파일을 다운로드해서 직접 빌드합니다.
  - 주소가 바뀌거나 파일 내용이 바뀌면 재현성이 깨질 수 있습니다.

필수 여부:
  필수

SourceUri:
```

#### SourceChecksum

```text
SourceChecksum

의미:
  SourceUri에서 받은 파일이 정확히 같은 파일인지 확인하기 위한 sha256 값입니다.

예:
  sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef

나중에 영향:
  - checksum이 없으면 source build recipe는 validate에서 실패합니다.
  - 소스 파일 변조나 변경을 감지할 수 있습니다.

필수 여부:
  필수

SourceChecksum:
```

#### SourceBuildCommands

```text
SourceBuildCommands

의미:
  소스를 받은 뒤 실행할 빌드 명령입니다.

예:
  make
  make install

나중에 영향:
  - 컨테이너 이미지 안에서 실제로 실행될 빌드 절차입니다.
  - 명령이 틀리면 이미지 빌드 단계에서 실패합니다.

필수 여부:
  최소 1개 필수

빌드 명령을 하나씩 입력하세요.
빈 줄을 입력하면 종료합니다.
```

---

### 20.5 dockerfile

필수 필드:

* BaseImage
* DockerfilePath 또는 DockerfileContent

선택 필드:

* BuildContext

#### BaseImage

```text
BaseImage

의미:
  Dockerfile의 첫 번째 FROM과 정확히 같아야 하는 기준 이미지입니다.

예:
  ubuntu:24.04@sha256:...

나중에 영향:
  - Dockerfile의 첫 FROM과 다르면 validate에서 실패합니다.
  - latest 태그나 digest 없는 이미지는 재현성을 깨뜨릴 수 있습니다.

필수 여부:
  필수

BaseImage:
```

#### DockerfilePath

```text
DockerfilePath

의미:
  사용할 Dockerfile 경로입니다.

예:
  ./Dockerfile

주의:
  Dockerfile 방식은 가장 자유롭지만 재현성 책임이 가장 큽니다.
  모든 FROM 이미지는 digest로 고정되어야 합니다.
  latest 태그는 허용되지 않습니다.

필수 여부:
  DockerfilePath 또는 DockerfileContent 중 하나 필수

DockerfilePath:
```

---

## 21. Inputs/Outputs UX 개선

기존 프리셋 방식은 유지한다.

다만 각 프리셋에 다음 설명을 추가한다.

* 언제 쓰는 입력/출력인지
* 실제 파일 예시
* Role
* Format
* Shape 또는 Class

---

### 21.1 Inputs 예시

```text
입력 항목을 추가하세요.
빈 이름을 입력하면 입력 목록 작성을 종료합니다.

이름:
> reads

이 입력은 어떤 데이터인가요?

[1] FASTQ paired-end reads
    두 개의 FASTQ 파일이 한 쌍을 이루는 read 입력입니다.
    예: sample_R1.fastq.gz, sample_R2.fastq.gz
    Role: sample-fastq
    Format: fastq
    Shape: pair

[2] FASTQ single-end reads
    하나의 FASTQ 파일만 사용하는 read 입력입니다.
    예: sample.fastq.gz
    Role: sample-fastq
    Format: fastq
    Shape: single

[3] BAM alignment
    이미 정렬된 BAM 파일입니다.
    예: sample.bam
    Role: alignment
    Format: bam
    Shape: single

[4] FASTA reference
    reference genome FASTA 파일입니다.
    예: reference.fa
    Role: reference
    Format: fasta
    Shape: single

[5] VCF variants
    variant call 결과 파일입니다.
    예: sample.vcf
    Role: variant
    Format: vcf
    Shape: single

[6] 직접 입력

선택:
```

주의:

```text
여기서 VCF variants는 생물정보학 데이터 형식으로서의 variant를 의미한다.
CLI 내부의 작성 방식 선택지에는 variant라는 용어를 사용하지 않는다.
```

---

### 21.2 Outputs 예시

```text
출력 항목을 추가하세요.
빈 이름을 입력하면 출력 목록 작성을 종료합니다.

이름:
> aligned

이 출력은 어떤 데이터인가요?

[1] BAM alignment output
    정렬 결과 BAM 파일입니다.
    예: aligned.bam
    Role: aligned-bam
    Format: bam
    Class: primary

[2] BAI index output
    BAM index 파일입니다.
    예: aligned.bam.bai
    Role: index
    Format: bai
    Class: secondary

[3] VCF variant output
    variant call 결과 VCF 파일입니다.
    예: calls.vcf
    Role: variant-call
    Format: vcf
    Class: primary

[4] Report output
    HTML, TXT, JSON 등의 리포트 파일입니다.
    Role: report
    Format: html/txt/json
    Class: secondary

[5] 직접 입력

선택:
```

---

## 22. recovery UX

최종 검증 실패 시 기존 recovery 화면은 유지하되, 오류 메시지를 더 쉽게 설명한다.

원칙:

1. L1 rule id는 숨기지 않는다.
2. 사용자-facing 쉬운 설명을 함께 보여준다.
3. 해결 방법을 구체적으로 제시한다.
4. 관련 필드만 수정할 수 있게 한다.
5. 취소 경로를 제공한다.

예:

```text
최종 검증에 실패했습니다.

문제:
  SourceChecksum이 비어 있습니다.

Rule:
  L1-SRC-001 (SourceChecksum)

왜 문제인가요?
  source build 방식은 소스 파일이 매번 같은 파일인지 확인해야 합니다.
  checksum이 없으면 재현성을 보장할 수 없습니다.

해결 방법:
  sha256:<64자리 hex> 형식의 checksum을 입력하세요.

수정할 항목:
[1] SourceChecksum 수정
[2] 작성 방식 변경
[3] 지금까지 입력한 내용 보기
[4] 저장하지 않고 종료

선택:
```

---

## 23. non-interactive 모드

non-interactive 모드는 기존 동작을 유지한다.

다만 `recipe create` 시작 화면의 `[3] 스크립트/CI 모드 사용법 보기`를 선택하면 다음 예시를 보여준다.

```text
스크립트/CI 모드는 프롬프트 없이 한 줄 명령으로 recipe를 만듭니다.

예:
  nodekit recipe create recipe.json \
    --non-interactive --method package \
    --field ToolName=bwa \
    --field ToolVersion=0.7.17 \
    --field Script=run.sh \
    --field BaseImage=condaforge/miniforge3:24.3.0-0@sha256:... \
    --field Packages=bwa=0.7.17=h5bf99c6_8 \
    --field Channels=bioconda \
    --input reads=fastq-paired \
    --output aligned=bam-primary

자세한 사용법:
  nodekit recipe create --help
```

이 선택지는 recipe 생성을 시작하지 않고 사용법을 보여준 뒤 종료한다.

---

### 23.1 --field 파싱 계약

`--field`는 첫 번째 `=`만 key/value 구분자로 사용한다.

예:

```bash
--field Packages=bwa=0.7.17=h5bf99c6_8
```

해석:

```text
key:
  Packages

value:
  bwa=0.7.17=h5bf99c6_8
```

즉, value 안의 추가 `=`는 그대로 유지한다.

---

### 23.2 리스트 필드 입력 계약

리스트 필드는 같은 `--field`를 반복해서 누적한다.

예:

```bash
nodekit recipe create recipe.json \
  --non-interactive --method package \
  --field ToolName=bwa \
  --field ToolVersion=0.7.17 \
  --field Script=run.sh \
  --field BaseImage=condaforge/miniforge3:24.3.0-0@sha256:... \
  --field Packages=bwa=0.7.17=h5bf99c6_8 \
  --field Packages=samtools=1.20=h50ea8bc_0 \
  --field Channels=bioconda \
  --field Channels=conda-forge \
  --input reads=fastq-paired \
  --output aligned=bam-primary
```

콤마 구분은 v0.9.2의 공식 문법으로 권장하지 않는다.

비권장 예:

```bash
--field Packages=bwa=0.7.17,samtools=1.20
```

이유:

1. shell quoting 문제가 생길 수 있다.
2. 값 자체에 콤마가 들어가는 경우 확장성이 떨어진다.
3. 반복 입력 방식이 parser 구현과 사용자 설명 모두에서 더 명확하다.

---

### 23.3 반복 가능한 list field 판정

반복 가능한 list field 목록은 문서에 하드코딩하지 않는다.

다음 원칙을 따른다.

```text
IsListType(field) == true
→ 같은 --field 반복 입력을 누적한다.

IsListType(field) == false
→ 같은 --field 반복 입력은 마지막 값으로 덮어쓸지, 오류로 처리할지 별도 정책에 따른다.
```

v0.9.2 수용 기준에서 확정하는 반복 list field는 다음이다.

```text
Packages
Channels
SourceBuildCommands
BuildDependencies
Command
```

`Command`는 `RecipeFieldCatalog`에서 `RecipeFieldType.StringList`로 확인되었으므로 포함한다 (`IsListType(Command) == true`).

---

### 23.4 --input / --output 파싱

`--input`과 `--output`은 기존 문법을 유지한다.

예:

```bash
--input reads=fastq-paired
--output aligned=bam-primary
```

custom 입력도 기존 문법을 유지한다.

```bash
--input reads=custom,sample-fastq,fastq,pair
--output aligned=custom,aligned-bam,bam,primary
```

---

## 24. 구현 구조 제안

### 24.1 새 구성 요소

```text
AuthoringModeSelector
  - 쉬운 안내 모드 / 빠른 설정 모드 / 스크립트 사용법 선택

BeginnerGuideFlow
  - 사용자가 가진 단서 기반으로 method 결정
  - 설치 명령 best-effort parsing
  - parsing 실패 시 manual fallback

FastQuestionnaireFlow
  - 기존 6문항 Q&A 유지
  - 설명 / 예시 / 영향 / 후속 필드 보강

MethodRecommendationPresenter
  - 추천 method
  - 추천 이유
  - 후속 필드
  - 주의사항

PromptCommandHandler
  - /help
  - /review
  - /change-method
  - /cancel
  - /quit
  - /exit

InstallCommandParser
  - Parsed / PartiallyParsed / Failed 반환

ImageReferenceNormalizer
  - ImageRef / ImageDigest 정규화
  - canonical image URI 생성
  - digest conflict detection
  - RecipeDocument 생성 직전에 실행

RecipeCancelledException 또는 RecipeCancelledResult
  - 사용자 취소를 정상 흐름으로 표현
  - 종료 코드 130으로 매핑

ConsoleCancellationAdapter
  - Ctrl+C 처리
  - 테스트 가능한 cancellation abstraction 제공
```

---

### 24.2 기존 구성 요소 재사용

```text
RecipeDocument
RecipeValidator
RecipeRenderer
BuildRequestFactory
L1 validators
Input/Output presets
Final recovery flow
RecipeFieldCatalog
RecipeMethodRecommender
RecipeAuthoringSession
  - method 결정 전/후의 임시 상태 저장
  - /review 출력에 사용
```

---

### 24.3 권장 구조

```text
NodeKit.Core
  - RecipeDocument
  - RecipeValidator
  - RecipeRenderer
  - BuildRequestFactory
  - MethodRecommender
  - FieldMetadata
  - InputOutputPresets
  - InstallCommandParser
  - ImageReferenceNormalizer

NodeKit.Cli
  - AuthoringModeSelector
  - BeginnerGuideFlow
  - FastQuestionnaireFlow
  - MethodRecommendationPresenter
  - PromptCommandHandler
  - ConsoleCancellationAdapter
  - Console rendering
```

CLI의 prompt 로직과 recipe 생성/검증 로직은 분리한다.

이 구조는 향후 MCP 확장에도 유리하다.

---

## 25. 구현 우선순위

### Phase 1-A: prompt command 기반 취소

우선 구현:

```text
/cancel
/quit
/exit
/review
exit code 130
```

이유:

* 두 모드 모두에 필요한 공통 기능이다.
* 사용자 체감 개선이 크다.
* BeginnerGuideFlow와 독립적으로 구현할 수 있다.
* 현재 가장 큰 문제인 “중간에 나갈 수 없음”을 해결한다.

---

### Phase 1-B: Ctrl+C signal 기반 취소

구현:

```text
Console.CancelKeyPress
cancellation flag 또는 CancellationToken
RecipeCancelledException 또는 Cancelled result
stack trace 방지
exit code 130
테스트 가능한 cancellation abstraction
```

이유:

* Ctrl+C는 `/cancel`과 구현 경로가 다르다.
* process signal을 정상 취소 결과로 매핑해야 한다.
* 구현 난이도가 Phase 1-A보다 높다.
* 별도 단계로 추적해야 일정 리스크가 줄어든다.

---

### Phase 2: 빠른 설정 모드 보강

구현:

```text
AuthoringModeSelector
FastQuestionnaireFlow
기존 6문항 설명 보강
method 추천 결과 화면
```

이유:

* 기존 Q&A 로직을 재사용할 수 있다.
* 구현 리스크가 낮다.
* 경험 있는 사용자 UX를 빠르게 개선한다.

---

### Phase 3: 쉬운 안내 모드

구현:

```text
BeginnerGuideFlow
단서 기반 method 매핑
InstallCommandParser 3-state
parser 실패 fallback
ImageReferenceNormalizer
도구 이름만 아는 경우 안내
아무것도 모르는 경우 최소 단서 안내
```

이유:

* 사용자 가치가 가장 크다.
* parsing과 unresolved 경로가 있어 구현 리스크도 가장 높다.
* 따라서 Phase 1/2 이후 구현한다.

---

### Phase 4: non-interactive 문법 정리

구현:

```text
--field 첫 '=' 기준 파싱 명시
리스트 필드 반복 입력 누적
문서와 실제 RecipeCreateOptions 동작 일치 검증
테스트 추가
```

이유:

* v0.9.2 신규 UX는 아니지만 문서 예시와 실제 동작을 맞춰야 한다.
* CI/스크립트 사용자는 문법 안정성이 중요하다.

---

## 26. MCP 확장 고려

v0.9.2에서는 MCP server를 구현하지 않는다.

다만 향후 MCP 확장을 고려해 core logic과 CLI UI를 분리한다.

향후 MCP tool 후보:

```text
nodekit.suggest_method
nodekit.create_recipe_draft
nodekit.validate_recipe
nodekit.render_build_request
nodekit.explain_field
nodekit.list_input_presets
nodekit.list_output_presets
```

역할 분리:

| 인터페이스               | 주요 사용자       | 역할                      |
| ------------------- | ------------ | ----------------------- |
| CLI 쉬운 안내 모드        | 처음 사용하는 사람   | 예시 기반 recipe 생성         |
| CLI 빠른 설정 모드        | 경험 있는 사용자    | 빠른 method 결정            |
| CLI non-interactive | CI/스크립트      | 자동 recipe 생성            |
| MCP                 | AI assistant | 대화형 recipe authoring 지원 |

MCP는 CLI를 대체하지 않는다.

MCP는 NodeKit Core 기능을 AI assistant가 호출할 수 있게 하는 별도 인터페이스다.

권장 구조:

```text
NodeKit.Core
  - suggest method
  - create recipe draft
  - validate
  - render
  - explain field
  - list presets

NodeKit.Cli
  - human terminal UX

NodeKit.McpServer
  - AI tool/resource interface
```

---

## 27. 종료 코드

기존 종료 코드는 유지한다.

| 코드 | 의미                                  |
| -- | ----------------------------------- |
| 0  | 성공                                  |
| 1  | recipe-level 또는 L1 검증 실패            |
| 2  | 사용법 오류, 인자 오류, 파일 읽기 실패, JSON 파싱 실패 |

v0.9.2에서 다음 종료 코드를 추가한다.

| 코드  | 의미                                          |
| --- | ------------------------------------------- |
| 130 | 사용자 취소, `/cancel`, `/quit`, `/exit`, Ctrl+C |

사용자 취소는 검증 실패나 사용법 오류가 아니므로 별도 코드로 구분한다.

---

## 28. 문서 업데이트 항목

기존 사용 가이드는 다음 구조로 업데이트한다.

```text
2. recipe create
  2-1. 진행 방식 선택
  2-2. 쉬운 안내 모드
  2-3. 빠른 설정 모드
  2-4. 공통 필드 입력
  2-5. method별 필드 입력
  2-6. Inputs/Outputs 입력
  2-7. recovery
  2-8. non-interactive
  2-9. 중간에 나가기 / review / method 변경
```

escape hatch 설명은 다음으로 확장한다.

```text
/help
/review
/change-method
/cancel
/quit
/exit
```

`/back`은 다음과 같이 문서화한다.

```text
/back은 v0.9.2 필수 기능이 아니다.
향후 버전에서 제한적으로 지원할 수 있다.
```

Dockerfile 경고는 강화한다.

```text
Dockerfile 방식은 가장 자유롭지만 재현성 책임이 가장 큽니다.
처음 사용하는 경우 package 또는 container 방식을 먼저 고려하세요.
```

digest 없는 이미지 처리도 명확히 한다.

```text
digest 없는 container 이미지는 기본 진행하지 않는다.
사용자는 digest 포함 이미지 주소를 다시 입력하거나,
ImageDigest를 따로 입력하거나,
다른 작성 방식으로 바꿔야 한다.
```

non-interactive 문법도 명확히 한다.

```text
--field는 첫 번째 '='만 key/value 구분자로 사용한다.
리스트 필드는 같은 --field를 반복해 누적한다.
ToolVersion은 내부 필드명이며, non-interactive에서는 --field ToolVersion=...을 사용한다.
```

---

## 29. 수용 기준

### 29.1 쉬운 안내 모드

* 사용자가 도구 이름만 알고 있어도 시작할 수 있다.
* 설치 명령을 붙여넣으면 package 방식으로 추천된다.
* 설치 명령 parser는 `Parsed`, `PartiallyParsed`, `Failed`를 구분한다.
* 설치 명령 파싱이 실패해도 종료하지 않고 manual fallback을 제공한다.
* 컨테이너 이미지 주소를 입력하면 container 방식으로 추천된다.
* digest 없는 이미지는 기본 진행하지 않는다.
* ImageRef와 ImageDigest를 따로 입력하면 canonical URI를 만들 수 있어야 한다.
* ImageReferenceNormalizer는 RecipeDocument 생성 직전에 실행된다.
* RecipeValidator, RecipeRenderer, L1 validator는 digest 포함 canonical URI를 입력으로 받는다.
* ImageRef digest와 별도 ImageDigest가 충돌하면 사용자에게 해결 선택지를 제공한다.
* GitHub/source URL을 입력하면 source 방식으로 추천된다.
* SourceChecksum이 없으면 source 방식 완성을 허용하지 않는다.
* Dockerfile 경로를 입력하면 dockerfile 방식으로 추천된다.
* Dockerfile 방식은 기본값 `N`의 경고 확인을 거쳐야 한다.
* 내부 저장소 주소를 입력하면 mirror 방식으로 추천된다.
* 아무것도 모르는 경우 최소 필요 단서를 안내하고 안전하게 종료할 수 있다.

---

### 29.2 빠른 설정 모드

* 기존 6문항 Q&A를 유지한다.
* 각 질문에는 의미, 예시, 선택 영향, 후속 필드 설명이 포함된다.
* `y/n/Enter` 입력이 명확히 안내된다.
* 추천 method 결과 화면이 제공된다.
* 사용자가 추천 method를 거절하면 직접 method를 선택할 수 있다.

---

### 29.3 공통 명령

* 모든 주요 입력 프롬프트에서 `/help`가 동작한다.
* 모든 주요 단계에서 `/review`가 동작한다.
* `/change-method`가 공통 필드를 최대한 보존한다.
* `/change-method`는 무효화되는 필드를 사용자에게 보여준다.
* `/change-method` 무효화 필드는 method field set 차집합으로 계산한다.
* field set 계산에는 `ToolVersion`을 사용한다. `Version`은 사용자-facing 라벨이다.
* `/cancel`, `/quit`, `/exit`가 저장 없이 종료한다.
* Ctrl+C가 사용자 취소로 처리된다.
* 사용자 취소 시 종료 코드 130을 반환한다.
* 취소 시 stack trace를 출력하지 않는다.

---

### 29.4 non-interactive

* 기존 non-interactive `recipe create` 호환성을 유지한다.
* `--field`는 첫 번째 `=`만 key/value 구분자로 사용한다.
* value 안의 추가 `=`는 보존한다.
* `ToolVersion`은 공식 내부 필드명이다.
* non-interactive 예시는 `--field ToolVersion=...`을 사용한다.
* 리스트 필드는 반복 입력으로 누적한다.
* v0.9.2에서 반복 입력을 명시적으로 보장하는 필드는 `Packages`, `Channels`, `SourceBuildCommands`, `BuildDependencies`, `Command`다 (`RecipeFieldCatalog.IsListType`이 true인 필드).
* 문서 예시와 실제 `RecipeCreateOptions` parsing 동작이 일치해야 한다.

---

### 29.5 기존 기능 유지

* `validate` 명령 동작은 유지된다.
* `render` 명령 동작은 유지된다.
* recipe 검증 실패 시 파일을 쓰지 않는 fail-closed 동작은 유지된다.
* legacy BuildRequest export 경로는 유지된다.
* NodeVault submit/build 기능은 추가하지 않는다.

---

## 30. 비범위

v0.9.2에서는 다음을 구현하지 않는다.

* NodeVault 조회
* public package 검색
* BioContainer 자동 검색
* 이미지 빌드
* registry push
* gRPC submit
* MCP server 구현
* draft 자동 저장
* `/save-draft`
* `/resume`
* 완전한 `/back`
* Dockerfile 자동 수정
* checksum 자동 계산
* digest 자동 조회
* `Version`을 공식 내부 필드명으로 사용하는 것

---

## 31. 향후 단계

### v0.10 후보

* 제한적 `/back`
* `/save-draft`
* `/resume`
* draft 저장 후 이어서 작성
* 더 풍부한 field explanation
* 자주 쓰는 생물정보학 도구 예시 추가
* package/container/source 예제 recipe 추가

### v0.11 후보

* NodeVault catalog 조회
* 내부 catalog 기반 method 추천
* BioContainer hint 제공
* package availability check
* image digest lookup 보조 기능

### MCP phase 후보

* `nodekit.suggest_method`
* `nodekit.create_recipe_draft`
* `nodekit.validate_recipe`
* `nodekit.render_build_request`
* `nodekit.explain_field`
* `nodekit.list_presets`

---

## 32. 최종 구현 체크리스트

### 문서/설계 체크

* [ ] v0.9.2 문서가 기존 v0.8 문서보다 최신임을 명시한다.
* [ ] 기존 v0.8 문서에서 이 문서로의 관계를 README 또는 docs index에 표시한다.
* [ ] 사용 가이드의 `recipe create` 섹션을 새 모드 구조로 업데이트한다.
* [ ] non-interactive `--field` 문법을 사용 가이드에 명시한다.
* [ ] 사용자-facing `Version`과 내부 `ToolVersion`의 관계를 사용 가이드에 명시한다.
* [x] `Command` 필드 타입은 RecipeFieldCatalog 기준으로 확인한다. (Sprint R8: `RecipeFieldType.StringList`로 확인됨)

### Phase 1-A

* [x] `/cancel` 구현 (Sprint R9)
* [x] `/quit` 구현 (Sprint R9: `/cancel`과 동일 처리)
* [x] `/exit` 구현 (Sprint R9: `/cancel`과 동일 처리)
* [x] `/review` 구현 (Sprint R9)
* [x] 취소 시 파일 미생성 보장 (Sprint R9)
* [x] 취소 시 exit code 130 반환 (Sprint R9)

### Phase 1-B

* [x] Ctrl+C signal 처리 (Sprint R10: `ConsoleCancelKeyCancellationSource`)
* [x] stack trace 방지 (Sprint R10: `RecipeCreateCancelledException` 캐치, /cancel과 동일 처리)
* [x] cancellation abstraction 추가 (Sprint R10: `IRecipeCreateCancellationSource`)
* [x] Ctrl+C 테스트 추가 (Sprint R10: fake `SequencedCancellationSource` 주입)
* [x] `/cancel`과 Ctrl+C 결과 일관성 검증 (Sprint R10)

### Phase 2

* [x] AuthoringModeSelector 추가 (Sprint R11: 3-choice 진입 화면, CI 모드 사용법 출력)
* [x] 빠른 설정 모드 시작 화면 추가 (Sprint R11: Section 15.2 인트로)
* [x] 기존 6문항 설명 보강 (Sprint R11: RecipeMethodQuestionDetailCatalog, 의미/예/영향 출력)
* [x] method 추천 결과 화면 추가 (Sprint R11: MethodRecommendationPresenter, Section 16 형식)

### Phase 3

* [ ] BeginnerGuideFlow 추가
* [ ] 단서 기반 method 매핑 구현
* [ ] InstallCommandParser 3-state 구현
* [ ] parser partial/failure fallback 구현
* [ ] ImageReferenceNormalizer 구현
* [ ] ImageReferenceNormalizer를 RecipeDocument 생성 직전에 실행
* [ ] digest conflict 처리 구현
* [ ] 아무것도 모르는 경우 최소 단서 안내 구현

### Phase 4

* [ ] non-interactive `--field` 첫 `=` 기준 parsing 확인
* [ ] 리스트 필드 반복 입력 누적 확인
* [ ] `ToolVersion` 필드명 기준으로 non-interactive 예시 업데이트
* [x] `Command` 반복 입력 허용 여부를 RecipeFieldCatalog 기준으로 확정 (StringList, 반복 입력 허용 — 회귀 테스트는 Sprint R14에서 추가)
* [ ] 문서 예시와 실제 parser 동작 일치 테스트

---

## 33. 결론

v0.9.2의 핵심은 기존 CLI를 버리는 것이 아니다.

핵심은 `recipe create`의 첫 진입 경험을 사용자 수준에 맞게 분리하고, 실제 구현에서 흔들릴 수 있는 계약을 닫는 것이다.

쉬운 안내 모드는 사용자가 아무것도 몰라도 시작할 수 있게 한다. 다만 v0.9.2 CLI는 외부 검색을 하지 않으므로 recipe 완성을 위해 최소한 하나의 구체적 단서가 필요하다는 한계도 명확히 알린다.

빠른 설정 모드는 기존 6문항 Q&A 방식을 유지하되, 질문의 의미와 선택의 영향을 충분히 설명한다.

공통적으로 `/cancel`, `/review`, `/change-method` 같은 안전장치를 추가해 사용자가 중간에 길을 잃어도 빠져나올 수 있게 한다.

`/back`은 유용하지만 구현 복잡도가 높으므로 v0.9.2 필수 범위에서는 제외하고, 향후 제한적 지원으로 넘긴다.

v0.9.2는 v0.9.1에서 남아 있던 마지막 구현 불일치 가능성을 닫는다.

특히 다음 세 가지를 추가로 확정한다.

1. 사용자-facing `Version`은 내부 필드명 `ToolVersion`에 매핑된다.
2. `ImageReferenceNormalizer`는 `RecipeDocument` 생성 직전에 실행되고, validator/renderer는 digest 포함 canonical URI를 받는다.
3. `Command`는 `RecipeFieldCatalog`에서 `RecipeFieldType.StringList`로 확인되었으므로 반복 list field로 취급한다 (Sprint R8에서 확정).

이 구조는 현재 CLI의 legacy export 범위를 유지하면서도, 향후 MCP 기반 대화형 authoring으로 확장하기 좋은 기반이 된다.