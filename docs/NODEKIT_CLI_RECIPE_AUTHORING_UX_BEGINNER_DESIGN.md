# NodeKit CLI Recipe Authoring UX — Beginner-Oriented Design v0.8

> Status: design draft, freeze-ready candidate.
> This document defines the intended NodeKit CLI recipe authoring UX and the reusable authoring core architecture for CLI, future GUI, and future MCP front ends.
> Nothing in this document is assumed to be implemented yet.
> Terminology in this document already applies the build-kind patch — see [`NODEKIT_CLI_RECIPE_AUTHORING_UX_TERMINOLOGY_PATCH.md`](NODEKIT_CLI_RECIPE_AUTHORING_UX_TERMINOLOGY_PATCH.md). "variant"/`RecipeVariant` is not used anywhere below; the internal concept is `RecipeBuildKind`.
> v0.8 patch: `ImageDigest` is corrected from `Recommended` to `Required`. CLAUDE.md §3 blocks any image reference without a pinned `@sha256:` digest at L1 with no exceptions; an authoring-level `Recommended` tier cannot relax that. Authoring-level field requirement and final L1 policy are two different layers — see §5.6, §9.3, §19.3, §34.

---

## 0. 목적

NodeKit의 `recipe.json`은 도구 이미지를 재현 가능하게 만들기 위한 선언 문서다. 그러나 초보자는 JSON 구조, package channel, image digest, source checksum, input/output role, format, shape 같은 개념을 처음부터 알기 어렵다.

따라서 NodeKit CLI authoring 기능의 목표는 단순히 JSON 필드를 순서대로 물어보는 것이 아니다.

목표는 다음과 같다.

```text
초보자가 자신의 상황을 말한다
→ NodeKit이 가능한 recipe 작성 방법을 추천한다
→ 추천 이유와 대안을 설명한다
→ 선택된 방법에 필요한 필드만 안내한다
→ 입력/출력은 preset 중심으로 작성한다
→ 검증 실패 시 작성 session을 유지하고 복구 경로를 제시한다
→ 검증에 성공한 경우에만 recipe.json을 쓴다
```

v0.7은 v0.6에서 거의 안정화된 구조를 유지하면서, 구현 직전에 모호할 수 있는 다음 계약을 닫는다.

```text
1. invalidated field는 blocking 상태가 아니라 warning 상태다.
2. invalidated field는 사용자가 재확인하거나 수정하면 해제된다.
3. default 적용 책임은 Build() 하나로 단일화한다.
4. ValidateDraft는 default를 실제 적용하지 않고 "적용 예정"으로만 표시한다.
5. Final validation은 Build() 이후 RecipeBuildKindResolver를 실행한다.
6. Inputs/Outputs는 공통 scalar field가 아니라 terminal required authoring section이다.
7. ValidateDraft는 L1/renderer/cross-field 검증을 실행하지 않는다.
8. MirrorKind는 v1에서 Optional로 둔다.
9. Image tag-only warning은 non-interactive 수락 flag를 요구하지 않는다.
```

---

## 1. 설계 범위

### 1.1 포함

```text
recipe.json 작성 wizard
작성 방법 추천
필드별 입력 session
입력/출력 preset
custom role/format/channel normalize
최종 검증 전 review
최종 검증 실패 후 recovery
비대화형 recipe 생성
향후 GUI/MCP 재사용 가능한 core 설계
```

### 1.2 제외

```text
실제 이미지 빌드
실제 Docker build
실제 image pull
실제 BioContainer 검색
실제 Bioconda package 검색
실제 source 다운로드
실제 checksum 계산
NodeVault submit
Kubernetes Job 생성
NodeSentinel dry-run 실행
GUI 구현
MCP server 구현
draft resume
```

NodeKit CLI authoring은 "이미지를 지금 만드는 도구"가 아니라 "이미지를 만들기 위한 recipe를 안전하게 작성하는 도구"다.

---

## 2. 핵심 원칙

### 2.1 Unknown is not false

초보자는 자주 "모르겠음"이라고 답한다.

```text
BioContainer가 있는지 모르겠음
Bioconda 패키지가 있는지 모르겠음
source checksum이 뭔지 모르겠음
내부 mirror URI를 모름
```

이 답은 `No`가 아니다.

NodeKit recommender는 다음 원칙을 따른다.

```text
Yes     = 해당 방법을 추천할 수 있는 근거
No      = 해당 방법을 배제할 수 있는 근거
Unknown = 추천 근거는 아니지만 배제 근거도 아님
```

예:

```text
HasExistingContainerImage == Unknown
```

처리:

```text
BioContainer를 배제하지 않는다.
다만 현재 정보만으로 BioContainer를 1순위 추천하지 않는다.
대안 또는 확인 항목으로 남긴다.
```

---

### 2.2 내부망은 최상위 gate다

내부망/폐쇄망 환경에서는 public Bioconda, conda-forge, Docker registry, GitHub release, 외부 base image에 접근하지 못할 수 있다.

따라서 내부망 여부는 단순 추천 조건이 아니라 최상위 gate로 처리한다.

원칙:

```text
IsRestrictedNetwork == Yes이면
public package channel, 외부 container registry, 외부 source URL, 외부 base image를 사용하는 방법에는 강한 경고를 붙인다.
```

내부망 gate는 container, package, source, dockerfile 추천보다 먼저 평가된다.

---

### 2.3 사용자-facing method와 내부 RecipeBuildKind는 다르다

사용자가 고르는 것은 method다.

```text
container
package
mirror
source
dockerfile
```

내부 렌더링/빌드 모델은 `RecipeBuildKind`를 사용할 수 있다.

```text
BioContainer
Conda
Micromamba
PackageMirror
SourceBuild
DockerfileFallback
```

이 둘은 1:1이 아니다.

특히:

```text
method: package
→ RecipeBuildKind.Conda 또는 RecipeBuildKind.Micromamba
```

따라서 authoring session은 `RecipeBuildKind`가 아니라 `RecipeMethodId`를 선택해야 한다.

---

### 2.4 package engine은 defaulted field다

초보자에게는 "패키지로 설치하기"만 보여준다.

내부적으로는 다음 engine이 가능하다.

```text
conda
micromamba
```

이 값은 session 밖에서 강제로 확정되는 것이 아니라 `PackageEngine` field로 관리한다.

기본값:

```text
PackageEngine = conda
```

중요한 의미론:

```text
PackageEngine은 Required가 아니다.
PackageEngine은 Defaulted field다.
값이 없으면 Build() 단계에서 conda 기본값으로 자동 충족된다.
```

고급 사용자는 CLI에서 다음처럼 지정할 수 있다.

```bash
nodekit recipe create recipe.json --method package --engine micromamba
```

CLI가 제공한 `--engine` 값도 session 내부에서는 `PackageEngine` field로 적용된다.

---

### 2.5 입력/출력은 preset 우선이다

초보자에게 다음 필드를 바로 묻지 않는다.

```text
role
format
shape
class
```

대신 의미 중심 선택지를 제공한다.

```text
FASTQ paired-end reads
FASTQ single-end reads
BAM alignment
FASTA reference
VCF variants
BAM index output
Log file
Metrics file
```

preset을 선택하면 내부 recipe 필드가 채워진다.

---

### 2.6 Fail-closed는 final recipe output에만 적용한다

최종 검증에 실패하면 잘못된 `recipe.json`은 쓰지 않는다.

하지만 사용자의 작성 session은 버리지 않는다.

```text
fail-closed applies to final recipe output,
not to the authoring session.
```

즉:

```text
검증 실패
→ recipe.json은 쓰지 않음
→ session 유지
→ 관련 필드 또는 관련 섹션 수정
→ 재검증
→ 성공 시 저장
```

---

### 2.7 RecipeFieldCatalog는 field 의미론의 단일 출처다

interactive wizard와 non-interactive mode는 반드시 같은 field catalog를 사용한다.

금지:

```csharp
if (method == "source" && checksum is null) fail;
if (method == "package" && packages.Count == 0) fail;
if (engine is null) fail;
```

권장:

```csharp
var fields = RecipeFieldCatalog.FieldsFor(method);
```

원칙:

```text
RecipeFieldCatalog is the single source of truth for:
- field order
- field requirement
- default value
- recommended warnings
- choices
- quick validation
```

---

### 2.8 UI 상호작용 흔적은 recipe에 넣지 않는다

경고를 봤는지, 사용자가 `y`를 눌렀는지 같은 authoring 행위는 recipe의 속성이 아니다.

따라서 다음 값은 recipe field가 아니다.

```text
DockerfileWarningsAccepted
ImageTagWarningAccepted
```

이 값들은 `RecipeAuthoringSession` metadata로만 둔다.

---

## 3. 핵심 타입 개요

v0.7의 핵심 타입은 다음과 같다.

```text
RecipeMethodId
RecipeMethodDescriptor
RecipeMethodCatalog
RecipeMethodQuestionCatalog
RecipeMethodRecommender
RecipeFieldDescriptor
RecipeFieldRequirement
RecipeFieldCatalog
InputOutputPresetCatalog
RecipeAuthoringSession
RecipeAuthoringSnapshot
RecipeBuildKindResolver
RecipeValidationPipeline
RecipeValidationRecoveryPlan
```

역할:

