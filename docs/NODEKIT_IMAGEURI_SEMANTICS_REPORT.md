# `ToolDefinition.ImageUri` 의미 정리 보고서 (2026-06-22)

> 범위: `docs/NODEKIT_CLI_RECIPE_SPEC_DRAFT.md` §7 open question #6 ("`ImageUri`
> 가 진짜 무슨 의미인지 확정 안 됨")에 대한 답. 결론만 문서로 반영하고, 실제
> validator 변경은 별도 작업으로 분리 — 이 보고서 자체는 분석/결정 문서이고
> 코드 변경은 포함하지 않음.

## 0. Scope (반드시 먼저 읽을 것)

**This report only resolves NodeKit authoring-time
`ToolDefinition.ImageUri` and the current legacy `BuildRequest.ImageUri`
usage. It does not redefine post-build `RegisterToolRequest.image_uri` or
`RegisteredToolDefinition.image_uri`, which may represent the registered
output image reference.**

이 보고서는 **NodeKit authoring 단계의 `ToolDefinition.ImageUri`와 현재
legacy `BuildRequest.ImageUri` 의미만 확정한다.** 빌드 후 단계의
`RegisterToolRequest.image_uri` / `RegisteredToolDefinition.image_uri`는
최종 등록 이미지 ref를 의미할 수 있으므로, 이 보고서에서 동일 의미로
재정의하지 않는다. 아래에서 proto의 `RegisterToolRequest`/
`RegisteredToolDefinition`을 언급하는 부분은 "`BuildRequest` 시점에
digest 필드가 없다는 사실"을 보여주기 위한 **대조 근거**일 뿐이며, 그
두 메시지의 `image_uri` 자체에 대한 결론은 아니다. (post-build 단계는
NodeVault 소유 영역이라 NodeKit이 단정할 수 있는 정보도 아니다.)

## TL;DR

NodeKit authoring 단계에서 `ToolDefinition.ImageUri`(그리고 그것을 그대로
옮기는 현재 legacy `BuildRequest.ImageUri`)는 **"빌드가 시작되는, 이미
존재하는 pinned input/base 이미지"**를 가리키는 필드다. Dockerfile의
`FROM` 라인과 별개의 개념(예: "빌드 결과물을 push할 목적지")이 아니라, 같은
값을 가리켜야 하는 필드다. 이건 추측이 아니라 `ImageUriValidator`의 실제
동작이 강제하는 결론이다 — 아래 1번 근거 참고.

`RecipeRenderer`가 지금 `ImageUri = BaseImage`(또는 `BioContainerImageUri`)로
채우는 동작은 **placeholder가 아니라 올바른 동작이었다.** 다만 기존
`DockerfileStructureValidator`가 `ImageUri`와 무관하게 Dockerfile의 `FROM`만
따로 검증하기 때문에, 둘이 서로 다른 값이어도 현재는 막히지 않는다 — 이게
실제로 정리가 필요한 gap이다 (아래 3번).

## 1. 근거: 빌드 전 digest 강제가 의미를 확정한다

`src/Validation/ImageUriValidator.cs`는 `ImageUri`에 대해 다음을 **빌드를
요청하기 전 시점**에 강제한다:

- `latest` 태그 금지 (`L1-IMG-002`)
- 태그 필수 (`L1-IMG-003`)
- `@sha256:<64-hex>` digest 필수 (`L1-IMG-004`, `L1-IMG-005`)

이 시점(NodeKit이 `BuildRequest`를 보내기 전)에 NodeVault는 아직 아무것도
빌드하지 않았다. **빌드 결과물의 digest는 빌드가 끝난 뒤에만 존재한다** —
이건 OCI 이미지의 digest 정의 자체가 콘텐츠 해시이기 때문에 당연하다. 따라서
"빌드 전에 이미 digest가 고정되어 있어야 하는 값"이라면, 그 값은 논리적으로
**아직 만들어지지 않은 결과물**을 가리킬 수 없고, **이미 존재하는 입력
이미지**만 가리킬 수 있다.

이건 proto 구조와도 대조해보면 더 분명해진다
(`protos/nodevault/v1/nodevault.proto`, 참고용 대조일 뿐 — 0번 Scope 참고):

```text
BuildRequest             { image_uri (4), dockerfile_content (5), ... }  // digest 필드 없음
RegisterToolRequest      { image_uri (4), digest (5), ... }             // 빌드 후, 별도 digest 필드
RegisteredToolDefinition { image_uri (4), digest (5), ... }             // 빌드 후, 별도 digest 필드
```

