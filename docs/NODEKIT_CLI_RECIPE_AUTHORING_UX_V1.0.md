# NodeKit CLI Recipe Authoring UX v1.0 개선 계획

문서명: `NODEKIT_CLI_RECIPE_AUTHORING_UX_V1.0.md`
상태: Draft — 리뷰 대기
대상: `nodekit recipe create` 대화형 UX 전반
기반: v0.9.2 구현 완료 이후 발견된 UX 병목 분석
비범위: NodeVault submit, 이미지 빌드, registry push, MCP server 구현, /back 네비게이션, DagEdit 통합

---

## 1. 목적

v0.9.2는 method 선택, BeginnerGuideFlow, InstallCommandParser, ImageReferenceNormalizer를 포함한
recipe authoring의 핵심 경로를 구현했다.

그러나 실제 도메인 전문가(유전체 연구자, 임상 연구자 등 CLI에 익숙하지 않은 사용자)의 관점에서
이 CLI를 사용했을 때 몇 가지 구조적인 UX 병목이 존재한다.

v1.0의 목표는 이 병목 세 가지를 제거해서 도메인 전문가가 CLI 경험 없이도
`nodekit recipe create`를 혼자 끝까지 완주할 수 있게 하는 것이다.

핵심 경로(`RecipeDocument → RecipeValidator → RecipeRenderer → ToolDefinition → BuildRequest`)는
변경하지 않는다.

---

## 2. 현재 UX 평가

### 2.1 평가 기준

대상 사용자: bioconda/conda 명령에 익숙하지만 터미널 고급 사용은 생소한 유전체 연구자, 의사.

| 항목 | 평가 |
|---|---|
| 7단서 picker | 좋음 — 아는 것에서 시작하는 아이디어 자체가 유효 |
| conda install 파싱 | 좋음 — 연구자들이 이미 갖고 있는 명령어를 재활용 |
| digest 획득 | 나쁨 — CLI가 도움 없이 사용자 혼자 해결해야 함 |
| "잘 모르겠다" 결과 | 나쁨 — 종료만 있고 다음 행동이 없음 |
| 오류 메시지 | 나쁨 — rule ID + 기술 용어, 다음 행동 없음 |
| 전체 파이프라인 맥락 | 보통 — wizard 완료 후 무엇을 해야 하는지 불분명 |

### 2.2 핵심 문제 요약

도메인 전문가가 `nodekit recipe create`를 사용할 때 막히는 순간은 세 가지다.

1. **image digest를 어디서 구하는지 모름** → 컨테이너 방식 전체가 막힘
2. **단서를 모를 때 출구가 없음** → CLI 바깥에서 정보를 구해와야 하는 방법을 알 수가 없음
3. **오류가 나면 무엇을 해야 할지 모름** → 기술 용어 기반 메시지로는 다음 행동이 없음

이 세 가지를 해결하면 현재 4점에서 7점 이상으로 올라갈 수 있다.

---

## 3. 개선 항목

### 3.1 [개선-1] digest 자동 조회

#### 3.1.1 현재 동작

`RunContainerImageFlow()`에서 사용자가 이미지 주소를 입력하고
digest가 없으면 두 가지 선택지를 제시한다.

```text
이미지 주소에는 digest가 없습니다.

  [1] digest가 포함된 주소를 다시 입력한다
  [2] ImageDigest를 따로 입력한다
```

사용자는 digest를 직접 구해와야 한다. 어디서 구하는지는 CLI가 알려주지 않는다.

#### 3.1.2 목표 동작

사용자가 이미지 주소를 입력하면 CLI가 먼저 자동 조회를 시도한다.

```text
이미지 주소를 입력하세요 (예: quay.io/biocontainers/bwa:0.7.17--h7132678_9):
> quay.io/biocontainers/bwa:0.7.17--h7132678_9

  → 이미지 정보를 확인하는 중...
  → sha256:3f2a1b9c... (확인됨)

이 digest를 사용할까요? [Y/n]
```

자동 조회 실패 시 폴백 흐름:

