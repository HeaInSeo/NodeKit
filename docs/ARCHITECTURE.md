# NodeKit 아키텍처 개요

버전: 1.0  
작성일: 2026-04-18  
상태: 현재 구현 기준

관련 문서:
- **[PLATFORM_MAP.md](../../NodeVault/docs/PLATFORM_MAP.md)** — 전체 플랫폼 구성, end-to-end 흐름, 현재 상태 (개발 세션 시작 시 먼저 읽을 것)
  - 절대 경로: `/opt/go/src/github.com/HeaInSeo/NodeVault/docs/PLATFORM_MAP.md`
- [CLAUDE.md](../CLAUDE.md) — 책임 경계, 재현성 규칙, 결정 체크리스트 (규범 문서)
- [NODEKIT_UI_STRUCTURE.md](NODEKIT_UI_STRUCTURE.md) — UI 패널 상세 구조 및 흐름
- [NODEKIT_BUILD_BOOTSTRAP.md](NODEKIT_BUILD_BOOTSTRAP.md) — 빌드 환경 설정

---

## 역할 한 줄 정의

관리자가 Tool/Data를 정의하고 L1 검증을 수행한 뒤 NodeVault로 빌드 요청을 전송하는 **관리자 전용 데스크톱 클라이언트**.

---

## 컴포넌트 레이어 구조

```
┌──────────────────────────────────────────────────────────┐
│  UI Layer (Avalonia)                                     │
│  MainWindow.axaml.cs                                     │
│  ├── AuthoringPanel     ToolDefinition 작성 + L1 검증   │
│  ├── ToolListPanel      등록된 Tool 목록 (Catalog REST)  │
│  ├── DataListPanel      등록된 Data 목록 (Catalog REST)  │
│  └── PolicyPanel        DockGuard 번들 관리              │
├──────────────────────────────────────────────────────────┤
│  Domain Layer (src/Authoring/)                           │
│  ├── ToolDefinition     Tool 초안 모델 (빌드 전 상태)   │
│  ├── DataDefinition     Data 초안 모델 (등록 전 상태)   │
│  ├── ToolInput/Output   Port 스펙 (name/role/format/shape) │
├──────────────────────────────────────────────────────────┤
│  Validation Layer (src/Validation/)                      │
│  ├── RequiredFieldsValidator   필수 필드 확인            │
│  ├── ImageUriValidator         @sha256: digest 필수, latest 차단 │
│  ├── PackageVersionValidator   pip/conda 버전 고정 확인  │
│  └── ValidatedDefinitionState  fingerprint 기반 검증 상태 추적 │
├──────────────────────────────────────────────────────────┤
│  Policy Layer (src/Policy/)                              │
│  ├── WasmPolicyChecker         DockGuard .wasm 실행 (L1) │
│  ├── GrpcPolicyBundleProvider  PolicyService gRPC 번들 로드 │
│  ├── LocalFilePolicyBundleProvider  로컬 .wasm 파일 로드 │
│  └── IPolicyBundleProvider     스왑 가능 인터페이스      │
├──────────────────────────────────────────────────────────┤
│  gRPC/HTTP Client Layer (src/Grpc/)                      │
│  ├── GrpcBuildClient           BuildService gRPC (빌드 스트림) │
│  ├── HttpCatalogClient         Catalog REST (Tool/Data 목록) │
│  ├── GrpcPolicyBundleProvider  PolicyService gRPC (정책 관리) │
│  └── GrpcToolRegistryClient    [레거시 — 미사용]         │
└──────────────────────────────────────────────────────────┘
```

---

## 외부 연결 엔드포인트

| 연결 대상 | 프로토콜 | UI 기본값 | 운영 주소 |
|-----------|---------|-----------|-----------|
| NodeVault BuildService | gRPC | `http://100.123.80.48:50051` | `http://nodevault.10.113.24.96.nip.io:80` |
| NodeVault PolicyService | gRPC | 위와 동일 (`NodeForgeAddressBox`) | 위와 동일 |
| Catalog REST API | HTTP | `http://100.123.80.48:8080` | `http://100.123.80.48:8080` |

> UI에 직접 입력하는 방식이므로 기본값이 달라도 사용자가 변경 가능하다.

---

## Tool 빌드 데이터 흐름

