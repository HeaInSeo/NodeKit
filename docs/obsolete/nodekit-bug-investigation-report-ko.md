붙여넣으신 초안을 기준으로, 기존 3건과 추가 5건을 하나의 문서 형식으로 통일해서 다시 정리했습니다.

---

# NodeKit 버그 조사 보고서

## 1. 문서 목적

이 문서는 현재 업로드된 `NodeKit` 프로젝트를 기준으로, 코드 리뷰를 통해 확인한 주요 버그와 수정 우선순위를 정리하기 위해 작성했다.

이번 조사는 전체 기능을 완전히 실행해 재현하는 방식보다, 우선 코드 구조와 검증 로직을 중심으로 실제 장애 가능성이 높은 지점을 식별하는 데 초점을 두었다. 그 결과, 기능 버그, UI 상태 관리 버그, 검증 누락, 그리고 빌드 재현성 문제를 포함한 다수의 이슈를 확인할 수 있었다.

---

## 2. 한 줄 요약

가장 먼저 수정해야 할 문제는 `ImageUriValidator`의 태그 판별 오류와, 검증 통과 후 폼을 수정해도 재검증 없이 빌드 요청을 전송할 수 있는 UI 상태 관리 버그다. 그 외에도 `EnvironmentSpec` 유실, L1 검증 누락, Dockerfile 검증 공백, conda `pip` 섹션 해석 오류, 그리고 외부 절대 경로 의존성 문제가 확인되었다.

---

## 3. 조사 범위

이번 검토는 다음 범위를 중심으로 진행했다.

* 이미지 URI 검증 로직
* 패키지 버전 및 환경 스펙 검증 로직
* UI에서 NodeForge 연결 주소 변경 시의 클라이언트 생성 방식
* UI 검증 상태와 실제 전송 내용의 일치 여부
* BuildRequest 생성 시 필드 누락 여부
* 프로젝트 단독 빌드 가능성 및 외부 경로 의존성

실행 환경 차이 때문에 전체 런타임 동작을 모두 확인한 것은 아니지만, 아래 항목들은 코드 기준으로 충분히 버그 가능성이 높거나 구조적으로 문제가 명확한 부분이다.

---

## 4. 확인된 주요 버그

### 4.1. 버그 1 — 이미지 URI 검증에서 포트와 태그를 혼동하는 문제

#### 대상 파일

`src/Validation/ImageUriValidator.cs`

#### 문제 설명

현재 이미지 URI 검증 로직은 이미지 문자열에 `:` 문자가 포함되어 있으면 태그가 존재한다고 판단하는 방식으로 보인다.

하지만 컨테이너 이미지 URI에서는 `:` 가 항상 태그를 의미하지 않는다. 레지스트리 포트가 포함된 경우에도 `:` 가 등장할 수 있다.

예를 들어 아래와 같은 이미지는 포트만 있고 태그는 없는 형태다.

`registry.example.com:5000/bwa-mem2@sha256:abc`

그런데 현재 로직은 `:5000` 을 태그 구분자로 오인하여, 태그가 없는 이미지도 태그가 있는 것처럼 통과시킬 가능성이 있다.

#### 왜 문제인가

이 프로젝트는 재현성을 위해 `버전 태그 + digest`를 요구하는 방향을 취하고 있다. 그런데 태그가 없는 이미지가 검증을 통과하면, 의도한 재현성 보장이 무너질 수 있다.

즉, 정책상 실패해야 하는 이미지가 L1 검증을 통과하는 상황이 생길 수 있다.

#### 예상 영향

* 태그가 없는 이미지가 잘못 통과할 수 있음
* 재현성 규칙이 약화됨
* 사용자는 “검증 통과된 이미지”라고 믿지만 실제 기준은 어긴 상태일 수 있음

#### 수정 방향

태그 존재 여부는 단순한 `:` 포함 여부가 아니라, 마지막 `/` 뒤쪽의 `:` 만 태그 구분자로 인정하도록 바꾸는 것이 바람직하다. 레지스트리 호스트의 포트(`:5000`)는 태그로 취급하면 안 된다.

#### 권장 후속 작업

포트는 있으나 태그는 없는 이미지 URI 케이스를 회귀 테스트로 반드시 고정해야 한다.

---