```text
  → 이미지 정보를 확인할 수 없습니다 (네트워크 연결 없음 또는 접근 권한 없음).

  digest를 직접 입력하거나, 나중에 추가할 수 있습니다.

  digest를 지금 입력하시겠습니까? [Y/n]
  y
  > sha256:
```

#### 3.1.3 설계 원칙

- 자동 조회는 시도이지 전제가 아니다. 실패해도 수동 입력 경로가 항상 열려 있어야 한다.
- 조회 대상 레지스트리는 설정 가능해야 한다. 기본값은 Harbor 내부 레지스트리.
- 조회 실패 시 오류 코드가 아닌 이유를 사람 말로 표시한다.
  - "네트워크 연결 없음", "인증 필요", "이미지를 찾을 수 없음"
- L1 reproducibility 규칙은 그대로 유지된다. digest 없이 통과하는 경로는 없다.

#### 3.1.4 인터페이스

```csharp
/// <summary>
/// 이미지 URI에서 digest를 조회한다. 조회 실패 시 null 반환 (예외 없음).
/// </summary>
public interface IImageDigestResolver
{
    Task<string?> TryResolveDigestAsync(string imageUri, CancellationToken ct);
}
```

구현체:

| 구현체 | 설명 |
|---|---|
| `HarborImageDigestResolver` | Harbor HTTP API를 통해 조회 |
| `SkopeoImageDigestResolver` | `skopeo inspect` subprocess 호출 |
| `NullImageDigestResolver` | 항상 null 반환 (테스트/오프라인 폴백) |

`BeginnerGuideFlow`는 `IImageDigestResolver`를 생성자에서 주입받는다.

#### 3.1.5 비범위

- Docker daemon 직접 연결 (`/var/run/docker.sock`) 금지 — NodeKit은 K8s API나 daemon에 직접 접근하지 않는다.
- NodeVault registry 직접 쿼리 — Catalog 서비스를 거치지 않는 직접 접근 금지.

---

### 3.2 [개선-2] "잘 모르겠다" → 학습 경로

#### 3.2.1 현재 동작

7개 단서 중 해당하는 것이 없거나 "잘 모르겠다"를 선택하면
최소 요건을 안내하고 종료한다.

사용자 입장에서는 "나가서 무엇을 준비해야 하는가"를 알 수 없다.

#### 3.2.2 목표 동작

"잘 모르겠다"를 선택했을 때 세 가지 경로를 제공한다.

```text
지금 알고 있는 정보로 어디서든 시작할 수 있습니다.

  [1] 도구 이름만 안다 → bioconda에서 검색하는 방법 안내
  [2] conda install 명령어가 있다 → 지금 붙여넣기
  [3] 컨테이너 이미지 주소가 있다 → digest 자동 조회 시작
  [4] GitHub 소스 링크가 있다 → source build 안내 시작
  [5] 나중에 다시 시작한다 → 종료
```

"도구 이름만 안다"를 선택하면:

```text
bioconda에서 도구를 찾는 방법:

  1. https://anaconda.org/bioconda/<도구이름> 에서 검색
     예: https://anaconda.org/bioconda/bwa

  2. 또는 지금 도구 이름을 입력하면 검색 URL을 보여드립니다.
     > bwa
     → https://anaconda.org/bioconda/bwa

  패키지를 찾으면 설치 명령어(conda install -c bioconda bwa=...)를
  복사해서 돌아오세요.

  [계속 — conda install 명령어 있음] [종료]
```

#### 3.2.3 설계 원칙

- "잘 모르겠다"는 종착역이 아니다. 배움의 입구여야 한다.
- CLI 바깥으로 나가야 할 때는 **구체적인 URL과 행동**을 보여준다.
- 돌아왔을 때 어디서 계속할지 항상 명확하게 안내한다.
- 사용자를 판단하지 않는다. "이건 알았어야 한다"는 암묵적 메시지를 주지 않는다.

#### 3.2.4 bioconda 검색 URL 생성

bioconda 패키지 검색 URL은 단순한 문자열 조합이므로 외부 API 호출 없이 생성 가능하다.

```text
https://anaconda.org/bioconda/<도구이름>
https://quay.io/repository/biocontainers/<도구이름>?tab=tags
```

