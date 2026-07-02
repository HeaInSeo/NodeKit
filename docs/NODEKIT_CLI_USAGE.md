# NodeKit CLI 사용 가이드

`src/NodeKit.Cli/`에 구현된 `nodekit` CLI 사용법이다. 처음 써보는 사람이 이
문서만 보고 recipe 하나를 끝까지 만들 수 있도록, 명령어 → 예시 → 막혔을 때
어떻게 하는지 순서로 적었다.

이 CLI는 **legacy `BuildRequest` 경로만** 다룬다 — `RecipeDocument →
RecipeValidator → RecipeRenderer → ToolDefinition → 기존 L1 validator 체인 →
legacy `BuildRequest` JSON`. gRPC 전송, NodeVault 조회, 이미지 빌드,
`submit`/`build` 명령은 이 CLI에 없다 (CLAUDE.md 1절, NodeKit 책임 경계).

명령 설계 배경은 [`NODEKIT_CLI_RECIPE_SPEC_DRAFT.md`](NODEKIT_CLI_RECIPE_SPEC_DRAFT.md)
§5/§6, recipe create 마법사의 v1.0 UX 계약은
[`NODEKIT_CLI_RECIPE_AUTHORING_UX_V1.0.md`](NODEKIT_CLI_RECIPE_AUTHORING_UX_V1.0.md),
실행 계획은
[`NODEKIT_CLI_RECIPE_AUTHORING_UX_V1.0_SPRINT_PLAN.md`](NODEKIT_CLI_RECIPE_AUTHORING_UX_V1.0_SPRINT_PLAN.md),
초기 설계 배경은
[`NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md`](NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md)
참고.

## 0. 빠른 시작

recipe.json을 손으로 쓰지 않아도 된다. 이게 핵심이다.
아래 명령은 **먼저 recipe.json을 생성**한다. 아직 파일이 없을 때
`validate recipe.json`부터 실행하면 "recipe 파일을 읽을 수 없습니다" 오류가 난다.

```bash
dotnet run --project src/NodeKit.Cli -- recipe create
```

경로 없이 실행하면 마법사 마지막 단계에서 저장 경로를 직접 입력한다(기본값 제안). 경로를 미리 지정하고 싶으면 세 번째 인자로 전달한다.

```bash
dotnet run --project src/NodeKit.Cli -- recipe create recipe.json
```

질문에 답하면서 따라가면 끝에 `recipe.json`이 저장된다. 그 다음:

```bash
dotnet run --project src/NodeKit.Cli -- validate recipe.json
dotnet run --project src/NodeKit.Cli -- render recipe.json --out build-request.json
```

아래 절들은 이 세 명령(`recipe create`, `validate`, `render`)을 차례로
자세히 설명한다.

중간에 그만두려면 대부분의 프롬프트에서 다음 중 하나를 입력한다.

```text
/cancel
/quit
/exit
```

필드 입력 단계에서는 한 번 더 확인하고, 시작 화면/쉬운 안내 모드/빠른 설정
질문처럼 아직 값이 저장되지 않은 단계에서는 바로 취소된다. 취소 시 파일은
저장되지 않고 종료 코드 `130`을 반환한다.

## 1. 빌드/실행

저장소 루트에서 실행한다.

```bash
# 전체 빌드
dotnet build NodeKit.sln

# CLI만 빌드
dotnet build src/NodeKit.Cli/NodeKit.Cli.csproj

# 1. recipe.json 생성
dotnet run --project src/NodeKit.Cli -- recipe create recipe.json

# 2. 생성 확인
ls -l recipe.json

# 3. 실행 방법 1: dotnet run
dotnet run --project src/NodeKit.Cli -- validate recipe.json

# 4. 실행 방법 2: 빌드된 바이너리 직접 실행
./src/NodeKit.Cli/bin/Debug/net10.0/NodeKit.Cli validate recipe.json
```

전체 테스트:

```bash
dotnet test --solution NodeKit.sln
```

Microsoft.Testing.Platform/xUnit v3를 사용하므로 특정 테스트 클래스만 돌릴 때는
다음처럼 `--` 뒤에 xUnit 옵션을 넘긴다.

```bash
dotnet test --project tests/NodeKit.Cli.Tests/NodeKit.Cli.Tests.csproj -- \
  --filter-class NodeKit.Cli.Tests.BeginnerGuideFlowTests
```

`NodeKit.Cli.csproj`는 NuGet 패키지를 전혀 참조하지 않는다 — `NodeKit.csproj`의
Avalonia/Grpc.Net.Client/Google.Protobuf/Wasmtime/ReactiveUI 의존성을 전혀
가져오지 않는다. 빌드 결과물(`bin/Debug/net10.0/`)에는
`NodeKit.Cli.{dll,pdb,deps.json,runtimeconfig.json}`만 있다.

### 1-1. UX 테스트용 빠른 실행

v1.0 authoring UX를 직접 확인하려면 아래 명령으로 시작한다.

```bash
dotnet run --project src/NodeKit.Cli -- recipe create
# 또는 경로를 미리 지정하는 경우:
dotnet run --project src/NodeKit.Cli -- recipe create /tmp/nodekit-recipe.json
```

권장 확인 경로:

| 확인할 UX | 입력 경로 |
|---|---|
| 도구 이름 lookup | `[1] 쉬운 안내 모드` → `[1] 도구 이름만 알고 있다` → `bwa` |
| no-clue 회복 | `[1] 쉬운 안내 모드` → `[7] 잘 모르겠다` |
| digest 안내 | `[1] 쉬운 안내 모드` → `[3] 컨테이너 이미지 주소` → `quay.io/biocontainers/bwa:0.7.17--h7132678_9` |
| checksum 안내 | `[1] 쉬운 안내 모드` → `[4] GitHub 또는 소스코드 주소` → archive URL → checksum 빈 입력 |
| final recovery 문구 | 빠른 설정 모드에서 container를 고르고 잘못된 digest(`sha256:bad`) 입력 |
| **인라인 예시 힌트** | 어느 방식이든 필드 프롬프트 아래에 `예:` 줄이 표시된다 |
| **포트 설정 화면** | 모든 필드 입력 완료 후 자동으로 표시된다 (빈 줄로 건너뛸 수 있음) |
| **저장 전 확인 화면** | 포트 설정 직후 최종 요약을 보여주고 `[n]`으로 취소할 수 있다 |
| **빌드 문자열 후보 선택 UX** | `NODEKIT_RESOLVE_RECIPE_STUB=1` 설정 후 package 방식으로 진행 (2-6절) |
| **이전 필드로 `/back`** | 필드 입력 중 `/back` → 직전 필드 재입력 (첫 필드에서는 모드 선택으로) |
| **쉬운 안내 ToolName 자동 채움** | `[1] 쉬운 안내 모드` → `[1] 도구 이름만 알고 있다` → 이름 입력 → 필드 루프에서 ToolName 건너뜀 |
| **3-구역 TUI 레이아웃** | 화면 상단 가로선 구분자 / 설명 구역 / 힌트 구분자+`>` 입력 프롬프트 순서 |

**패키지 빌드 문자열 후보 선택 UX 확인 방법:**

```bash
# 환경변수 한 개만 추가하면 된다 — 실제 NodeVault 연결 없이 동작
NODEKIT_RESOLVE_RECIPE_STUB=1 \
  dotnet run --project src/NodeKit.Cli -- recipe create /tmp/test-package.json
```

빠른 설정 모드(`[2]`) → `public channel에 패키지가 있나요?` → `y` → package 방식으로
진행하면 모든 필드 입력 완료 후 **빌드 문자열 선택** 화면이 자동으로 나온다.

생성된 recipe가 있으면 다음으로 확인한다.

```bash
dotnet run --project src/NodeKit.Cli -- validate /tmp/nodekit-recipe.json
dotnet run --project src/NodeKit.Cli -- render /tmp/nodekit-recipe.json --out /tmp/build-request.json
```

## 2. `nodekit recipe create` — recipe 마법사

가장 많이 쓸 명령이다. 질문에 답하기만 하면 reproducibility 규칙(CLAUDE.md
3절: `latest` 태그 금지, digest 고정, 패키지 버전 고정)을 어기지 않는
`recipe.json`을 만들어 준다.

