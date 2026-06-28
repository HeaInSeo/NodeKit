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
nodekit recipe create <recipe.json> [--method ...] [--non-interactive ...]
```

옵션 없이 실행하면 **대화형 모드**로 들어간다. `--non-interactive`와 함께
`--method`/`--field` 등을 모두 지정하면 프롬프트 없이 한 번에 만든다(2-8절).

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

```
쉬운 안내 모드

정확히 몰라도 괜찮습니다.
알고 있는 것만 선택하세요.

무엇을 알고 있나요?

[1] 도구 이름만 알고 있다
[2] 설치 명령을 알고 있다     예: conda install -c bioconda bwa=0.7.17
[3] 컨테이너 이미지 주소를 알고 있다   예: quay.io/biocontainers/bwa:0.7.17...
[4] GitHub 또는 소스코드 주소를 알고 있다
[5] Dockerfile을 가지고 있다
[6] 회사/학교 내부 저장소를 써야 한다
[7] 잘 모르겠다

선택:
```

| 선택 | 연결되는 method |
|---|---|
| `[1]` 도구 이름만 | 추가 질문으로 method 결정 |
| `[2]` 설치 명령 | `package` (conda/micromamba) |
| `[3]` 컨테이너 이미지 | `container` |
| `[4]` GitHub/소스 주소 | `source` |
| `[5]` Dockerfile | `dockerfile` |
| `[6]` 내부 저장소 | `mirror` |
| `[7]` 잘 모르겠다 | 도구 이름 lookup, 설치 명령, 이미지 주소, 소스 주소, Dockerfile, 종료 중 선택 |

**도구 이름만 아는 경우:** `[1]`을 선택하고 `bwa` 같은 도구 이름을 입력하면
외부 API 호출 없이 확인 URL만 보여준다.

```text
bioconda 패키지:
  https://anaconda.org/bioconda/bwa

BioContainers 이미지:
  https://quay.io/repository/biocontainers/bwa?tab=tags
```

bioconda 페이지에서 `conda install` 명령어를 찾으면 package 방식으로,
BioContainers tag 페이지에서 이미지 주소를 찾으면 container 방식으로 이어가면 된다.

**컨테이너 이미지 — digest 필수 처리:** `[3]`을 선택하면 이미지 주소를
입력받는다. `NODEKIT_HARBOR_URL` 환경변수가 설정되어 있으면 내부 Harbor에서
digest를 자동으로 조회한다. 환경변수가 없거나 다른 registry이면 registry에서
직접 복사해 입력해야 한다.

**Harbor 자동 digest 조회 (패쇄망 환경):**

```bash
export NODEKIT_HARBOR_URL=https://harbor.lab.local
export NODEKIT_HARBOR_CA_CERT=~/.config/infra-lab/certs/harbor-ca.crt  # 자체 서명 CA
export NODEKIT_HARBOR_USER=admin
export NODEKIT_HARBOR_PASSWORD=<password>  # harbor-secrets.env 참조
```

설정 후 이미지 주소를 tag까지만 입력하면 (`harbor.lab.local/project/bwa:0.7.17`)
Harbor OCI Distribution API에서 `Docker-Content-Digest` 헤더를 읽어 확인을 물어본다.

```
이미지 digest를 확인했습니다.

  sha256:0123456789abcdef...

이 digest를 사용할까요? [Y/n]
```

다른 registry 이미지(`quay.io/...`, `ghcr.io/...`)이거나 `NODEKIT_HARBOR_URL`이
설정되지 않은 경우에는 기존과 같이 수동 입력으로 폴백된다.

```
입력한 이미지 주소에는 digest가 없습니다.
NodeKit은 재현성을 위해 digest 고정을 요구합니다.