CLI는 이 URL을 출력하고 사용자가 브라우저에서 열도록 안내한다.
직접 HTTP 요청을 보내 결과를 파싱하지 않는다 (네트워크 의존 최소화).

---

### 3.3 [개선-3] 오류 메시지에서 "다음 행동" 안내

#### 3.3.1 현재 동작

L1 validation 오류는 rule ID와 기술 용어로 표시된다.

```text
L1-IMG-004: 이미지 digest(@sha256:...)가 없습니다.
L1-SRC-001: source build kind에는 SourceChecksum이 필요합니다 — 체크섬 없이는 재현성을 보장할 수 없습니다.
L1-PKG-001: 패키지 bwa에 버전이 고정되지 않았습니다.
```

다음에 무엇을 해야 하는지 없다.

#### 3.3.2 목표 동작

오류 메시지를 두 계층으로 분리한다.

**사용자 화면 (사람 말):**

```text
✗ 이미지 digest가 없습니다.
  → 이미지 주소에 "@sha256:..." 부분이 필요합니다.
  → Harbor에서 이미지를 찾아 [Tags] 탭의 "Digest" 열을 복사하세요.
  → 또는 이미지 주소를 다시 입력하면 자동으로 조회를 시도합니다.
  [다시 입력] [이미지 주소 수정] [도움말]
```

```text
✗ 패키지 버전이 고정되지 않았습니다: bwa
  → "bwa=0.7.17=h7132678_9" 형식으로 버전과 build string을 함께 입력하세요.
  → bioconda에서 정확한 버전 표기를 확인할 수 있습니다:
     https://anaconda.org/bioconda/bwa
```

```text
✗ SourceChecksum이 없습니다.
  → 소스 코드의 sha256 체크섬이 필요합니다. 재현성 보장을 위해 생략할 수 없습니다.
  → 다음 명령으로 계산할 수 있습니다:
     curl -fsSL <URL> | sha256sum
  → 결과의 앞부분 64자리 hex 값을 "sha256:" 뒤에 붙여서 입력하세요.
     예: sha256:3f2a1b9c...
```

**로그 파일 (기술 정보):**

```text
[2026-06-26T12:00:00Z] WARN L1-IMG-004 field=BioContainerImageUri value="quay.io/biocontainers/bwa:0.7.17--h7132678_9"
```

rule ID는 로그에만 남긴다. 사용자 화면에는 표시하지 않는다.

#### 3.3.3 설계 원칙

- 사용자 화면의 오류 메시지에 rule ID를 노출하지 않는다.
- 모든 오류에는 "다음에 무엇을 해야 하는가"가 포함되어야 한다.
- 할 수 있는 행동은 항상 구체적이어야 한다 (URL, 명령어, 또는 CLI 선택지).
- 재현성 규칙(CLAUDE.md §3)은 예외 없이 유지된다. bypass 경로를 추가하지 않는다.

#### 3.3.4 인터페이스

```csharp
/// <summary>
/// ValidationViolation을 사용자 친화적 메시지로 변환한다.
/// rule ID는 로그 파일에만 남기고 사용자 화면에는 노출하지 않는다.
/// </summary>
internal static class ViolationMessageFormatter
{
    public static UserFacingViolation Format(ValidationViolation violation);
}

internal sealed record UserFacingViolation(
    string Summary,       // 한 줄 요약 — 무엇이 문제인가
    string[] Guidance,    // 다음 행동 목록 — 어떻게 해결하는가
    string[] Actions);    // CLI에서 바로 선택 가능한 행동 (재입력, 도움말 등)
```

---

## 4. SourceChecksum 계산 안내 (3.1 보완)

`IImageDigestResolver`와 유사하게, source build 방식에서도 사용자가 막히는 지점은
checksum 계산이다.

CLI 안에서 다음 안내를 제공한다.

```text
SourceChecksum이 없습니다.

다음 중 하나를 선택하세요.

  [1] 지금 계산한다 (소스 URL 입력 → curl + sha256sum 명령 출력)
  [2] 직접 입력한다 (sha256:...)
  [3] 나중에 추가한다 (draft 저장 후 종료)
```