```bash
nodekit recipe create [<recipe.json>] [--method ...] [--non-interactive ...]
```

출력 경로는 선택사항이다. 생략하면 마법사 마지막 단계에서 경로를 직접 입력한다(기본값: `현재디렉터리/ToolName-ToolVersion.json`). 기존 디렉터리 경로를 전달하면 해당 디렉터리 아래 기본값 파일명을 사용한다.

옵션 없이 실행하면 **대화형 모드**로 들어간다. `--non-interactive`와 함께
`--method`/`--field` 등을 모두 지정하면 프롬프트 없이 한 번에 만든다(2-10절).
`--non-interactive`에서는 출력 경로가 필수다.

### 2-1. 진행 방식 선택

실행하면 가장 먼저 진행 방식을 고른다.

```
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

`[3]`은 recipe를 만들지 않는다. `--non-interactive` 사용법을 출력하고 종료한다.

이 화면에서도 `/cancel`, `/quit`, `/exit`로 바로 종료할 수 있다.

### 2-2. 쉬운 안내 모드

처음 사용하거나, 어떤 method를 써야 할지 모를 때 선택한다. 무엇을 알고
있는지 고르면 그에 맞는 입력 흐름으로 바로 안내한다.

**각 서브-화면은 이전 텍스트를 지우고 단독으로 표시된다.** 선택 후 나오는
설치 명령 입력, 이미지 주소 입력, 소스 주소 입력 등 모든 화면이 동일하게
한 화면씩 전환된다. 입력 프롬프트 바로 위에는 `/cancel: 종료` 힌트가 표시된다.

```
쉬운 안내 모드
/back: 이전 화면   /cancel: 종료

정확히 몰라도 괜찮습니다.
알고 있는 것만 선택하세요.

무엇을 알고 있나요?

[1] 도구 이름만 알고 있다
    예: bwa, samtools, fastqc

[2] 설치 명령을 알고 있다
    예: conda install -c bioconda bwa=0.7.17

[3] 컨테이너 이미지 주소를 알고 있다
    예: quay.io/biocontainers/bwa:0.7.17--h7132678_9

[4] GitHub 또는 소스코드 주소를 알고 있다
[5] Dockerfile을 가지고 있다
[6] 회사/학교 내부 저장소를 써야 한다
[7] 잘 모르겠다

선택:
```

| 선택 | 연결되는 method |
|---|---|
| `[1]` 도구 이름만 | 도구 이름 입력 → bioconda/BioContainers URL 표시 → 추가 선택으로 method 결정 |
| `[2]` 설치 명령 | install command 파싱 → `package` |
| `[3]` 컨테이너 이미지 | `container` |
| `[4]` GitHub/소스 주소 | `source` |
| `[5]` Dockerfile | `dockerfile` |
| `[6]` 내부 저장소 | `mirror` |
| `[7]` 잘 모르겠다 | 단서 부족 안내 → 1~6 중 선택 또는 종료 |

**도구 이름만 아는 경우** (`[1]`): 도구 이름을 입력하면 별도 화면에서
bioconda/BioContainers 확인 URL을 보여준 뒤, 찾은 것이 무엇인지(설치 명령/이미지/소스
등)를 다시 선택한다. **이때 입력한 도구 이름은 뒤에 나오는 필드 입력 단계에서
`ToolName` 필드에 자동으로 채워진다** — ToolName을 다시 묻지 않는다.

```
다음 위치에서 도구를 확인해보세요.

  bioconda 패키지:
    https://anaconda.org/bioconda/bwa

  BioContainers 이미지:
    https://quay.io/repository/biocontainers/bwa?tab=tags

...
'bwa' 도구를 설치하거나 실행하는 예시를 본 적 있나요?

[1] conda install 또는 micromamba install 예시를 봤다
[2] docker run 또는 컨테이너 이미지 주소를 봤다
...
선택:
```

**설치 명령** (`[2]`): install command를 붙여 넣으면 파싱 후 확인 화면이 뜬다.

```
설치 명령을 입력해 주세요.

예:
  conda install -c bioconda bwa=0.7.17

/cancel: 종료
설치 명령:
> conda install -c bioconda bwa=0.7.17
```

파싱에 성공하면 추출된 값을 보여주고 확인을 요청한다(별도 화면).

```
설치 명령을 이해했습니다.

이해한 값:
  PackageEngine: conda
  Channels:
    - bioconda
  Packages:
    - bwa=0.7.17

선택:
[1] 이해한 값을 사용하고 부족한 값을 직접 입력한다
[2] 설치 명령을 다시 입력한다
[3] 다른 작성 방식을 선택한다
[4] 취소한다

선택:
```

**컨테이너 이미지 — digest 처리** (`[3]`): 이미지 주소를 입력받는다.
`NODEKIT_HARBOR_URL` 환경변수가 설정되어 있으면 내부 Harbor에서 digest를
자동으로 조회한다. 자동 조회 성공 시 별도 화면에서 확인을 요청한다.

```
이미지 digest를 확인했습니다.

  sha256:0123456789abcdef...

이 digest를 사용할까요? [Y/n]
```

digest가 없거나 조회에 실패하면 별도 화면에서 선택지를 보여준다.

```
입력한 이미지 주소에는 digest가 없습니다.

현재 값:
  quay.io/biocontainers/bwa:0.7.17--h7132678_9

[1] digest가 포함된 이미지 주소를 다시 입력한다
[2] ImageDigest를 따로 입력한다
[3] 다른 작성 방식으로 바꾼다
[4] 취소한다
선택:
```

**Harbor 자동 digest 조회 환경변수:**

```bash
export NODEKIT_HARBOR_URL=https://harbor.lab.local
export NODEKIT_HARBOR_CA_CERT=~/.config/infra-lab/certs/harbor-ca.crt
export NODEKIT_HARBOR_USER=admin
export NODEKIT_HARBOR_PASSWORD=<password>
```

**source build — checksum 필수 처리** (`[4]`): 소스 URI를 입력받은 뒤
별도 화면에서 checksum 계산 방법을 보여준다.

```
소스 코드 검증값이 필요합니다.

  curl -fsSL "<SourceUri>" | sha256sum

/cancel: 종료
SourceChecksum:
```

checksum을 입력하지 않으면 다시 선택 화면이 뜬다. checksum 없이 넘어가는
경로는 없다.

**Dockerfile — 재현성 경고** (`[5]`): 경로 입력 후 별도 화면에서 경고가 나온다.
Enter 또는 `n`이면 방식 선택으로 돌아간다. `y`를 입력해야 계속된다.

```
/cancel: 종료   아니오(Enter/n): 방식 선택으로 돌아가기
계속 진행할까요? [y/N]
```

**아무것도 모름** (`[7]`): 별도 화면에서 다음 선택지를 보여준다.

```
아직 recipe를 완성하기 위한 단서가 부족합니다.

[1] 도구 이름으로 bioconda/BioContainers 확인 방법을 본다
[2] 설치 명령을 입력한다
[3] 컨테이너 이미지 주소를 입력한다
[4] 소스코드 주소를 입력한다
[5] Dockerfile 경로를 입력한다
[6] 저장하지 않고 종료한다
선택:
```

`[6]`은 종료 코드 0으로 끝난다. 어느 입력 프롬프트에서든 `/cancel`·`/quit`·`/exit`를
입력하면 종료 코드 130으로 취소된다.

### 2-3. 빠른 설정 모드

6개의 예/아니오 질문에 답하면 방법(method)을 추천해 준다. **각 질문은 별도
화면에 한 개씩 표시된다.** 매 화면 위에 진행도(`[1 / 6]`)와 탈출 힌트가
나온다.

```
빠른 설정 모드  [1 / 6]
/back: 이전 질문   /cancel: 종료

Q. 내부망/폐쇄망 환경인가요?

   서버나 워크스테이션에서 인터넷에 접속할 수 없거나
   회사/학교 내부망만 사용할 수 있는 환경이면 y입니다.