| 타입                             | 역할                                                    |
| ------------------------------ | ----------------------------------------------------- |
| `RecipeMethodId`               | 사용자-facing 작성 방법                                      |
| `RecipeMethodDescriptor`       | method 설명, 준비물, 경고, 대안                                |
| `RecipeMethodCatalog`          | method 목록 제공                                          |
| `RecipeMethodQuestionCatalog`  | 추천 질문 순서의 단일 출처                                       |
| `RecipeMethodRecommender`      | 답변을 바탕으로 method 추천                                    |
| `RecipeFieldDescriptor`        | 필드 설명, requirement, default, choice, quick validation |
| `RecipeFieldRequirement`       | Required / Defaulted / Optional / Recommended 구분      |
| `RecipeFieldCatalog`           | method별 field 목록과 field 의미론의 단일 출처                    |
| `InputOutputPresetCatalog`     | 입력/출력 preset 제공                                       |
| `RecipeAuthoringSession`       | I/O 없는 authoring state machine                        |
| `RecipeAuthoringSnapshot`      | incomplete 상태도 표현 가능한 review/debug snapshot           |
| `RecipeBuildKindResolver`      | method + field 값으로 내부 RecipeBuildKind 결정              |
| `RecipeValidationPipeline`     | validate/render/create 공유 final 검증                    |
| `RecipeValidationRecoveryPlan` | 검증 실패 후 수정 경로 생성                                      |

---

## 4. 사용자-facing method와 내부 build kind

### 4.1 RecipeMethodId

```csharp
internal enum RecipeMethodId
{
    Container,
    Package,
    Mirror,
    Source,
    Dockerfile
}
```

### 4.2 초보자-facing method

CLI 기본 화면에서는 다음 5가지만 보여준다.

```text
1. 기존 컨테이너 이미지 사용
2. 패키지로 설치하기
3. 내부 패키지 미러에서 설치하기
4. 소스코드로 직접 빌드하기
5. Dockerfile 직접 작성하기
```

### 4.3 내부 build kind 매핑

| CLI method   | 내부 RecipeBuildKind      | 설명                   |
| ------------ | ----------------------- | -------------------- |
| `container`  | `BioContainer`          | 이미 존재하는 컨테이너 이미지 사용  |
| `package`    | `Conda` 또는 `Micromamba` | 패키지 기반 설치            |
| `mirror`     | `PackageMirror`         | 내부 mirror 기반 설치      |
| `source`     | `SourceBuild`           | source archive 직접 빌드 |
| `dockerfile` | `DockerfileFallback`    | Dockerfile 직접 작성     |

### 4.4 RecipeBuildKindResolver

authoring session은 method를 선택한다.
내부 build kind는 final validation 또는 render 직전에 resolve한다.

```csharp
internal static class RecipeBuildKindResolver
{
    public static RecipeBuildKind Resolve(
        RecipeMethodId method,
        RecipeDocument document)
    {
        return method switch
        {
            RecipeMethodId.Container => RecipeBuildKind.BioContainer,
            RecipeMethodId.Mirror => RecipeBuildKind.PackageMirror,
            RecipeMethodId.Source => RecipeBuildKind.SourceBuild,
            RecipeMethodId.Dockerfile => RecipeBuildKind.DockerfileFallback,

            RecipeMethodId.Package =>
                document.PackageEngine == "micromamba"
                    ? RecipeBuildKind.Micromamba
                    : RecipeBuildKind.Conda,

            _ => throw new InvalidOperationException(
                $"Unsupported recipe method: {method}")
        };
    }
}
```

주의:

```text
RecipeBuildKindResolver는 Build()로 Defaulted field가 적용된 뒤에만 호출한다.
Build() 전에 RecipeBuildKindResolver를 호출하는 것은 계약 위반이다.
ValidateDraft는 RecipeBuildKindResolver를 확정적으로 호출하지 않는다.
```

권장 가드:

```csharp
if (method == RecipeMethodId.Package && string.IsNullOrWhiteSpace(document.PackageEngine))
{
    throw new InvalidOperationException(
        "PackageEngine must be defaulted before resolving RecipeBuildKind.");
}
```

---

## 5. CLI 옵션 규칙

### 5.1 허용되는 method

```bash
nodekit recipe create recipe.json --method container
nodekit recipe create recipe.json --method package
nodekit recipe create recipe.json --method mirror
nodekit recipe create recipe.json --method source
nodekit recipe create recipe.json --method dockerfile
```

### 5.2 package engine 지정

```bash
nodekit recipe create recipe.json --method package --engine micromamba
```

기본값:

```text
--engine conda
```

### 5.3 금지되는 조합

`--engine`은 `--method package`일 때만 허용한다.

금지:

```bash
nodekit recipe create recipe.json --method container --engine micromamba
nodekit recipe create recipe.json --method source --engine conda
nodekit recipe create recipe.json --method dockerfile --engine micromamba
```

오류 메시지:

```text
--engine can only be used with --method package.
```

### 5.4 내부 build kind 이름은 기본 CLI 옵션으로 받지 않는다

v1에서는 다음을 허용하지 않는다.

```bash
nodekit recipe create recipe.json --method conda
nodekit recipe create recipe.json --method micromamba
nodekit recipe create recipe.json --method source-build
nodekit recipe create recipe.json --method dockerfile-fallback
```

이유:

```text
사용자-facing method와 내부 RecipeBuildKind가 섞이면 CLI 옵션 충돌이 생긴다.
초보자 CLI는 안정적인 public method 이름만 받는다.
```

### 5.5 non-interactive Dockerfile warning

Dockerfile method를 non-interactive로 사용할 때는 recipe field가 아니라 CLI flag로 warning 수락을 받는다.

```bash
nodekit recipe create recipe.json \
  --method dockerfile \
  --dockerfile ./Dockerfile \
  --accept-dockerfile-warning \
  --non-interactive
```

`--accept-dockerfile-warning`은 recipe에 저장하지 않는다.
authoring session metadata에만 반영한다.

### 5.6 image tag-only warning

`ImageDigest`는 `Required` field다. CLAUDE.md §3은 `@sha256:` digest가 없는 이미지 참조를 L1에서 예외 없이 block한다 — authoring-level requirement는 이 정책을 완화할 수 없다.

다만 authoring 중에는 digest를 아직 모르는 상태로 `ImageRef`에 tag만 입력하는 것을 막지 않는다. 그 시점에는 즉시 blocking하지 않고 warning만 보여준다.

```text
tag-only ImageRef를 입력하면:
이 상태로 저장을 시도하면 final validation(L1)에서 차단됩니다.
digest를 알고 있다면 지금 입력하세요.
모른다면 나중에 digest를 채운 뒤 다시 저장을 시도해야 합니다.
```

`ImageDigest`는 v1에서 자동으로 resolve되지 않는다(§1.2 — 실제 image pull 확인은 v1 범위 밖). 즉 v1에서 tag-only로 작성을 끝까지 진행한 사용자는 final validation에서 막힌다는 사실을 authoring 중에 정직하게 미리 안내한다.

이것은 accept-flag로 우회할 수 있는 warning이 아니라 누락된 Required field이므로, 별도 수락 flag를 두지 않는다.

```text
--accept-image-tag-warning 옵션은 v1에 두지 않는다.
```

---

## 6. RecipeFieldRequirement

### 6.1 왜 필요한가

단순한 `bool Required`는 충분하지 않다.

예:

```text
PackageEngine = 값이 없으면 conda 기본값 적용
BuildDependencies = 없어도 되지만 재현성 차원에서 명시를 권장
MirrorKind = v1에서는 없어도 됨
```

이 값들은 모두 `Required == false`처럼 보일 수 있지만 의미가 다르다.

따라서 field requirement를 네 가지로 분리한다.

```csharp
internal enum RecipeFieldRequirement
{
    Required,
    Defaulted,
    Optional,
    Recommended
}
```

### 6.2 의미

| Requirement   | 의미              | 값이 없을 때                 |
| ------------- | --------------- | ----------------------- |
| `Required`    | 사용자가 반드시 제공해야 함 | blocking violation      |
| `Defaulted`   | 값이 없으면 기본값 적용   | default value로 자동 충족    |
| `Optional`    | 없어도 됨           | 통과                      |
| `Recommended` | 없어도 되지만 권장      | warning 표시, blocking 아님 |

### 6.3 non-interactive mode에서의 처리

```text
Required 누락      → 실패
Defaulted 누락     → default 적용 후 계속
Recommended 누락   → warning 출력 후 계속
Optional 누락      → 계속
```

예:

```text
PackageEngine 누락
→ Defaulted
→ conda 적용
→ 계속
```

```text
BuildDependencies 누락
→ Recommended
→ dependency 명시 권장 warning 출력
→ 계속
```

```text
SourceChecksum 누락
→ Required
→ 실패
```

---

## 7. RecipeFieldDescriptor

### 7.1 LocalizedText

Ko/En 고정 record 대신 locale dictionary를 권장한다.

```csharp
internal sealed record LocalizedText(
    IReadOnlyDictionary<string, string> Values)
{
    public string Get(string locale, string fallbackLocale = "en");
}
```

예:

```csharp
new LocalizedText(new Dictionary<string, string>
{
    ["ko"] = "도구 이름",
    ["en"] = "Tool name"
});
```

### 7.2 Field type

```csharp
internal enum RecipeFieldType
{
    Scalar,
    Choice,
    StringList,
    InputList,
    OutputList
}
```

### 7.3 Choice

```csharp
internal sealed record RecipeChoice(
    string Value,
    LocalizedText Label,
    LocalizedText Description);
```

### 7.4 Descriptor

```csharp
internal sealed record RecipeFieldDescriptor(
    string Name,
    RecipeFieldType Type,
    RecipeFieldRequirement Requirement,
    object? DefaultValue,
    LocalizedText Label,
    LocalizedText Help,
    IReadOnlyList<string> Examples,
    IReadOnlyList<RecipeChoice> Choices,
    Action<RecipeDocument, object> Apply,
    Func<object, ValidationViolation?>? QuickValidate = null);
```

