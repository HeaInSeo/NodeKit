# NodeKit CLI 사용 가이드

`src/NodeKit.Cli/`에 구현된 `nodekit` CLI 사용법이다. 이 CLI는 **legacy
`BuildRequest` 경로만** 다룬다 — `RecipeDocument → RecipeValidator →
RecipeRenderer → ToolDefinition → 기존 L1 validator 체인 → legacy
`BuildRequest` JSON`. gRPC 전송, NodeVault 조회, 이미지 빌드, `submit`/`build`
명령은 이 CLI에 없다 (CLAUDE.md 1절, NodeKit 책임 경계).

명령 설계 배경은 [`NODEKIT_CLI_RECIPE_SPEC_DRAFT.md`](NODEKIT_CLI_RECIPE_SPEC_DRAFT.md)
§5/§6 참고.

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

## 2. 명령어

### `nodekit validate <recipe.json>`

recipe를 검증만 한다. 파일을 만들지 않는다.

1. `RecipeValidator.Validate(recipe)` — recipe 레벨 완전성 검사
   (variant별 필수 필드, source checksum 형식).
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
L1-SRC-001 (SourceChecksum): source build variant에는 SourceChecksum이 필요합니다 — 체크섬 없이는 재현성을 보장할 수 없습니다.
$ echo $?
1
```

### `nodekit render <recipe.json> --out <build-request.json>`

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
L1-SRC-001 (SourceChecksum): source build variant에는 SourceChecksum이 필요합니다 — 체크섬 없이는 재현성을 보장할 수 없습니다.
$ echo $?
1
$ ls build-request.json
ls: cannot access 'build-request.json': No such file or directory
```

### 종료 코드

| 코드 | 의미 |
|---|---|
| 0 | 성공 (검증 통과, 또는 검증 통과 후 render 완료) |
| 1 | recipe-level 또는 L1 검증 위반 1개 이상 |
| 2 | 사용법 오류, 인자 누락, 파일을 읽을 수 없음, recipe JSON 파싱 실패 |

### 그 외

- `nodekit` 단독 실행, 또는 `validate`/`render`가 아닌 명령 → 사용법 안내
  출력, 종료 코드 2.
- `nodekit submit` 같은 명령은 존재하지 않는다 — 만들다 만 stub이 아니라
  의도적으로 빠져 있다. NodeVault로의 실제 전송은 이 CLI의 책임이 아니다.

## 3. `recipe.json` 작성법

`RecipeDocument`는 flat POCO다. JSON 키는 C# 속성명과 동일(대소문자 구분
안 함, `PropertyNameCaseInsensitive`). `Variant`는 문자열로 쓴다 (예:
`"Conda"`, `"DockerfileFallback"`).

### 공통 필드 (모든 variant)

| 필드 | 타입 | 필수 여부 |
|---|---|---|
| `Variant` | string (enum) | 필수 — `Conda`/`Micromamba`/`BioContainer`/`SourceBuild`/`PackageMirror`/`DockerfileFallback` |
| `ToolName` | string | 필수 |
| `Version` | string | 필수 |
| `Script` | string | 필수 |
| `Inputs` | `[{ Name, Role, Format, Shape }]` | 최소 1개, `Shape`는 `single`\|`pair` |
| `Outputs` | `[{ Name, Role, Format, Shape, Class }]` | 최소 1개, `Class`는 `primary`\|`secondary` |
| `Command` | string[] | 선택, K8s 런타임 커맨드 오버라이드 |
| `DisplayLabel`/`DisplayDescription`/`DisplayCategory`/`DisplayTags` | string/string[] | 선택, UI 팔레트 표시용 |

### Variant별 추가 필드

| Variant | 추가 필드 |
|---|---|
| `Conda` / `Micromamba` | `BaseImage` (필수, digest pinned), `Channels`(선택), `Packages`(최소 1개) |
| `PackageMirror` | 위 + `PackageMirrorUri` (필수) |
| `BioContainer` | `BioContainerImageUri` (필수, digest pinned) — `BaseImage` 안 씀 |
| `SourceBuild` | `BaseImage` (필수), `SourceUri` (필수), `SourceChecksum` (필수, `sha256:<64-hex>` 형식), `SourceBuildCommands` (최소 1개) |
| `DockerfileFallback` | `BaseImage` (필수, Dockerfile의 첫 `FROM`과 정확히 같아야 함), `DockerfileContent` (필수) |

각 variant에서 `BaseImage`/`BioContainerImageUri`는 그대로 렌더링된
`ToolDefinition.ImageUri`가 되고, 반드시 Dockerfile의 첫 번째 `FROM`
이미지와 동일해야 한다 (`L1-IMG-006`). 멀티스테이지 Dockerfile은 모든
`FROM`이 latest 태그 없이 digest로 고정되어야 한다 — builder stage라고
예외는 없다. 자세한 배경은
[`NODEKIT_IMAGEURI_SEMANTICS_REPORT.md`](NODEKIT_IMAGEURI_SEMANTICS_REPORT.md)
참고.

### 최소 동작 예시 (`DockerfileFallback`)

```json
{
  "Variant": "DockerfileFallback",
  "ToolName": "bwa",
  "Version": "0.7.17",
  "BaseImage": "registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
  "DockerfileContent": "FROM registry.example.com/bwa:0.7.17@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\nRUN echo ok\n",
  "Script": "bwa mem",
  "Inputs": [ { "Name": "reads", "Role": "sample-fastq", "Format": "fastq", "Shape": "pair" } ],
  "Outputs": [ { "Name": "aligned", "Role": "aligned-bam", "Format": "bam", "Shape": "single", "Class": "primary" } ]
}
```

## 4. 범위 / 제한사항

- gRPC 전송, NodeVault 조회, 이미지 레지스트리 push, 로컬 docker/buildah/buildkit
  실행 — 전부 이 CLI의 범위 밖이다.
- `ToolSpecRequest`/`ResolveToolSpec`/`SubmitToolBuild` 계열은 구현하지
  않는다 (NodeVault Phase 1/2 게이트가 아직 열리지 않음 — CLAUDE.md 0절).
- 6개 variant가 생성하는 Dockerfile은 NodeKit L1 정적 검증만 통과했을 뿐,
  실제 `docker build`로 검증된 적은 없다.
