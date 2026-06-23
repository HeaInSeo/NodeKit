# NodeKit CLI Recipe Authoring UX v0.7 — Terminology Patch

> **Status: design only, nothing in this document is implemented yet.**
> No `RecipeBuildKind`, `RecipeMethodId`, or `RecipeBuildKindResolver` type
> exists in code today — the implemented enum is still `RecipeVariant`
> (`src/Authoring/Recipes/RecipeVariant.cs`). This patch defines the target
> naming and the new method/build-kind split for the authoring session
> proposed in
> [`NODEKIT_RECIPE_AUTHORING_SESSION_DESIGN_DRAFT.md`](NODEKIT_RECIPE_AUTHORING_SESSION_DESIGN_DRAFT.md);
> it does not by itself change any shipped code.

## 용어 수정 원칙

이 문서는 bioinformatics / genomics 사용자를 대상으로 한다.
따라서 `variant`라는 표현은 사용하지 않는다.

이유:

```text
variant는 유전체 분야에서 보통 "유전 변이"를 의미한다.
NodeKit의 이미지 작성 방식이나 내부 렌더링 종류를 variant라고 부르면,
사용자가 유전체 variant와 혼동할 수 있다.
```

따라서 문서 전체에서 다음 용어를 사용한다.

| 기존 표현                   | 수정 표현                     | 비고                              |
| ----------------------- | ------------------------- | ------------------------------- |
| `variant`               | `build kind`              | 내부 렌더링/빌드 종류                    |
| `RecipeVariant`         | `RecipeBuildKind`         | 내부 enum 이름                      |
| `RecipeVariantResolver` | `RecipeBuildKindResolver` | method + fields → build kind 결정 |
| `internal variant`      | `internal build kind`     | 설명 문장용                          |
| `method/variant split`  | `method/build-kind split` | 아키텍처 설명용                        |

---

## 사용자-facing method와 내부 build kind는 다르다

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

## RecipeBuildKind

```csharp
internal enum RecipeBuildKind
{
    BioContainer,
    Conda,
    Micromamba,
    PackageMirror,
    SourceBuild,
    DockerfileFallback
}
```

`RecipeBuildKind`는 사용자에게 직접 노출하지 않는다.

CLI, GUI, MCP에서는 다음 표현을 사용한다.

```text
작성 방법
method
```

---

## RecipeBuildKindResolver

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

## 문서 내 금지 표현

문서 본문에서는 다음 표현을 쓰지 않는다.

```text
variant
RecipeVariant
RecipeVariantResolver
internal variant
variant-specific
method/variant
```

대신 다음처럼 쓴다.

```text
build kind
RecipeBuildKind
RecipeBuildKindResolver
internal build kind
build-kind-specific
method/build-kind
```

---

## 수정 예시

기존:

```text
사용자-facing method와 내부 RecipeVariant는 다르다.
```

수정:

```text
사용자-facing method와 내부 RecipeBuildKind는 다르다.
```

기존:

```text
RecipeVariant는 Build() 이후 final validation/render 직전에 resolve한다.
```

수정:

```text
RecipeBuildKind는 Build() 이후 final validation/render 직전에 resolve한다.
```

기존:

```text
ValidateDraft는 RecipeVariantResolver를 확정적으로 호출하지 않는다.
```

수정:

```text
ValidateDraft는 RecipeBuildKindResolver를 확정적으로 호출하지 않는다.
```

기존:

```text
method package + PackageEngine=conda → RecipeVariant.Conda
```

수정:

```text
method package + PackageEngine=conda → RecipeBuildKind.Conda
```

---

## 최종 용어 규칙

```text
사용자에게 보이는 것은 method다.
내부 렌더링/빌드 분기는 build kind다.
variant라는 단어는 문서와 CLI에서 사용하지 않는다.
```

이 규칙은 NodeKit이 유전체 분석 도구를 대상으로 하기 때문에 중요하다.
`variant`는 유전체 분야에서 이미 강한 의미를 가진 단어이므로, 이미지 작성 방식이나 빌드 종류를 설명하는 용어로 사용하지 않는다.