[1] digest가 포함된 이미지 주소를 다시 입력한다
[2] ImageDigest를 따로 입력한다
[3] 다른 작성 방식으로 바꾼다
[4] 취소한다
```

digest가 이미지 주소에 포함된 경우(`repo:tag@sha256:...`)에는 이 프롬프트가
나오지 않고 바로 다음 단계로 진행된다.

**source build — checksum 필수 처리:** `[4]`를 선택하면 `SourceChecksum` 입력 전에
계산 방법을 보여준다. CLI가 `curl`을 직접 실행하지는 않는다.

```text
curl -fsSL "<SourceUri>" | sha256sum
```

checksum이 비어 있으면 계산 방법을 다시 보거나, 직접 입력하거나, 다른 작성
방식으로 바꾸거나, 저장하지 않고 종료할 수 있다. checksum 없이 진행하는
경로는 없다.

**Dockerfile — 기본값 N 경고:** `[5]`를 선택하면 재현성 경고가 나온다.
Enter 또는 `n`을 입력하면 진행되지 않는다. `y`를 입력해야 계속된다.

```
Dockerfile 방식은 가장 자유롭지만 재현성 책임이 가장 큽니다.
처음 사용하는 경우 package 또는 container 방식을 먼저 고려하세요.

계속하시겠습니까? [y/N]:
```

**아무것도 모름 — 회복 경로:** `[7]`을 선택하면 바로 종료하지 않고 다음
선택지를 보여준다.

```text
[1] 도구 이름으로 bioconda/BioContainers 확인 방법을 본다
[2] 설치 명령을 입력한다
[3] 컨테이너 이미지 주소를 입력한다
[4] 소스코드 주소를 입력한다
[5] Dockerfile 경로를 입력한다
[6] 저장하지 않고 종료한다
```

`[6]`을 선택하면 recipe를 저장하지 않고 종료 코드 0으로 끝난다.
언제든 명령 입력 위치에서 `/cancel`, `/quit`, `/exit`를 입력하면 종료 코드
130으로 취소된다.

### 2-3. 빠른 설정 모드

6개의 예/아니오 질문에 답하면 방법(method)을 추천해 준다. `y`/`n`/모르면
그냥 Enter(`u`로 처리됨).

| 질문 | 의미 |
|---|---|
| 내부망/폐쇄망 환경인가요? | public 인터넷에서 패키지/이미지를 받을 수 없는지 |
| 내부 package mirror URI를 아시나요? | 내부 conda/pip mirror가 있는지 |
| 기존 컨테이너 이미지 URI가 있나요? | 이미 쓸만한 이미지(BioContainer 등)가 있는지 |
| public channel에 패키지가 있나요? | conda-forge/bioconda 같은 곳에 패키지가 있는지 |
| source URL과 checksum이 있나요? | 소스를 직접 받아 빌드할 수 있는지 |
| 기존 Dockerfile이 있나요? | 이미 작성된 Dockerfile이 있는지 |

답을 마치면 5가지 method 중 하나를 추천해 준다. 추천을 그대로 쓰려면
**Enter**, 다른 method를 고르려면 화면에 보이는 번호를 입력한다.

| Method | 의미 | 준비물 |
|---|---|---|
| `container` | 기존 컨테이너 이미지 사용 | digest로 고정된 이미지 URI |
| `package` | conda/micromamba로 패키지 설치 | public channel에 있는 패키지 |
| `mirror` | 내부 package mirror에서 설치 | 내부 mirror URI |
| `source` | 소스코드로 직접 빌드 | SourceUri + SourceChecksum(sha256) |
| `dockerfile` | Dockerfile 직접 작성 | Dockerfile 경로 또는 내용 (최후의 수단) |

`dockerfile`을 고르면 재현성 경고가 한 번 더 나온다 — `y`로 동의해야 진행된다.

### 2-4. 공통 필드 입력

method가 정해지면 공통 필드를 먼저 물어본다. 화면마다 **라벨 — 설명**이
같이 나온다.

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
| `Packages` | 필수, 최소 1개 | 설치할 패키지 (예: `bwa=0.7.17=h5bf99c6_8` — 버전+빌드 문자열까지 고정) |
| `Channels` | 필수, 최소 1개 | conda channel (예: `bioconda`) |
| `PackageEngine` | 비워두면 자동 | `conda`(기본) 또는 `micromamba` |

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

### 2-6. Inputs/Outputs 입력

`Inputs`/`Outputs`는 프리셋을 고르거나 직접 입력(`custom`)할 수 있다.

```
입력 항목을 추가하세요 (빈 줄 입력 시 종료)
이름:
reads
  [1] FASTQ paired-end reads
  [2] FASTQ single-end reads
  [3] BAM alignment
  [4] FASTA reference
  [5] VCF variants
  [6] 직접 입력