`BuildRequest`(빌드 *전* 단계)에는 별도 `digest` 필드가 없다 — 그 시점에
`ImageUri` 자체에 digest를 직접 박아 넣어야 한다는 뜻이고, `ImageUriValidator`
가 정확히 그걸 강제한다. 빌드가 끝난 뒤의 두 메시지(`RegisterToolRequest`,
`RegisteredToolDefinition`)에는 `image_uri`와 `digest`가 분리된 별도
필드로 존재한다는 사실은, 적어도 "빌드 후 단계에서는 image_uri와 digest가
별개로 다뤄질 수 있는 구조"라는 것만 보여준다 — 그 단계의 `image_uri`가
정확히 무엇을 의미하는지(최종 등록 위치인지, 입력 이미지를 그대로 보존한
것인지)는 NodeVault 쪽 결정이고, 이 보고서가 답할 범위가 아니다(0번 Scope).

이 보고서가 단정할 수 있는 것은 **`BuildRequest.ImageUri`만이다**: 그
시점에는 분리된 digest 필드가 없으므로, `ImageUri` 자신이 이미
digest-pinned 상태여야 하고, 그건 "이미 존재하는 입력 이미지"만 가리킬 수
있다는 뜻이다.

## 2. 결론: authoring 단계의 `ImageUri` ≡ Dockerfile `FROM`의 base image

위 근거로부터, **NodeKit authoring 단계의** `ToolDefinition.ImageUri`(및
현재 legacy `BuildRequest.ImageUri`)는 그 Dockerfile의 `FROM` 라인이
가리키는 base image와 **같은 참조를 가리켜야 하는 필드**다. 별도 필드로 존재
하는 이유는 개념이 달라서가 아니라, 실용적인 이유로 보인다 —
`ImageUriValidator`는 단순 문자열 정규식 검증이고,
`DockerfileStructureValidator`는 Dockerfile을 파싱해야 하므로 비용이 더
크다. 같은 값을 두 경로로 따로 검증할 수 있게 만들어 둔 것으로 보인다
(확정은 아니고, 가장 설명력 있는 해석).

```text
recipe.BaseImage
  → ToolDefinition.ImageUri
  → Dockerfile FROM
```

`RecipeRenderer`가 모든 variant에서 `ImageUri`에 그 variant의 pinned base
image(`BaseImage` 또는 `BioContainerImageUri`)를 그대로 채워 넣는 것은 이
결론과 정확히 일치한다 — 이전 초안에서 "확정되지 않은 임시 선택"이라고 적어둔
부분을 이번 보고서로 confirmed semantics로 승격할 수 있다.

**BioContainer variant**도 같은 논리로 정리된다: 현재 legacy 경로에서
BioContainer image URI는 "NodeKit-side no-build 최종 등록 경로"가 아니라,

```text
BioContainer image URI
  → pinned external input/base image
  → NodeVault-owned materialization의 입력
```

이다. NodeKit은 pinned image URI를 recipe input으로 NodeVault에 넘기고,
그 입력을 wrapper build / mirror / copy / reject 중 어떤 방식으로
materialize할지는 NodeVault가 결정한다. NodeKit 쪽에서 "이미 등록된 최종
이미지"로 취급해 빌드를 건너뛰는 것은 아니다.

## 3. 실제로 남는 문제: 두 필드가 서로 다른 값이어도 지금은 안 막힌다

`DockerfileStructureValidator.ValidateBaseImagePinning`
(`src/Validation/DockerfileStructureValidator.cs:96-117`)은 Dockerfile의
첫 번째 `FROM` 라인만 보고, `latest` 금지(`L1-DOCKER-008`)와 `@sha256:` 존재
(`L1-DOCKER-009`, **substring `Contains` 체크라 `ImageUriValidator`의
정규식 검증보다 약함**)만 확인한다. **`ToolDefinition.ImageUri` 값은 전혀
참조하지 않는다.**

그 결과, 지금 코드 상태로는 — 둘 다 "독립적으로 pinned"이기만 하면 — 다음처럼
서로 다른 값을 넣어도 모든 validator가 통과한다:

```text
ToolDefinition.ImageUri:
  registry-a.example.com/base:1.0@sha256:aaaa...

Dockerfile FROM:
  registry-b.example.com/base:1.0@sha256:bbbb...
```

