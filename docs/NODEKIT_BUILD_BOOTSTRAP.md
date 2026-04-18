# NodeKit Build Bootstrap

작성일: 2026-04-08  
최종 수정: 2026-04-18

---

## 목적

이 문서는 NodeKit을 현재 개발 환경 외부에서 빌드할 때 필요한 외부 의존성과 경로 설정을 정리한다.

현재 NodeKit은 두 가지 외부 입력을 전제한다.

* `api-protos` 저장소의 `nodeforge.proto` (proto 컴파일 용도)
* `DockGuard` 저장소의 Dockerfile 정책 디렉터리 (dockguard.wasm 생성 용도)

---

## 1. api-protos 설정

### 현재 상태 (2026-04-18)

**api-protos는 freeze 상태다.** `nodeforge.proto`를 포함한 proto 파일들이 NodeForge 저장소로 이관 작업 중이며, 이관 완료 전까지 `api-protos` 경로를 계속 사용한다.

- 이관 계획: NodeForge `docs/PROTO_OWNERSHIP_SPRINT_PLAN.md` Sprint 3/4
- 이관 후: `NodeForge/protos/nodeforge/v1/nodeforge.proto` 경로 사용
- 이관 전까지: 아래 `api-protos` 경로 그대로 사용

### 경로 설정

`NodeKit.csproj`는 `ApiProtosRoot` MSBuild 속성 아래에서 `nodeforge/v1/nodeforge.proto`를 찾는다.

허용되는 방식은 두 가지다.

1. 기본 탐지 경로 사용 (아래 중 하나가 있으면 자동 감지)
   * `/opt/go/src/github.com/HeaInSeo/api-protos/protos`
   * `../api-protos/protos` (NodeKit 저장소 옆에 `api-protos`를 둔 경우)

2. 명시적으로 경로 지정

```bash
dotnet test NodeKit.sln /p:ApiProtosRoot=/path/to/api-protos/protos
dotnet build NodeKit.sln /p:ApiProtosRoot=/path/to/api-protos/protos
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

NodeKit, `api-protos`, NodeForge의 proto 스키마가 일치해야 한다:
- `BuildRequest`, `RegisterToolRequest`, `RegisteredToolDefinition` 필드가 세 저장소 모두 같아야 함
- api-protos freeze 기간 중에는 proto 변경 없이 현 상태를 유지

### compiler warning

현재 `dotnet build` 시 276개 경고가 존재한다 (CA1062).
CLAUDE.md §8 기준 위반 — 다음 작업 전 해소 필요.

---

## 4. 권장 부트스트랩 순서

```bash
# 1. proto 빌드 (api-protos 경로 지정)
dotnet test NodeKit.sln /p:ApiProtosRoot=/opt/go/src/github.com/HeaInSeo/api-protos/protos

# 2. DockGuard .wasm 번들 생성 (DockGuard 저장소 경로 지정)
make policy DOCKGUARD=/opt/dotnet/src/github.com/HeaInSeo/DockGuard/policy/dockerfile

# 3. 최종 빌드
dotnet build NodeKit.sln /p:ApiProtosRoot=/opt/go/src/github.com/HeaInSeo/api-protos/protos
```

현재 개발 환경 경로:
- api-protos: `/opt/go/src/github.com/HeaInSeo/api-protos`
- DockGuard: `/opt/dotnet/src/github.com/HeaInSeo/DockGuard`