프리셋 번호 또는 'custom':
1
```

번호를 고르면 Role/Format/Shape(또는 Class)가 프리셋 값으로 자동 채워진다.
`custom`을 고르면 Role/Format/Shape(Outputs는 Class)를 직접 입력한다.
이름을 빈 줄로 두면 목록 입력이 끝난다(필수 목록은 최소 1개가 있어야
끝낼 수 있다).

### 2-7. recovery — 마지막 검증 실패 시 수정

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

`Inputs`/`Outputs`를 고치는 경우에는 기존 항목을 보여주고 `e<번호>`(수정),
`d<번호>`(삭제), 빈 줄(계속/추가)로 다룬다. 다시 검증에 실패하면 같은
화면이 반복된다. 빈 줄을 입력하면 저장하지 않고 종료한다(종료 코드 1).

### 2-8. non-interactive 모드 (스크립트/CI용)

프롬프트가 전혀 나오지 않고 빠진 값이 있으면 즉시 에러로 끝난다.

```bash
nodekit recipe create recipe.json \
  --non-interactive --method package \
  --field ToolName=bwa-mem \
  --field ToolVersion=0.7.17 \
  --field "Script=bwa mem" \
  --field "ImageRef=condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" \
  --field Packages=bwa=0.7.17=h5bf99c6_8 \
  --field Channels=bioconda \
  --input reads=fastq-paired \
  --output bam=bam-primary
```

옵션 정리:

| 옵션 | 의미 |
|---|---|
| `--non-interactive` | 필수. 프롬프트 없이 한 번에 처리 |
| `--method <container\|package\|mirror\|source\|dockerfile>` | 필수. 사용자용 method 이름 (내부 build-kind 이름인 `conda`/`micromamba`/`source-build`/`dockerfile-fallback`은 받지 않는다) |
| `--field Name=Value` | scalar/choice/list 필드 하나씩 지정. **첫 번째 `=`만 구분자** — `Packages=bwa=0.7.17=h5bf99c6_8`처럼 value 안에 `=`가 있어도 그대로 보존된다 |
| `--field Name=Value` (반복) | 목록 필드(`Packages`, `Channels`, `Command`, `SourceBuildCommands`, `BuildDependencies`)는 같은 이름의 `--field`를 반복해 항목을 누적한다 |
| `--input Name=Spec` | Inputs 항목 하나. `Spec`은 프리셋 id(`fastq-paired` 등) 또는 `custom,role,format,shape[,optional]` |
| `--output Name=Spec` | Outputs 항목 하나. `Spec`은 프리셋 id(`bam-primary` 등) 또는 `custom,role,format,class` |
| `--engine <conda\|micromamba>` | `--method package`에만 사용 가능 |
| `--accept-dockerfile-warning` | `--method dockerfile`을 non-interactive로 쓸 때 필수 (대화형의 경고 동의를 대신함) |

`Optional` 필드는 `--field`로 안 주면 비워진 채 진행된다. `Recommended`
필드(`BuildDependencies`)를 비우면 표준에러에 경고만 찍고 계속 진행한다.
필수 필드가 빠지거나 최종 검증에 실패하면 파일을 쓰지 않고 종료 코드 1을
반환한다.

### 2-9. 중간에 나가기 / review / method 변경

필드를 입력하는 중 값 대신 아래 명령을 쓸 수 있다.

| 명령 | 동작 |
|---|---|
| `/help` | 지금 필드의 라벨, 설명, 예시, 필수 여부를 다시 보여준다 |
| `/review` | 지금까지 입력한 값 전체를 요약해서 보여준다 |
| `/back` | 초기 선택, 쉬운 안내, 빠른 설정 화면 사이에서 이전 주요 화면으로 돌아간다 |
| `/change-method` | 공통 필드 값을 최대한 보존하면서 method 선택 화면으로 돌아간다 |
| `/cancel` | 저장하지 않고 종료한다 (종료 코드 130) |
| `/quit` | `/cancel`과 동일 |
| `/exit` | `/cancel`과 동일 |

`/back`은 v1.0에서 주요 초기 화면 이동까지만 지원한다. 필드 루프는 아직
단방향이므로 직전 필드 값을 되감지는 않는다. 필드 입력 중 이전 값을 고치려면
`/review`로 현재 값을 확인하거나, `/change-method` 또는 최종 recovery 화면(2-7절)에서
수정한다.

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
  "Inputs": [ { "Name": "reads", "Role": "sample-fastq", "Format": "fastq", "Shape": "pair", "Required": true } ],
  "Outputs": [ { "Name": "aligned", "Role": "aligned-bam", "Format": "bam", "Shape": "single", "Class": "primary" } ],
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
| `Inputs` | `[{ Name, Role, Format, Shape }]` | 최소 1개, `Shape`는 `single`\|`pair` |
| `Outputs` | `[{ Name, Role, Format, Shape, Class }]` | 최소 1개, `Class`는 `primary`\|`secondary` |
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
  "Script": "bwa mem",
  "Inputs": [ { "Name": "reads", "Role": "sample-fastq", "Format": "fastq", "Shape": "pair" } ],
  "Outputs": [ { "Name": "aligned", "Role": "aligned-bam", "Format": "bam", "Shape": "single", "Class": "primary" } ]
}
```