"지금 계산한다"를 선택하면:

```text
소스 URL을 입력하세요:
> https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz

다음 명령을 실행하면 checksum을 얻을 수 있습니다.

  curl -fsSL "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz" | sha256sum

결과 예시:
  3f2a1b9c...  -

"sha256:" 앞에 붙여서 입력하세요.
> sha256:
```

CLI가 직접 curl을 실행하지 않는다. 명령어를 출력하고 사용자가 실행하도록 안내한다.
이 결정은 의도적이다 — subprocess 실행 권한 없이도 동작해야 하고,
사용자가 무엇을 실행하는지 직접 확인할 수 있어야 한다.

---

## 5. 용어 개선

사용자 화면에 노출되는 용어를 기술 내부 용어에서 도메인 언어로 교체한다.

| 현재 (내부/기술 용어) | 변경 후 (사용자 언어) |
|---|---|
| ImageRef | 컨테이너 이미지 주소 |
| ImageDigest | 이미지 고정 코드 (digest) |
| BioContainerImageUri | BioContainer 이미지 주소 |
| SourceChecksum | 소스 코드 검증값 (sha256) |
| PackageMirrorUri | 내부 미러 주소 |
| DockerfileContent | Dockerfile 내용 |
| BuildKind | (사용자에게 노출하지 않음) |
| RecipeBuildKind | (사용자에게 노출하지 않음) |
| L1-IMG-004 | (로그에만 기록) |

내부 필드명(`ImageRef`, `BuildKind` 등)은 변경하지 않는다.
변경 대상은 사용자 화면에 출력되는 프롬프트 문자열뿐이다.

---

## 6. 비범위 — v1.0에서 다루지 않는 것

다음 항목은 이 문서의 범위 밖이다.

| 항목 | 이유 |
|---|---|
| `/back` 네비게이션 | NODEKIT_CLI_RECIPE_AUTHORING_UX_V0.9.2.md Section 17.2에서 구조적 이유로 비범위 결정 |
| bioconda API를 통한 자동 버전 검색 | 외부 API 의존성 추가, 오프라인 환경 대응 필요 — 별도 검토 |
| Harbor webhook 또는 push 트리거 | NodeVault 범위 |
| Avalonia UI 통합 | DagEdit/NodeKit UI 별도 트랙 |
| `ToolSpecRequest` / `SubmitToolBuild` 경로 | NodeVault Phase 1 완료 이후 |
| conda environment.yml 파일 파싱 | InstallCommandParser 확장 — 별도 검토 |

---

## 7. 구현 우선순위

| 우선순위 | 항목 | 이유 |
|---|---|---|
| P1 | [개선-3] 오류 메시지 개선 | 코드 변경 없이 문자열 수정만으로 즉시 효과 |
| P1 | [개선-2] "잘 모르겠다" 학습 경로 | BeginnerGuideFlow 흐름 수정, 외부 의존성 없음 |
| P2 | SourceChecksum 계산 안내 | source build 방식 사용자 수 상대적으로 적음 |
| P2 | 용어 개선 | 일괄 문자열 교체, 리스크 낮음 |
| P3 | [개선-1] digest 자동 조회 | `IImageDigestResolver` 인터페이스 설계 + 구현체 필요, 가장 큰 UX 효과이나 구현 비용도 가장 큼 |

P3가 가장 큰 UX 효과를 내지만, 인터페이스 설계를 먼저 확정하지 않으면
나중에 구현체를 교체할 때 비용이 커진다.
따라서 P3 구현 전에 `IImageDigestResolver` 인터페이스를 팀과 리뷰하는 것을 권장한다.

---

## 8. 목표 사용자 경험 시나리오

### 8.1 bioconda 도구 — 가장 일반적인 경로

