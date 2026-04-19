# NodeKit Build Bootstrap

작성일: 2026-04-08  
최종 수정: 2026-04-19

---

## 목적

이 문서는 NodeKit을 현재 개발 환경 외부에서 빌드할 때 필요한 외부 의존성과 경로 설정을 정리한다.

현재 NodeKit은 두 가지 외부 입력을 전제한다.

* `NodeForge/protos/` 의 `nodeforge.proto` (proto 컴파일 용도) ← api-protos Sprint 1-4 완료, canonical 이관
* `DockGuard` 저장소의 Dockerfile 정책 디렉터리 (dockguard.wasm 생성 용도)

---

## 1. proto 소스 경로 설정

### 현재 상태 (2026-04-19)

**api-protos Sprint 1-4 완료.** `nodeforge.proto` canonical 경로는 `NodeForge/protos/nodeforge/v1/`이다.
`NodeKit.csproj`는 이제 `NodeForge/protos/`를 자동 탐지 기준으로 사용한다.

### 경로 설정

`NodeKit.csproj`는 `ApiProtosRoot` MSBuild 속성 아래에서 `nodeforge/v1/nodeforge.proto`를 찾는다.

1. 기본 탐지 경로 사용 (자동 감지)
   * `/opt/go/src/github.com/HeaInSeo/NodeForge/protos` ← canonical

2. 명시적으로 경로 지정

```bash
dotnet test NodeKit.sln /p:ApiProtosRoot=/opt/go/src/github.com/HeaInSeo/NodeForge/protos
dotnet build NodeKit.sln /p:ApiProtosRoot=/opt/go/src/github.com/HeaInSeo/NodeForge/protos
```

`ApiProtosRoot`가 비어 있거나 `nodeforge.proto`가 없으면 빌드가 명확한 에러 메시지와 함께 중단된다.

---

## 2. DockGuard 정책 설정

`Makefile`의 `policy` 타겟은 기본적으로 아래 경로를 사용한다.

```text
../DockGuard/policy/dockerfile
```

기본 경로가 맞지 않으면 `DOCKGUARD`를 명시해 실행한다.

```bash
make policy DOCKGUARD=/path/to/DockGuard/policy/dockerfile
```

경로가 없으면 `Makefile`이 즉시 실패하며 필요한 변수명을 안내한다.

빌드된 `dockguard.wasm`은 `assets/policy/dockguard.wasm`에 복사된다.

---

## 3. 현재 한계 및 주의사항

### 버전 정합성

NodeKit과 NodeForge의 proto 스키마가 일치해야 한다:
- `BuildRequest`, `RegisterToolRequest`, `RegisteredToolDefinition` 필드가 두 저장소 모두 같아야 함
- proto 변경 시 NodeForge `protos/nodeforge/v1/nodeforge.proto` 수정 후 NodeKit 재빌드 필요

### compiler warning

현재 `dotnet build` 시 276개 경고가 존재한다 (CA1062).
CLAUDE.md §8 기준 위반 — 다음 작업 전 해소 필요.

---

## 4. 권장 부트스트랩 순서

```bash
# 1. 빌드 (NodeForge/protos/ 자동 탐지됨 — 경로 지정 불필요)
dotnet build NodeKit.sln

# 2. DockGuard .wasm 번들 생성 (DockGuard 저장소 경로 지정)
make policy DOCKGUARD=/opt/dotnet/src/github.com/HeaInSeo/DockGuard/policy/dockerfile

# 3. 테스트
dotnet test NodeKit.sln
```

현재 개발 환경 경로:
- NodeForge protos: `/opt/go/src/github.com/HeaInSeo/NodeForge/protos` (canonical)
- DockGuard: `/opt/dotnet/src/github.com/HeaInSeo/DockGuard`