이건 의도된 유연성이 아니라 **검증 공백**이다 — 2번 결론대로 (NodeKit
authoring 단계에서) 두 값이 같은 개념을 가리켜야 한다면, 서로 달라도
통과하는 지금 동작은 "재현성 보장"이라는 프로젝트의 핵심 철학
(CLAUDE.md §3)과 어긋난다. 어떤 이미지에서 실제로 빌드됐는지(`FROM`)와
NodeKit이 그 빌드를 "어떤 이미지"라고 기록하는지(`ImageUri`)가 달라질 수
있다는 뜻이기 때문이다.

## 4. 후속 규칙 후보 (이번 라운드에서 구현하지 않음)

cross-field 검증 규칙을 추가한다면 규칙 ID는 기존 체계와 충돌하지 않게
정해야 한다. 현재 사용 중인 ID:

```text
L1-IMG-001 ~ L1-IMG-005     (ImageUriValidator)
L1-DOCKER-001 ~ L1-DOCKER-010  (DockerfileStructureValidator — 010까지 이미 사용 중, ARG/ENV 변수 참조 차단)
```

`L1-DOCKER-010`은 이미 다른 규칙(빌드 컨텍스트 source의 변수 참조 차단)에
쓰이고 있어 재사용할 수 없다. 빈 번호인 **`L1-IMG-006`**이 더 적합하다 —
의미상으로도 "`ImageUri`와 Dockerfile의 관계를 검증한다"는 건
`ImageUriValidator`(또는 그 확장) 쪽 책임으로 보는 게 자연스럽다.

```text
L1-IMG-006: ToolDefinition.ImageUri must match the first Dockerfile FROM base image.
```

이 규칙 자체는 **이번 라운드에서 구현하지 않는다.** CLAUDE.md §9
("L1 rule change → 새 규칙에 대한 직접 테스트, pass + block 케이스")와 §7
("small diffs")에 따라 별도 커밋/별도 결정으로 분리한다.

## 5. 후속 이슈: 멀티스테이지 Dockerfile

위 4번 규칙을 실제로 구현하기 전에 먼저 결정해야 할 것이 있다 —
`DockerfileStructureValidator`는 지금 **첫 번째 `FROM`만** 검사한다
(`instructions[0]`). 멀티스테이지 Dockerfile은 검증되지 않는 `FROM`을
가질 수 있다:

```dockerfile
FROM ubuntu:22.04@sha256:...
RUN echo build

FROM alpine:latest
COPY --from=0 /x /x
```

여기서 두 번째 `FROM alpine:latest`는 `latest` 태그인데도 현재
`ValidateFromInstruction`이 `instructions[0]`만 보기 때문에 전혀 검사되지
않는다. `L1-IMG-006`(또는 그 대안)을 설계할 때 다음을 같이 결정해야 한다 —
이번 보고서는 질문만 남기고 답하지 않는다:

```text
1. 첫 번째 FROM은 ToolDefinition.ImageUri와 반드시 같아야 하는가?
2. 모든 FROM에 digest pinning을 강제해야 하는가?
3. 모든 FROM에서 latest를 금지해야 하는가?
4. 멀티스테이지를 허용할 것인가, 아니면 현재 Sprint에서는 단일 FROM만 허용할 것인가?
```

## 6. 이번에 실제로 바뀌는 것 / 안 바뀌는 것

- **바뀜(문서만)**: `NODEKIT_CLI_RECIPE_SPEC_DRAFT.md` §7 질문 #6을 이
  보고서 결론(0번 Scope 포함)으로 resolved 처리.
- **안 바뀜(이번 라운드)**: validator 코드, `RecipeRenderer` 동작, proto —
  전부 변경 없음. authoring 단계 동작은 이미 결론과 일치하는 상태라 고칠
  필요가 없다.
- **다음 결정이 필요한 것**: `L1-IMG-006`(`ImageUri` ↔ Dockerfile `FROM`
  cross-field 검증) 추가 여부와, 그 전에 먼저 정해야 하는 멀티스테이지
  Dockerfile 처리 방침(5번). 둘 다 사용자 검토 후 별도 작업으로 진행할지
  결정.

## 7. 최종 결론

**Accepted with scope restriction.**

NodeKit authoring-time `ToolDefinition.ImageUri` and the current legacy
`BuildRequest.ImageUri` mean the pinned input/base image and should match
Dockerfile `FROM`. `RecipeRenderer`'s current `BaseImage` reuse is
therefore correct. However, this decision does not redefine post-build
`RegisterToolRequest.image_uri` or `RegisteredToolDefinition.image_uri`,
which may represent the registered output image reference. Cross-field
validation between `ImageUri` and Dockerfile `FROM` remains a separate
follow-up task (candidate rule `L1-IMG-006`), and that follow-up must
first decide how multistage Dockerfiles are handled (Section 5) before
implementation.
