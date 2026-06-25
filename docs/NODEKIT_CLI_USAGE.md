# NodeKit CLI 사용 가이드

`src/NodeKit.Cli/`에 구현된 `nodekit` CLI 사용법이다. 처음 써보는 사람이 이
문서만 보고 recipe 하나를 끝까지 만들 수 있도록, 명령어 → 예시 → 막혔을 때
어떻게 하는지 순서로 적었다.

이 CLI는 **legacy `BuildRequest` 경로만** 다룬다 — `RecipeDocument →
RecipeValidator → RecipeRenderer → ToolDefinition → 기존 L1 validator 체인 →
legacy `BuildRequest` JSON`. gRPC 전송, NodeVault 조회, 이미지 빌드,
`submit`/`build` 명령은 이 CLI에 없다 (CLAUDE.md 1절, NodeKit 책임 경계).

명령 설계 배경은 [`NODEKIT_CLI_RECIPE_SPEC_DRAFT.md`](NODEKIT_CLI_RECIPE_SPEC_DRAFT.md)
§5/§6, recipe create 마법사의 전체 UX 설계는
[`NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md`](NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md)
참고.

## 0. 빠른 시작

recipe.json을 손으로 쓰지 않아도 된다. 이게 핵심이다.

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

## 1. 빌드/실행

```bash
# 빌드
dotnet build src/NodeKit.Cli/NodeKit.Cli.csproj

# 실행 방법 1: dotnet run
dotnet run --project src/NodeKit.Cli -- validate recipe.json

# 실행 방법 2: 빌드된 바이너리 직접 실행
./src/NodeKit.Cli/bin/Debug/net10.0/NodeKit.Cli validate recipe.json
```

`NodeKit.Cli.csproj`는 NuGet 패키지를 전혀 참조하지 않는다 — `NodeKit.csproj`의
Avalonia/Grpc.Net.Client/Google.Protobuf/Wasmtime/ReactiveUI 의존성을 전혀
가져오지 않는다. 빌드 결과물(`bin/Debug/net10.0/`)에는
`NodeKit.Cli.{dll,pdb,deps.json,runtimeconfig.json}`만 있다.

## 2. `nodekit recipe create` — recipe 마법사

가장 많이 쓸 명령이다. 질문에 답하기만 하면 reproducibility 규칙(CLAUDE.md
3절: `latest` 태그 금지, digest 고정, 패키지 버전 고정)을 어기지 않는
`recipe.json`을 만들어 준다.

```bash
nodekit recipe create <recipe.json> [--method ...] [--non-interactive ...]
```

옵션 없이 실행하면 **대화형 모드**로 들어간다. 모든 옵션을 미리 지정하면
**non-interactive 모드**(스크립트/CI용)로 한 번에 만들 수도 있다. 아래 2-1은
대화형, 2-5는 non-interactive를 다룬다.

### 2-1. 1단계 — 방법 추천 질문 (Q&A)

가장 먼저 6개의 예/아니오 질문에 답한다. `y`/`n`/모르면 그냥 Enter(`u`로
처리됨).

| 질문 | 의미 |
|---|---|
| 내부망/폐쇄망 환경인가요? | public 인터넷에서 패키지/이미지를 받을 수 없는지 |
| 내부 package mirror URI를 아시나요? | 내부 conda/pip mirror가 있는지 |
| 기존 컨테이너 이미지 URI가 있나요? | 이미 쓸만한 이미지(BioContainer 등)가 있는지 |
| public channel에 패키지가 있나요? | conda-forge/bioconda 같은 곳에 패키지가 있는지 |
| source URL과 checksum이 있나요? | 소스를 직접 받아 빌드할 수 있는지 |
| 기존 Dockerfile이 있나요? | 이미 작성된 Dockerfile이 있는지 |

답을 마치면 5가지 방법(method) 중 하나를 추천해 준다:

| Method | 의미 | 준비물 |
|---|---|---|
| `container` | 기존 컨테이너 이미지 사용 | digest로 고정된 이미지 URI |
| `package` | conda/micromamba로 패키지 설치 | public channel에 있는 패키지 |
| `mirror` | 내부 package mirror에서 설치 | 내부 mirror URI |
| `source` | 소스코드로 직접 빌드 | SourceUri + SourceChecksum(sha256) |
| `dockerfile` | Dockerfile 직접 작성 | Dockerfile 경로 또는 내용 (최후의 수단) |

추천을 그대로 쓰려면 **Enter**, 다른 방법을 쓰려면 화면에 보이는 번호를
입력한다. `dockerfile`을 고르면 "재현성을 스스로 책임져야 한다"는 강한 경고가
한 번 더 뜬다 — `y`로 동의해야 진행된다.

### 2-2. 2단계 — 필드 채우기

방법이 정해지면 그 방법에 필요한 필드를 하나씩 물어본다. 화면에 매번
**라벨 — 설명**이 같이 나오므로 무슨 값을 넣어야 하는지 바로 알 수 있다.

방법별 필드는 다음과 같다 (공통 필드는 모든 방법에 있음):

**공통 필드**

| 필드 | 필수 여부 | 설명 |
|---|---|---|
| `ToolName` | 필수 | recipe가 식별할 도구 이름 (예: `bwa-mem`) |
| `ToolVersion` | 필수 | 도구 버전 (예: `0.7.17`) |
| `Script` | 필수 | 실행 스크립트 경로/명령 (예: `run.sh`) |
| `Inputs` | 필수, 최소 1개 | 입력 정의 목록 (2-3절) |
| `Outputs` | 필수, 최소 1개 | 출력 정의 목록 (2-3절) |

**`container`**

| 필드 | 필수 여부 | 설명 |
|---|---|---|
| `ImageRef` | 필수 | 이미지 참조 (tag만으로도 일단 진행 가능, 예: `condaforge/miniforge3:24.3.0-0`) |
| `ImageDigest` | 필수 | digest 고정 (예: `sha256:...`) — 비어 있으면 최종 검증에서 막힘 |
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
| `ImageRef` | 필수 | 기반 이미지 — Dockerfile의 첫 `FROM`과 정확히 같아야 함 |
| `DockerfilePath` 또는 `DockerfileContent` | 필수 (둘 중 하나) | Dockerfile 경로 또는 내용 |
| `BuildContext` | 비워두면 자동 | 비어 있으면 현재 디렉터리(`.`) |

각 필드를 입력할 때 잘못된 값을 넣으면(예: 버전 핀 없는 패키지) 바로
이유와 함께 다시 물어본다 — 끝까지 가서야 막히지 않는다.

#### 막혔을 때 쓰는 명령 (escape hatch)

필드를 입력하는 중에는 값 대신 아래 두 명령을 쓸 수 있다:

- `/help` — 지금 필드의 라벨, 설명, 예시, 필수/선택 여부를 다시 보여주고
  같은 필드를 다시 물어본다.
- `/change-method` — 지금까지 입력한 값을 버리지 않고 1단계(Q&A)로 돌아가
  다른 방법을 고른다.

### 2-3. Inputs/Outputs 입력하기

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

### 2-4. 마지막에 검증 실패하면 — 수정(recovery) 화면

모든 필드를 채운 뒤 내부적으로 최종 검증을 한 번 더 돈다. 필드 하나씩
받을 때는 못 잡아내는 교차 필드 규칙(예: Dockerfile의 첫 `FROM`과 `ImageRef`가
일치해야 함, Output의 `Class`가 허용된 값인지) 때문에 여기서 막힐 수 있다.
이 경우 입력했던 값을 모두 버리지 않고 **고칠 항목만 선택**해서 수정한다.

```
최종 검증에 실패했습니다. 다음 중 수정할 항목을 선택하세요:
  [1] ImageRef, ImageDigest 항목 함께 수정 — ...
      힌트: ...
번호를 입력하세요 (취소하려면 빈 줄):
1
```