### 4.2. 버그 2 — NodeForge 주소를 바꿔도 gRPC 클라이언트가 갱신되지 않는 문제

#### 대상 파일

`UI/MainWindow.axaml.cs`

#### 문제 설명

현재 UI 코드에서 gRPC 관련 클라이언트들이 다음과 같은 패턴으로 생성되는 것으로 보인다.

* `_buildClient ??= new GrpcBuildClient(address);`
* `_toolRegistryClient ??= new GrpcToolRegistryClient(address);`
* `_policyProvider ??= new GrpcPolicyBundleProvider(address);`

이 패턴은 최초 1회 생성 후에는 이후 주소가 바뀌어도 기존 인스턴스를 계속 재사용하게 만든다.

즉, 사용자가 UI에서 NodeForge 주소를 변경하더라도 실제 요청은 여전히 이전 주소로 전송될 수 있다.

#### 왜 문제인가

사용자는 주소를 변경하면 새로운 서버로 연결이 바뀔 것이라고 기대한다. 하지만 내부 클라이언트가 재생성되지 않으면 설정 변경이 실제 통신에 반영되지 않는다.

#### 예상 영향

* 주소를 바꿨는데도 이전 서버에 계속 붙는 현상
* 정책 조회, 툴 목록 조회, 빌드 요청이 서로 다른 서버를 보는 것처럼 보이는 현상
* 설정 변경이 즉시 반영되지 않는 심각한 UX 문제

#### 수정 방향

현재 연결 주소를 추적하고, 주소가 달라질 경우 기존 gRPC 클라이언트를 정리한 뒤 새 주소 기준으로 재생성하는 방식으로 바꿔야 한다.

#### 권장 후속 작업

주소 A에서 연결한 뒤 주소 B로 변경하고, 이후 요청이 실제로 B로 가는지 확인하는 테스트를 추가하는 것이 좋다.

---

### 4.3. 버그 3 — 프로젝트 단독 빌드 재현성이 깨지는 외부 절대 경로 의존성

#### 대상 파일

`NodeKit.csproj`, `Makefile`

#### 문제 설명

현재 `.csproj` 안에 Protobuf 파일 경로가 외부 절대 경로에 강하게 묶여 있다. 예를 들면 `/opt/go/src/github.com/HeaInSeo/api-protos/...` 같은 형태다.

이 구조는 개발자 개인 머신에서는 맞을 수 있지만, 새 개발 환경, CI, zip으로 프로젝트만 전달받은 환경, 다른 운영체제 또는 다른 디렉터리 구조에서는 쉽게 깨질 수 있다.

또한 `Makefile`도 repo 외부 디렉터리를 전제하는 부분이 있어 프로젝트 단독 사용성이 떨어진다.

#### 왜 문제인가

이 문제는 기능 버그와는 조금 성격이 다르지만, 실제 협업과 배포, 재현성 측면에서 매우 중요하다. 즉, 소스는 있어도 다른 사람이 바로 빌드할 수 없는 상태가 된다.

#### 예상 영향

* 로컬에서는 되는데 다른 환경에서는 빌드 실패
* CI 도입 시 경로 문제로 실패
* 샘플 또는 오픈소스 프로젝트로 공유하기 어려움
* 협업자 온보딩 비용 증가

#### 수정 방향

필요한 protobuf 정의를 repo 내부로 가져오거나, submodule 또는 명시적 bootstrap 절차를 제공하거나, 상대 경로 또는 환경변수 기반 해석으로 정리하는 것이 바람직하다.

#### 권장 후속 작업

단독 빌드에 필요한 외부 의존성을 문서화하거나, 필요한 정의 파일을 repo 내부로 가져와 독립 빌드 가능 상태로 정리해야 한다.

---

### 4.4. 버그 4 — 검증 통과 후 폼을 수정해도 재검증 없이 전송 가능한 문제

#### 대상 파일

`UI/MainWindow.axaml.cs`

#### 문제 설명

검증 성공 시 `_l1Passed = true` 와 `SendBuildButton.IsEnabled = true` 가 설정된다. 그런데 이후 사용자가 `ToolName`, `ImageUri`, `Dockerfile`, `Script`, `EnvironmentSpec`, I/O 등을 수정해도 이 상태를 다시 무효화하는 로직이 보이지 않는다.

