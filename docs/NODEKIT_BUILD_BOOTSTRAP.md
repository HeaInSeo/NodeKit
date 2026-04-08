# NodeKit Build Bootstrap

작성일: 2026-04-08
최종 수정: 2026-04-08

---

## 목적

이 문서는 NodeKit을 현재 개발 환경 외부에서 빌드할 때 필요한 외부 의존성과 경로 설정을 정리한다.

현재 NodeKit은 두 가지 외부 입력을 전제한다.

* `api-protos` 저장소의 `nodeforge.proto`
* `DockGuard` 저장소의 Dockerfile 정책 디렉터리

---

## 1. api-protos 설정

`NodeKit.csproj`는 `ApiProtosRoot` MSBuild 속성 아래에서 `nodeforge/v1/nodeforge.proto`를 찾는다.

허용되는 방식은 두 가지다.

1. 기본 탐지 경로 사용
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

---

## 3. 현재 한계

`EnvironmentSpec` 전파는 이제 NodeKit, `api-protos`, `NodeForge`까지 연결되어 있다. 남아 있는 제약은 저장소 간 버전 정합성이다.

즉, 아래 조건을 함께 유지해야 한다.

* NodeKit: 로컬 DTO, UI, validator, gRPC factory가 최신 proto와 일치해야 한다.
* api-protos / NodeForge: `BuildRequest`, `RegisterToolRequest`, `RegisteredToolDefinition` 스키마가 같은 필드 집합을 유지해야 한다.

---

## 4. 권장 부트스트랩 순서

```bash
dotnet test NodeKit.sln /p:ApiProtosRoot=/path/to/api-protos/protos
make policy DOCKGUARD=/path/to/DockGuard/policy/dockerfile
dotnet build NodeKit.sln /p:ApiProtosRoot=/path/to/api-protos/protos
```