선택 [y/n/Enter]:
```

답을 모르면 그냥 Enter(`u`로 처리됨). `/back`을 입력하면 이전 질문으로
돌아간다. 첫 번째 질문에서 `/back`을 입력하면 모드 선택 화면으로 돌아간다.

| # | 질문 | 의미 |
|---|---|---|
| 1 | 내부망/폐쇄망 환경인가요? | public 인터넷에서 패키지/이미지를 받을 수 없는지 |
| 2 | 내부 package mirror URI를 아시나요? | 내부 conda/pip mirror가 있는지 |
| 3 | 기존 컨테이너 이미지 URI가 있나요? | 이미 쓸만한 이미지(BioContainer 등)가 있는지 |
| 4 | public channel에 패키지가 있나요? | conda-forge/bioconda 같은 곳에 패키지가 있는지 |
| 5 | source URL과 checksum이 있나요? | 소스를 직접 받아 빌드할 수 있는지 |
| 6 | 기존 Dockerfile이 있나요? | 이미 작성된 Dockerfile이 있는지 |

6번째 질문까지 답하면 별도 화면에서 추천 method와 이유를 보여준다. Enter로
추천을 수락하거나 번호를 입력해 다른 method를 선택한다.

| Method | 의미 | 준비물 |
|---|---|---|
| `container` | 기존 컨테이너 이미지 사용 | digest로 고정된 이미지 URI |
| `package` | conda/micromamba로 패키지 설치 | public channel에 있는 패키지 |
| `mirror` | 내부 package mirror에서 설치 | 내부 mirror URI |
| `source` | 소스코드로 직접 빌드 | SourceUri + SourceChecksum(sha256) |
| `dockerfile` | Dockerfile 직접 작성 | Dockerfile 경로 또는 내용 (최후의 수단) |

`dockerfile`을 고르면 재현성 경고 화면이 별도로 뜬다 — `y`로 동의해야 진행된다.

### 2-3.5. base image 선택 (Package/Mirror/Source 방식 전용)

`package`, `mirror`, `source` 방식으로 진행하면 채널 확인 다음에 **base image 선택** 화면이 자동으로 나온다. 이 화면에서 번호를 선택하면 digest를 자동으로 조회해 `ImageRef` 필드를 설정한다. `0`을 입력하면 다음 필드 입력 단계에서 직접 입력한다.

```
── Base image 선택 ─────────────────────────────────────────
사용할 기반 이미지를 선택하세요. Digest는 자동으로 조회합니다.

  [1] condaforge/miniforge3:24.3.0-0
      공식 conda-forge Miniforge 기반 이미지 (conda/mamba 포함)
  [2] mambaorg/micromamba:1.5.8
      Micromamba 경량 기반 이미지 (빠른 설치)

  [0] 직접 입력 (다음 단계에서 직접 입력)

/cancel: 종료
번호를 선택하세요 (1–2, 0 = 직접 입력):
> 1

condaforge/miniforge3:24.3.0-0 의 digest를 조회합니다...
설정 완료: condaforge/miniforge3:24.3.0-0@sha256:abcdef...
```

digest 조회에 성공하면 `ImageRef` 필드가 자동으로 채워지고 이후 단계에서 건너뛴다. 조회에 실패하면 재시도 또는 `0`을 입력해 직접 입력 모드로 전환할 수 있다.

**자동 조회 동작 조건:**

| 환경 | 동작 |
|---|---|
| `NODEKIT_HARBOR_URL` 설정 | Harbor에서 digest 조회 |
| `NODEKIT_BASE_IMAGE_STUB=1` | 테스트/UX 확인용 고정 digest 반환 |
| 아무것도 없음 | 이 화면이 나타나지 않음 (다음 단계에서 직접 입력) |

공개 registry 자동 조회(`PublicRegistryImageDigestResolver`)는 현재 비활성화 상태다 — 오픈망 환경에서 이 화면을 테스트하려면 `NODEKIT_BASE_IMAGE_STUB=1`을 사용한다.

### 2-4. 공통 필드 입력

method가 정해지면 공통 필드를 먼저 물어본다. 각 화면은 다음 **3-구역 레이아웃**으로
표시된다.

```
────────────────────────────────────────────────────────────────  ← 상단 구분자 (구역 1 시작)
[1 / 6]

도구 이름 — recipe에서 식별할 도구 이름입니다.
   예: bwa-mem

── /back: 이전 필드   /cancel: 종료   /review: 현재 값   /change-method: 작성 방식 변경 ──
>                                                                  ← 입력 프롬프트
```

- **구역 1 (설명)**: 상단 가로선 구분자로 시작. 진행도(`[1 / 6]`), 필드 라벨·설명, `예:` 줄 표시.
- **구역 2 (힌트)**: 사용 가능한 명령(`/back`, `/cancel` 등)이 가로선 형태로 표시.
  필드 설명 아래에 위치하므로 내용을 먼저 읽은 뒤 명령을 확인할 수 있다.
- **구역 3 (입력)**: `> ` 프롬프트에서 값을 입력한다.

| 필드 | 필수 여부 | 설명 |
|---|---|---|
| `ToolName` | 필수 | recipe가 식별할 도구 이름 (예: `bwa-mem`) |
| `ToolVersion` | 필수 | 도구 버전 (예: `0.7.17`) |
| `Script` | 필수 | 기본 실행 명령 또는 이미지 안의 스크립트 경로 (예: `bwa mem`, `/app/run.sh`) |

`Script`라는 내부 필드명은 legacy `BuildRequest` 호환 때문에 유지된다. 장기
NodeVault toolspec/toolprofile 모델에서는 실행 정보가 `runtime.command`와
dry-run profile의 `runnerScriptDigest`/observed I/O 기록으로 더 명확히 분리된다.
따라서 CLI 화면에서는 이 값을 "기본 실행 명령"으로 이해하면 된다.

잘못된 값을 넣으면 이유와 함께 같은 필드를 다시 물어본다. 끝까지 가서야
막히지 않는다.

### 2-5. method별 필드 입력

공통 필드 다음에 method 전용 필드를 채운다.

**`container`**

| 필드 | 필수 여부 | 설명 |
|---|---|---|
| `ImageRef` | 필수 | 이미지 주소. `repo:tag@sha256:...` 형식이면 digest 자동 추출. tag만 있으면 digest를 따로 요구한다 |
| `ImageDigest` | 필수 | digest 고정 (예: `sha256:<64-hex>`). `ImageRef`에 이미 포함된 경우 생략됨 |
| `Command` | 선택 | 이미지 기본 entrypoint를 바꾸고 싶을 때만 |

**`package`**

| 필드 | 필수 여부 | 설명 |
|---|---|---|
| `ImageRef` | 필수 | 기반 이미지, digest 포함 필요 (예: `condaforge/miniforge3:24.3.0-0@sha256:...`) |
| `Packages` | 필수, 최소 1개 | 설치할 패키지. `bwa=0.7.17`(버전만)으로 충분하며, 빌드 문자열(`=version=build` 부분)은 저장 전 ResolveRecipe 단계(2-6절)에서 선택한다. 직접 고정하려면 `bwa=0.7.17=h5bf99c6_8` 형식을 쓰면 된다 |
| `Channels` | 필수, 최소 1개 | conda channel (예: `bioconda`) |
| `PackageEngine` | 자동 적용 | `conda`(기본) 또는 `micromamba`. `Defaulted` 필드로, 마법사에서 프롬프트 없이 기본값이 자동 적용됨 — 변경하려면 `--field PackageEngine=micromamba` (non-interactive) 또는 `/change-method` 후 재입력 |

**`mirror`**

| 필드 | 필수 여부 | 설명 |
|---|---|---|
| `ImageRef` | 필수 | 기반 이미지, digest 포함 필요 |
| `MirrorUri` | 필수 | 내부 mirror URI |
| `Packages` | 필수, 최소 1개 | 설치할 패키지 |
| `MirrorKind` | 선택 | v1에서는 비워둬도 됨 |

**`source`**

| 필드 | 필수 여부 | 설명 |
|---|---|---|
| `ImageRef` | 필수 | 기반 이미지, digest 포함 필요 |
| `SourceUri` | 필수 | source archive/release URI |
| `SourceChecksum` | 필수 | `sha256:<64-hex>` 형식만 허용 |
| `SourceBuildCommands` | 필수, 최소 1개 | 빌드 명령 (예: `make`, `make install`) |
| `BuildDependencies` | 권장 (비워도 진행됨) | 빌드 의존성 목록 |

**`dockerfile`**

| 필드 | 필수 여부 | 설명 |
|---|---|---|
| `ImageRef` | 필수 | 기반 이미지 — Dockerfile의 첫 `FROM`과 정확히 같아야 함, digest 포함 필요 |
| `DockerfilePath` 또는 `DockerfileContent` | 필수 (둘 중 하나) | Dockerfile 경로 또는 내용 |
| `BuildContext` | 비워두면 자동 | 비어 있으면 현재 디렉터리(`.`) |

### 2-6. 패키지 빌드 문자열 선택 (ResolveRecipe)

`package` 또는 `mirror` 방식으로 만든 레시피에서, 모든 필드 입력을 마치면
**빌드 문자열 후보 선택** 화면이 자동으로 나온다. 이 단계는 NodeVault
`ResolveRecipe` API를 통해 패키지별 conda build string 후보를 받아 확정하는
과정이다.

**후보가 1개인 경우** — 자동 선택, 화면 출력 없이 넘어간다.

**후보가 여러 개인 경우** — 번호 목록이 나온다.

```
패키지 빌드 문자열 선택