```text
$ nodekit recipe create

진행 방식을 선택하세요.
  [1] 쉬운 안내 모드  — 단계별로 안내해드립니다
  [2] 빠른 설정 모드  — 6개 질문으로 바로 시작합니다
> 1

어떤 것을 알고 있나요? (해당하는 것 모두 선택)
  [1] conda install ... 명령어가 있다
  [2] ...
> 1

conda install 명령어를 붙여넣으세요:
> conda install -c bioconda bwa=0.7.17 -y

  → 패키지: bwa=0.7.17
  → 채널: bioconda
  → 방식: Conda

도구 이름 (예: bwa-mem2):
> bwa-mem

버전 (예: 0.7.17):
> 0.7.17

...

✓ recipe.json 저장 완료

다음 단계:
  nodekit recipe validate recipe.json   — L1 검증
  nodekit recipe render recipe.json     — BuildRequest 미리보기
  nodekit build-request submit          — NodeVault에 전송
```

### 8.2 BioContainer — digest 자동 조회

```text
컨테이너 이미지 주소를 입력하세요:
> quay.io/biocontainers/bwa:0.7.17--h7132678_9

  → 이미지 정보를 확인하는 중...
  → sha256:3f2a1b9c4d5e... (확인됨)

이 digest를 사용할까요? [Y/n]
> Y

✓ 이미지: quay.io/biocontainers/bwa:0.7.17--h7132678_9@sha256:3f2a1b9c4d5e...
```

### 8.3 아무것도 모를 때

```text
어떤 것을 알고 있나요?
  ...
  [8] 잘 모르겠다

> 8

지금 알고 있는 정보로 어디서든 시작할 수 있습니다.

  [1] 도구 이름만 안다  → bioconda 검색 URL 안내
  [2] conda install 명령어가 있다  → 지금 붙여넣기
  [3] 컨테이너 이미지 주소가 있다  → digest 자동 조회
  [4] GitHub 소스 링크가 있다  → source build 안내
  [5] 나중에 다시 시작한다  → 종료

> 1

bwa를 bioconda에서 검색하려면:
  https://anaconda.org/bioconda/bwa

패키지 페이지의 "Install" 탭에서 conda install 명령어를 복사하세요.
복사하셨으면 [계속]을 누르거나, 명령어를 바로 붙여넣으세요.
> [계속]
```

---

## 9. 변경이 필요한 파일 (예상)

| 파일 | 변경 내용 |
|---|---|
| `src/NodeKit.Cli/BeginnerGuideFlow.cs` | [개선-2] "잘 모르겠다" 경로 확장, [개선-1] digest 자동 조회 통합 |
| `src/NodeKit.Cli/IImageDigestResolver.cs` | 신규 — [개선-1] 인터페이스 정의 |
| `src/NodeKit.Cli/HarborImageDigestResolver.cs` | 신규 — [개선-1] Harbor HTTP API 구현체 |
| `src/NodeKit.Cli/NullImageDigestResolver.cs` | 신규 — [개선-1] 테스트/오프라인 폴백 |
| `src/Validation/ViolationMessageFormatter.cs` | 신규 — [개선-3] rule ID → 사람 말 변환 |
| `src/NodeKit.Cli/RecipeCreateCommand.cs` | [개선-3] ViolationMessageFormatter 적용 |
| `tests/NodeKit.Cli.Tests/BeginnerGuideFlowTests.cs` | [개선-1][개선-2] 신규 테스트 케이스 |
| `tests/NodeKit.Tests/ViolationMessageFormatterTests.cs` | 신규 — [개선-3] 각 rule ID별 포맷 검증 |

---

## 10. 완료 조건

이 문서가 구현됐다고 볼 수 있는 조건은 다음과 같다.

1. `IImageDigestResolver`가 정의되어 있고 `BeginnerGuideFlow`가 이를 주입받는다.
2. `NullImageDigestResolver`가 구현되어 있고 모든 테스트가 이를 사용한다.
3. `HarborImageDigestResolver` 또는 대안 구현체가 1개 이상 구현되어 있다.
4. BeginnerGuideFlow에서 "잘 모르겠다" 선택 시 종료 대신 안내 경로가 제공된다.
5. `ViolationMessageFormatter`가 L1-IMG-004, L1-SRC-001, L1-PKG-001 등 주요 rule ID를
   사람 말로 변환한다.
6. 사용자 화면에 rule ID가 노출되는 코드 경로가 없다.
7. `dotnet test` 333건 이상 통과, 빌드 warning 0.