`DefaultValue`는 `Requirement == Defaulted`일 때 의미가 있다.
`Recommended` field는 값이 없을 때 warning을 낸다.

---

## 8. RecipeFieldCatalog

```csharp
internal static class RecipeFieldCatalog
{
    public static IReadOnlyList<RecipeFieldDescriptor> CommonScalarFields { get; }

    public static IReadOnlyDictionary<RecipeMethodId, IReadOnlyList<RecipeFieldDescriptor>> MethodFields { get; }

    public static RecipeFieldDescriptor InputsField { get; }

    public static RecipeFieldDescriptor OutputsField { get; }

    public static IReadOnlyList<RecipeFieldDescriptor> FieldsFor(RecipeMethodId method);

    public static IReadOnlyList<RecipeFieldDescriptor> BlockingRequiredFieldsFor(RecipeMethodId method);

    public static IReadOnlyList<RecipeFieldDescriptor> DefaultedFieldsFor(RecipeMethodId method);

    public static IReadOnlyList<RecipeFieldDescriptor> RecommendedFieldsFor(RecipeMethodId method);
}
```

`FieldsFor`는 다음 순서를 보장한다.

```text
CommonScalarFields
→ MethodFields[method]
→ InputsField
→ OutputsField
```

중요:

```text
Inputs/Outputs는 모든 method에서 Required지만, UX 순서상 공통 scalar field가 아니다.
Inputs/Outputs는 terminal required authoring section이다.
BlockingRequiredFieldsFor(method)는 InputsField와 OutputsField를 반드시 포함한다.
RecipeFieldCatalog는 RecipeBuildKind가 아니라 RecipeMethodId 기준이다.
RecipeBuildKind는 Build() 이후 RecipeBuildKindResolver가 결정한다.
```

---

## 9. method별 field requirement 표

### 9.1 공통 scalar fields

모든 method에 필요하다.

| Field         | Requirement | Default | 설명                           |
| ------------- | ----------- | ------- | ----------------------------- |
| `ToolName`    | Required    | -       | recipe에서 식별할 도구 이름           |
| `ToolVersion` | Required    | -       | 도구 버전 또는 고정된 release/version |

`ToolVersion`은 모든 method에서 required로 둔다. SourceBuild와 DockerfileFallback도 예외가 아니다.

### 9.2 terminal required authoring sections

모든 method에 필요하지만 UX상 마지막에 배치한다.

| Field     | Requirement | Default | 설명              |
| --------- | ----------- | ------- | --------------- |
| `Inputs`  | Required    | -       | 최소 1개 이상의 입력 정의 |
| `Outputs` | Required    | -       | 최소 1개 이상의 출력 정의 |

Inputs/Outputs는 `CommonScalarFields`에 포함하지 않는다.
대신 `FieldsFor(method)`의 마지막 단계로 합성한다.

---

### 9.3 container

| Field                     | Requirement | Default | 설명                              |
| ------------------------- | ----------- | ------- | ------------------------------- |
| `ImageRef`                | Required    | -       | 사용할 컨테이너 이미지 참조                 |
| `ImageDigest`             | Required    | -       | digest 고정. CLAUDE.md §3 L1 규칙에 따라 final validation에서 필수 |
| `Entrypoint` 또는 `Command` | Optional    | -       | 이미지 기본 entrypoint를 그대로 쓰지 않을 경우 |

v1 정책:

```text
authoring 중에는 ImageRef tag-only 입력을 막지 않는다.
다만 digest가 없으면 final validation(L1, CLAUDE.md §3)에서 block된다.
이 사실을 authoring 중 warning으로 미리 안내한다.
warning 표시는 field 입력 자체를 막지 않지만, ImageDigest 누락은
final validation에서 blocking violation으로 처리된다.
```

---

### 9.4 package

| Field           | Requirement | Default | 설명                      |
| --------------- | ----------- | ------- | ----------------------- |
| `Packages`      | Required    | -       | 설치할 package 목록          |
| `Channels`      | Required    | -       | package channel 목록      |
| `PackageEngine` | Defaulted   | `conda` | `conda` 또는 `micromamba` |

`PackageEngine`은 recipe field이지만 사용자가 직접 입력하지 않아도 된다.
값이 없으면 Build() 단계에서 `conda`가 적용된다.

---

### 9.5 mirror

| Field        | Requirement | Default | 설명                            |
| ------------ | ----------- | ------- | ----------------------------- |
| `MirrorUri`  | Required    | -       | 내부 package mirror URI         |
| `Packages`   | Required    | -       | 설치할 package 목록                |
| `MirrorKind` | Optional    | -       | mirror 종류. v1에서는 optional로 둔다 |

v1에서는 mirror 종류 모델이 아직 확정되지 않았으므로 `MirrorKind`를 `Defaulted`로 두지 않는다.
향후 mirror 종류가 안정되면 v1.1에서 `Defaulted` 또는 `Choice` field로 승격할 수 있다.

---

### 9.6 source

| Field                 | Requirement | Default | 설명                             |
| --------------------- | ----------- | ------- | ------------------------------ |
| `SourceUri`           | Required    | -       | source archive 또는 release URI  |
| `SourceChecksum`      | Required    | -       | v1에서는 `sha256:<64 hex>` 형식만 허용 |
| `SourceBuildCommands` | Required    | -       | source를 빌드하는 명령어 목록            |
| `BuildDependencies`   | Recommended | -       | 빌드에 필요한 dependency 목록 — 없어도 되지만 재현성을 위해 명시 권장 |

`SourceChecksum` parser는 향후 `algo:hex` 일반형으로 확장 가능하게 설계하되, v1 validator는 `sha256`만 허용한다.

---

### 9.7 dockerfile

| Field                                   | Requirement | Default           | 설명                   |
| ---------------------------------------- | ----------- | ----------------- | -------------------- |
| `DockerfilePath` 또는 `DockerfileContent` | Required    | -                 | Dockerfile 위치 또는 내용  |
| `BuildContext`                          | Defaulted   | current directory | Docker build context |

다음은 recipe field가 아니다.

```text
DockerfileWarningsAccepted
```

경고 수락 여부는 authoring session metadata다.

---

## 10. 추천 질문 모델

### 10.1 Answer enum

```csharp
internal enum Answer
{
    Yes,
    No,
    Unknown
}
```

### 10.2 답변 모델

```csharp
internal sealed record RecipeMethodAnswers(
    Answer IsRestrictedNetwork,
    Answer HasInternalPackageMirror,
    Answer HasExistingContainerImage,
    Answer HasPackageInPublicChannels,
    Answer HasSourceArchiveAndChecksum,
    Answer HasExistingDockerfile);
```

주의:

```text
HasPackageInPublicChannels는 "존재 여부"다.
내부망에서는 "존재"가 곧 "접근 가능"을 의미하지 않는다.
접근 가능성은 internal network gate에서 별도로 판단한다.
```

### 10.3 질문 순서

질문 순서는 record field 순서에 의존하지 않는다.
별도 질문 리스트로 정의한다.

```csharp
internal static class RecipeMethodQuestionCatalog
{
    public static IReadOnlyList<RecipeMethodQuestion> Questions { get; }
}
```

질문 순서:

```text
1. 내부망/폐쇄망 환경인가?
2. 내부망이면 내부 package mirror URI를 아는가?
3. 기존 컨테이너 이미지 URI가 있는가?
4. public channel 패키지가 있는가?
5. source URL과 checksum이 있는가?
6. 기존 Dockerfile이 있는가?
```

---

## 11. 추천 알고리즘: gate + priority table

### 11.1 추천 결과 모델

추천 결과는 method 중심이다.
추천 단계에서 내부 `RecipeBuildKind`를 확정하지 않는다.

```csharp
internal sealed record RecipeMethodRecommendation(
    RecipeMethodId? RecommendedMethod,
    string Reason,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<RecipeMethodCandidate> Alternatives,
    IReadOnlyList<string> MissingInformation);
```

```csharp
internal sealed record RecipeMethodCandidate(
    RecipeMethodId Method,
    string Label,
    string Reason,
    int Priority);
```

### 11.2 Gate 1 — 내부망

가장 먼저 내부망 여부를 평가한다.

| 조건                                                                     | 추천                    | 대안                                            | 설명                                                               |
| ---------------------------------------------------------------------- | --------------------- | --------------------------------------------- | ---------------------------------------------------------------- |
| `IsRestrictedNetwork == Yes` and `HasInternalPackageMirror == Yes`     | `mirror`              | `source`, `container`, `dockerfile`           | 내부망에서는 내부 mirror가 가장 자연스럽다.                                      |
| `IsRestrictedNetwork == Yes` and `HasInternalPackageMirror == No`      | 확정 추천 없음              | `source`, `container`, `dockerfile`           | mirror가 없으면 public package install은 기본 추천하지 않는다.                 |
| `IsRestrictedNetwork == Yes` and `HasInternalPackageMirror == Unknown` | 확정 추천 없음              | `mirror`, `source`, `container`, `dockerfile` | mirror 정보 확인이 필요하다.                                              |
| `IsRestrictedNetwork == Unknown`                                       | 일반 추천 흐름으로 진행하되 경고 추가 | 후보 전체                                         | 내부망이면 public channel, 외부 registry, GitHub source가 실패할 수 있음을 알린다. |
| `IsRestrictedNetwork == No`                                            | 일반 추천 흐름 진행           | -                                             | public channel과 외부 registry 사용 가능성을 열어둔다.                        |