```
[관리자]
  │ UI 폼 입력
  ▼
ToolDefinition (초안 모델)
  │
  ├── Validation Layer
  │   ├── RequiredFieldsValidator
  │   ├── ImageUriValidator       → latest 차단, @sha256: 필수
  │   └── PackageVersionValidator → pip/conda 버전 고정 확인
  │
  ├── Policy Layer
  │   └── WasmPolicyChecker       → DockGuard .wasm (DFM/DSF/DGF 규칙)
  │
  │   L1 통과
  │
  ├── BuildRequestFactory.FromToolDefinition()
  │
  ▼
BuildRequest (proto)
  │ gRPC stream
  ▼
[NodeVault BuildService]
  → L2(podbridge5) → L3(dry-run) → L4(smoke) → index 등록
  → BuildEvent stream →
  ▼
[NodeKit] 빌드 로그 표시 → 완료 알림
```

---

## 정책 번들 관리 흐름

```
초기화 (앱 시작):
  LocalFilePolicyBundleProvider → assets/policy/dockguard.wasm → WasmPolicyChecker

런타임 갱신 (PolicyPanel):
  GrpcPolicyBundleProvider.GetBundleAsync() → NodeVault PolicyService
    → 새 .wasm 번들 수신 → WasmPolicyChecker.ReloadAsync()

IPolicyBundleProvider 인터페이스가 두 Provider를 추상화.
```

---

## Catalog 조회 흐름

```
NavToolListButton / NavDataListButton 클릭
  → HttpCatalogClient.ListToolsAsync() / ListDataAsync()
  → GET {CatalogAddressBox}/v1/catalog/tools (또는 /data)
  → List<RegisteredTool> / List<RegisteredData>
  → UI 카드 표시
```

`lifecycle_phase = Active` 항목만 반환됨 (NodeVault 서버 측 필터).

---

## 현재 구현 완료 항목

| 기능 | 상태 | 관련 클래스 |
|------|------|-------------|
| Tool 정의 UI (AuthoringPanel) | 완료 | `ToolDefinition`, `MainWindow` |
| L1 정적 검증 | 완료 | `RequiredFieldsValidator`, `ImageUriValidator`, `PackageVersionValidator` |
| DockGuard .wasm 정책 검사 | 완료 | `WasmPolicyChecker`, `LocalFilePolicyBundleProvider` |
| gRPC 정책 번들 동적 로드 | 완료 | `GrpcPolicyBundleProvider` |
| BuildRequest gRPC 전송 + 스트림 수신 | 완료 | `GrpcBuildClient`, `BuildRequestFactory` |
| AdminToolList (Catalog REST) | 완료 | `HttpCatalogClient` |
| AdminDataList (Catalog REST) | 완료 | `HttpCatalogClient.ListDataAsync()` |
| Data 초안 모델 | 완료 (도메인 객체만) | `DataDefinition`, `DataRegisterRequest` |

---

## 알려진 미완료 항목

### 즉시 수정 필요

**compiler warning 276개** — CLAUDE.md §8 위반
- 원인: `HttpCatalogClient.cs` CA1062 (null 검증 누락), `DataRegisterRequestFactory.cs` CA1062
- 수정 필요: 다음 작업 시작 전 해소

### 구현 대기

| 항목 | 상태 | 관련 TODO |
|------|------|-----------|
| DataRegisterRequest UI 연결 | DataPanel에 입력 폼 없음 | NodeVault P3 TODO-12 |
| DataRegisterRequest gRPC 전송 | Factory 존재하나 UI 미연결 | NodeVault P3 TODO-12 |

---

## 레거시 클래스

### `GrpcToolRegistryClient`

`src/Grpc/GrpcToolRegistryClient.cs` 에 존재하지만 **MainWindow에서 사용되지 않는다**.
`HttpCatalogClient` (Catalog REST)로 완전히 대체됨.

- `IToolRegistryClient` 인터페이스와 `RegisteredTool` 클래스가 같은 파일에 정의됨
- `HttpCatalogClient`도 `IToolRegistryClient`를 구현함
- 향후 api-protos cleanup 후 이 파일 삭제 검토 가능

---

## 빌드 의존성 요약

| 외부 의존성 | 용도 | 경로 |
|-------------|------|------|
| `NodeVault/protos/` | `nodeforge.proto` 빌드 시 proto 컴파일 | `ApiProtosRoot` MSBuild 속성 (자동 탐지) |
| `DockGuard` 저장소 | `dockguard.wasm` 번들 생성 | `make policy DOCKGUARD=...` |

api-protos Sprint 1-4 완료. canonical source는 `NodeVault/protos/nodeforge/v1/`.