bwa=0.7.17 에 대한 빌드 문자열 후보입니다.

  [1] bwa=0.7.17=h5bf99c6_8
      채널: bioconda
  [2] bwa=0.7.17=h7132678_8
      채널: conda-forge

번호를 선택하세요 [1-2] (Enter = 1번):
> (Enter)

bwa → bwa=0.7.17=h5bf99c6_8
```

Enter만 치면 1번(첫 번째 후보)이 선택된다. 선택된 full pin이 recipe.json에 저장된다.

**패키지를 찾지 못한 경우(`NotFound`)** — 폐쇄망 Harbor에 미리 등록이 필요하다는
안내가 나온다.

```
'bwa=0.7.17' 패키지를 Harbor에서 찾을 수 없습니다.
폐쇄망 환경에서는 Harbor에 패키지를 먼저 등록한 뒤 다시 시도하세요.
```

**지원하지 않는 경우(`Unsupported`)** — 실제 gRPC 클라이언트가 연결되기 전
(`GrpcResolveRecipeClient` Sprint R17)이므로, 기본 상태에서는 이 단계가 건너뛰어지고
입력한 패키지 문자열이 그대로 저장된다. 버전만 입력했으면(`bwa=0.7.17`) build string
없이 저장된다.

**UX 테스트 방법** — 실제 NodeVault 없이 후보 선택 UI를 확인하려면:

```bash
NODEKIT_RESOLVE_RECIPE_STUB=1 \
  dotnet run --project src/NodeKit.Cli -- recipe create /tmp/test.json
```

stub 모드는 각 패키지에 대해 `bioconda` 채널 1개 + `conda-forge` 채널 1개 후보를
자동 생성한다.

### 2-7. 포트 설정 (선택사항)

모든 필드 입력이 끝나면 이 도구가 받는 **입력 파일 유형**과 **출력 파일 유형**을
선택하는 화면이 자동으로 나온다. 파이프라인 DAG에서 이 tool과 다른 tool을
연결할 때 포트 호환성 검사에 쓰인다.

```
── 포트 설정 (선택사항) ────────────────────────────────────
이 도구가 받는 입력 파일 유형을 설정합니다.
나중에 ToolFunctionSpec으로 교체할 초안입니다.

  [1] FASTQ paired-end reads
      쌍을 이루는 FASTQ 시퀀싱 리드입니다.
      예: sample_R1.fastq.gz, sample_R2.fastq.gz
  [2] FASTQ single-end reads
      단일 FASTQ 시퀀싱 리드입니다.
      예: sample.fastq.gz
  [3] BAM alignment
      정렬된 BAM 파일입니다.
      예: sample.bam
  [4] FASTA reference
      참조 서열 FASTA 파일입니다.
      예: reference.fasta
  [5] VCF variants
      variant 호출 결과 VCF 파일입니다.
      예: sample.vcf.gz
  [6] 직접 입력

번호 입력 (쉼표 구분, 빈 줄 = 건너뛰기):
> 1,5
```

번호를 쉼표로 구분해 여러 개 선택할 수 있다. **빈 줄을 입력하면 건너뛴다.**
입력 포트 선택이 끝나면 같은 방식으로 출력 포트를 선택한다.

> 이 포트 정보는 ToolFunctionSpec이 완성되면 교체될 초안 데이터다.
> `recipe.json`에 `Inputs`/`Outputs` 배열로 저장되므로 텍스트 편집기로
> 직접 수정하거나 제거할 수 있다.

### 2-8. 저장 전 최종 확인 / 경로 확정

포트 설정까지 완료되면 지금까지 입력한 내용 전체를 요약해 보여주고 저장한다.
동작은 시작 시 경로를 지정했는지에 따라 달라진다.

**경로를 미리 지정한 경우** (`recipe create recipe.json`):

요약을 보여주고 `[Enter / y] 저장   [n] 처음부터 다시 작성` 프롬프트가 나온다.
Enter 또는 `y`를 입력하면 지정한 경로에 바로 저장된다.

```
── 저장 확인 ──────────────────────────────────────────
  도구 이름: bwa-mem
  도구 버전: 0.7.17
  기본 실행 명령: bwa mem
  기반 이미지: condaforge/miniforge3:24.3.0-0@sha256:...
  패키지: bwa=0.7.17=h5bf99c6_8
  입력 포트: reads

[Enter / y] 저장   [n] 처음부터 다시 작성
> (Enter)

저장되었습니다: recipe.json
```

**경로 없이 실행한 경우** (`recipe create`) — 또는 디렉터리만 지정한 경우:

요약 바로 아래에 저장 경로 입력 화면이 이어진다 (`[Enter / y]` 프롬프트 없음).
ToolName과 ToolVersion을 기반으로 기본 파일명이 제안된다.

```
── 저장 확인 ──────────────────────────────────────────
  도구 이름: bwa-mem
  도구 버전: 0.7.17
  기본 실행 명령: bwa mem
  ...

저장 위치를 확인하세요.

기본 경로: /home/user/bwa-mem-0.7.17.json
다른 경로를 입력하거나 Enter로 기본 경로를 사용 [n = 처음부터 다시 작성]:
── /cancel: 종료 ──
> (Enter)

저장되었습니다: /home/user/bwa-mem-0.7.17.json
```

- Enter → 기본 경로에 저장
- 다른 경로 입력 → 해당 경로에 저장 (파일이 이미 있으면 덮어쓰기 확인)
- `n` → 처음부터 다시 작성(입력한 값 초기화)

디렉터리를 지정한 경우(`recipe create /tmp/`)에는 해당 디렉터리가 기준이
되어 `bwa-mem-0.7.17.json`이 `/tmp/` 아래에 생성된다.

### 2-9. recovery — 마지막 검증 실패 시 수정

모든 필드를 채운 뒤 최종 검증을 한 번 더 돈다. 필드 하나씩 받을 때는
못 잡아내는 교차 필드 규칙(예: Dockerfile 첫 `FROM`과 `ImageRef` 불일치,
Output의 `Class` 허용값 위반) 때문에 여기서 막힐 수 있다. 입력값을
버리지 않고 **고칠 항목만 선택**해서 수정한다.

```
최종 검증에 실패했습니다. 다음 중 수정할 항목을 선택하세요:
  [1] 이미지 digest 입력하기 — 컨테이너 이미지가 나중에 바뀌지 않도록 @sha256:... digest가 필요합니다.
      힌트: Quay 또는 Harbor의 tag 상세 화면에서 sha256 digest를 복사하세요...
