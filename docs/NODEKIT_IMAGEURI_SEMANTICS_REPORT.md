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

## 4. 멀티스테이지 Dockerfile 정책 — 결정됨

**Decision.** 멀티스테이지 Dockerfile은 허용한다. 단, builder 스테이지를
예외로 두지 않고 모든 `FROM` instruction에 재현성 규칙을 동일하게
적용한다.

```text
- First FROM must match ToolDefinition.ImageUri.
- Every FROM must be digest-pinned with @sha256.
- Every FROM must reject the latest tag.
- No builder-stage exception.
- Package-level reproducibility inside stages is a separate follow-up.
```

**근거**

1. **Builder stage도 최종 산출물에 영향을 준다.** Builder stage 자체는
   최종 이미지 레이어에 직접 남지 않을 수 있지만, 그 안에서 컴파일한
   바이너리/생성한 인덱스 등 빌드 산출물은 `COPY --from`으로 최종 stage에
   복사된다. 예:

   ```dockerfile
   FROM golang:latest AS builder
   RUN go build -o app

   FROM debian:12@sha256:...
   COPY --from=builder /src/app /usr/local/bin/app
   ```

   `golang:latest`가 바뀌면 컴파일러/libc/빌드 도구 버전이 바뀌고, 그 결과
   `app` 바이너리도 달라질 수 있다. "최종 stage만 고정하면 충분하다"는
   전제는 이 경로를 놓친다.

2. **FROM digest pinning은 author 비용이 낮다.** `image:tag@sha256:...`
   한 줄을 추가하는 정도라, builder stage라고 면제해서 얻는 실익이 크지
   않다. 반대로 면제를 두면 재현성 규칙(CLAUDE.md §3)에 케이스별 예외가
   생긴다 — 이 프로젝트는 `latest`/digest 미고정에 예외/완화 플래그를
   두지 않는다는 원칙을 이미 갖고 있고, builder stage 예외도 그 성격의
   완화에 해당한다.

3. **stage 내부 패키지 pinning은 별개 문제다.** builder stage 안에서
   `apt-get install -y gcc`처럼 OS 패키지를 받는 부분도 재현성에 영향을
   주지만, 이건 `FROM` 이미지 자체의 pinning과는 다른 검증 표면이다(이미
   `PackageVersionValidator`가 conda/micromamba만 보고 apt/apk/yum/pip/npm
   은 보지 않는 것과 같은 종류의 별도 gap). 이번 결정은 `FROM` image
   pinning만 다루고, stage 내부 패키지 pinning은 분리된 후속 이슈로
   남긴다.

4. **최종 stage 판별 로직은 도입하지 않는다.** "최종 stage만 강제, builder
   는 예외"를 구현하려면 어느 stage가 최종인지, `COPY --from`이 어떤
   stage를 참조하는지를 분석해야 한다 — 구현 복잡도와 버그 표면이 늘어난다.
   이번 정책은 stage 구분 없이 모든 `FROM`에 같은 규칙을 적용하는 단순한
   경로를 택한다.

**현재 schema 한계.** legacy `ToolDefinition`은 `ImageUri` 필드가
하나뿐이라, 멀티스테이지의 모든 base image를 표현할 방법이 없다.

```text
Current legacy ToolDefinition has a single ImageUri field.

Therefore:
- ToolDefinition.ImageUri represents the first Dockerfile FROM image.
- The first FROM must match ToolDefinition.ImageUri.
- Additional FROM instructions are validated at the Dockerfile level.
- Additional FROM images are not yet normalized into a baseImages[] model.
```

향후 ToolSpec/Recipe 쪽에서는 스테이지별 base image를 구조화하는 모델을
고려할 수 있다 —

```json
{
  "baseImages": [
    { "stage": "builder", "image": "golang:1.22@sha256:..." },
    { "stage": "final", "image": "debian:12@sha256:..." }
  ]
}
```

이번 Sprint에서는 `baseImages[]` 모델을 새로 도입하지 않는다 — legacy
`ToolDefinition`의 단일 `ImageUri` 구조를 그대로 두고, Dockerfile 자체의
정적 검증만 확장한다.

## 5. 규칙 ID 정리 (충돌 해결)

기존에 사용 중인 ID를 확인한 결과:

```text
L1-IMG-001 ~ L1-IMG-005        (ImageUriValidator)
L1-DOCKER-001 ~ L1-DOCKER-010  (DockerfileStructureValidator)
```

`L1-DOCKER-010`은 이미 다른 규칙(빌드 컨텍스트 source의 ARG/ENV 변수
참조 차단)에 쓰이고 있어 "모든 FROM 검증"용으로 재사용할 수 없다. 의미는
유지하되 ID는 다음처럼 조정한다:

```text
L1-DOCKER-008 (기존 ID 유지, 적용 범위만 확장):
  Every Dockerfile FROM image must not use the latest tag.
  (지금까지 첫 번째 FROM에만 적용 → 모든 FROM에 적용으로 확장)

L1-DOCKER-009 (기존 ID 유지, 적용 범위만 확장):
  Every Dockerfile FROM image must be digest-pinned with @sha256.
  (지금까지 첫 번째 FROM에만 적용 → 모든 FROM에 적용으로 확장)

L1-IMG-006 (신규):
  ToolDefinition.ImageUri must match the first Dockerfile FROM image.
```

