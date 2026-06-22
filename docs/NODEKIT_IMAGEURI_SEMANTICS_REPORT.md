# `ToolDefinition.ImageUri` 의미 정리 보고서 (2026-06-22)

> 범위: `docs/NODEKIT_CLI_RECIPE_SPEC_DRAFT.md` §7 open question #6 ("`ImageUri`
> 가 진짜 무슨 의미인지 확정 안 됨")에 대한 답. 결론만 코드로 반영하고, 실제
> validator 변경은 별도 작업으로 분리 — 이 보고서 자체는 분석/결정 문서이고
> 코드 변경은 포함하지 않음.

## TL;DR

**`ImageUri`는 "빌드가 시작되는, 이미 존재하는 pinned input 이미지"를 가리키는
필드다.** Dockerfile의 `FROM` 라인과 별개의 개념(예: "빌드 결과물을 push할
목적지")이 아니라, 같은 값을 가리켜야 하는 필드다. 이건 추측이 아니라
`ImageUriValidator`의 실제 동작이 강제하는 결론이다 — 아래 근거 참고.

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

이건 proto 설계와도 정확히 일치한다
(`protos/nodevault/v1/nodevault.proto`):

```text
BuildRequest             { image_uri (4), dockerfile_content (5), ... }       // digest 필드 없음
RegisterToolRequest      { image_uri (4), digest (5), ... }                   // 빌드 후, 별도 digest 필드
RegisteredToolDefinition { image_uri (4), digest (5), ... }                   // 빌드 후, 별도 digest 필드
```

빌드 *후* 단계(`RegisterToolRequest`/`RegisteredToolDefinition`)에서는
`image_uri`와 `digest`가 **분리된 필드**로 존재한다 — 즉 "최종 push 위치"와
"그 결과물의 콘텐츠 digest"는 원래부터 별개 개념으로 설계되어 있고, digest는
빌드가 끝난 뒤 NodeVault가 채워 넣는 값이다. `BuildRequest`(빌드 *전* 단계)에는
이 별도 digest 필드가 없다 — 즉 `BuildRequest.image_uri`에 NodeKit이 직접
digest를 박아 넣어야 하는 지금 구조에서, 그 필드가 "최종 push 위치"라는
의미라면 NodeKit이 빌드도 하기 전에 결과물의 digest를 알아야 한다는 모순이
생긴다. 모순 없이 일관되는 해석은 하나뿐이다 — **`image_uri`는 빌드 단계가
다르더라도 항상 "그 시점에 이미 존재가 확인된, pinned된 이미지 참조"를
가리킨다.** 빌드 전(`BuildRequest`)에는 입력(base) 이미지, 빌드 후
(`RegisterToolRequest`/`RegisteredToolDefinition`)에는 그 입력 이미지 자체이거나
NodeVault가 결정한 최종 위치 — 어느 쪽이든 "사후에 알게 되는 디지털 콘텐츠
해시"가 아니다.

## 2. 결론: `ImageUri` ≡ Dockerfile `FROM`의 base image

위 근거로부터, `ToolDefinition.ImageUri`는 그 Dockerfile의 `FROM` 라인이
가리키는 base image와 **같은 참조를 가리켜야 하는 필드**다. 별도 필드로 존재
하는 이유는 개념이 달라서가 아니라, 실용적인 이유다 — `ImageUriValidator`는
단순 문자열 정규식 검증이고, `DockerfileStructureValidator`는 Dockerfile을
파싱해야 하므로 비용이 더 크다. 같은 값을 두 경로로 따로 검증할 수 있게
만들어 둔 것으로 보인다 (확정은 아니고, 가장 설명력 있는 해석).

`RecipeRenderer`가 모든 variant에서 `ImageUri`에 그 variant의 pinned base
image(`BaseImage` 또는 `BioContainerImageUri`)를 그대로 채워 넣는 것은 이
결론과 정확히 일치한다 — 이전 초안에서 "확정되지 않은 임시 선택"이라고 적어둔
부분을 이번 보고서로 confirmed semantics로 승격할 수 있다.

## 3. 실제로 남는 문제: 두 필드가 서로 다른 값이어도 지금은 안 막힌다

`DockerfileStructureValidator.ValidateBaseImagePinning`
(`src/Validation/DockerfileStructureValidator.cs:96-117`)은 Dockerfile의
`FROM` 라인만 보고, `latest` 금지(`L1-DOCKER-008`)와 `@sha256:` 존재
(`L1-DOCKER-009`, **substring `Contains` 체크라 `ImageUriValidator`의
정규식 검증보다 약함**)만 확인한다. **`ToolDefinition.ImageUri` 값은 전혀
참조하지 않는다.**

그 결과, 지금 코드 상태로는 — 둘 다 "독립적으로 pinned"이기만 하면 — `ImageUri`
에 레지스트리 A의 digest, Dockerfile `FROM`에 레지스트리 B의 다른 digest를
넣어도 `RequiredFieldsValidator`/`ImageUriValidator`/
`DockerfileStructureValidator` 모두 통과한다. 이건 의도된 유연성이 아니라
**검증 공백**이다 — 2번 결론대로 두 값이 같은 개념을 가리켜야 한다면, 서로
달라도 통과하는 지금 동작은 "재현성 보장"이라는 프로젝트의 핵심 철학
(CLAUDE.md §3)과 어긋난다. 어떤 이미지에서 실제로 빌드됐는지(`FROM`)와
NodeKit/NodeVault가 그 빌드를 "어떤 이미지"라고 기록하는지(`ImageUri`)가
달라질 수 있다는 뜻이기 때문이다.

## 4. 정리 옵션

**Option A (권장) — 문서로 의미 확정 + 추후 cross-field 검증 추가**

- 지금 당장: 이 보고서의 결론을 `NODEKIT_CLI_RECIPE_SPEC_DRAFT.md` §7
  open question #6에 반영해 "resolved"로 갱신 (코드 변경 없음, 문서만).
- 별도 작업(이번 라운드 범위 밖, 새 커밋/새 결정으로 분리): `ImageUri`와
  Dockerfile `FROM` base image가 일치하는지 검증하는 새 규칙을 추가한다
  (예: `L1-IMG-006` 또는 `DockerfileStructureValidator` 쪽에 새 규칙). 이건
  기존 validator의 동작을 바꾸는 것이라 CLAUDE.md §9 표("L1 rule change → 새
  규칙에 대한 직접 테스트, pass + block 케이스") 적용 대상이고, §7
  "small diffs" 원칙상 이번 문서 정리 커밋과 묶지 않고 따로 진행해야 한다.

**Option B — 의미를 확정하지 않고 현재 동작만 문서화**

- `ImageUri`를 "지금은 base image와 동일한 값을 넣지만, 향후 다른 의미로
  쓰일 수도 있는 필드"로 애매하게 남겨둔다. 코드 변경 전혀 없음.
- 단점: 위 3번 검증 공백이 그대로 남고, 그 공백이 "의도된 것"인지 "버그"인지
  다음에 또 같은 질문이 반복된다. 재현성이 핵심 철학인 프로젝트에서 이 모호함을
  영구히 남겨두는 건 권장하지 않는다.

**권장: Option A.** 의미 확정 자체는 코드 변경 없이 지금 문서에 반영 가능하고,
실제 검증 공백을 메우는 작업은 의도적으로 분리해 별도로 검토/구현한다.

## 5. 이번에 실제로 바뀌는 것 / 안 바뀌는 것

- **바뀜(문서만)**: `NODEKIT_CLI_RECIPE_SPEC_DRAFT.md` §7 질문 #6을 이
  보고서 결론으로 resolved 처리.
- **안 바뀜(이번 라운드)**: validator 코드, `RecipeRenderer` 동작 — 둘 다
  이미 결론과 일치하는 상태라 고칠 필요가 없다.
- **다음 결정이 필요한 것**: `ImageUri` ↔ Dockerfile `FROM` cross-field
  검증 규칙 추가 여부와 시점. 사용자 검토 후 별도 작업으로 진행할지 결정.