번호를 입력하세요 (취소하려면 빈 줄):
1
```

대표 recovery 문구:

| 상황 | 표시되는 action |
|---|---|
| 이미지 digest 누락/오류 | `이미지 digest 입력하기` |
| source checksum 누락/오류 | `소스 코드 검증값 입력하기` |
| 패키지 버전 미고정 | `패키지 버전 고정하기` |

다시 검증에 실패하면 같은 화면이 반복된다. 빈 줄을 입력하면 저장하지 않고
종료한다(종료 코드 1).

### 2-10. non-interactive 모드 (스크립트/CI용)

프롬프트가 전혀 나오지 않고 빠진 값이 있으면 즉시 에러로 끝난다.

```bash
nodekit recipe create /tmp/recipe.json \
  --non-interactive --method package \
  --field ToolName=bwa-mem \
  --field ToolVersion=0.7.17 \
  --field "Script=bwa mem" \
  --field "ImageRef=condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" \
  --field Packages=bwa=0.7.17=h5bf99c6_8 \
  --field Channels=bioconda
```

옵션 정리:

| 옵션 | 의미 |
|---|---|
| `--non-interactive` | 필수. 프롬프트 없이 한 번에 처리 |
| `--method <container\|package\|mirror\|source\|dockerfile>` | 필수. 사용자용 method 이름 (내부 build-kind 이름인 `conda`/`micromamba`/`source-build`/`dockerfile-fallback`은 받지 않는다) |
| `--field Name=Value` | scalar/choice/list 필드 하나씩 지정. **첫 번째 `=`만 구분자** — `Packages=bwa=0.7.17=h5bf99c6_8`처럼 value 안에 `=`가 있어도 그대로 보존된다 |
| `--field Name=Value` (반복) | 목록 필드(`Packages`, `Channels`, `Command`, `SourceBuildCommands`, `BuildDependencies`)는 같은 이름의 `--field`를 반복해 항목을 누적한다 |
| `--engine <conda\|micromamba>` | `--method package`에만 사용 가능 |
| `--accept-dockerfile-warning` | `--method dockerfile`을 non-interactive로 쓸 때 필수 (대화형의 경고 동의를 대신함) |

`Optional` 필드는 `--field`로 안 주면 비워진 채 진행된다. `Recommended`
필드(`BuildDependencies`)를 비우면 표준에러에 경고만 찍고 계속 진행한다.
필수 필드가 빠지거나 최종 검증에 실패하면 파일을 쓰지 않고 종료 코드 1을
반환한다.

### 2-11. 중간에 나가기 / review / method 변경

필드를 입력하는 중 값 대신 아래 명령을 쓸 수 있다.

| 명령 | 동작 |
|---|---|
| `/help` | 지금 필드의 라벨, 설명, 예시, 필수 여부를 다시 보여준다 |
| `/review` | 지금까지 입력한 값 전체를 요약해서 보여준다 |
| `/back` | **필드 입력 중**: 직전 필드로 돌아가 다시 입력한다. 첫 번째 필드에서 `/back`을 입력하면 모드 선택 화면으로 돌아간다. 초기 선택/쉬운 안내/빠른 설정 화면에서도 이전 주요 화면으로 이동한다 |
| `/change-method` | 공통 필드 값을 최대한 보존하면서 method 선택 화면으로 돌아간다 |
| `/cancel` | 저장하지 않고 종료한다 (종료 코드 130) |
| `/quit` | `/cancel`과 동일 |
| `/exit` | `/cancel`과 동일 |

`/back`은 필드 루프 안에서도 동작한다. 직전 필드의 값이 지워지고 해당 필드
프롬프트가 다시 나온다. 여러 번 `/back`을 입력하면 그만큼 앞 필드로 거슬러
올라갈 수 있다. 저장 전 확인(2-8절) 화면에서는 `n`을 입력하면 처음부터
다시 작성할 수 있다.

## 3. `nodekit validate <recipe.json>`

recipe를 검증만 한다. 파일을 만들지 않는다.

1. `RecipeValidator.Validate(recipe)` — recipe 레벨 완전성 검사
   (build kind별 필수 필드, source checksum 형식).
2. `RecipeRenderer.Render(recipe)` — `ToolDefinition`으로 변환.
3. 기존 L1 validator 체인 (`RequiredFieldsValidator`, `ImageUriValidator`,
   `DockerfileStructureValidator`, `PackageVersionValidator`) 실행.

위반이 없으면 `OK`를 표준출력에 찍고 종료 코드 0. 위반이 있으면 각 위반을
`<RuleId> (<Field>): <Message>` 형식으로 표준에러에 한 줄씩 찍고 종료 코드 1.

```bash
$ nodekit validate recipe.json
OK
$ echo $?
0
```

```bash
$ nodekit validate bad-recipe.json
L1-SRC-001 (SourceChecksum): source build kind에는 SourceChecksum이 필요합니다 — 체크섬 없이는 재현성을 보장할 수 없습니다.
$ echo $?
1
```

## 4. `nodekit render <recipe.json> --out <build-request.json>`

`validate`와 동일한 검증을 내부에서 먼저 수행한다 (fail-closed — 검증 안 된
정의는 절대 export하지 않는다). 통과하면 `BuildRequestFactory`로
`ToolDefinition → BuildRequest`를 매핑하고, legacy `BuildRequest` POCO 형태
그대로(PascalCase 필드명) indented JSON으로 `--out` 경로에 쓴다. 네트워크
호출 없음 — 파일만 쓴다.

`--out -`을 쓰면 파일 대신 표준출력에 JSON을 찍는다.

검증 실패 시 출력 파일을 만들지 않고 위반 목록을 표준에러에 찍은 뒤 종료
코드 1을 반환한다.

```bash
$ nodekit render recipe.json --out build-request.json
$ echo $?
0
$ cat build-request.json
{
  "RequestId": "...",
  "ToolDefinitionId": "...",
  "ToolName": "bwa",
  "Version": "0.7.17",
  "ImageUri": "registry.example.com/bwa:0.7.17@sha256:...",
  "DockerfileContent": "FROM registry.example.com/bwa:0.7.17@sha256:...\nRUN echo ok\n",
  "Script": "bwa mem",
  "Command": [],
  "EnvironmentSpec": "",
  "DisplayLabel": "",
  "DisplayDescription": "",
  "DisplayCategory": "",
  "DisplayTags": [],
  "CreatedAt": "2026-06-23T07:49:39Z"
}
```

```bash
$ nodekit render bad-recipe.json --out build-request.json
L1-SRC-001 (SourceChecksum): source build kind에는 SourceChecksum이 필요합니다 — 체크섬 없이는 재현성을 보장할 수 없습니다.
$ echo $?
1
$ ls build-request.json
ls: cannot access 'build-request.json': No such file or directory
```

## 5. 종료 코드

| 코드 | 의미 |
|---|---|
| 0 | 성공 (검증 통과, 또는 검증 통과 후 render/recipe create 완료) |
| 1 | recipe-level 또는 L1 검증 위반 1개 이상, 또는 recipe create 최종 검증 실패 |
| 2 | 사용법 오류, 인자 누락, 알 수 없는 옵션/필드, 파일을 읽을 수 없음, recipe JSON 파싱 실패 |

### 그 외

- `nodekit` 단독 실행, 또는 `validate`/`render`/`recipe`가 아닌 명령 → 사용법
  안내 출력, 종료 코드 2.
- `nodekit submit` 같은 명령은 존재하지 않는다 — 만들다 만 stub이 아니라
  의도적으로 빠져 있다. NodeVault로의 실제 전송은 이 CLI의 책임이 아니다.

## 6. `recipe.json`을 손으로 쓰거나 고칠 때

`recipe create`로 만든 파일을 텍스트 편집기로 직접 손보고 싶을 때를 위한
참고용 스키마다. `RecipeDocument`는 flat POCO다. JSON 키는 C# 속성명과
동일(대소문자 구분 안 함, `PropertyNameCaseInsensitive`).

### 공통 필드 (모든 build kind)

| 필드 | 타입 | 필수 여부 |
|---|---|---|
| `BuildKind` | string (enum) | 필수 — `Conda`/`Micromamba`/`BioContainer`/`SourceBuild`/`PackageMirror`/`DockerfileFallback` |
| `ToolName` | string | 필수 |
| `Version` | string | 필수 |
| `Script` | string | 필수 |
| `Command` | string[] | 선택, K8s 런타임 커맨드 오버라이드 |
| `DisplayLabel`/`DisplayDescription`/`DisplayCategory`/`DisplayTags` | string/string[] | 선택, UI 팔레트 표시용 |

> `recipe create` 마법사가 보여주는 `ImageRef`/`ToolVersion`은 authoring
> 단계의 이름이다. 실제로 저장되는 JSON 키는 위 표처럼 `BaseImage`(또는
> build kind별 이미지 필드)/`Version`이다.

### BuildKind별 추가 필드

| BuildKind | 추가 필드 |
|---|---|
| `Conda` / `Micromamba` | `BaseImage` (필수, digest pinned), `Channels`(선택), `Packages`(최소 1개) |
| `PackageMirror` | 위 + `PackageMirrorUri` (필수) |
| `BioContainer` | `BioContainerImageUri` (필수, digest pinned) — `BaseImage` 안 씀 |
| `SourceBuild` | `BaseImage` (필수), `SourceUri` (필수), `SourceChecksum` (필수, `sha256:<64-hex>` 형식), `SourceBuildCommands` (최소 1개) |
| `DockerfileFallback` | `BaseImage` (필수, Dockerfile의 첫 `FROM`과 정확히 같아야 함), `DockerfileContent` (필수) |

각 build kind에서 `BaseImage`/`BioContainerImageUri`는 그대로 렌더링된
`ToolDefinition.ImageUri`가 되고, 반드시 Dockerfile의 첫 번째 `FROM`
이미지와 동일해야 한다 (`L1-IMG-006`). 멀티스테이지 Dockerfile은 모든
`FROM`이 latest 태그 없이 digest로 고정되어야 한다 — builder stage라고
예외는 없다. 자세한 배경은
[`NODEKIT_IMAGEURI_SEMANTICS_REPORT.md`](NODEKIT_IMAGEURI_SEMANTICS_REPORT.md)
참고.

### 최소 동작 예시 (`DockerfileFallback`)

```json
{
  "BuildKind": "DockerfileFallback",
  "ToolName": "bwa",
  "Version": "0.7.17",
  "BaseImage": "registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
  "DockerfileContent": "FROM registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\nRUN echo ok\n",
  "Script": "bwa mem"
}
```

## 7. 베스트 프랙티스 따라하기

실제로 입력하며 따라갈 수 있는 완성 시나리오 두 가지다.
`>` 로 시작하는 줄이 직접 입력하는 값이다.

---

### 시나리오 A — 패쇄망: Harbor container recipe (bwa-mem2)

**전제 조건:**
- `harbor.lab.local`에 `harbor.lab.local/bioinformatics/bwa-mem2:2.2.1` 이미지가 존재한다
- Harbor CA cert와 admin 패스워드를 알고 있다
- 실제 이미지/프로젝트 경로는 자신의 Harbor에 맞게 바꿔 입력한다

**1. 환경변수 설정**

```bash
source ~/.config/infra-lab/harbor-secrets.env