"모든 FROM에 latest 금지/digest 필수"는 기존 `L1-DOCKER-008`/`009`가
이미 표현하는 위반 종류와 같다 — 차이는 *검사 범위*(첫 번째 instruction만
→ 전체 `FROM` instruction)뿐이므로, 새 ID를 만드는 대신 기존 ID의 검사
범위를 넓히는 쪽으로 정리했다(`L1-DOCKER-011`은 만들지 않음). `ImageUri`
↔ 첫 번째 `FROM` 비교는 기존에 없던 cross-field 검증이라 새 ID
(`L1-IMG-006`)가 필요하고, `ImageUriValidator` 쪽 책임으로 둔다 — 두
검증기 모두 같은 `ToolDefinition` 인스턴스를 받으므로 구현상 제약은 없다.

## 6. Follow-up 구현 계획 (제안, 이번 라운드에서 코드 변경 없음)

**변경 범위 (작게 유지)**

- `src/Validation/DockerfileStructureValidator.cs`: `ValidateFromInstruction`
  호출을 `instructions[0]`에서 `instructions.Where(i => i.Cmd == "FROM")`
  전체로 확장. `L1-DOCKER-002`/`003`(첫 instruction은 FROM이어야 함)은
  구조 검사라 의미상 그대로 첫 번째 instruction 위치만 본다 — 바뀌는 건
  pinning 검사(`L1-DOCKER-008`/`009`) 범위뿐이다.
- `src/Validation/ImageUriValidator.cs`: 첫 번째 `FROM`의 base image를
  추출해 `definition.ImageUri`와 비교하는 `L1-IMG-006` 검사 추가. 기존
  `DockerfileStructureValidator`가 쓰는 `DockerfileParser`를 재사용해
  파싱 로직을 중복 구현하지 않는다.

**하지 않는 것**

```text
- builder stage 예외 만들기
- 최종 stage만 검증하는 정책 만들기
- stage graph / COPY --from 분석 도입
- baseImages[] schema를 이번 Sprint에 도입
- apt/apk/yum/pip/npm package pinning까지 한 번에 해결하려고 하기
```

**테스트 계획**

```text
통과:
  FROM ubuntu:22.04@sha256:aaa... AS builder   (ToolDefinition.ImageUri와 일치)
  RUN echo build
  FROM debian:12@sha256:bbb...
  COPY --from=builder /x /x

실패 1 — 첫 번째 FROM이 ImageUri와 불일치 (L1-IMG-006)
실패 2 — 두 번째 FROM이 latest (L1-DOCKER-008, 확장된 범위)
실패 3 — 두 번째 FROM에 digest 없음 (L1-DOCKER-009, 확장된 범위)
실패 4 — builder stage(첫 FROM 아님)가 latest여도 차단됨, 예외 없음 확인
```

이 절은 **구현 계획 제안이며, 이번 라운드에서 코드를 변경하지 않는다.**
CLAUDE.md §9(L1 rule 변경 시 pass/block 케이스 직접 테스트)와 §7
(small diffs)에 따라, 실제 구현은 이 계획에 대한 별도 확인 후 별도
커밋으로 진행한다.

## 7. 이번에 실제로 바뀌는 것 / 안 바뀌는 것

- **바뀜(문서만)**: `NODEKIT_CLI_RECIPE_SPEC_DRAFT.md` §7 질문 #6을
  이 보고서 결론(0번 Scope 포함)으로 resolved 처리. 멀티스테이지 정책(4번)
  과 규칙 ID 정리(5번)도 문서에 확정.
- **안 바뀜(이번 라운드)**: validator 코드, `RecipeRenderer` 동작, proto —
  전부 변경 없음. 6번의 구현 계획은 제안일 뿐 아직 코드로 옮기지 않았다.
- **다음 단계**: 6번 구현 계획에 대한 go-ahead를 받으면 별도 커밋으로
  `DockerfileStructureValidator`/`ImageUriValidator`를 수정하고 4개
  테스트 케이스를 추가한다.

## 8. 최종 결론

**Accepted.**

NodeKit allows multi-stage Dockerfiles, but reproducibility rules apply
uniformly to every `FROM` instruction. The first `FROM` must match
`ToolDefinition.ImageUri`. Every `FROM`, including builder stages, must
be digest-pinned and must not use the `latest` tag. Builder stages are
not exempt because they can affect final build outputs. Package-level
reproducibility inside stages remains a separate follow-up issue.

이 결정은 NodeKit authoring-time `ToolDefinition.ImageUri`와 현재 legacy
`BuildRequest.ImageUri`에만 적용된다 — post-build
`RegisterToolRequest.image_uri` / `RegisteredToolDefinition.image_uri`
의미는 이 보고서가 재정의하지 않는다(0번 Scope). 실제 validator 구현은
6번 계획에 대한 별도 확인 후 별도 작업으로 진행한다.