내부망에서 public package가 존재한다고 답해도 `package`가 mirror보다 우선하지 않는다.

### 11.3 내부망 대안 표시 원칙

내부망에서 `mirror`가 확정되지 않으면, 대안은 "순위"보다 "접근성 조건" 중심으로 보여준다.

예:

```text
가능한 후보:
1. 내부 패키지 미러에서 설치하기
   - 내부 mirror URI가 필요합니다.

2. 소스코드로 직접 빌드하기
   - SourceUri가 내부 mirror 또는 접근 가능한 위치에 있어야 합니다.

3. 기존 컨테이너 이미지 사용
   - ImageRef가 내부 registry에서 접근 가능해야 합니다.

4. Dockerfile 직접 작성하기
   - base image와 build dependency가 내부망에서 접근 가능해야 합니다.
```

v1에서는 URI가 내부인지 자동 판별하지 않는다.
v1.1에서 다음 질문을 추가할 수 있다.

```text
이미지/소스/base image가 내부 registry 또는 내부 mirror에 있나요?
```

### 11.4 General priority table

내부망 gate를 통과했거나 내부망이 아니라고 답한 경우, 다음 우선순위를 적용한다.

| 우선순위 | 조건                                   | 추천 method    | 이유                                 |
| ---- | ------------------------------------ | ------------ | ---------------------------------- |
| 1    | `HasExistingContainerImage == Yes`   | `container`  | 이미 있는 이미지를 쓰는 것이 가장 빠르고 단순하다.      |
| 2    | `HasPackageInPublicChannels == Yes`  | `package`    | 일반적인 bioinformatics 도구에 적합하다.      |
| 3    | `HasSourceArchiveAndChecksum == Yes` | `source`     | 패키지가 없거나 특정 소스가 필요할 때 사용한다.        |
| 4    | `HasExistingDockerfile == Yes`       | `dockerfile` | 마지막 수단이지만 기존 Dockerfile이 있으면 가능하다. |
| 5    | Yes가 부족하고 Unknown이 많음                | 확정 추천 없음     | 확인 항목과 후보를 보여준다.                   |

여러 조건이 동시에 `Yes`인 경우 우선순위가 높은 것이 기본 추천이 된다. 나머지는 alternatives에 표시한다.

### 11.5 IsRestrictedNetwork == Unknown 처리

내부망 여부가 Unknown인데 package가 추천될 수 있다.
이 경우 warning은 반드시 추천 결과에 포함한다.

예:

```text
IsRestrictedNetwork == Unknown
HasPackageInPublicChannels == Yes
```

결과:

```text
추천 방법: 패키지로 설치하기

주의:
내부망인지 확실하지 않다고 답했습니다.
내부망이라면 public channel package 설치가 실패할 수 있습니다.
먼저 bioconda/conda-forge 접근 가능 여부를 확인하세요.
```

### 11.6 Unknown-heavy 결과

충분한 `Yes`가 없고 `Unknown`이 많으면 단일 method를 강제 추천하지 않는다.

예:

```text
HasExistingContainerImage == Unknown
HasPackageInPublicChannels == Unknown
HasSourceArchiveAndChecksum == Unknown
HasExistingDockerfile == No
```

결과:

```text
아직 하나의 작성 방법을 확정 추천하기 어렵습니다.

확인하면 좋은 항목:
1. 기존 컨테이너 이미지 URI가 있는지
2. public channel 패키지가 있는지
3. source URL과 checksum이 있는지

가능한 후보:
1. 기존 컨테이너 이미지 사용
2. 패키지로 설치하기
3. 소스코드로 직접 빌드하기
```

---

## 12. 추천 결과 화면

추천 결과는 반드시 다음을 포함한다.

```text
추천 방법
추천 이유
사용자 답변 근거
주의점
대안
부족한 정보
```

예:

```text
추천 방법: 기존 컨테이너 이미지 사용

추천 이유:
- 이미 사용할 수 있는 컨테이너 이미지 URI가 있다고 답했습니다.
- 이 방법은 새로 패키지를 설치하거나 소스코드를 빌드하지 않아도 됩니다.

주의:
- digest로 고정된 이미지 URI가 필요합니다.
- tag만 알고 있다면 우선 진행할 수 있지만, digest 없이는 최종 저장 시점에 검증이 막힙니다.

대안:
1. 패키지로 설치하기
   - public channel 패키지가 있다면 recipe를 구조적으로 작성할 수 있습니다.
2. 소스코드로 직접 빌드하기
   - 특정 source release를 고정해야 할 때 사용할 수 있습니다.

이 방법으로 계속할까요?

1. 예, 이 방법으로 진행
2. 대안 중에서 선택
3. 처음부터 다시 답변
4. 취소
```

---

## 13. 입력/출력 preset

### 13.1 목적

초보자에게 `role`, `format`, `shape`, `class`를 직접 묻지 않는다.
preset을 통해 의미 있는 선택지를 제공하고 내부값을 채운다.

### 13.2 Input preset

```csharp
internal sealed record ToolInputPreset(
    string Id,
    LocalizedText Label,
    LocalizedText Description,
    string Role,
    string Format,
    string Shape,
    IReadOnlyList<string> Examples);
```

기본 목록:

| ID                | Label                  | Role        | Format  | Shape    |
| ----------------- | ---------------------- | ----------- | ------- | -------- |
| `fastq-paired`    | FASTQ paired-end reads | `reads`     | `fastq` | `pair`   |
| `fastq-single`    | FASTQ single-end reads | `reads`     | `fastq` | `single` |
| `bam-alignment`   | BAM alignment          | `alignment` | `bam`   | `single` |
| `fasta-reference` | FASTA reference        | `reference` | `fasta` | `single` |
| `vcf-variants`    | VCF variants           | `variants`  | `vcf`   | `single` |
| `custom`          | 직접 입력                  | -           | -       | -        |

### 13.3 Output preset

```csharp
internal sealed record ToolOutputPreset(
    string Id,
    LocalizedText Label,
    LocalizedText Description,
    string Role,
    string Format,
    string Class,
    IReadOnlyList<string> Examples);
```

기본 목록:

| ID             | Label                | Role        | Format | Class     |
| -------------- | -------------------- | ----------- | ------ | --------- |
| `bam-primary`  | BAM alignment output | `alignment` | `bam`  | `primary` |
| `bai-index`    | BAM index output     | `index`     | `bai`  | `index`   |
| `vcf-primary`  | VCF variant output   | `variants`  | `vcf`  | `primary` |
| `log-file`     | Log file             | `log`       | `txt`  | `log`     |
| `metrics-file` | Metrics file         | `metrics`   | `txt`  | `metrics` |
| `custom`       | 직접 입력                | -           | -      | -        |

---

## 14. custom role/format/channel 정규화

### 14.1 known format 선택

custom format도 먼저 known format 목록에서 선택하게 한다.

```text
format을 선택하세요.

1. fastq
2. fasta
3. bam
4. bai
5. sam
6. vcf
7. bed
8. txt
9. 기타 직접 입력
```

`기타 직접 입력`을 선택할 때만 문자열 입력을 허용한다.

### 14.2 format normalize

format normalize 규칙:

```text
trim
lowercase
leading dot 제거
확장자 전체가 아니라 데이터 포맷만 입력하도록 안내
```

normalize는 조용히 적용하지 않는다.
사용자에게 한 줄로 알려준다.

예:

```text
입력한 `FASTQ`를 표준 format 값 `fastq`로 처리합니다.
```

처리 표:

| 사용자 입력     | 처리                                                   |
| ---------- | ---------------------------------------------------- |
| `FASTQ`    | `fastq`로 normalize하고 안내                              |
| `.fastq`   | `fastq`로 normalize하고 안내                              |
| `fq`       | `fastq` suggestion, 사용자 확인 필요                        |
| `fastq.gz` | compression suffix 감지, `fastq` suggestion, 사용자 확인 필요 |
| `vcf.gz`   | compression suffix 감지, `vcf` suggestion, 사용자 확인 필요   |

중요:

```text
.gz 제거는 조용한 normalize가 아니다.
compression suffix를 감지한 뒤 format 제안과 warning을 표시한다.
```

예:

```text
`fastq.gz`에서 compression suffix `.gz`를 감지했습니다.
v1 recipe에서는 compression을 별도 필드로 저장하지 않습니다.
format을 `fastq`로 사용할까요? [Y/n]
```

v1에서는 `Compression` 필드를 recipe에 추가하지 않는다.
v1.1에서 별도 모델링을 검토한다.

### 14.3 known role 선택

role도 먼저 known role 목록에서 선택하게 한다.

```text
role을 선택하세요.

1. reads
2. reference
3. alignment
4. variants
5. index
6. log
7. metrics
8. 기타 직접 입력
```

직접 입력 normalize:

```text
trim
lowercase
공백은 underscore로 변환
snake_case로 통일
```

예:

| 사용자 입력             | normalize 결과       |
| ------------------ | ------------------ |
| `Read Pair`        | `read_pair`        |
| `Reference Genome` | `reference_genome` |
| `LOG`              | `log`              |

normalize 후 안내:

```text
입력한 `Reference Genome`을 표준 role 값 `reference_genome`으로 처리합니다.
```

### 14.4 channel 입력 UX

channel도 choice-first로 입력한다.

```text
channel을 선택하거나 입력하세요.

1. bioconda
2. conda-forge
3. defaults
4. 직접 입력
```

직접 입력을 선택한 경우:

```text
channel 이름을 입력하세요.
예: internal-bioconda, company-conda, research-mirror
```

known channel과 유사한 오타가 감지되면 suggestion을 보여준다.