## 8. 베스트 프랙티스 따라하기

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
dotnet run --project src/NodeKit.Cli -- recipe create /tmp/bwa-mem2.json
```

**3. 모드 선택 — 쉬운 안내 모드**

```
[1] 쉬운 안내 모드
[2] 빠른 설정 모드

> 1
```

**4. 도구 이름 입력**

```
도구 이름을 입력하세요.

> bwa-mem2
```

**5. 도구를 이미 알고 있음 선택**

```
bwa-mem2 에 대해 얼마나 알고 계신가요?
[1] 이름만 안다
[2] 설치 방법을 안다 (conda 패키지명, 컨테이너 이미지 등)
[3] 아무것도 모른다

> 2
```

**6. 컨테이너 이미지 경로 선택**

```
bwa-mem2 의 배포 방식을 선택하세요.
[1] 설치 명령 (conda install / pip install 등)
[2] 컨테이너 이미지
[3] 소스코드 빌드
[4] Dockerfile
[5] 저장하지 않고 종료

> 2
```

**7. 이미지 주소 입력 — tag까지만 입력**

```
컨테이너 이미지 주소를 입력해 주세요.

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

> digest를 확인할 수 없거나 Harbor에 연결되지 않으면 "자동 조회를 사용할 수 없습니다"
> 메시지가 나오고 수동 입력으로 넘어간다. 이 경우 Harbor UI에서 이미지 → 태그를
> 클릭하면 `sha256:...` digest를 복사할 수 있다.

**8. 이후 필드 입력 (빠른 설정 모드로 method 선택 후 진행됨)**

method가 `container`로 확정되면 공통/method 필드를 순서대로 입력한다.

```
[1 / 8]
도구 이름 — recipe에서 식별할 도구 이름입니다.
> bwa-mem2

[2 / 8]
도구 버전 — 도구 버전 또는 고정된 release/version입니다.
> 2.2.1

[3 / 8]
기본 실행 명령 — 도구 실행 시 사용할 기본 명령 또는 이미지 안의 스크립트 경로입니다.
> bwa-mem2 mem

[4 / 8]
이미지 참조 — ...
> harbor.lab.local/bioinformatics/bwa-mem2:2.2.1

[5 / 8]
이미지 digest — ...
> sha256:xxxxxxxx...

[6 / 8]
실행 명령 — (선택) 기본 entrypoint를 바꾸지 않으면 그냥 Enter
> (Enter)
```

**9. Inputs 입력**