`Inputs`/`Outputs`를 고치는 경우에는 기존 항목을 보여주고 `e<번호>`(수정),
`d<번호>`(삭제), 빈 줄(계속/추가)로 다룬다:

```
현재 Outputs 항목:
  [0] bam (alignment/bam, class=bogus)
e0
이름 (비우면 유지): bam
  [1] BAM alignment output
  ...
프리셋 번호 또는 'custom':
1
```

여기서도 다시 검증에 실패하면 같은 화면이 또 나온다 — 빈 줄을 입력하면
저장하지 않고 종료한다(종료 코드 1).

### 2-5. non-interactive 모드 (스크립트/CI용)

같은 일을 한 줄로 끝내고 싶을 때 쓴다. 질문/프롬프트가 전혀 나오지 않고,
빠진 값이 있으면 즉시 에러로 끝난다.

```bash
nodekit recipe create recipe.json \
  --non-interactive --method package \
  --field ToolName=bwa-mem --field ToolVersion=0.7.17 --field Script=run.sh \
  --field ImageRef=condaforge/miniforge3:24.3.0-0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef \
  --field Packages=bwa=0.7.17=h5bf99c6_8 --field Channels=bioconda \
  --input reads=1 \
  --output aligned=1
```

옵션 정리:

| 옵션 | 의미 |
|---|---|
| `--non-interactive` | 필수. 프롬프트 없이 한 번에 처리 |
| `--method <container\|package\|mirror\|source\|dockerfile>` | 필수. 사용자용 method 이름 (내부 build-kind 이름인 `conda`/`micromamba`/`source-build`/`dockerfile-fallback`은 받지 않는다) |
| `--field Name=Value` | scalar/choice/list 필드 하나씩 지정. 목록 필드(`Packages`, `Channels`, `Command`, `SourceBuildCommands`, `BuildDependencies`)는 같은 `--field`를 여러 번 줘서 항목을 누적할 수 있다 |
| `--input Name=Spec` | Inputs 항목 하나. `Spec`은 프리셋 id(`fastq-paired` 등) 또는 `custom,role,format,shape[,optional]` |
| `--output Name=Spec` | Outputs 항목 하나. `Spec`은 프리셋 id(`bam-primary` 등) 또는 `custom,role,format,class` |
| `--engine <conda\|micromamba>` | `--method package`에만 사용 가능 |
| `--accept-dockerfile-warning` | `--method dockerfile`을 non-interactive로 쓸 때 필수 (대화형의 경고 동의를 대신함) |

값을 비워도 되는 `Optional` 필드는 `--field`로 안 주면 자동으로 비워진
채 진행된다. `Recommended` 필드(`BuildDependencies`)를 비우면 표준에러에
경고만 찍고 계속 진행한다. 필수 필드가 빠졌거나 최종 검증에 실패하면
파일을 쓰지 않고 종료 코드 1을 반환한다.

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

## 7. 범위 / 제한사항

- gRPC 전송, NodeVault 조회, 이미지 레지스트리 push, 로컬 docker/buildah/buildkit
  실행 — 전부 이 CLI의 범위 밖이다.
- `ToolSpecRequest`/`ResolveToolSpec`/`SubmitToolBuild` 계열은 구현하지
  않는다 (NodeVault Phase 1/2 게이트가 아직 열리지 않음 — CLAUDE.md 0절).
- 5개 method가 생성하는 Dockerfile은 NodeKit L1 정적 검증만 통과했을 뿐,
  실제 `docker build`로 검증된 적은 없다.
- `recipe create`의 escape hatch는 `/help`, `/change-method` 두 가지만
  구현되어 있다. 설계 문서에 이름만 나오는 `/review`/`/cancel`/`/skip`은
  아직 없다 — Ctrl+C로 중단하거나 끝까지 진행 후 recovery 화면에서 취소
  (빈 줄)하는 것으로 대신한다.
