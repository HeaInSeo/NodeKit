# NodeKit SourceBuild Structured Intent — Design (R22 pre-implementation)

Status: **설계 확정, 구현 미착수**
Created: 2026-07-12
Related: GitHub Issue [#36](https://github.com/HeaInSeo/NodeKit/issues/36),
`docs/NODEKIT_CLI_FIRST_SPRINT_PLAN.md` §13 (R18-R21, 완료),
`docs/NODEVAULT_LIVE_RECIPE_REPRO_IMPROVEMENT_NODEKIT.md` (원본 목표 설계)

이 문서는 Issue #36에서 남긴 "열린 질문"에 대한 최종 결론이다. 구현은 아직
시작하지 않았다 — 이 문서 자체가 산출물이고, 실제 코드 변경은 아래 §11
후속 이슈들이 각자 별도로 진행한다.

## 0. 이 문서에서 하지 않는 것 (범위 밖, 명시적 금지)

다음은 이번 설계 확정 작업의 범위가 아니며, 실제로 구현하지 않았다:

- `RecipeRenderer.RenderSourceBuild`를 3-stage Dockerfile로 전환
- `FetchImage`/`BuilderImage`/`RuntimeImage`를 wizard 필수 질문으로 추가
- SourceBuild Dockerfile에 `ENTRYPOINT` 추가
- 존재하지 않는 NodeVault production API 추가
- legacy `BuildRequest` 제거
- `DockerfileFallback` 동작 변경
- NodeVault 저장소의 어떤 파일도 수정 (읽기 전용으로만 조사)

## 1. 문제 정의

`RecipeRenderer.RenderSourceBuild`(`src/Authoring/Recipes/RecipeRenderer.cs:109-144`)가
만드는 Dockerfile은 단일 스테이지다:

```dockerfile
FROM <BaseImage>
RUN curl -fsSL -o source.tar.gz "<SourceUri>" && \
    echo "<sha256>  source.tar.gz" | sha256sum -c - && \
    tar -xzf source.tar.gz && \
    <SourceBuildCommands>
USER 1000
```

`BaseImage` 하나가 (a) 소스를 내려받는 fetch 환경과 (b) 도구를 실행하는 최종
runtime 이미지 역할을 동시에 한다. 2026-07-08 seoy-libvirt-cilium 실 클러스터
라이브 테스트(`docs/NODEKIT_LOCAL_GRPC_TEST_SCENARIO.md` §12,
NodeVault `docs/NODEKIT_LIVE_RECIPE_REPRO_TEST_2026-07-08.md` F-01)에서
확인된 결과:

| Base image | 결과 |
|---|---|
| `alpine:3.20` | FAIL — `curl: not found` |
| `condaforge/miniforge3` | FAIL — `curl: not found` |
| `curlimages/curl` | PASS — 그러나 fetch 전용 이미지가 그대로 최종 실행 이미지가 됨 |

R20(`SourceBuildBaseImageAdvisor`, 커밋 `82aacff`)이 known fetch-전용
이미지 패턴에 대해 non-blocking 경고를 추가했지만, 구조 자체는 그대로라
경고를 무시하면 여전히 재현된다.

## 2. 가장 먼저 확인한 구조적 제약 (증거 기반)

Issue #36을 등록할 때는 "NodeVault가 구조화된 intent를 내부적으로
해석해 stage를 나눌 수 있는가"가 미확인 상태였다. 이번에 NodeVault
저장소를 읽기 전용으로 직접 조사해 답을 확정했다.

### 2.1 proto/BuildRequest는 Dockerfile을 다시 쓰지 않는다

`protos/nodevault/v1/nodevault.proto:195`(NodeVault 저장소, 원문 그대로):

> `// BUILD_KIND_TOOLSPEC: base image from build recipe. NodeVault does not rewrite`

`raw_spec`(신규 `ToolSpecRequest` 경로)도 구조가 다른 게 아니라, 기존
`BuildRequest`를 그대로 JSON 직렬화한 것이다 —
`pkg/build/submit_tool_build.go:151-154`: `json.Unmarshal([]byte(spec.RawSpec), &req)`를
`*nfv1.BuildRequest`에 그대로 언마샬한다. 즉 legacy 경로와 신규 경로가
와이어 스키마 수준에서는 동일하다.

### 2.2 Builder 인터페이스는 문자열 하나만 받는다

`pkg/build/builder.go:24-27`(NodeVault 저장소, 원문 그대로):

```go
type Builder interface {
    Build(ctx context.Context, dockerfileContent, outputRef string) (imageID, digest string, err error)
    Close() error
}
```

`podbridge5Builder.Build`(`pkg/build/builder.go:93`)는
`podbridge5.BuildDockerfileContentUserNamespace(ctx, b.store, dockerfileContent, cfg)`를
직접 호출한다 — fetch/build/runtime을 구분하는 개념이 이 경로 어디에도
없다. NodeVault는 문자열 하나를 그대로 Buildah에 넘긴다.

### 2.3 그런데 Buildah는 멀티스테이지를 이미 지원하고, NodeVault의 정적 검증도 이미 스테이지별로 동작한다

`pkg/build/validate_test.go:58-70`(NodeVault 저장소, 원문 그대로 —
`/opt/dotnet/src/github.com/HeaInSeo/NodeKit`에서 직접 실행해 재확인함):

```go
func TestValidateBuildRequest_RejectsEveryUnpinnedStage(t *testing.T) {
	req := &nfv1.BuildRequest{
		ToolName: "bwa",
		DockerfileContent: "FROM alpine:3.20@" + validDigestA + " AS builder\n" +
			"RUN true\n" +
			"FROM busybox:1.36\n" +
			"COPY --from=builder /tmp /tmp",
	}
	err := ValidateBuildRequest(req)
	if err == nil || !strings.Contains(err.Error(), "busybox") {
		t.Fatalf("ValidateBuildRequest error got %v, want second FROM rejection", err)
	}
}
```

이 테스트는 (a) `AS builder`+두 번째 `FROM`+`COPY --from=builder`로
구성된 진짜 멀티스테이지 Dockerfile이 구조적으로 이미 받아들여지고,
(b) 두 번째 스테이지(`busybox:1.36`, digest 없음)가 정확히 거부됨을
증명한다 — `validateDockerfilePolicy`(`pkg/build/validate.go:45-70`)가
모든 `FROM`을 순회하며 digest pinning을 검사하기 때문이다(NodeKit의
`DockerfileStructureValidator.ValidateAllFromInstructionsPinning`과
같은 종류의 검증을 NodeVault도 이미 하고 있다는 뜻).

### 2.4 DFM001-004(서버 쪽 진짜 멀티스테이지 계약)는 코드가 아니라 문서다

`DockGuard` 저장소 `policy/dockerfile/multistage.rego`에는 정확히 이번
설계가 원하는 계약이 **Rego 주석으로만** 적혀 있다: 사용자 Dockerfile은
`FROM ... AS builder` 하나만 허용(DFM001/002), `final` 스테이지 이름은
예약(DFM003, "시스템이 자동 생성합니다"), `COPY --from=builder` 금지(DFM004).

그러나 `docs/PLATFORM_MAP.md:42`(NodeVault 저장소, 원문): "DockGuard WASM
직접 실행은 아직 NodeVault build path에 연결되지 않았고, 정책 drift 축소를
위한 후속 작업으로 추적한다." NodeVault의 실제 서버 쪽 게이트
(`pkg/build/validate.go`)는 DFM001-004를 전혀 강제하지 않는다 — 여러 개의
`FROM`을 허용하고, `AS builder`를 요구하지 않고, `final`을 예약하지 않고,
`COPY --from=builder`를 막지 않는다(§2.3의 테스트가 정확히 이걸 증명한다 —
2-stage Dockerfile이 구조 자체로는 통과했다).

### 2.5 로드맵(P2)은 "stage를 나눠주는 것"이 아니라 "마지막 stage를 감시하는 것"이다

`docs/PLATFORM_SCHEDULE.md`(NodeVault 저장소)의 Sprint 9/10(P2a/P2b,
둘 다 체크박스 미완료 `[ ]`):

- **Sprint 9 (static policy)**: `riskyRuntimeTools` 차단 목록(`curl, wget,
  git, ssh, scp, apt, apt-get, apk, yum, dnf, mamba, conda, micromamba, gcc,
  g++, clang, make, cmake`)을 **마지막 `FROM` 이후의 `RUN`에만** 적용 —
  "중간 빌드 스테이지는 허용"이라고 명시. `allow_runtime_tools`/
  `allow_runtime_tools_reason` proto 필드 추가 예정.
- **Sprint 10 (post-build scan)**: 빌드된 이미지를 tar-export해서 실제
  바이너리를 스캔 — `podbridge5`의 상류 공개 API 이슈에 막혀 있다고
  기재.

**중요한 뉘앙스**: 이 로드맵은 "NodeVault가 fetch/build/runtime을
합성(synthesize)한다"가 아니라, "**클라이언트가 이미 멀티스테이지로
낸 Dockerfile의 마지막 스테이지를 NodeVault가 감시(police)한다**"는
설계다. Stage를 나누는 주체는 여전히 클라이언트(Dockerfile 작성자)라는
전제다.

### 2.6 다섯 가지 구조적 질문에 대한 결론

Issue #36에서 던진 질문에 증거와 함께 명확히 답한다.

**Q1. 현재 API만으로 NodeVault가 내부 멀티스테이지 계획을 만들 수 있는가?**

아니오, "구조화된 intent를 받아 NodeVault가 내부적으로 stage를
합성한다"는 의미로는 불가능하다 — 그런 해석 계층(Build Planner)이
코드에 전혀 없다(§2.2, §2.4).

**다만** 다른 의미로는 이미 가능하다: `dockerfile_content`는 완전히
투명한 문자열이고(§2.1) Buildah는 멀티스테이지를 네이티브로 지원하며
NodeVault의 정적 검증은 이미 스테이지별로 정확히 동작한다(§2.3). 즉
**NodeKit이 클라이언트 쪽에서 완성된 멀티스테이지 Dockerfile 문자열을
만들어 기존 `dockerfile_content` 필드로 제출하면, NodeVault 코드를 단
한 줄도 바꾸지 않고도 오늘 당장 동작한다.** 이건 "NodeVault가 stage를
나눠준다"는 이상적인 그림과는 다르지만, 최종 이미지에서 fetch/build
도구를 빼는 실질적 목표는 지금 API로 달성 가능하다.

**Q2. 불가능하다면 어떤 구조화된 SourceBuild intent가 추가로 필요한가?**

- 지금 당장 가능한 범위(NodeKit 내부 모델만): §5에서 정의하는
  Source/Build/Export/Runtime intent 필드들. `BuildRequest`/`raw_spec`
  와이어 스키마는 전혀 바꾸지 않는다 — `RecipeRenderer`가 오늘처럼
  Dockerfile 문자열을 만드는데, 그 로직이 더 정교해질 뿐이다.
- 진짜 이상형(NodeVault가 stage를 소유): `dockerfile_content` 대신
  구조화된 intent(파일 하나가 아니라 소스/빌드/런타임을 구분하는 필드)를
  받는 새 API. 이건 존재하지 않고, 지금 만들 수도 없다(NodeVault
  코드 변경 금지).

**Q3. 그 intent는 NodeKit 내부 모델 / legacy BuildRequest 확장 /
향후 ToolSpec API 중 어디에 속해야 하는가?**

지금 만들 수 있는 부분(§5)은 **NodeKit 내부 모델에만** 속한다 — 와이어
계약을 바꾸지 않으므로 legacy `BuildRequest`도, 신규 `ToolSpecRequest`
`raw_spec`도 손댈 필요가 없다(둘 다 이미 §2.1에서 확인했듯 같은
"불투명한 Dockerfile 문자열" 스키마이기 때문). 진짜 구조화 전달(Q2의
두 번째 항목)은 `raw_spec`을 확장하는 게 아니라 **완전히 새로운
API/메시지**여야 한다 — `dockerfile_content`는 본질적으로 "불투명한
문자열"로 설계되어 있어 구조화된 intent를 얹기에 적합하지 않다.

**Q4. AGENTS.md의 legacy API 유지 방침과 충돌하지 않고 단계적으로
도입할 수 있는가?**

지금 가능한 범위(§5)는 전혀 충돌하지 않는다 — 와이어 프로토콜을
바꾸지 않는 순수 NodeKit 내부 변경이라 legacy 경로도 신규 경로도
그대로 유지된다. 미래의 진짜 구조화 API는, Phase 6이 "NodeVault
Phase 1 gate 열림"을 전제로 열렸던 것과 같은 패턴 — 즉 NodeVault
쪽에 새 capability가 갖춰진 뒤 게이트가 열리는 방식 — 을 따르면
된다. 다만 그 capability는 현재 로드맵(§2.5)에 없다.

**Q5. 현재 API에 필요한 정보가 없는데도 NodeKit renderer만 수정해
문제가 해결된 것처럼 만들 위험은 없는가?**

**있었다 — 이 설계에서 가장 중요하게 짚어야 할 위험이었고, 지금은
부분적으로만 남아 있다(2026-07-13 갱신, 적대적 리뷰 Major-1 대응,
Issue #41).**

원래 이 문단을 쓸 당시(설계 확정 시점) 클라이언트 쪽 멀티스테이지
렌더링(§2.1의 "당장 가능한" 경로)만 구현하고 멈추면 서버 쪽에는 그걸
강제하는 장치가 전혀 없다고 판단했다. 그런데 NodeVault가 같은 날
(2026-07-13) **Sprint 9 P2a를 실제로 머지했다**(`pkg/build/validate.go`,
커밋 `645c594`) — 최종 스테이지 RUN 라인에서 curl/wget/git/make 등
risky tool을 정적으로 스캔해 거부하는 서버 쪽 검사가 지금 실제로
동작한다. 그래서 이 위험은 다음과 같이 좁혀졌다:

- **이제 있음**: 최종 스테이지 RUN이 risky tool을 직접 호출/설치하면
  NodeVault가 거부한다(정적 텍스트 스캔). `SourceBuildStructured`의
  2-stage 출력은 최종 스테이지에 RUN이 아예 없어 이 검사를 자연스럽게
  통과한다.
- **여전히 없음**: base image 자체가 이미 risky tool을 포함하는
  경우(예: `curlimages/curl`을 RuntimeProfileImage로 직접 지정)는
  정적 텍스트 스캔으로 탐지 불가능하다 — 빌드된 이미지의 실제 콘텐츠를
  봐야 하는 Sprint 10(post-build 이미지 스캔)이 필요하고, 이건 아직
  미구현(podbridge5 issue #2 선행 필요, NodeVault
  `docs/PLATFORM_SCHEDULE.md` 확인).
- NodeKit CLI를 거치지 않고 `SubmitToolBuild`/`BuildAndRegister`를
  직접 호출해도 Sprint 9 검사는 서버 쪽에서 여전히 적용된다(클라이언트
  우회 불가) — 다만 Sprint 10 몫인 "이미 포함된 도구" 케이스는 그
  검사망에도 안 걸린다.
- 이전에 등록한 `HeaInSeo/NodeVault#16`("SubmitToolBuild이
  DockerfileContent를 서버 쪽 재검증 없이 그대로 Buildah에 넘김")은
  이제 정확한 설명이 아니다 — dockerfile_content는 여전히 재작성되지
  않지만(§2.1), 최소한 risky-tool 정책 재검증은 이제 존재한다. 해당
  이슈도 좁혀서 갱신이 필요하다(NodeVault 담당, NodeKit이 대신 닫지
  않음).

이번 설계(및 R22-C 구현)가 "SourceBuild 보안 문제를 완전히
해결했다"는 주장은 여전히 하지 않는다 — Sprint 10이 남아 있는 한
"base image에 이미 있는 도구" 클래스는 열려 있다. §8에서
NodeKit/NodeVault/NodeSentinel 책임 경계를 명시하고, §11에서
NodeVault 쪽 upstream 의존성으로 명확히 분리해 기록한다.

## 3. 보안 목적

멀티스테이지 분리의 목적은 이미지 크기 최적화가 아니다. **빌드
환경의 강력한 도구(컴파일러, 패키지 매니저, curl/wget/git, SSH
클라이언트, 소스 트리, 빌드 캐시)가 실제 실행 환경으로 전파되지
않게 막는 보안 경계**다. ToolSpec 이미지는 이후 ToolFunctionSpec
이미지의 base가 되므로(`NODEKIT_NODEVAULT_REPRO_IMPROVEMENT_NODEVAULT.md`
P2 참조), 여기서 새어나간 도구는 그 이후 모든 파생 이미지에도
전파된다.

## 4. 사용자-facing 모델 — 방식 A vs 방식 B

Issue #36과 이번 지시 모두 "이미지 세 개(FetchImage/BuilderImage/
RuntimeImage)를 그대로 wizard 질문으로 만들지 않는다"는 원칙을 명시했다.
비교:

**방식 A (이미지 직접 입력)**: `FetchImage`, `BuilderImage`,
`RuntimeImage` 세 필드를 그대로 입력받음. 사용자가 직접 정확한
이미지+digest를 세 번 골라야 한다.

**방식 B (profile/preset 중심)**: "빌드 환경 프로필"(예: conda/C-C++
toolchain/Python/Rust/기타)과 "런타임 프로필"(예: 최소 런타임/Python
런타임/conda 런타임/기타)을 고르게 하고, 각 프로필이 내부적으로
큐레이션된 이미지+digest에 매핑된다. 고급 사용자만 "직접 이미지
입력"으로 override.

### 결정: 방식 B를 기본으로, 방식 A는 고급 옵션으로 유지

**이유**:
- `NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md` §2.4가 이미
  `PackageEngine`을 Defaulted+숨김 필드로 다루는 선례가 있다 — 내부
  구현 세부사항(어떤 패키지 매니저)을 초보자에게 노출하지 않는
  기존 철학과 정확히 같은 패턴이다.
- 이미 `BaseImageCatalog`(`src/NodeKit.Cli/`)가 "큐레이션된 후보
  목록 + `0`으로 직접 입력" 패턴을 구현하고 있다 — 방식 B는 이
  기존 UX를 그대로 재사용하는 것이지 새 패턴을 만드는 게 아니다.
- §2.1 "Unknown is not false" 원칙과 정확히 맞는다 — 초보자가
  "빌드 환경이 뭐가 필요한지 모른다"고 답해도 프로필 기본값으로
  진행할 수 있다.

### 확정 결정 (2026-07-13): 3-profile이 아니라 2-profile(Build/Runtime)

원본 목표 설계(`NODEVAULT_LIVE_RECIPE_REPRO_IMPROVEMENT_NODEKIT.md`)는
fetch/builder/final 3-stage를 제시했다. 하지만:
- curl/tar/sha256sum은 아주 가벼운 요구사항이라, "빌드 환경" 프로필이
  이 도구들을 기본으로 포함하도록 큐레이션하면 fetch 전용 스테이지를
  따로 둘 이유가 줄어든다(라이브 테스트에서 실패한 alpine/miniforge3도
  "빌드 환경"으로 큐레이션된 이미지가 아니라 사용자가 직접 고른
  BaseImage였다는 점에 주목).
- 프로필 질문이 3개에서 2개로 줄면 초보자 wizard 진입장벽이 낮아진다.

2026-07-13 사용자 확인으로 **2-profile(Build/Runtime)을 확정**한다 —
더 이상 "권장, 확정 아님" 상태가 아니다. §10 결정표 D-2b, §11 Phase B의
`RecipeFieldCatalog` 작업은 이 결정을 전제로 진행한다. 다만 실제
큐레이션 작업(어떤 이미지가 정말 fetch 도구를 포함하는지, Sprint R22-B
범위) 과정에서 2-profile로 도저히 충분하지 않다고 판명되면 3-profile로
되돌릴 수 있게 렌더러 내부 로직은 확장 가능하게 설계한다(사용자 모델
자체는 영향받지 않도록) — 이건 "재검토 가능성"이지 지금 결정을
번복하는 게 아니다.

## 5. Resolved Build Plan — 내부 모델 (NodeKit 전용, 와이어 프로토콜 불변)

`RecipeDocument`에 추가할 필드(전부 `RecipeBuildKind.SourceBuildStructured`
전용, §9 참조):

```text
Source intent (기존 필드 재사용)
  SourceUri            — 기존
  SourceChecksum       — 기존
  (archive 형식/source root는 v1에서 tar.gz 고정, 확장은 후속)

Build intent
  BuildProfile         — Choice 필드, 큐레이션된 프리셋(예: "generic",
                          "conda", "compiler-toolchain") + "advanced: custom
                          image" 옵션
  BuildProfileImage    — BuildProfile == advanced일 때만 활성화되는 직접
                          입력 필드(기존 BaseImageField() 패턴 재사용)
  SourceBuildCommands  — 기존 필드 그대로 재사용(L1-RCP-015 개행 차단 포함)
  BuildDependencies    — 기존 필드, 이제 BuildProfile 큐레이션과 실제
                          연동(R21에서는 경고만 했지만 이제 profile이
                          실제로 해석 가능한 신호가 됨 — 다만 "자동
                          설치"는 여전히 하지 않음, §7 참조)

Export intent (신규 필드 없음 — 관례로 처리)
  고정 export root 경로(예: /nodekit/output/)를 SourceBuildCommands의
  도움말/예시에 문서화. 사용자 필드로 만들지 않는다 — Dockerfile
  COPY --from 문법을 사용자에게 노출하지 않기 위해서다(원칙 #3).

Runtime intent
  RuntimeProfile        — Choice 필드, 큐레이션된 런타임 프리셋(예:
                          "minimal", "python", "conda-runtime") + "advanced:
                          custom image"
  RuntimeProfileImage    — RuntimeProfile == advanced일 때만
  RuntimeDependencies    — 신규 필드, BuildDependencies와 분리(원본
                          목표 설계의 요청 그대로) — v1에서는 R21과
                          동일하게 "경고만, 자동 설치 안 함" 정책
  (실행 사용자는 기존처럼 렌더러가 USER 1000을 고정 부여 — 신규
  필드 없음, §7 참조)
```

이 필드들은 모두 `RecipeFieldCatalog.MethodFields[RecipeMethodId.Source]`에
`RecipeFieldDescriptor`를 추가하는 것만으로 구현 가능하다 —
`RecipeAuthoringSession`의 완료 판정/리스트 버퍼링/`Build()`는 전부
카탈로그 기반이라 세션 파일 자체를 고칠 필요가 거의 없다(SourceBuild
전용 하드코딩은 `RecipeAuthoringSession.cs`에 `SourceChecksumRecoveryAction`
하나뿐 — 근거: 직접 소스 코드 조사).

`RecipeRenderer.RenderSourceBuild`(새 build kind 전용 새 메서드, 기존
메서드는 legacy `SourceBuild`용으로 그대로 유지)는 이 profile들을
해석해 client-side로 완성된 Dockerfile 문자열을 만든다 — §2.6 Q1에서
확인했듯 이건 NodeVault의 `dockerfile_content` 계약을 그대로 쓴다.
2-stage(Build/Runtime) 기본형 결과물 예시:

```dockerfile
FROM <BuildProfileImage>@sha256:<digest> AS builder
RUN curl -fsSL -o source.tar.gz "<SourceUri>" && \
    echo "<sha256>  source.tar.gz" | sha256sum -c - && \
    tar -xzf source.tar.gz && \
    <SourceBuildCommands>

FROM <RuntimeProfileImage>@sha256:<digest>
COPY --from=builder /nodekit/output/ /
USER 1000
```

`ENTRYPOINT`는 추가하지 않는다(§7). `Script`/`Command`는 지금처럼
`ToolDefinition`/`BuildRequest`를 통해 별도 경로로 전달된다.

## 6. 산출물 export 계약

비교한 5가지 대안과 최종 선택은 §10 결정표 D-3에 기록한다. 결론만
요약: **고정 export root 관례**(대안 3)를 선택한다 — `SourceBuildCommands`가
빌드 결과물을 정해진 경로(예: `/nodekit/output/`) 아래에 설치하도록
안내 문구/예시로 유도하고, 최종 스테이지는 그 디렉터리 전체를
`COPY --from=builder /nodekit/output/ /`로 복사한다. 이 방식은 단일
실행 파일뿐 아니라 복수 바이너리, 공유 라이브러리, 설정 파일, 데이터
디렉터리, symbolic link, Python 런타임 패키지 트리까지 전부 "디렉터리
트리 복사"라는 동일 메커니즘으로 자연스럽게 지원한다 — 파일별로
경로를 열거하는 필드가 필요 없다.

이 경로명과 `COPY --from` 구문 자체는 **사용자 계약이 아니라 내부
구현 세부사항**이다 — 원칙 #3을 지키기 위해 recipe 필드로 노출하지
않는다.

## 7. Script, Command, USER — 충돌 방지

- `Script`/`Command`는 지금처럼 실행 계약으로 유지한다. 코드
  조사로 확인: `ToolDefinition.Command`의 문서 주석(`src/Authoring/
  ToolDefinition.cs`)은 "Dockerfile CMD를 대체한다. ENTRYPOINT가
  아니다"라고 명시하고, 어떤 `RecipeRenderer.Render*` 메서드도 CMD나
  ENTRYPOINT를 Dockerfile에 쓰지 않는다 — 실행 계약은 항상
  `BuildRequest`/`ToolDefinition` 필드를 통해 K8s 런타임 레벨에서
  주입된다. 원본 목표 설계 문서의 `ENTRYPOINT ["tool"]` 스케치는
  이 아키텍처와 맞지 않으므로 **채택하지 않는다** — 이것이 이번
  설계에서 원본 문서를 수정해야 할 부분이다(§12).
- fetch/build 스테이지는 편의를 위해 root로 실행될 수 있다(원칙 #6).
  non-root 검증(L1-RCP-009류)은 최종 runtime 스테이지 또는 최종
  생성 Dockerfile 기준으로 해석한다 — 새 구조에서는 렌더러가 최종
  스테이지 마지막 줄에 `USER 1000`을 고정 부여하는 지금 방식(#29)을
  그대로 유지한다. 빌드 스테이지에는 USER를 부여하지 않는다.
- 기존 `SourceBuildCommands` 개행 차단(L1-RCP-015, #31)은 어느
  스테이지에 배치되든 계속 유효하다 — 이 필드가 여전히 하나의
  RUN 라인으로 합쳐지는 한 값을 검증해야 하는 이유가 그대로 남아있다.

## 8. 보안 정책 — 책임 경계

```text
NodeKit
→ authoring validation과 UX 경고 (지금 있는 것: SourceBuildBaseImageAdvisor,
  BuildDependenciesAdvisor, L1-RCP-015 등). 새 구조에서는 profile 기반
  안내로 대체/확장된다. NodeKit은 fetch/build 도구 잔존 여부, compiler/
  package manager 잔존 여부를 "authoring 시점 힌트"로만 판단할 수 있다
  — 실제 빌드된 이미지를 열어볼 수 없기 때문이다.

NodeVault Build Planner/BuildService
→ 오늘은 이 역할이 사실상 없다(§2.2/2.4) — Buildah에 문자열을 그대로
  넘길 뿐이다. Sprint 9(P2a, 로드맵에만 존재)가 구현되면 "마지막
  FROM 이후의 RUN"에서 risky tool을 정적으로 스캔하는 역할을 맡게
  된다. Sprint 10(P2b)이 구현되면 실제 빌드된 이미지를 스캔하는
  역할을 맡는다. **둘 다 지금은 없다** — 이번 설계의 client-side
  멀티스테이지 렌더링은 이 역할을 대신하지 않는다.

NodeSentinel
→ L3/L4 dry-run/smoke-run 및 최종 이미지·실행 결과 관측. 이번 조사
  범위에서 NodeSentinel 코드까지 직접 확인하지는 않았다 — Sprint
  10(P2b)의 이미지 컨텐츠 스캔이 이 계층에 속할 가능성이 높다는
  것만 로드맵 문서에서 확인했다(§2.5). **이 부분은 추론이며 코드로
  확인하지 않았음을 명시한다.**

NodeVault
→ 최종 정책 결과와 artifact metadata 보존(image_digest, lifecycle_phase,
  integrity_health 등) — 기존 §10(전 세션 기록)의 NodeKit↔NodeVault
  계약 검토에서 이미 확인한 그대로, 변경 없음.
```

**핵심 결론**: 이번 설계(§5)가 구현되어도 "최종 이미지가 실제로
깨끗한지"를 강제하는 서버 쪽 장치는 여전히 없다. 그 장치는
NodeVault Sprint 9/10이 구현해야 생긴다 — §11에서 upstream 의존성으로
명시한다.

## 9. 하위 호환 정책

기존 `draft-1` SourceBuild recipe는 `BaseImage` 필드 하나만 쓴다.
비교한 5가지 대안은 §10 결정표 D-1에 기록한다. 최종 선택:

**새 `RecipeBuildKind.SourceBuildStructured` 값을 추가하고, 기존
`RecipeBuildKind.SourceBuild`는 그대로 둔다.**

- 기존 `SourceBuild`(단일 `BaseImage`) recipe: 지금처럼 단일 스테이지로
  렌더링을 계속한다. 자동 추론/자동 업그레이드 없음(명시적으로
  금지된 "새 필드가 비어 있으면 조용히 폴백" 패턴을 피하기 위해).
  `SourceBuildBaseImageAdvisor`(R20)도 이 kind에 대해서만 계속
  동작한다.
- `RecipeMethodId.Source`(사용자가 보는 "source" 방식) wizard가 새
  구조를 지원하게 되면(Phase C 이후), 신규 authoring 세션은 기본적으로
  `SourceBuildStructured`를 생성한다. 기존 `SourceBuild` kind는 **읽기/
  검증/렌더/제출은 계속 지원하되, wizard가 신규 생성 옵션으로 더는
  제시하지 않는다** — 이미 저장된 recipe나 non-interactive 자동화
  스크립트를 깨뜨리지 않으면서, 새로 만드는 사람은 자연히 안전한
  경로로 유도된다.
- `RecipeDocument.SchemaVersion`(`"draft-1"`)은 이번 필드 추가로
  값 자체를 올릴 필요는 없다고 판단한다 — 새 필드들은 전부 additive이고
  `BuildKind`로 이미 명시적으로 구분되기 때문이다. 다만 이 필드가
  이미 "schema version" 목적으로 존재하므로, 만약 이후 기존 필드의
  의미가 바뀌는 변경이 생기면 그때는 `SchemaVersion`을 올려야 한다는
  점을 후속 작업자에게 남겨둔다.

**금지된 패턴 재확인**: "새 필드가 비어 있으면 아무 설명 없이 기존
단일-stage 방식으로 자동 폴백"은 구현하지 않는다 — `BuildKind`가
명시적 마커 역할을 하므로 애초에 그런 폴백 로직 자체가 필요 없는
설계다.

## 10. 결정표

| 질문 | 검토한 선택지 | 선택 | 선택 이유 | 포기한 대안 | 후속 영향 |
|---|---|---|---|---|---|
| D-1. 하위 호환 | (1) legacy 항상 단일-stage (2) 자동 추론 (3) 명시적 migration (4) legacy 읽기 전용, 신규 생성 금지 (5) legacy 경고/차단 | (1)+(4) 조합: legacy 렌더링 유지 + wizard가 신규 생성으로 더는 제시하지 않음 | (2)는 명시적 마커 원칙 위반(보안 의미가 조용히 바뀜). (5)는 기존 자동화 스크립트를 깨뜨림. (3)은 사용자에게 강제 작업을 요구해 UX상 과함 | (2) 자동 추론, (5) 강제 차단 | 새 `RecipeBuildKind.SourceBuildStructured` 필요(§9) |
| D-2. 사용자 모델 | 방식 A(이미지 직접) vs 방식 B(profile) | 방식 B 기본 + 방식 A는 advanced override | 기존 `PackageEngine`/`BaseImageCatalog` UX 패턴과 일치, 초보자 wizard 철학과 일치 | 방식 A를 기본으로 하는 안 | `BuildProfile`/`RuntimeProfile` Choice 필드 + advanced override 필드 필요 |
| D-2b. 스테이지 개수 | 3-stage(fetch/build/final) vs 2-stage(build/final) | **2-stage 확정(2026-07-13)** | 프로필 질문 수 감소, fetch 도구를 build profile 큐레이션 책임으로 흡수 가능 | 3-stage 고정 | 큐레이션 작업에서 fetch 도구 부족이 반복되면 3-stage로 되돌릴 수 있게 렌더러 내부를 설계 |
| D-3. 산출물 export | (1) 단일 BuiltArtifactPath (2) 여러 artifact mapping (3) 고정 export root (4) install/export 명령 분리 (5) profile이 export 규칙 제공 | (3) 고정 export root | 파일 개수/종류에 무관하게 "디렉터리 복사" 하나로 일반화됨, 신규 필드 불필요 | (1)은 복수 파일 케이스를 못 다룸, (2)/(4)는 필드가 늘어나 UX 복잡도 증가 | `SourceBuildCommands` 도움말에 관례 문서화 필요, 사용자 필드는 추가하지 않음 |
| D-4. 이 설계의 소유 계층 | NodeKit 내부 모델 / legacy BuildRequest 확장 / 신규 ToolSpec API | NodeKit 내부 모델만 (지금 가능한 범위) | 와이어 프로토콜 불변 — legacy도 신규 경로도 안 건드림(§2.6 Q3) | BuildRequest 확장(§2.1이 "불투명 문자열" 설계라 부적합함을 증명) | NodeVault 쪽 진짜 구조화 API는 별도 upstream 의존성(§11)으로 분리, 지금 만들지 않음 |
| D-5. SourceBuildBaseImageAdvisor 거취 | 유지 / 폐기 / runtime hygiene advisor로 변경 / heuristic-실제검사 분리 | 레거시 `SourceBuild` kind에는 그대로 유지, `SourceBuildStructured`에는 새 advisor(RuntimeProfileHygieneAdvisor류) 별도 작성 | 레거시 recipe는 여전히 단일 BaseImage 구조라 기존 advisor 로직이 계속 맞음; 새 kind는 profile 기반이라 다른 휴리스틱이 필요 | 완전 폐기(레거시 recipe에 대한 경고가 없어짐) | 새 advisor는 최종 스테이지(RuntimeProfileImage)만 검사하도록 범위를 좁혀야 함 |
| D-6. ENTRYPOINT | 원본 설계대로 추가 vs 추가 안 함 | 추가 안 함 | 코드 조사로 확인: 어떤 build kind도 CMD/ENTRYPOINT를 Dockerfile에 쓰지 않음, `Command` 필드가 이미 그 역할(BuildRequest 경유)을 함 | 원본 목표 설계의 `ENTRYPOINT ["tool"]` | §12에서 원본 개선 문서를 이 결론에 맞게 수정 필요 |
| D-7. USER 처리 | 매 스테이지 vs 최종 스테이지만 | 최종 스테이지만(기존 #29 방식 유지, 위치만 재해석) | 원칙 #6(fetch/build는 root 허용)과 정확히 일치 | 모든 스테이지에 USER 강제 | 렌더러가 최종 스테이지 마지막 줄에만 USER 1000 부여 |
| D-8. BuildDependencies/RuntimeDependencies 분리 | 통합 유지 vs 분리 | 분리(원본 요청 그대로) — 단, 둘 다 "경고만, 자동 설치 안 함" 정책 유지(R21 정책 승계) | 자동 설치는 pin/snapshot 정책 없이 추가하면 새 재현성 문제를 만든다는 원본 문서의 경고를 존중 | BuildDependencies 자동 설치 로직 추가 | 신규 `RuntimeDependencies` 필드, advisor 확장 필요 |

## 11. 단계별 구현 계획

```text
Phase A — 문서와 계약 확정
  이 문서(NODEKIT_SOURCEBUILD_STRUCTURED_INTENT_DESIGN.md) 작성.
  Issue #36 갱신, 후속 이슈 분리(아래 목록).
  완료 시점: 이 커밋.

Phase B — NodeKit authoring model에 구조화된 SourceBuild intent 도입
  RecipeBuildKind.SourceBuildStructured 추가.
  RecipeDocument에 BuildProfile/BuildProfileImage/RuntimeProfile/
  RuntimeProfileImage/RuntimeDependencies 필드 추가.
  RecipeFieldCatalog에 큐레이션된 profile 선택지 추가(초기 큐레이션은
  BaseImageCatalog의 기존 후보 재사용 가능 여부부터 검토).
  RecipeValidator에 새 kind 전용 검증(digest pinning은 두 이미지
  모두에 적용, L1-RCP-015류는 그대로 재사용).
  RecipeBuildKindResolver.Resolve에 새 매핑 추가.
  이 Phase는 NodeVault/NodeSentinel 어느 쪽도 건드리지 않는다.

Phase C — RecipeRenderer가 client-side 멀티스테이지 Dockerfile을 합성
  RenderSourceBuildStructured(신규 메서드, 기존 RenderSourceBuild는
  legacy kind 전용으로 그대로 둠) 구현 — §5의 2-stage(또는 3-stage)
  템플릿.
  기존 dockerfile_content 필드/BuildRequest/raw_spec 와이어 계약은
  전혀 바꾸지 않는다(§2.6 Q1의 "지금 가능한 경로").
  이 Phase가 끝나면 최종 이미지에서 빌드 도구가 빠지는 실질적 효과가
  생긴다 — 릴리스 노트/문서에는 §2.6 Q5(2026-07-13 갱신)대로
  "최종 스테이지 RUN 정적 검사는 서버 쪽에 있음(Sprint 9), base
  image에 이미 포함된 도구 탐지는 아직 없음(Sprint 10 필요)"으로
  명시해야 한다 — "서버 쪽 강제가 전혀 없다"는 표현은 더 이상
  정확하지 않다.

Phase D — NodeKit 쪽 hygiene advisor 갱신
  RuntimeProfileHygieneAdvisor(가칭) 작성 — RuntimeProfileImage/
  RuntimeProfile 선택 기준 최종 스테이지 관점의 경고.
  SourceBuildBaseImageAdvisor는 legacy SourceBuild kind 전용으로 범위
  축소.

Phase E — NodeVault 쪽 서버 강제 (upstream 의존성, NodeKit이 만들지 않음)
  NodeVault Sprint 9(P2a, static risky-tool RUN scan) — **완료**
  (2026-07-13, 커밋 `645c594`). 최종 스테이지 RUN의 risky tool을
  정적으로 거부.
  NodeVault Sprint 10(P2b, post-build image scan) — 아직 구현 대기,
  podbridge5 상류 이슈 해소 필요.
  Sprint 9가 끝나서 §8/§2.6 Q5의 위험이 절반 해소됐다 — "base image에
  이미 포함된 도구" 케이스만 Sprint 10을 더 기다려야 한다. NodeKit
  세션은 이 Phase를 추적만 한다([[project_nodevault_parallel_agent]]
  메모리 참조).

Phase F — legacy SourceBuild migration/제거 정책
  Phase B/C/D가 충분히 안정화된 뒤 판단. 이번 문서에서는 결정하지
  않는다 — §9에서 이미 "legacy는 읽기/렌더/제출 계속 지원, wizard
  신규 생성만 제한"으로 잠정 결론을 냈고, 완전 제거 여부는 별도
  논의가 필요하다.
```

## 12. 원본 개선 문서와의 차이점 (수정 필요 사항)

`docs/NODEVAULT_LIVE_RECIPE_REPRO_IMPROVEMENT_NODEKIT.md`는 이 설계의
출발점이었지만, 코드 조사 결과 다음 부분이 실제 아키텍처와 맞지 않아
이번 문서가 대체/보정한다:

1. **`ENTRYPOINT ["tool"]`** — 원본 문서의 conceptual output에 있었지만,
   §7/D-6에서 확인했듯 NodeKit의 실행 계약은 `Script`/`Command`이지
   Dockerfile `ENTRYPOINT`가 아니다. 이번 설계는 추가하지 않는다.
2. **3-stage(fetch/builder/final) 고정** — §4/D-2b에서 2-stage 축소를
   권장안으로 제시(확정 아님, 큐레이션 결과에 따라 조정 가능하게
   설계).
3. **"NodeVault가 stage를 나눈다"는 암묵적 전제** — 원본 문서는
   "NodeKit should define the recipe/UX contract first; NodeVault
   should then enforce"라고만 적어 이 부분을 명확히 하지 않았다.
   이번 설계는 §2.6에서 증거를 들어 명확히 한다: 지금 당장은 **NodeKit
   client-side 렌더링**으로 실질적 격리 효과를 내고, "NodeVault가
   진짜로 stage를 소유"하는 건 별도 upstream 작업(Phase E)이라는 걸
   분리했다.

이 문서를 원본 개선 문서보다 SourceBuild 관련 부분에서 우선한다.
원본 문서 자체는 수정하지 않고 그대로 둔다(다른 항목 — R18/R19의
근거 — 은 여전히 유효하므로).

## 13. Non-goals (이번 설계의 영구적 범위 밖)

- BuildDependencies/RuntimeDependencies 자동 설치(§10 D-8) — pin/snapshot
  정책 없이는 하지 않는다.
- 사용자에게 Dockerfile stage 이름이나 `COPY --from` 문법을 노출하는 것.
- NodeVault의 실제 이미지 콘텐츠 스캔(Sprint 10/P2b) — NodeKit이 대신
  구현하지 않는다.
- DockerfileFallback을 SourceBuild의 구조화된 계약으로 강제 전환하는 것
  — DockerfileFallback은 별도 escape hatch로 그대로 유지한다. 최종
  이미지 hygiene 정책(risky tool 경고 등)은 NodeVault Sprint 9
  (2026-07-13 완료)부터 build kind에 무관하게 `dockerfile_content`
  문자열 기준으로 적용된다 — 즉 DockerfileFallback으로 손으로 쓴
  Dockerfile도 최종(유일한) 스테이지에 curl/make 등이 있으면 똑같이
  거부당한다. NodeKit이 이걸 로컬에서 미리 막을지는 별도 결정 사항
  (이 문서에서는 결정하지 않음).

## 14. 위험 요소

- **§2.6 Q5의 위험**: NodeVault Sprint 9(2026-07-13 완료)가 최종
  스테이지 RUN의 risky tool을 정적으로 거부하기 시작해서 위험의
  절반은 해소됐다 — Phase C(client-side 렌더링)만으로 "완전히
  해결됐다"고 오인할 위험은 여전히 남아 있지만, 이제 "authoring-time
  UX 개선일 뿐 서버 쪽엔 아무 장치도 없다"는 이전 표현은 부정확하다.
  남은 절반(base image에 이미 포함된 도구, Sprint 10)은 여전히
  진행 중이므로 그 경계까지 정확히 문서화해야 한다. 후속 이슈(§15)에
  이 경고를 명시적으로 남긴다.
- 2-stage 축소(D-2b)가 실제 큐레이션에서 실패할 위험 — 초기 profile
  이미지들이 실제로 fetch 도구를 포함하지 않으면 3-stage로 되돌려야
  한다. 렌더러 내부 구조를 그렇게 확장 가능하게 짜야 한다(Phase C
  구현 시 유의).
- 신규 `RecipeBuildKind`를 추가하면 `RecipeBuildKindResolver`,
  `RecipeRenderer.Render()`의 switch, `RecipeValidator.Validate()`의
  switch, `RecipeFieldCatalog.MethodFields` 전부에 새 분기가 필요하다
  — 코드 조사에서 확인한 82줄/11개 테스트 파일의 blast radius(§16
  참고, 조사 리포트 원본)를 감안하면 Phase B/C는 단일 커밋이 아니라
  여러 개로 쪼개야 한다.

## 15. 테스트 전략

Phase B/C 각각 다음을 갖춰야 한다(기존 R18-R21 세션의 관례를 그대로
따름):
- 각 신규 `RecipeFieldDescriptor`에 대한 `RecipeFieldCatalog` 단위
  테스트.
- `RecipeValidator`의 새 kind 분기에 대한 digest pinning/개행 차단
  등 기존 규칙 재적용 테스트.
- `RecipeRenderer`의 새 렌더 메서드가 실제로 유효한 멀티스테이지
  Dockerfile 문자열을 만드는지 확인하는 테스트(`RecipeRendererTests.cs`
  패턴).
- 기존 `SourceBuild`(legacy) kind가 전혀 영향받지 않는다는 회귀
  테스트 — 새 kind 추가가 기존 동작을 조용히 바꾸지 않는지 확인.
- 가능하면 seoy 실 클러스터 라이브 테스트로 실제 Buildah가 이
  Dockerfile을 빌드해 최종 이미지에 curl 등이 없는지 확인(§2 문서
  §12/13에서 이미 확립된 라이브 테스트 관례).

## 16. 미결정 사항

- **BuildProfile/RuntimeProfile의 구체적인 이미지 매핑** — 어떤
  이미지가 실제로 "curl/tar/sha256sum을 포함한 빌드 환경"으로 믿을
  만한지는 도메인 큐레이션 작업이 필요하다. 이 문서는 프레임워크만
  정의하고, 실제 매핑 표는 Phase B 구현 이슈에서 별도로 채운다.
- **RecipeDocument.SchemaVersion을 올릴지 여부** — §9에서 "지금은
  안 올려도 된다"고 판단했지만, 실제 구현 중 다른 이유로 스키마
  버전이 필요해질 수 있다.
- **DockerfileFallback에 대한 최종 이미지 hygiene 정책 적용 시점** —
  Phase E(NodeVault 쪽)가 구현된 뒤 결정할 문제라 지금은 열어둔다.
- **legacy `SourceBuild` kind의 완전 제거 여부**(Phase F) — 이번
  문서에서 결정하지 않음.
