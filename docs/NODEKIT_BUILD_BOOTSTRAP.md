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

`EnvironmentSpec`은 현재 NodeKit 내부 DTO와 테스트에서는 보존된다. 다만 외부 `api-protos` 저장소의 `nodeforge.proto`에는 아직 이 필드가 없어서, gRPC 전송 단계까지는 완전히 연결되지 않는다.

즉, 아래 두 저장소 변경이 함께 필요하다.

* NodeKit: 로컬 DTO 및 UI/validator 정합성 유지
* api-protos / NodeForge: `BuildRequest` proto에 `EnvironmentSpec` 추가

---

## 4. 권장 부트스트랩 순서

```bash
dotnet test NodeKit.sln /p:ApiProtosRoot=/path/to/api-protos/protos
make policy DOCKGUARD=/path/to/DockGuard/policy/dockerfile
dotnet build NodeKit.sln /p:ApiProtosRoot=/path/to/api-protos/protos
```