export NODEKIT_HARBOR_URL=https://harbor.lab.local
export NODEKIT_HARBOR_CA_CERT=~/.config/infra-lab/certs/harbor-ca.crt
export NODEKIT_HARBOR_USER=admin
export NODEKIT_HARBOR_PASSWORD=$HARBOR_ADMIN_PASSWORD
```

**2. 마법사 실행**

```bash
dotnet run --project src/NodeKit.Cli -- recipe create
```

**3. 모드 선택 — 쉬운 안내 모드**

```
[1] 쉬운 안내 모드
[2] 빠른 설정 모드

> 1
```

**4. 도구 이름 입력**

```
도구 이름:
> bwa-mem2
```

**5. bioconda/BioContainers 확인 안내 + 찾은 내용 선택**

도구 이름을 입력하면 확인할 수 있는 URL이 표시되고, 찾은 것이 무엇인지 선택하는 화면이 나온다.

```
다음 위치에서 도구를 확인해보세요.

  bioconda 패키지:
    https://anaconda.org/bioconda/bwa-mem2

  BioContainers 이미지:
    https://quay.io/repository/biocontainers/bwa-mem2?tab=tags

bioconda 페이지에서 conda install 명령어를 찾았다면 package 방식으로 진행할 수 있습니다.
BioContainers 페이지에서 이미지 주소를 찾았다면 container 방식으로 진행할 수 있습니다.

'bwa-mem2' 도구를 설치하거나 실행하는 예시를 본 적 있나요?

[1] conda install 또는 micromamba install 예시를 봤다
[2] docker run 또는 컨테이너 이미지 주소를 봤다
[3] GitHub 또는 source archive 주소를 봤다
[4] Dockerfile을 받았다
[5] 회사/학교 내부 저장소에서 설치해야 한다
[6] 아무것도 모른다

선택:
> 2
```

**6. 컨테이너 이미지 주소 입력**

`[2]`를 선택하면 컨테이너 이미지 입력 화면으로 전환된다.
Harbor가 설정된 경우 digest를 자동으로 조회한다.

```
컨테이너 이미지 주소를 입력해 주세요.

예:
  quay.io/biocontainers/bwa:0.7.17--h7132678_9@sha256:...
  ghcr.io/example/tool:1.0.0@sha256:...

이미지 주소:
> harbor.lab.local/bioinformatics/bwa-mem2:2.2.1
```

Harbor가 digest를 응답하면:

```
이미지 digest를 확인했습니다.

  sha256:xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

이 digest를 사용할까요? [Y/n]
> (Enter)
```

> digest를 확인할 수 없거나 Harbor에 연결되지 않으면 수동 입력으로 넘어간다.
> Harbor UI에서 이미지 → 태그를 클릭하면 `sha256:...` digest를 복사할 수 있다.

**7. 이후 필드 입력**

쉬운 안내 모드에서 도구 이름(`bwa-mem2`)을 입력했으므로 `ToolName`이 자동으로
채워진다. 컨테이너 이미지 흐름에서 `ImageRef`와 `ImageDigest`도 자동으로
설정된다. 필드 루프에서는 나머지 항목만 입력한다.

```
[1 / 6]

도구 버전 — 도구 버전 또는 고정된 release/version입니다.
   예: 2.2.1

── /back: 이전 필드   /cancel: 종료   /review: 현재 값   /change-method: 작성 방식 변경 ──
> 2.2.1

[2 / 6]

기본 실행 명령 — 도구 실행 시 사용할 기본 명령 또는 이미지 안의 스크립트 경로입니다. 현재 legacy BuildRequest 호환을 위해 필요합니다.
   예: bwa mem, /app/run.sh

── /back: 이전 필드   /cancel: 종료   /review: 현재 값   /change-method: 작성 방식 변경 ──
> bwa-mem2 mem

[3 / 6]

실행 명령 — (선택) 기본 entrypoint를 바꾸지 않으면 그냥 Enter

── /back: 이전 필드   /cancel: 종료   /review: 현재 값   /change-method: 작성 방식 변경 ──
> (Enter)
```

> `[1/6]`은 진행도 분자(이번이 몇 번째 프롬프트인지)이고 분모(6)는 container
> method 전체 필드 수다. pre-filled 필드는 분모에 포함되지만 프롬프트가 나오지
> 않으므로 자연스럽게 건너뛰어진다.

**9. 저장 확인 및 경로 입력**

경로 없이 시작했으므로 요약 바로 아래에 경로 입력이 이어진다 (`[Enter / y]` 없음).

```
── 저장 확인 ──────────────────────────────────────────
  도구 이름: bwa-mem2
  도구 버전: 2.2.1
  기본 실행 명령: bwa-mem2 mem
  기반 이미지: harbor.lab.local/bioinformatics/bwa-mem2:2.2.1@sha256:...

저장 위치를 확인하세요.

기본 경로: /home/user/bwa-mem2-2.2.1.json
다른 경로를 입력하거나 Enter로 기본 경로를 사용 [n = 처음부터 다시 작성]:
── /cancel: 종료 ──
> (Enter)