반면 실제 전송 시에는 다시 `BuildDefinitionFromForm()` 으로 현재 폼 값을 읽어서 요청을 생성한다.

#### 왜 문제인가

사용자는 A 내용으로 검증을 통과한 뒤, 그 다음에 B 내용으로 수정하고도 재검증 없이 그대로 빌드 요청을 보낼 수 있다. 즉, “검증된 내용”과 “실제로 전송되는 내용”이 달라질 수 있다.

#### 예상 영향

* 잘못된 `ImageUri` 가 재검증 없이 전송될 수 있음
* 잘못된 `EnvironmentSpec` 이 재검증 없이 전송될 수 있음
* 잘못된 스크립트나 I/O 선언이 서버로 전달될 수 있음
* UI가 검증 게이트 역할을 제대로 하지 못함

#### 수정 방향

폼의 주요 입력값이 변경될 때마다 `_l1Passed` 상태를 초기화하고, `SendBuildButton` 을 다시 비활성화해야 한다. 또는 검증 성공 시점의 스냅샷과 현재 입력을 비교하여 불일치 시 재검증을 요구하도록 바꿔야 한다.

#### 권장 후속 작업

“검증 후 수정 → 전송 시도” 시나리오를 UI 회귀 테스트로 고정하는 것이 좋다.

---

### 4.5. 버그 5 — EnvironmentSpec 이 BuildRequest 에 실리지 않는 문제

#### 대상 파일

* `src/Authoring/ToolDefinition.cs`
* `src/Grpc/BuildRequest.cs`
* `src/Grpc/BuildRequestFactory.cs`
* `UI/MainWindow.axaml.cs`
* `docs/NODEKIT_SPRINT.md`

#### 문제 설명

폼에서는 `EnvironmentSpec` 을 수집하고, `ToolDefinition` 에도 `EnvironmentSpec` 필드가 존재한다. 그런데 `BuildRequest` 에는 `EnvironmentSpec` 필드 자체가 없고, `BuildRequestFactory` 도 이를 매핑하지 않는다.

즉, 사용자는 UI에서 환경 스펙을 입력하고 L1 검증까지 받지만, 실제 NodeForge 로 보내는 요청에는 그 값이 포함되지 않는다.

#### 왜 문제인가

문서상 BuildRequest 는 ToolDefinition 과 build context 를 포함하는 것으로 보이는데, 현재 구현은 그 의도와 맞지 않는다. 입력값 일부가 요청 생성 단계에서 사라지는 구조다.

#### 예상 영향

* 사용자 입력 일부가 전송 전에 유실됨
* UI 검증 기준과 실제 서버 입력이 어긋남
* NodeForge 쪽 빌드/등록 판단이 의도와 달라질 수 있음

#### 수정 방향

`BuildRequest` 에 `EnvironmentSpec` 필드를 추가하고, `BuildRequestFactory` 에서 `ToolDefinition.EnvironmentSpec` 을 명시적으로 매핑해야 한다.

#### 권장 후속 작업

BuildRequest 생성 단위 테스트에 `EnvironmentSpec` 보존 여부를 추가하는 것이 좋다.

---

### 4.6. 버그 6 — L1 필수 필드 검증이 사실상 빠져 있는 문제

#### 대상 파일

`UI/MainWindow.axaml.cs`, `docs/NODEKIT_SPRINT.md`

#### 문제 설명

현재 L1 검증에서 실제로 수행하는 것은 `ImageUriValidator.Validate(definition)` 와 `PackageVersionValidator.Validate(definition)` 정도로 보인다.

하지만 문서상 L1 에서는 다음도 확인해야 한다.

* 필수 필드 누락 여부
* Tool name / version 형식
* I/O 슬롯 기본 형식
* Dockerfile 존재 여부 및 기본 구조
* BuildRequest 생성 가능한 최소 입력 여부

#### 왜 문제인가

현재 구현상 아래와 같은 값도 이미지 URI 와 환경 스펙만 괜찮으면 검증을 통과할 가능성이 있다.

* Tool 이름 빈 값
* Script 빈 값
* Dockerfile 빈 값
* I/O 선언 없음 또는 비정상
* BuildRequest 생성 최소 조건 미충족

#### 예상 영향

* UI가 막아야 할 잘못된 authoring 입력이 서버까지 넘어갈 수 있음
* 문서상 L1 의미와 실제 구현이 어긋남