```text
`defalts`는 `defaults`를 의미하나요? [Y/n]
```

---

## 15. 필드 입력 중 escape hatch

추천 화면에서만 method 변경을 허용하면 부족하다.
필드 입력 중 사용자가 준비물을 모른다는 사실을 알게 될 수 있다.

예:

```text
패키지 이름을 입력하세요.
```

여기서 사용자가 패키지명을 모르면 막힌다.

따라서 모든 prompt는 다음 명령을 공통으로 지원한다.

```text
/help
/change-method
/review
/cancel
```

optional field에서는 추가로:

```text
/skip
```

### 15.1 /help

현재 필드 설명, 예시, 필요한 이유를 보여준다.

### 15.2 /change-method

현재까지 입력한 값 중 일부를 보존하고 method 선택 화면으로 돌아간다.

보존은 "새 method에서도 유효함"을 의미하지 않는다.
보존은 "사용자 입력을 임시로 유지함"을 의미한다.

임시 보존 가능한 항목:

```text
ToolName
ToolVersion
Inputs
Outputs
```

주의:

```text
Inputs/Outputs는 새 method 기준으로 다시 검증됩니다.
새 method의 렌더링 규칙과 맞지 않으면 review 또는 final validation 단계에서 수정이 필요할 수 있습니다.
```

method-specific field는 새 method와 호환되지 않으면 discarded 후보로 표시한다.

예:

```text
현재 method: package
새 method: source

임시 보존:
- ToolName
- ToolVersion
- Inputs
- Outputs

새 method에서 다시 검증 필요:
- Inputs
- Outputs

버려질 수 있는 항목:
- Packages
- Channels
- PackageEngine

초기화되는 metadata:
- package method 관련 경고 상태

계속할까요? [y/N]
```

---

## 16. ChangeMethod 계약

`ChangeMethod`는 단순히 selected method만 바꾸는 API가 아니다.

반드시 다음 세 가지를 함께 처리한다.

```text
1. method-specific fields discard
2. method-specific metadata reset
3. preserved fields의 validation state invalidation
```

### 16.1 method-specific fields discard

예:

```text
package → source:
discard:
- Packages
- Channels
- PackageEngine

source → package:
discard:
- SourceUri
- SourceChecksum
- SourceBuildCommands
- BuildDependencies

dockerfile → package:
discard:
- DockerfilePath
- DockerfileContent
- BuildContext
```

### 16.2 method-specific metadata reset

method가 바뀌면 떠나는 method에 묶인 metadata는 reset한다.

예:

```text
dockerfile → package:
reset:
- DockerfileWarningAccepted

container → source:
reset:
- ImageTagWarningShown
- ImageTagWarningAccepted
```

다시 같은 method로 돌아오면 필요한 경고를 다시 보여준다.

### 16.3 preserved fields validation state invalidation

`ToolName`, `ToolVersion`, `Inputs`, `Outputs`를 보존하더라도, 새 method에서 유효하다고 보장하지 않는다.

따라서 method 변경 후:

```text
Inputs/Outputs는 새 method 기준 재검증 대상으로 표시한다.
ValidateDraft와 Review에서 warning으로 표시한다.
Final validation 전 사용자가 재확인하거나 수정할 수 있게 한다.
```

### 16.4 invalidated field의 의미

`invalidatedFields`는 blocking 상태가 아니다.

```text
invalidated field는 IsComplete를 막지 않는다.
invalidated field는 Build()를 막지 않는다.
invalidated field는 ValidateDraft와 Snapshot/Review에서 warning으로 표시된다.
final validation은 invalidated field가 남아 있어도 실행할 수 있다.
```

이유:

```text
invalidated는 "이 값이 틀렸다"가 아니라
"method 변경 이후 새 method 기준으로 다시 확인하는 것이 좋다"는 뜻이다.
```

### 16.5 invalidated field 해제 규칙

invalidated flag는 다음 경우에 clear된다.

```text
1. 사용자가 해당 field를 수정 완료한 경우
2. 사용자가 해당 field review 화면에서 "그대로 사용"을 명시적으로 선택한 경우
3. 사용자가 preset을 다시 선택한 경우
4. 해당 field가 method 변경 과정에서 discard된 경우
```

예:

```text
현재 입력/출력은 이전 method에서 가져온 값입니다.
새 method에서도 그대로 사용할까요?

1. 그대로 사용
2. 수정
3. preset으로 다시 선택
4. 돌아가기
```

사용자가 1번을 선택하면:

```text
Inputs/Outputs invalidation cleared.
```

---

## 17. RecipeAuthoringSession

### 17.1 API

```csharp
internal sealed class RecipeAuthoringSession
{
    public bool IsMethodSelected { get; }

    public void SelectMethod(RecipeMethodId method);

    public RecipeFieldDescriptor? NextField();

    public IReadOnlyList<ValidationViolation> SetField(string fieldName, object value);

    public IReadOnlyList<ValidationViolation> AppendListItem(string fieldName, object item);

    public void CompleteListField(string fieldName);

    public void SkipOptionalField(string fieldName);

    public ChangeMethodPreview PreviewMethodChange(RecipeMethodId nextMethod);

    public void ChangeMethod(RecipeMethodId nextMethod, ChangeMethodDecision decision);

    public void ConfirmInvalidatedField(string fieldName);

    public RecipeAuthoringSnapshot Snapshot();

    public IReadOnlyList<ValidationViolation> ValidateDraft();

    public RecipeValidationRecoveryPlan BuildRecoveryPlan(
        IReadOnlyList<ValidationViolation> violations);

    public bool IsComplete { get; }

    public RecipeDocument Build();
}
```

문서와 API에서는 `SelectBuildKind`를 쓰지 않는다.
authoring session API는 `RecipeMethodId` 중심이다.

### 17.2 내부 상태

```csharp
private RecipeMethodId? _selectedMethod;
private readonly RecipeDocument _document = new();

private readonly HashSet<string> _filledFields = new();
private readonly HashSet<string> _skippedOptionalFields = new();
private readonly HashSet<string> _completedListFields = new();

private readonly HashSet<string> _invalidatedFields = new();

private readonly List<RecipeAuthoringEvent> _history = new();

private readonly RecipeAuthoringSessionMetadata _metadata = new();
```

### 17.3 session metadata

```csharp
internal sealed record RecipeAuthoringSessionMetadata
{
    public bool DockerfileWarningAccepted { get; init; }
    public bool ImageTagWarningShown { get; init; }
    public bool ImageTagWarningAccepted { get; init; }
}
```

metadata는 recipe에 저장하지 않는다.

### 17.4 완료 판단

```text
Required scalar:
  filledFields에 있어야 완료

Defaulted scalar:
  filledFields에 있거나 default 적용 가능하면 완료

Recommended scalar:
  filledFields에 없으면 warning 대상이지만 완료 가능

Optional scalar:
  filledFields 또는 skippedOptionalFields에 있으면 완료

List field:
  completedListFields에 있어야 완료

Invalidated field:
  IsComplete를 막지 않음
  Snapshot/ValidateDraft/Review에서 warning으로 표시
```

---

## 18. Snapshot과 Build 계약

### 18.1 Snapshot

`Snapshot()`은 review, debug, UI 표시용이다.

특징:

```text
incomplete session에서도 호출 가능
현재 method, 입력된 값, missing fields, default 예정 값, warnings 표시
final recipe로 간주하지 않음
RecipeValidationPipeline을 실행하지 않음
Defaulted field를 실제 적용하지 않음
```

```csharp
internal sealed record RecipeAuthoringSnapshot(
    RecipeMethodId? SelectedMethod,
    IReadOnlyList<RecipeFieldValueSummary> Values,
    IReadOnlyList<string> MissingRequiredFields,
    IReadOnlyList<string> DefaultedFields,
    IReadOnlyList<string> RecommendedWarnings,
    IReadOnlyList<string> InvalidatedFields);
```

### 18.2 Build

`Build()`는 final recipe 후보 생성용이다.

계약:

```text
IsComplete == false이면 Build()는 실패한다.
Build()는 incomplete document를 반환하지 않는다.
Build()는 Defaulted field를 실제 적용한다.
review나 UI 표시에는 Snapshot()을 사용한다.
```

권장 구현:

```csharp
public RecipeDocument Build()
{
    if (!IsComplete)
    {
        throw new InvalidOperationException(
            "Cannot build an incomplete recipe authoring session.");
    }

    ApplyDefaultedFields();
    return _document;
}
```

### 18.3 default 적용 책임

default 적용 책임은 `Build()`에 있다.

```text
ValidateDraft:
- default를 실제 적용하지 않음
- 적용 예정 값만 표시

Snapshot:
- default를 실제 적용하지 않음
- 적용 예정 값만 표시

Build:
- default를 실제 적용함

Final validation:
- Build() 결과를 사용함
- 별도 default 적용 단계를 반복하지 않음
```

---

## 19. ValidateDraft와 final validation 분리

### 19.1 QuickValidate

`QuickValidate`는 단일 필드 형식 검사를 수행한다.

예:

```text
SourceChecksum 형식이 sha256:<64 hex>인지
required string이 비어 있지 않은지
choice 값이 허용 범위인지
```

### 19.2 ValidateDraft

`ValidateDraft()`는 authoring-level validation이다.

목표:

```text
작성 중 명백한 문제를 조기에 알려준다.
그러나 final RecipeValidationPipeline을 실행하지 않는다.
```

ValidateDraft에서 하는 것:

```text
Required field 누락 표시
Defaulted field의 예정 default 표시
Recommended field 누락 warning 표시
invalidated field warning 표시
단일 필드 QuickValidate 결과 표시
```