저장되었습니다: /home/user/bwa-mem2-2.2.1.json
```

**10. 검증**

```bash
dotnet run --project src/NodeKit.Cli -- validate /home/user/bwa-mem2-2.2.1.json
```

```
검증을 통과했습니다.
```

---

### 시나리오 B — 공개망: bioconda package recipe (bwa)

bioconda에 패키지가 있는 경우의 빠른 설정 모드 흐름이다.

**사전 준비 — 두 가지 정보를 미리 구한다**

package method에서 입력해야 하는 이미지는 **bwa 이미지가 아니다.**
conda가 설치된 빌드 환경 이미지(condaforge/miniforge3 등)의 digest가 필요하다.

**① 패키지 문자열** — anaconda.org Files 탭에서 확인

```
https://anaconda.org/bioconda/bwa
```

패키지 문자열은 **버전만 입력하면 된다** — build string은 저장 직전에 나오는
후보 선택 화면(2-6절)에서 결정한다.

```
bwa=0.7.17     ← 권장. 마법사 실행 중 ResolveRecipe 단계에서 build string 선택
```

build string까지 직접 고정하려면 `bwa=0.7.17=h5bf99c6_8` 형식을 써도 된다.
이 경우 Files 탭에서 대상 platform(`linux-64`)의 파일명을 확인한다.

```
linux-64/bwa-0.7.17-h5bf99c6_8.tar.bz2
         ^^^  ^^^^^^  ^^^^^^^^^^
         이름  버전    build string
```

**② base image digest** — 커맨드로 가져오기

miniforge3 이미지 digest를 가져오는 가장 간단한 방법:

```bash
# skopeo가 있는 경우 (권장)
skopeo inspect docker://condaforge/miniforge3:24.3.0-0 | python3 -m json.tool | grep Digest
# → "Digest": "sha256:xxxxxxxx..."

# docker가 있는 경우
docker pull condaforge/miniforge3:24.3.0-0
docker inspect condaforge/miniforge3:24.3.0-0 \
  --format='{{index .RepoDigests 0}}'
# → condaforge/miniforge3@sha256:xxxxxxxx...

# 아무것도 없을 때 — curl + Docker Hub API
TOKEN=$(curl -sf \
  "https://auth.docker.io/token?service=registry.docker.io&scope=repository:condaforge/miniforge3:pull" \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['token'])")
curl -sI \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/vnd.docker.distribution.manifest.v2+json" \
  "https://registry-1.docker.io/v2/condaforge/miniforge3/manifests/24.3.0-0" \
  | grep -i docker-content-digest
# → docker-content-digest: sha256:xxxxxxxx...
```

digest를 구했으면 아래 형식으로 조합해 놓는다:

```
condaforge/miniforge3:24.3.0-0@sha256:<위에서 구한 64자 hex>
```

**실행**

```bash
dotnet run --project src/NodeKit.Cli -- recipe create
```

```
> 2    ← 빠른 설정 모드

n      ← 내부망 아님
n      ← 내부 package mirror 없음
n      ← 기존 컨테이너 이미지 없음
y      ← public channel에 패키지 있음 (bioconda)
n      ← source build 불필요
n      ← Dockerfile 없음

(Enter) ← 추천 method(package) 수락
```

채널 확인 다음에 **base image 선택** 화면이 나온다(`NODEKIT_HARBOR_URL` 설정 시):

```
── Base image 선택 ─────────────────────────────────────────
  [1] condaforge/miniforge3:24.3.0-0
  [2] mambaorg/micromamba:1.5.8
  [0] 직접 입력

번호를 선택하세요 (1–2, 0 = 직접 입력):
> 1

condaforge/miniforge3:24.3.0-0 의 digest를 조회합니다...
설정 완료: condaforge/miniforge3:24.3.0-0@sha256:abcdef...
```

환경변수가 없으면 이 화면이 나타나지 않고 필드 입력 단계에서 직접 입력한다.

필드 입력:

```
[1 / 7] 도구 이름
> bwa

[2 / 7] 도구 버전
> 0.7.17

[3 / 7] 기본 실행 명령
> bwa mem

[4 / 7] 기반 이미지 — (base image 선택에서 자동 설정된 경우 건너뜀)
> condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef...

[5 / 7] 패키지 목록
패키지 문자열 (완료하려면 빈 줄):
> bwa=0.7.17
> (Enter)

[6 / 7] 채널 목록
채널 (완료하려면 빈 줄):
> bioconda
> conda-forge
> defaults
> (Enter)
```

PackageEngine(conda/micromamba 선택)은 `Defaulted` 필드이므로 `NextField()`가 반환하지 않는다. 빈 줄로 Channels를 완료하면 세션이 바로 종료 판정되어 **빌드 문자열 선택** 화면으로 넘어간다.

```
패키지 빌드 문자열 선택

bwa=0.7.17 에 대한 빌드 문자열 후보입니다.

  [1] bwa=0.7.17=h5bf99c6_8
      채널: bioconda
  [2] bwa=0.7.17=h7132678_8
      채널: conda-forge

번호를 선택하세요 [1-2] (Enter = 1번):
> 1

bwa → bwa=0.7.17=h5bf99c6_8
```

> 위 출력은 `NODEKIT_RESOLVE_RECIPE_STUB=1` 환경변수를 켠 UX 테스트 결과다.
> stub 없이 실행하면 (`GrpcResolveRecipeClient` Sprint R17 전) 이 화면이 나오지 않고
> 입력한 `bwa=0.7.17`이 그대로 저장된다.

**포트 설정 (선택사항):**

```
── 포트 설정 (선택사항) ────────────────────────────────────
이 도구가 받는 입력 파일 유형을 설정합니다.

  [1] FASTQ paired-end reads
      쌍을 이루는 FASTQ 시퀀싱 리드입니다.
      예: sample_R1.fastq.gz, sample_R2.fastq.gz
  ...

번호 입력 (쉼표 구분, 빈 줄 = 건너뛰기):
> 1

이 도구가 만드는 출력 파일 유형을 설정합니다.

  [1] FASTQ paired-end reads
  [2] FASTQ single-end reads
  [3] BAM alignment
  ...

번호 입력 (쉼표 구분, 빈 줄 = 건너뛰기):
> 3
```

**저장 전 최종 확인 및 경로 입력:**

경로 없이 시작했으므로 요약 바로 아래에 경로 입력이 이어진다 (`[Enter / y]` 없음).

```
── 저장 확인 ──────────────────────────────────────────
  도구 이름: bwa
  도구 버전: 0.7.17
  기본 실행 명령: bwa mem
  기반 이미지: condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef...
  패키지: bwa=0.7.17=h5bf99c6_8
  입력 포트: reads

저장 위치를 확인하세요.

기본 경로: /home/user/bwa-0.7.17.json
다른 경로를 입력하거나 Enter로 기본 경로를 사용 [n = 처음부터 다시 작성]:
── /cancel: 종료 ──
> (Enter)

저장되었습니다: /home/user/bwa-0.7.17.json
```

```bash
dotnet run --project src/NodeKit.Cli -- validate /home/user/bwa-0.7.17.json
```

---

### 시나리오 C — 쉬운 안내 모드: 설치 명령으로 시작 (samtools)

bioconda에서 설치하는 방법만 알고 있는 경우의 흐름이다.
설치 명령을 붙여 넣으면 Packages/Channels/PackageEngine이 자동으로 채워지고,
나머지 항목만 직접 입력하면 된다.

**사전 준비 — base image digest만 있으면 된다**

시나리오 B의 `condaforge/miniforge3` digest 획득 방법과 동일하다.
이미 구해 놓은 경우 그대로 사용한다.

```
condaforge/miniforge3:24.3.0-0@sha256:<64자 hex>
```

**실행**

```bash
dotnet run --project src/NodeKit.Cli -- recipe create
```

경로는 마지막 단계에서 ToolName/ToolVersion을 기반으로 제안된다.
시작 전에 파일명을 알 수 없으므로 경로를 미리 지정하지 않는다.

**1. 모드 선택 — 쉬운 안내 모드**

```
[1] 쉬운 안내 모드
[2] 빠른 설정 모드

> 1
```

**2. 무엇을 알고 있나요?**

```
쉬운 안내 모드

정확히 몰라도 괜찮습니다.
알고 있는 것만 선택하세요.

무엇을 알고 있나요?

[1] 도구 이름만 알고 있다
[2] 설치 명령을 알고 있다     예: conda install -c bioconda bwa=0.7.17
[3] 컨테이너 이미지 주소를 알고 있다
[4] GitHub 또는 소스코드 주소를 알고 있다
[5] Dockerfile을 가지고 있다
[6] 회사/학교 내부 저장소를 써야 한다
[7] 잘 모르겠다