#### 수정 방향

L1 검증 단계를 별도 validator 체인 또는 composite validator 형태로 확장하여, 필수 필드와 기본 구조 검증을 명시적으로 수행하도록 정리하는 것이 좋다.

#### 권장 후속 작업

L1 체크리스트를 테스트 케이스 목록으로 고정하고, 문서 기준과 구현 기준을 일치시켜야 한다.

---

### 4.7. 버그 7 — 패키지 버전 검증이 Dockerfile install 구문을 전혀 검사하지 않는 문제

#### 대상 파일

`src/Validation/PackageVersionValidator.cs`, `docs/NODEKIT_SPRINT.md`

#### 문제 설명

`PackageVersionValidator` 는 오직 `definition.EnvironmentSpec` 만 검사하는 것으로 보인다.

그런데 문서에는 검증 대상이 다음 두 경로라고 정리되어 있다.

* Dockerfile 내 `micromamba install` / `conda install` 구문
* `environment.yml` 같은 environment spec 파일

즉, 현재 구현은 Dockerfile 기반 설치 경로를 전혀 검사하지 않는다.

#### 왜 문제인가

예를 들어 Dockerfile 에 아래처럼 적혀 있어도,

`RUN micromamba install -y bwa samtools`

현재 구현은 `EnvironmentSpec` 이 비어 있으면 그대로 통과시킬 수 있다.

#### 예상 영향

* 문서상 재현성 규칙을 실제 구현이 지키지 못함
* Dockerfile 기반 설치 경로가 검증 사각지대가 됨

#### 수정 방향

Dockerfile 파싱 범위를 최소한의 룰 기반 검사로 확장하여, `micromamba install`, `conda install` 등의 설치 구문에서 버전 pinning 여부를 함께 확인해야 한다.

#### 권장 후속 작업

Dockerfile 기반 설치 케이스를 별도의 validator 테스트 세트로 추가하는 것이 좋다.

---

### 4.8. 버그 8 — conda environment.yml 안의 pip 섹션을 잘못 해석하는 문제

#### 대상 파일

`src/Validation/PackageVersionValidator.cs`

#### 문제 설명

현재 conda YAML 검증은 정규식 기반 줄 단위 검사에 의존하는 것으로 보인다. 그런데 이 방식은 conda `dependencies` 안의 `pip:` subsection 구조를 제대로 처리하지 못한다.

예를 들어 다음과 같은 파일을 생각할 수 있다.

```yaml
dependencies:
  - python=3.11=h123
  - pip
  - pip:
    - requests==2.31.0
    - numpy
```

#### 왜 문제인가

이 구조에서 현재 로직은 다음 문제를 동시에 일으킬 수 있다.

* `- pip` 를 일반 패키지처럼 보고 버전 없음 위반으로 오탐할 수 있음
* `requests==2.31.0` 같은 pip subsection 의 실제 패키지는 conda 정규식에 안 맞아 누락될 수 있음
* 결과적으로 정상 파일은 잘못 실패하고, 막아야 할 비고정 pip 패키지는 통과할 수 있음

#### 예상 영향

* 정상 environment.yml 이 잘못 실패할 수 있음
* 실제로 막아야 할 pip 패키지 누락 가능
* conda + pip 혼합 환경에서 검증 신뢰성 저하

#### 수정 방향

줄 단위 정규식 해석 대신 최소한의 YAML 구조 인지를 하거나, `pip:` subsection 을 별도 규칙으로 처리하는 방식으로 보완해야 한다.

#### 권장 후속 작업

conda + pip 혼합 환경 예제를 테스트 픽스처로 고정하는 것이 좋다.

---

## 5. 우선순위 제안

현재 기준으로는 다음 순서로 처리하는 것이 가장 현실적이다.

### 분류 원칙

이번 항목들은 같은 종류의 버그가 아니다. 스프린트 계획은 아래 3개 축으로 묶는 것이 적절하다.

* **축 A — 검증-전송 불일치 해소**: 버그 4, 5
* **축 B — 잘못된 통과 차단**: 버그 1, 6, 7, 8
* **축 C — 연결/개발환경 품질 보강**: 버그 2, 3

이 분류를 쓰면 “무조건 번호순 처리”보다 실제 위험도와 수정 결합도를 더 잘 반영할 수 있다.