ValidateDraft에서 하지 않는 것:

```text
RecipeBuildKindResolver로 최종 build kind 확정
RecipeRenderer 실행
L1 validator 실행
package resolve 확인
image pull 확인
source download 확인
복잡한 cross-field consistency 검증
```

중요:

```text
PackageEngine이 아직 사용자가 명시하지 않은 상태라면
ValidateDraft는 "conda 기본값이 적용될 예정입니다"라고 표시할 수 있다.
하지만 Conda build kind 기준 L1 검증을 실행하지 않는다.
```

### 19.3 Final validation

final validation은 저장 직전에만 실행한다.

순서:

```text
1. IsComplete 확인
2. Build() 호출
   - 이 단계에서 Defaulted field 적용
3. RecipeBuildKindResolver.Resolve(method, document)
4. document.BuildKind 확정 또는 renderer 입력에 build kind 전달
5. RecipeValidationPipeline.ValidateRecipe(document)
6. 성공 시 recipe.json 작성
7. 실패 시 RecoveryPlan 생성 후 session 유지
```

`RecipeValidationPipeline`은 다음에서 공유한다.

```text
nodekit validate
nodekit render
nodekit recipe create
```

원칙:

```text
recipe create가 만든 recipe는 nodekit validate에서 다시 실패하면 안 된다.
```

authoring-level field requirement(`RecipeFieldRequirement`)와 final L1 정책(CLAUDE.md §3)은 별개의 층이다.

```text
RecipeFieldRequirement는 authoring session이 "다음 필드로 넘어갈 수 있는가"를 결정한다.
CLAUDE.md §3의 reproducibility 규칙(latest tag block, digest 미고정 block,
version+build string 미고정 block)은 authoring requirement와 무관하게
final validation의 L1 chain에서 항상 적용된다.
```

따라서 `ImageDigest`처럼 L1이 block하는 값은 authoring-level requirement도 `Required`로 둔다 — `Recommended`/`Optional`로 두면 "create는 성공하지만 validate는 실패"하는 상태가 생겨 위 원칙을 어긴다. tag-only `ImageRef`로 작성을 진행한 session은 final validation에서 반드시 block되며, 이는 fail-closed 원칙(§2.6)에 따라 `recipe.json`을 쓰지 않고 RecoveryPlan을 생성한다.

---

## 20. RecoveryPlan

### 20.1 Recovery action kind

단일 field에 매핑되는 violation만 있다고 가정하면 안 된다.
L1 validator와 renderer 단계에서는 cross-field 오류가 많다.

```csharp
internal enum RecoveryActionKind
{
    EditSingleField,
    EditRelatedFields,
    ReviewSection,
    ShowExplanationOnly
}
```

### 20.2 Recovery action

```csharp
internal sealed record RecipeValidationRecoveryAction(
    string Label,
    RecoveryActionKind Kind,
    IReadOnlyList<string> RelatedFields,
    LocalizedText Description,
    LocalizedText BeginnerHint);
```

### 20.3 Recovery plan

```csharp
internal sealed record RecipeValidationRecoveryPlan(
    IReadOnlyList<RecipeValidationRecoveryAction> Actions,
    IReadOnlyList<ValidationViolation> UnmappedViolations);
```

---

## 21. RecoveryPlan 생성 규칙

### 21.1 단일 필드 오류

예:

```text
Channels must not be empty.
```

Recovery action:

```text
Kind: EditSingleField
RelatedFields: Channels
Label: channel 항목 수정
BeginnerHint: 패키지를 설치할 channel을 하나 이상 선택하세요. 보통 bioconda와 conda-forge를 함께 사용합니다.
```

### 21.2 관련 필드 오류

예:

```text
Package install recipe requires both Packages and Channels.
```

Recovery action:

```text
Kind: EditRelatedFields
RelatedFields:
- Packages
- Channels
Label: package/channel 항목 함께 수정
BeginnerHint: package 이름과 channel은 함께 맞아야 합니다. 예: bwa=0.7.17, bioconda, conda-forge
```

### 21.3 입력/출력 조합 오류

예:

```text
Input/output definition is inconsistent with rendered build request.
```

Recovery action:

```text
Kind: ReviewSection
RelatedFields:
- Inputs
- Outputs
Label: 입력/출력 섹션 확인
BeginnerHint: 직접 입력한 role/format/shape/class가 특수하면 기본 preset으로 다시 선택해보세요.
```

### 21.4 렌더링 실패 또는 L1 generic 오류

예:

```text
L1: build request render failed.
```

Recovery action:

```text
Kind: ShowExplanationOnly
RelatedFields:
- Method-specific fields
- Inputs
- Outputs
Label: 전체 recipe 구조 확인
BeginnerHint:
이 오류는 한 필드만의 문제가 아닐 수 있습니다.
작성 방법에 필요한 필드가 모두 있는지, 입력/출력 preset이 적절한지, package/source/dockerfile 정보가 서로 맞는지 확인하세요.
```

이 경우 사용자를 단순히 "전체 리뷰"로 보내지 않는다.
관련 가능성이 높은 섹션을 함께 제시한다.

---

## 22. 검증 실패 UX

검증 실패 시:

```text
최종 검증에 실패했습니다.
recipe 파일은 아직 작성하지 않았습니다.

문제:
1. Package install recipe requires both Packages and Channels.
2. Input/output definition is inconsistent with rendered build request.

추천 수정:
1. package/channel 항목 함께 수정
   - package 이름과 channel은 함께 맞아야 합니다.
   - 관련 항목: Packages, Channels

2. 입력/출력 섹션 확인
   - 직접 입력한 값이 특수하면 기본 preset으로 다시 선택해보세요.
   - 관련 항목: Inputs, Outputs

3. 전체 리뷰 화면으로 돌아가기
4. 취소

선택:
```

검증 실패 후 session은 유지된다.
수정 후 다시 final validation을 실행한다.

---

## 23. draft 저장과 resume 정책

### 23.1 v1 정책

v1에서는 draft resume을 구현하지 않는다.

v1 범위:

```text
같은 실행 session 안에서 검증 실패 후 수정 가능
검증 성공 시에만 final recipe 저장
검증 실패 시 final recipe 미작성
```

### 23.2 v1.1 이후

draft 저장/resume은 v1.1 이후로 미룬다.

이유:

```text
draft 파일은 신뢰할 수 없는 입력이다.
resume하려면 schema 검증, 버전 확인, QuickValidate 재실행, migration 정책이 필요하다.
```

v1.1에서 필요한 규칙:

```text
draft schema version 확인
현재 RecipeFieldCatalog와 호환성 확인
QuickValidate 재실행
손상된 필드 무시 또는 복구
구버전 draft migration
```

---

## 24. 리스트 필드 수정 UX

v1에서 리스트 전체 재작성을 기본으로 두면 오타 하나 때문에 사용자가 같은 항목을 다시 입력해야 한다.

따라서 v1 최소 구현은 다음을 지원한다.

```text
기존 항목 표시
항목 단위 수정
새 항목 추가
항목 삭제
전체 다시 작성
```

예:

```text
현재 channels:

1. bioconda
2. conda-forge
3. defalts

무엇을 할까요?

1. 항목 수정
2. 새 항목 추가
3. 항목 삭제
4. 전체 다시 작성
5. 돌아가기

선택: 1

수정할 항목 번호: 3
현재 값: defalts

`defalts`는 `defaults`를 의미하나요? [Y/n]
```

Input/Output 리스트도 동일하게 적용한다.
단, v1에서 복잡한 reorder는 제외한다.

---

## 25. DockerfileFallback UX

DockerfileFallback은 마지막 수단이다.
그러나 사용자가 기존 Dockerfile만 가지고 있을 수 있으므로 막지는 않는다.

추천 화면:

```text
추천 가능 방법: Dockerfile 직접 작성하기

추천 이유:
- 기존 Dockerfile이 있다고 답했습니다.

강한 주의:
- 이 방법은 가장 자유롭지만 재현성 검증이 어렵습니다.
- NodeKit이 package, source, channel 정보를 구조적으로 이해하기 어렵습니다.
- Dockerfile의 base image가 외부 registry에 있으면 내부망에서 실패할 수 있습니다.
- 가능하면 기존 컨테이너 이미지, 패키지 설치, 소스 빌드 방식을 먼저 고려하세요.

계속할까요? [y/N]
```

v1에서는 긴 확인 문구 입력을 요구하지 않는다.
단순 `y/N`으로 충분하다.

수락 여부는 session metadata에 저장한다.
recipe에는 저장하지 않는다.

---

## 26. 초보자 시나리오 1 — BWA package recipe

상황:

```text
사용자는 BWA 이미지를 만들고 싶다.
내부망은 아니다.
BioContainer가 있는지는 모른다.
Bioconda 패키지는 있다고 알고 있다.
JSON은 직접 쓰고 싶지 않다.
```

실행:

```bash
nodekit recipe create recipe.bwa.json
```

추천 질문:

```text
Q1. 인터넷이 제한된 내부망/폐쇄망 환경인가요?
선택: 아니오

Q2. 이미 사용할 수 있는 컨테이너 이미지 URI가 있나요?
선택: 모르겠음

Q3. 도구가 Bioconda 또는 conda-forge 같은 public channel 패키지로 제공되나요?
선택: 예
```

추천 결과:

```text
추천 방법: 패키지로 설치하기

추천 이유:
- 내부망 환경은 아니라고 답했습니다.
- public channel 패키지가 있다고 답했습니다.
- 기존 컨테이너 이미지는 있는지 모른다고 답했습니다.

대안:
1. 기존 컨테이너 이미지 사용
   - image URI를 찾을 수 있다면 더 빠를 수 있습니다.
2. 소스코드로 직접 빌드하기
   - 패키지가 원하는 버전과 맞지 않을 때 사용할 수 있습니다.

이 방법으로 계속할까요?
선택: 예
```

필드 입력:

```text
도구 이름: bwa
도구 버전: 0.7.17
PackageEngine: conda 기본값 적용 예정
패키지: bwa=0.7.17
channels:
- bioconda
- conda-forge
```

입력 preset:

```text
입력 파일의 종류를 선택하세요.

1. FASTQ paired-end reads
2. FASTQ single-end reads
3. BAM alignment
4. FASTA reference
5. VCF variants
6. 직접 입력

선택: 1
이름 기본값: reads
```

출력 preset:

```text
출력 파일의 종류를 선택하세요.

1. BAM alignment output
2. BAM index output
3. VCF variant output
4. Log file
5. Metrics file
6. 직접 입력

선택: 1
이름 기본값: alignment
```

리뷰와 저장:

```text
작성할 recipe 요약

1. 작성 방법: 패키지로 설치하기
2. engine: conda (기본값)
3. 도구 이름: bwa
4. 도구 버전: 0.7.17
5. 패키지:
   - bwa=0.7.17
6. channels:
   - bioconda
   - conda-forge
7. 입력:
   - reads / FASTQ paired-end reads
8. 출력:
   - alignment / BAM alignment output

저장할까요? [y/N]
```

검증 성공 시:

```text
검증 성공.
recipe.bwa.json 파일을 작성했습니다.
```

---

## 27. 초보자 시나리오 2 — 내부망 gate

상황:

```text
사용자는 병원 내부망에서 recipe를 만들어야 한다.
public Bioconda 접근은 제한된다.
내부 mirror URI는 모른다.
Bioconda에 패키지가 있다는 것은 알고 있다.
```

답변:

```text
내부망인가요? → 예
내부 mirror URI를 알고 있나요? → 모르겠음
public channel 패키지가 있나요? → 예
```

결과:

```text
아직 확정 추천하기 어렵습니다.

이유:
- 내부망 환경이라고 답했습니다.
- 내부 mirror URI를 모른다고 답했습니다.
- public channel 패키지가 있더라도 내부망에서는 접근이 제한될 수 있습니다.

먼저 확인할 항목:
1. 내부 package mirror URI
2. 조직에서 허용한 package source
3. source archive와 checksum 보유 여부
4. 내부 registry 또는 내부 source mirror 여부

가능한 후보:
1. 내부 패키지 미러에서 설치하기
2. 소스코드로 직접 빌드하기
3. 기존 컨테이너 이미지 사용
4. Dockerfile 직접 작성하기

주의:
source/container/dockerfile 방식도 외부 URL, 외부 registry, 외부 base image에 의존하면 내부망에서 실패할 수 있습니다.
```

public package install은 기본 추천으로 올라오지 않는다.

---

## 28. 초보자 시나리오 3 — 내부망 여부 Unknown + package 추천

상황:

```text
사용자는 네트워크 제약을 잘 모른다.
Bioconda 패키지는 있다고 알고 있다.
```

답변:

```text
내부망인가요? → 모르겠음
public channel 패키지가 있나요? → 예
```

결과:

```text
추천 방법: 패키지로 설치하기

추천 이유:
- public channel 패키지가 있다고 답했습니다.

주의:
- 내부망인지 확실하지 않다고 답했습니다.
- 내부망이라면 bioconda/conda-forge 접근이 실패할 수 있습니다.
- 먼저 현재 환경에서 public channel 접근이 가능한지 확인하세요.

이 방법으로 계속할까요?
```

---

## 29. 초보자 시나리오 4 — 필드 입력 중 method 변경

상황:

```text
사용자는 package method로 진행했다.
하지만 패키지 이름을 모른다.
```

prompt:

```text
설치할 패키지를 입력하세요.
예: bwa=0.7.17

입력:
```

사용자:

```text
/change-method
```

CLI:

```text
다른 작성 방법으로 전환합니다.

현재 method: 패키지로 설치하기

임시 보존:
- ToolName
- ToolVersion
- Inputs
- Outputs

새 method에서 다시 검증 필요:
- Inputs
- Outputs

method 전환 시 버려질 수 있는 항목:
- Packages
- Channels
- PackageEngine

초기화되는 metadata:
- package method 관련 경고 상태

어떤 방법으로 바꿀까요?

1. 기존 컨테이너 이미지 사용
2. 내부 패키지 미러에서 설치하기
3. 소스코드로 직접 빌드하기
4. Dockerfile 직접 작성하기
5. 돌아가기
```

이후 사용자가 입력/출력 섹션을 다시 열면:

```text
현재 입력/출력은 이전 method에서 가져온 값입니다.
새 method에서도 그대로 사용할까요?

1. 그대로 사용
2. 수정
3. preset으로 다시 선택
4. 돌아가기
```

사용자가 "그대로 사용"을 선택하면 invalidated flag를 해제한다.

---

## 30. 초보자 시나리오 5 — cross-field validation recovery

상황:

```text
사용자가 직접 입력으로 input/output을 작성했다.
최종 L1 검증에서 입력/출력 조합이 렌더링 규칙과 맞지 않는다.
```

실패:

```text
최종 검증에 실패했습니다.
recipe 파일은 아직 작성하지 않았습니다.

문제:
1. Input/output definition is inconsistent with rendered build request.

추천 수정:
1. 입력/출력 섹션 확인
   - 직접 입력한 role/format/shape/class가 특수하면 기본 preset으로 다시 선택해보세요.
   - 관련 항목: Inputs, Outputs

2. 전체 리뷰 화면으로 돌아가기
3. 취소

선택: 1
```

수정 화면:

```text
현재 입력:
1. sample_reads / role=fastq / format=FASTQ / shape=pair

추천:
- FASTQ paired-end reads preset을 사용하면 role=reads, format=fastq, shape=pair로 정규화됩니다.

무엇을 할까요?

1. FASTQ paired-end reads preset으로 교체
2. 직접 수정
3. 돌아가기
```

---

## 31. 테스트 계획

### 31.1 Field requirement tests

```text
PackageEngine → Defaulted, default conda
ImageDigest → Required, missing이면 final validation에서 blocking violation (CLAUDE.md §3 L1 규칙)
BuildDependencies → Recommended, missing이면 warning
SourceChecksum → Required, missing이면 blocking violation
BuildContext → Defaulted, default current directory
MirrorKind → Optional
DockerfileWarningsAccepted → RecipeFieldCatalog에 없음
```

### 31.2 non-interactive requirement tests

```text
Required 누락 → 실패
Defaulted 누락 → default 적용 후 계속
Recommended 누락 → warning 출력 후 계속
Optional 누락 → 계속
ImageDigest 누락 → Required 누락이므로 실패 (CLAUDE.md §3 L1 규칙)
BuildDependencies 누락 → stderr warning 후 계속
--accept-image-tag-warning 옵션은 존재하지 않음
```

### 31.3 Field composition tests

```text
FieldsFor(method)는 CommonScalarFields → MethodFields → InputsField → OutputsField 순서
Inputs/Outputs는 CommonScalarFields에 없음
BlockingRequiredFieldsFor(method)는 InputsField/OutputsField 포함
Inputs/Outputs는 모든 method에서 Required
```

### 31.4 Recommender tests

```text
내부망 Yes + mirror Yes → mirror 추천
내부망 Yes + mirror Unknown + public package Yes → 확정 추천 없음, mirror/source/container/dockerfile 후보
내부망 Yes + source/container 대안 → 외부 의존성 경고 포함
내부망 No + container Yes + package Yes → container 추천, package alternative
내부망 No + package Yes → package 추천
내부망 Unknown + package Yes → package 추천 + 내부망 경고
Unknown-heavy → 확정 추천 없음
Unknown은 No처럼 배제하지 않음
```

### 31.5 CLI option tests

```text
--method package --engine micromamba → 허용
--method container --engine micromamba → 실패
--method source --engine conda → 실패
--method conda → 실패
--method micromamba → 실패
--method dockerfile --accept-dockerfile-warning --non-interactive → 허용
--accept-dockerfile-warning with non-dockerfile method → 실패 또는 warning
--accept-image-tag-warning → unknown option
```

### 31.6 Method/build-kind resolver tests

```text
method container → RecipeBuildKind.BioContainer
method mirror → RecipeBuildKind.PackageMirror
method source → RecipeBuildKind.SourceBuild
method dockerfile → RecipeBuildKind.DockerfileFallback
method package + PackageEngine=conda → RecipeBuildKind.Conda
method package + PackageEngine=micromamba → RecipeBuildKind.Micromamba
method package + PackageEngine missing after Build() defaults applied → RecipeBuildKind.Conda
method package + PackageEngine missing before Build() defaults applied → contract violation or guard failure
```

### 31.7 ValidateDraft vs final validation tests

```text
ValidateDraft는 RecipeBuildKindResolver를 확정적으로 호출하지 않음
ValidateDraft는 L1 validator를 실행하지 않음
ValidateDraft는 Renderer를 실행하지 않음
ValidateDraft는 cross-field consistency 검증을 하지 않음
ValidateDraft는 Defaulted field 예정값을 표시
ValidateDraft는 invalidated fields를 warning으로 표시
Final validation은 Build → build kind resolve → RecipeValidationPipeline 순서로 실행
```

### 31.8 Build/Snapshot tests