```
[7 / 8]
입력 — 최소 1개 이상의 입력 정의가 필요합니다.

입력 이름:
> reads

  [1] FASTQ paired-end reads
      쌍을 이루는 FASTQ 시퀀싱 리드입니다.
      예: sample_R1.fastq.gz, sample_R2.fastq.gz
  [2] FASTQ single-end reads
  ...
  [7] 직접 입력

프리셋 번호 또는 'custom':
> 1

입력 이름 (완료하려면 빈 줄):
> (Enter)
```

**10. Outputs 입력**

```
[8 / 8]
출력 — 최소 1개 이상의 출력 정의가 필요합니다.

출력 이름:
> aligned

  [1] BAM — primary alignment
  ...

프리셋 번호 또는 'custom':
> 1

출력 이름 (완료하려면 빈 줄):
> (Enter)
```

**11. 저장 확인**

```
저장되었습니다: /tmp/bwa-mem2.json
```

**12. 검증**

```bash
dotnet run --project src/NodeKit.Cli -- validate /tmp/bwa-mem2.json
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

Overview 화면의 설치 명령에는 build string이 빠져 있는 경우가 많다.
재현성을 위해 build string까지 고정하려면 **Files 탭**을 열고 대상 platform
(보통 `linux-64`)의 파일명을 확인한다.

```
linux-64/bwa-0.7.17-h5bf99c6_8.tar.bz2
         ^^^  ^^^^^^  ^^^^^^^^^^
         이름  버전    build string
```

conda 패키지 파일명은 `<name>-<version>-<build>.tar.bz2` 형식이므로,
위 파일명은 다음 package spec으로 입력한다.

```
bwa=0.7.17=h5bf99c6_8
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
dotnet run --project src/NodeKit.Cli -- recipe create /tmp/bwa.json
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

필드 입력:

```
[1 / 7] 도구 이름
> bwa

[2 / 7] 도구 버전
> 0.7.17

[3 / 7] 기본 실행 명령
> bwa mem

[4 / 7] 기반 이미지 — digest 포함 필요
> condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef...

[5 / 7] 패키지 목록
패키지 문자열 (완료하려면 빈 줄):
> bwa=0.7.17=h5bf99c6_8
> (Enter)

[6 / 7] 채널 목록
채널 (완료하려면 빈 줄):
> bioconda
> conda-forge
> defaults
> (Enter)

[7 / 7] 패키지 엔진 — conda / micromamba 중 선택. 기본값: conda
> (Enter)
```

그 다음 Inputs / Outputs를 시나리오 A와 동일하게 입력한다.

```bash
dotnet run --project src/NodeKit.Cli -- validate /tmp/bwa.json
```

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

## 7. 범위 / 제한사항

- gRPC 전송, NodeVault 조회, 이미지 레지스트리 push, 로컬 docker/buildah/buildkit
  실행 — 전부 이 CLI의 범위 밖이다.
- `ToolSpecRequest`/`ResolveToolSpec`/`SubmitToolBuild` 계열은 구현하지
  않는다 (NodeVault Phase 1/2 게이트가 아직 열리지 않음 — CLAUDE.md 0절).
- 5개 method가 생성하는 Dockerfile은 NodeKit L1 정적 검증만 통과했을 뿐,
  실제 `docker build`로 검증된 적은 없다.
- `recipe create`의 escape hatch는 `/help`, `/review`, `/change-method`,
  `/back`, `/cancel`, `/quit`, `/exit`이다. `/cancel`/`/quit`/`/exit`는 시작 화면,
  쉬운 안내 모드, 빠른 설정 질문, 필드 입력, recovery 화면에서 사용할 수 있다.
  `/back`은 필드 입력 중 이전 필드로 돌아가거나, 첫 번째 필드에서 입력하면
  모드 선택 화면으로 돌아간다. draft 저장/resume은 범위 밖이다.
- digest 자동 조회는 `NODEKIT_HARBOR_URL` 환경변수가 설정된 경우 내부 Harbor에
  한해 동작한다. 공개 registry(quay.io, ghcr.io 등) 자동 조회는 범위 밖이다.