선택:
> 2
```

**3. 설치 명령 입력**

```
설치 명령을 입력해 주세요.

예:
  conda install -c bioconda bwa=0.7.17
  micromamba install -c bioconda samtools=1.20

설치 명령:
> conda install -c bioconda samtools=1.17
```

**4. 파싱 결과 확인**

버전만 있고 build string이 없으면 `주의:` 경고가 함께 나온다.
(build string은 저장 전 ResolveRecipe 단계에서 선택한다.)

```
설치 명령을 이해했습니다.

이해한 값:
  PackageEngine: conda
  Channels:
    - bioconda
  Packages:
    - samtools=1.17

주의:
  samtools=1.17: build string이 고정되어 있지 않습니다.

선택:
[1] 이해한 값을 사용하고 부족한 값을 직접 입력한다
[2] 설치 명령을 다시 입력한다
[3] 다른 작성 방식을 선택한다
[4] 취소한다

선택:
> 1
```

**5. 채널 확인**

패키지 채널 목록을 확인한다. 이상 없으면 Enter.

```
채널 확인

  채널: 1개 항목

[Enter / y] 이 채널 사용   [n] 채널 다시 입력:
> (Enter)
```

**6. 필드 입력**

install command에서 자동 채워진 항목: **Packages, Channels, PackageEngine** (3개)

직접 입력이 필요한 항목: **ToolName, ToolVersion, 실행 명령, 기반 이미지** (4개)

> **기반 이미지(ImageRef)는 install command에서 알 수 없어 항상 직접 입력해야 한다.**
> install 명령에는 `samtools=1.17` 패키지 정보만 있고, 어떤 conda base 이미지를
> 쓸지는 포함되지 않기 때문이다.

```
[1 / 7]

도구 이름 — recipe에서 식별할 도구 이름입니다.
   예: samtools, bwa-mem2, gatk4

── /back: 이전 필드   /cancel: 종료   /review: 현재 값   /change-method: 작성 방식 변경 ──
> samtools

[2 / 7]

도구 버전 — 도구 버전 또는 고정된 release/version입니다.
   예: 1.17, 2.2.1

── /back: 이전 필드   /cancel: 종료   /review: 현재 값   /change-method: 작성 방식 변경 ──
> 1.17

[3 / 7]

기본 실행 명령 — 도구 실행 시 사용할 기본 명령 또는 이미지 안의 스크립트 경로입니다. 현재 legacy BuildRequest 호환을 위해 필요합니다.
   예: bwa mem, /app/run.sh

── /back: 이전 필드   /cancel: 종료   /review: 현재 값   /change-method: 작성 방식 변경 ──
> samtools view

[4 / 7]

기반 이미지 — 이 빌드의 기반이 되는 컨테이너 이미지입니다. 별도의 digest 필드가 없으므로 이 값 자체에 @sha256:... digest를 포함해야 최종 검증을 통과합니다.
   예: condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef

── /back: 이전 필드   /cancel: 종료   /review: 현재 값   /change-method: 작성 방식 변경 ──
> condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef...
```

Packages/Channels/PackageEngine은 자동 채워졌으므로 `[5/7]`, `[6/7]`, `[7/7]`은
건너뛴다.

**6. 포트 설정 (선택사항)**

```
── 포트 설정 (선택사항) ────────────────────────────────────
이 도구가 받는 입력 파일 유형을 설정합니다.

번호 입력 (쉼표 구분, 빈 줄 = 건너뛰기):
> (Enter)     ← 건너뛰기

이 도구가 만드는 출력 파일 유형을 설정합니다.

번호 입력 (쉼표 구분, 빈 줄 = 건너뛰기):
> (Enter)     ← 건너뛰기
```

**7. 저장 확인 및 경로 입력**

경로 없이 시작했으므로 요약 바로 아래에 경로 입력이 이어진다 (`[Enter / y]` 없음).

```
── 저장 확인 ──────────────────────────────────────────
  도구 이름: samtools
  도구 버전: 1.17
  기본 실행 명령: samtools view
  기반 이미지: condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef...
  패키지: samtools=1.17

저장 위치를 확인하세요.

기본 경로: /home/user/samtools-1.17.json
다른 경로를 입력하거나 Enter로 기본 경로를 사용 [n = 처음부터 다시 작성]:
── /cancel: 종료 ──
> (Enter)

저장되었습니다: /home/user/samtools-1.17.json
```

생성된 파일에는 Packages, Channels, PackageEngine이 이미 채워져 있다.

```bash
dotnet run --project src/NodeKit.Cli -- validate /home/user/samtools-1.17.json
```

```
검증을 통과했습니다.
```

> **쉬운 안내 모드로 시작하는 이유:**
> `[2] 설치 명령을 알고 있다`를 선택하면 채널과 패키지를 명령에서 자동으로
> 추출한다. bioconda 페이지에서 복사한 `conda install` 명령을 그대로 붙여 넣으면
> 된다. 버전이 없는 명령(`conda install samtools`)을 입력하면 경고가 표시되고
> 버전 고정 후 계속할지 묻는다.

---

### 공통 팁

| 상황 | 입력 |
|---|---|
| 방금 입력한 값이 틀렸다 | `/back` → 이전 필드로 돌아감 |
| 지금까지 입력한 값을 보고 싶다 | `/review` |
| method를 바꾸고 싶다 | `/change-method` |
| 처음부터 다시 시작 | 첫 번째 필드에서 `/back` → 모드 선택 화면 |
| 그냥 나가기 | `/cancel` (파일 저장 안 됨, 종료 코드 130) |

> **digest 없이 저장하려 하면 막힌다.** validate에서 `L1-IMG-003` 또는
> `L1-IMG-006`이 나오면 `imageRef`에 `@sha256:` 부분이 빠진 것이다.
> `/review`로 현재 값을 확인하고, 해당 필드에서 `/back`으로 돌아가서 수정한다.

## 8. 범위 / 제한사항

- gRPC 전송, NodeVault 조회, 이미지 레지스트리 push, 로컬 docker/buildah/buildkit
  실행 — 전부 이 CLI의 범위 밖이다.
- `ToolSpecRequest`/`ResolveToolSpec`/`SubmitToolBuild` 계열은 구현하지
  않는다 (NodeVault Phase 1/2 게이트가 아직 열리지 않음 — CLAUDE.md 0절).
- **`ResolveRecipe` 클라이언트 인터페이스(`IResolveRecipeClient`)는 구현 완료**,
  UX 테스트용 stub(`NODEKIT_RESOLVE_RECIPE_STUB=1`)도 동작한다.
  실제 gRPC 클라이언트(`GrpcResolveRecipeClient`)는 NodeVault proto에
  `ResolveRecipe` RPC가 추가된 후 Sprint R17에서 연결한다. 그 전까지는
  빌드 문자열 선택 화면이 나오지 않고 입력한 버전 문자열이 그대로 저장된다.
- 5개 method가 생성하는 Dockerfile은 NodeKit L1 정적 검증만 통과했을 뿐,
  실제 `docker build`로 검증된 적은 없다.
- `recipe create`의 escape hatch는 `/help`, `/review`, `/change-method`,
  `/back`, `/cancel`, `/quit`, `/exit`이다. `/cancel`/`/quit`/`/exit`는 시작 화면,
  쉬운 안내 모드, 빠른 설정 질문, 필드 입력, recovery 화면, 빌드 문자열 선택 화면에서
  사용할 수 있다. `/back`은 필드 입력 중 이전 필드로 돌아가거나, 첫 번째 필드에서
  입력하면 모드 선택 화면으로 돌아간다. draft 저장/resume은 범위 밖이다.
- base image digest 자동 조회는 두 경로가 있다: (1) `NODEKIT_HARBOR_URL` 환경변수가
  설정된 경우 내부 Harbor에서 조회하거나, (2) `NODEKIT_BASE_IMAGE_STUB=1`로 고정
  digest를 반환한다. `PublicRegistryImageDigestResolver`(Docker Hub / quay.io)는
  구현은 완료되었으나 현재 비활성화 상태다 — 오픈망 환경의 자동 조회는
  Harbor 우선 사용을 권장한다.