```text
Snapshot은 incomplete session에서도 가능
Snapshot은 missing required fields를 표시
Snapshot은 default 예정값을 표시하지만 실제 적용하지 않음
Build는 IsComplete == false이면 실패
Build는 Defaulted field를 적용
Build 이후 PackageEngine 기본값 conda가 document에 존재
Build 결과만 final validation 입력으로 사용
```

### 31.9 invalidated field tests

```text
/change-method 후 Inputs/Outputs invalidated 표시
invalidated fields는 IsComplete를 막지 않음
invalidated fields는 Build를 막지 않음
ValidateDraft는 invalidated fields warning 표시
사용자가 "그대로 사용" 선택 시 invalidated flag clear
사용자가 field 수정 완료 시 invalidated flag clear
사용자가 preset 재선택 시 invalidated flag clear
```

### 31.10 Preset and normalization tests

```text
FASTQ paired-end preset → role=reads, format=fastq, shape=pair
BAM output preset → role=alignment, format=bam, class=primary
FASTQ 입력 → fastq normalize + 안내 표시
.fastq 입력 → fastq normalize + 안내 표시
fq 입력 → fastq suggestion + 사용자 확인
fastq.gz 입력 → compression suffix warning + fastq suggestion + 사용자 확인
Read Pair role 입력 → read_pair normalize + 안내 표시
```

### 31.11 Change-method tests

```text
method 변경 시 ToolName/ToolVersion 보존
Inputs/Outputs는 임시 보존되지만 새 method 기준 재검증 대상으로 표시
method-specific fields는 discarded 후보로 표시
method-specific metadata는 reset
dockerfile → package 변경 시 DockerfileWarningAccepted reset
container → source 변경 시 ImageTagWarningShown/Accepted reset
package → source 변경 시 Packages/Channels/PackageEngine discarded
```

### 31.12 Recovery tests

```text
단일 field violation → EditSingleField action
Packages+Channels violation → EditRelatedFields action
Input/output violation → ReviewSection action
Generic L1 violation → ShowExplanationOnly + related section hints
검증 실패 후 session 유지
수정 후 재검증 가능
검증 실패 시 final recipe 미작성
```

### 31.13 Golden transcript tests

```text
BWA package happy path
내부망 gate path
내부망 Unknown + package warning path
Unknown-heavy recommendation path
필드 입력 중 /change-method path
invalidated Inputs/Outputs 재확인 path
cross-field validation recovery path
DockerfileFallback warning path
```

---

## 32. v1 구현 순서

권장 구현 순서:

```text
1. RecipeMethodId 도입
2. RecipeFieldRequirement 도입
3. RecipeFieldCatalog를 RecipeMethodId + Requirement 기준으로 구현
4. RecipeFieldCatalog field composition 계약 구현
5. RecipeValidationPipeline 분리
6. RecipeMethodCatalog 구현
7. RecipeMethodQuestionCatalog 구현
8. RecipeMethodRecommender 구현
9. RecipeBuildKindResolver 구현
10. InputOutputPresetCatalog 구현
11. format/role/channel normalization 구현
12. RecipeAuthoringSession 구현
13. Snapshot / Build 계약 구현
14. ValidateDraft 구현
15. ChangeMethod field+metadata reset 구현
16. invalidated field lifecycle 구현
17. RecoveryPlan 구현
18. list edit UX 구현
19. nodekit recipe create CLI 구현
20. non-interactive mode 구현
21. golden transcript tests 추가
```

이 순서의 이유:

```text
method와 build kind 분리를 먼저 해야 session API가 흔들리지 않는다.
field requirement 의미론을 먼저 닫아야 required/default/optional 판정이 갈라지지 않는다.
field composition을 명확히 해야 Inputs/Outputs required 판정이 흔들리지 않는다.
추천 알고리즘은 내부망 gate와 priority table로 고정해야 한다.
ValidateDraft와 final validation을 분리해야 작성 중 잘못된 build kind 기준 검증을 피할 수 있다.
RecoveryPlan이 있어야 fail-closed가 사용자 입력 손실로 이어지지 않는다.
CLI는 마지막에 얇게 붙인다.
```

---

## 33. v1 범위

v1에 포함:

```text
nodekit recipe create
interactive beginner wizard
RecipeMethodId 기반 method 선택
RecipeFieldRequirement 기반 field catalog
method recommendation
internal network gate
external dependency warnings
package engine default conda
--engine micromamba
RecipeBuildKindResolver
input/output presets
known role/format/channel normalization
Snapshot
ValidateDraft
final validation
final review
validation recovery within same session
/change-method with field+metadata reset
invalidated field lifecycle
list item edit
non-interactive mode
golden transcript tests
```

v1에서 제외:

```text
draft resume
실제 package 검색
실제 container image 검색
실제 image pull 확인
실제 source 다운로드
실제 checksum 계산
실제 Docker build
도구별 preset 자동 추천
Compression field
GUI
MCP server
```

v1.1 후보:

```text
draft save/resume
draft schema validation and migration
external package/container search provider
internal/external URI classification
compression field modeling
BWA/samtools/fastqc 등 tool-specific preset
MCP server front end
GUI ViewModel
MirrorKind를 Defaulted 또는 Choice field로 승격
```

---

## 34. 확정된 결정

다음 항목은 v0.7~v0.8에서 확정한다. (24, 25는 v0.8 패치에서 추가)

```text
1. 사용자가 고르는 것은 RecipeMethodId다.
2. RecipeBuildKind는 Build() 이후 final validation/render 직전에 resolve한다.
3. PackageEngine은 Defaulted field이고 기본값은 conda다.
4. --engine micromamba는 v1에 포함한다.
5. ImageRef tag-only는 authoring 중에는 입력을 막지 않지만, final validation(L1)에서 CLAUDE.md §3에 따라 block된다.
6. ImageDigest는 Required field다 — CLAUDE.md §3 L1 digest-pinning 규칙과 일치시킨다.
7. tag-only ImageRef는 accept flag로 우회할 수 있는 warning이 아니라 Required field 누락이므로, 별도 accept flag를 두지 않는다.
8. SourceChecksum은 v1에서 sha256:<64 hex>만 허용한다.
9. role normalize는 snake_case로 한다.
10. fastq.gz는 v1에서 compression warning + format suggestion만 제공한다.
11. Compression field는 v1에 넣지 않는다.
12. Dockerfile warning 수락은 session metadata다.
13. Image tag warning 수락도 session metadata다.
14. method 변경 후 full final validation은 즉시 실행하지 않는다.
15. method 변경 후에는 ValidateDraft 수준의 compatibility warning만 표시한다.
16. Build()는 incomplete session에서 실패한다.
17. Snapshot()은 incomplete session에서도 가능하다.
18. Build()가 Defaulted field 적용 책임을 가진다.
19. ValidateDraft는 default 예정값만 표시하고 실제 적용하지 않는다.
20. Inputs/Outputs는 terminal required authoring section이다.
21. invalidated field는 blocking이 아니라 warning 상태다.
22. invalidated field는 사용자가 재확인하거나 수정하면 해제된다.
23. MirrorKind는 v1에서 Optional이다.
24. BuildDependencies는 v1에서 Recommended다.
25. RecipeFieldRequirement(authoring-level)와 CLAUDE.md §3 reproducibility 규칙(final L1)은 서로 다른 층이며, L1이 block하는 필드는 authoring-level requirement도 Required로 둔다.
```

---

## 35. 남은 결정 질문

이제 남은 질문은 최소화한다.

```text
1. --accept-dockerfile-warning 이름을 확정할 것인가?
2. Recommended field warning을 stderr에 출력할 것인가, review summary에만 표시할 것인가?
3. ValidateDraft compatibility warning의 범위를 단일 필드 + invalidated 표시로만 제한할 것인가?
```

권장 답:

```text
1. --accept-dockerfile-warning으로 확정
2. non-interactive는 stderr, interactive는 review summary와 field-level warning 둘 다 표시
3. v1에서는 단일 필드 + default 예정값 + recommended warning + invalidated warning까지만
```

---

## 36. 결론

v0.7 설계의 핵심은 다음이다.

```text
사용자가 고르는 것은 RecipeBuildKind가 아니라 RecipeMethodId다.
RecipeBuildKind는 method + field 값으로 Build() 이후 final validation 전에 resolve한다.
PackageEngine은 Required가 아니라 Defaulted field다.
Required, Defaulted, Optional, Recommended는 서로 다른 상태다.
RecipeFieldCatalog는 field requirement와 default의 단일 출처다.
Inputs/Outputs는 공통 scalar field가 아니라 terminal required authoring section이다.
ValidateDraft와 final validation은 다르다.
ValidateDraft는 authoring-level check이고 L1 validator를 실행하지 않는다.
Build()는 complete session에서만 가능하며 Defaulted field를 적용한다.
Snapshot()은 incomplete session에서도 가능하다.
ChangeMethod는 field discard, metadata reset, validation state invalidation을 함께 수행한다.
invalidated field는 blocking이 아니라 warning 상태다.
invalidated field는 사용자가 재확인하거나 수정하면 해제된다.
내부망은 최상위 gate이며, package뿐 아니라 container/source/dockerfile의 외부 의존성도 경고한다.
custom role/format/channel은 normalize하되 사용자에게 결과를 보여준다.
검증 실패는 final recipe output만 막고, authoring session은 보존한다.
Dockerfile warning 수락은 recipe field가 아니라 session metadata다.
```

이 구조를 따르면 NodeKit CLI는 단순 JSON 생성기가 아니라, 초보자가 자신의 상황에서 가장 안전한 recipe 작성 경로를 찾아가도록 돕는 authoring wizard가 된다.