### 1순위 — 축 A

* 검증 통과 후 폼을 수정해도 재검증 없이 전송되는 문제
* `EnvironmentSpec` 누락 문제
* `ImageUriValidator`의 포트/태그 판별 오류

이 3개는 “사용자가 검증했다고 믿는 값”과 “실제로 통과/전송된 값” 사이의 신뢰를 직접 깨뜨린다. 따라서 가장 먼저 처리하는 것이 맞다.

### 2순위 — 축 B

* L1 필수 필드 검증 보강
* Dockerfile install 구문 검증 추가
* conda + pip subsection 해석 오류 수정

이 묶음은 문서상 L1 계약과 실제 구현을 다시 맞추는 작업이다. 개별 핫픽스보다 validator 체계 정리가 중요하다.

### 3순위 — 축 C

* NodeForge 주소 변경 시 gRPC 클라이언트 재생성 처리
* `.csproj` / `Makefile` 외부 경로 의존성 정리 또는 bootstrap 문서화

이 항목들은 중요하지만 앞선 두 축에 비해 “즉시 잘못된 검증/전송”보다는 연결 품질과 협업 품질 문제에 가깝다.

---

## 6. 권장 액션 아이템

이번 조사 결과를 기준으로, 바로 다음 스프린트 또는 small diff 작업으로는 아래 정도가 적절하다.

### 실행 방식

수정 순서는 단순 구현 순서보다 다음 원칙을 따르는 것이 바람직하다.

* 각 버그마다 먼저 재현 테스트 또는 실패 시나리오를 고정
* 그 다음 최소 수정으로 통과
* 수정 후 문서와 테스트 설명을 구현 기준에 맞게 정렬
* 미해결 항목은 “잔여 리스크”로 분리 기록

### 액션 1

검증 상태와 폼 입력값의 동기화 정리

* 입력 변경 시 `_l1Passed` 초기화
* `SendBuildButton` 재비활성화
* 필요 시 검증 성공 시점 스냅샷 도입

### 액션 2

`ImageUriValidator` 태그 판별 로직 수정 및 회귀 테스트 추가

* 포트만 있고 태그 없는 이미지
* 포트와 태그가 모두 있는 이미지
* digest 는 있으나 태그 없는 이미지
* 태그는 있으나 digest 없는 이미지

### 액션 3

`BuildRequest` 에 `EnvironmentSpec` 추가 및 매핑 보강

* 모델 필드 추가
* factory 매핑 추가
* 단위 테스트 추가

### 액션 4

L1 validator 범위 확장

* 필수 필드 검증
* Tool name / version 형식 검증
* Dockerfile 기본 구조 검증
* I/O 슬롯 기본 형식 검증
* BuildRequest 생성 가능 최소 조건 검증

### 액션 5

`PackageVersionValidator` 개선

* Dockerfile install 구문 검사 추가
* conda `pip:` subsection 처리 추가
* 혼합 케이스 테스트 추가

### 액션 6

gRPC 클라이언트 주소 변경 감지형 재초기화 적용

* 현재 주소 추적
* 주소 변경 시 기존 클라이언트 dispose
* 새 주소 기반 재생성

### 액션 7

외부 절대 경로 의존성 정리 또는 문서화

* protobuf 정의 repo 내부화 또는 bootstrap 절차 제공
* 독립 빌드 가능 여부 명확화

---

## 7. 결론

이번 코드 리뷰 기준으로, 현재 프로젝트의 핵심 문제는 단순한 개별 validator 오류를 넘어서, **검증된 내용과 실제 전송되는 내용이 어긋날 수 있는 구조**에 있다.

특히 다음 세 가지는 우선순위가 높다.

* 검증 통과 후 폼 수정 시 재검증 없이 전송 가능한 문제
* 이미지 URI 검증에서 포트와 태그를 혼동하는 문제
* `EnvironmentSpec` 이 BuildRequest 에 포함되지 않는 문제

그 외에도 L1 검증 범위 누락, Dockerfile 기반 설치 구문 미검사, conda `pip` subsection 해석 오류, 외부 절대 경로 의존성 문제는 프로젝트의 신뢰성과 재현성을 떨어뜨릴 수 있으므로 순차적으로 정리하는 것이 바람직하다.

---
