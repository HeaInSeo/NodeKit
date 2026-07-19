# NodeKit 아키텍처 개요

버전: 1.3
작성일: 2026-04-18 / 갱신: 2026-07-19 (컴포넌트 레이어/데이터 흐름/완료 항목 표에 남아있던
GrpcBuildClient/BuildRequest 서술을 GrpcToolSpecClient/raw_spec 경로로 정정)
상태: 현재 구현 + active planning 기준

관련 문서:
- [NODEKIT_CLI_FIRST_SPRINT_PLAN.md](NODEKIT_CLI_FIRST_SPRINT_PLAN.md) — 현재 NodeKit 작업 기준
- **[PLATFORM_MAP.md](../../NodeVault/docs/PLATFORM_MAP.md)** — 전체 플랫폼 구성, end-to-end 흐름, 현재 상태 (개발 세션 시작 시 먼저 읽을 것)
  - 절대 경로: `/opt/go/src/github.com/HeaInSeo/NodeVault/docs/PLATFORM_MAP.md`
- [CLAUDE.md](../CLAUDE.md) — 책임 경계, 재현성 규칙, 결정 체크리스트 (규범 문서)
- [NODEKIT_UI_STRUCTURE.md](NODEKIT_UI_STRUCTURE.md) — UI 패널 상세 구조 및 흐름
- [NODEKIT_BUILD_BOOTSTRAP.md](NODEKIT_BUILD_BOOTSTRAP.md) — 빌드 환경 설정

---

## 역할 한 줄 정의

관리자가 Tool/Data를 정의하고 L1 검증을 수행한 뒤 NodeVault로 빌드 요청을 전송하는 **관리자 전용 데스크톱 클라이언트**.

NodeVault는 Kubernetes data-plane app이며, NodeKit은 문서화된 gRPC/REST API로만 연동한다.
NodeKit은 Kubernetes API를 직접 호출하지 않는다.
NodeVault 및 관련 플랫폼 서비스의 live 테스트는 원격 인프라를 기본으로 하며,
접속 정보와 운영 문서는 `~/.config/infra-lab` 아래 문서를 확인한다.
NodeKit 쪽 live 연동 테스트(`GrpcToolSpecClientIntegrationTests`,
`GrpcResolveRecipeClientIntegrationTests`)는 기본적으로 스킵되며
`NODEKIT_NODEVAULT_URL` 환경변수를 설정해야 옵트인된다(예:
`NODEKIT_NODEVAULT_URL=http://100.123.80.48:50051`). 고정 recipe fixture와
seoy 수동 절차는 `docs/NODEKIT_SEOY_SMOKE_FIXTURES.md` 참조.

`ToolSpecRequest → ResolveToolSpec → SubmitToolBuild → WatchToolBuild`
경로만 사용한다(NodeVault Phase 1 gate 2026-07-02 오픈,
`PLATFORM_SCHEDULE.md` Phase 6 완료). 기존 `BuildRequest` / `BuildAndRegister`
legacy gRPC 경로는 CLI와 Avalonia GUI 양쪽 모두에서 완전히 제거됐다.

---

## 컴포넌트 레이어 구조

```
┌──────────────────────────────────────────────────────────┐
│  UI Layer (Avalonia)                                     │
│  MainWindow.axaml.cs                                     │
│  ├── AuthoringPanel     ToolDefinition 작성 + L1 검증   │
│  ├── ToolListPanel      등록된 Tool 목록 (Catalog REST)  │
│  ├── DataListPanel      등록된 Data 목록 (Catalog REST)  │
│  ├── PolicyPanel        DockGuard 번들 관리              │
│  └── SettingsPanel      서버 주소 설정 + JSON 영속화     │
├──────────────────────────────────────────────────────────┤
│  Domain Layer (src/Authoring/)                           │
│  ├── ToolDefinition     Tool 초안 모델 (빌드 전 상태)   │
│  ├── DataDefinition     Data 초안 모델 (등록 전 상태)   │
│  ├── ToolInput/Output   Port 스펙 (name/role/format/shape) │
├──────────────────────────────────────────────────────────┤
│  Validation Layer (src/Validation/)                      │
│  ├── RequiredFieldsValidator   필수 필드 확인            │
│  ├── ImageUriValidator         @sha256: digest 필수, latest 차단 │
│  ├── PackageVersionValidator   pip/conda 버전 고정 확인 (=version 형식 요구; │
│  │                             build string은 NodeVault ResolveToolSpec이 담당) │
│  └── ValidatedDefinitionState  fingerprint 기반 검증 상태 추적 │
├──────────────────────────────────────────────────────────┤
│  Policy Layer (src/Policy/)                              │
│  ├── WasmPolicyChecker         DockGuard .wasm 실행 (L1) │
│  ├── GrpcPolicyBundleProvider  PolicyService gRPC 번들 로드 │
│  ├── LocalFilePolicyBundleProvider  로컬 .wasm 파일 로드 │
│  └── IPolicyBundleProvider     스왑 가능 인터페이스      │
├──────────────────────────────────────────────────────────┤
│  gRPC/HTTP Client Layer (src/Grpc/)                      │
│  ├── GrpcToolSpecClient        BuildService gRPC          │
│  │   (ResolveToolSpec → SubmitToolBuild → WatchToolBuild) │
│  ├── ToolSpecRawSpecFactory    ToolDefinition → raw_spec  │
│  ├── HttpCatalogClient         Catalog REST (Tool/Data 목록) │
│  ├── GrpcPolicyBundleProvider  PolicyService gRPC (정책 관리) │
│  └── GrpcToolRegistryClient    [레거시 — 미사용]         │
└──────────────────────────────────────────────────────────┘
```

`UI/ViewModels/BuildSubmissionViewModel.cs`가 `GrpcToolSpecClient`의 수명과
in-flight build ID 추적을 전담한다 (CLI의 `SubmitCommand`와 동일한 패턴).

---

## 외부 연결 엔드포인트

| 연결 대상 | 프로토콜 | 기본값 | 설정 위치 |
|-----------|---------|--------|-----------|
| NodeVault BuildService | gRPC | `http://100.123.80.48:50051` | ⚙ 서버 설정 → NodeVault 주소 |
| NodeVault PolicyService | gRPC | 위와 동일 | 위와 동일 |
| Catalog REST API | HTTP | `http://100.123.80.48:8080` | ⚙ 서버 설정 → Catalog 주소 |

주소는 `AppSettings` (`src/Settings/AppSettings.cs`)에 저장되며 앱 시작 시 로드된다.
설정 파일: Linux `~/.config/NodeKit/settings.json`, Windows `%AppData%\NodeKit\settings.json`

> 설정 패널(`SettingsPanel`)에서 변경 후 저장하면 즉시 반영되며 캐시된 클라이언트가 폐기된다.

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
  │   └── PackageVersionValidator → pip/conda 버전 고정 확인 (=version 형식만 요구)
  │
  ├── Policy Layer
  │   └── WasmPolicyChecker       → DockGuard .wasm (DFM/DSF/DGF 규칙)
  │
  │   L1 통과
  │
  ├── [사전 조회] NodeVault ResolveRecipe 호출 (conda/micromamba/mirror/BioContainer)
  │   ├── Harbor에 tool+version 이미지 있음 → artifact 후보 1개 반환 (자동 선택)
  │   ├── 없음 + 열린망 → 외부 소스 조회 → 후보 목록 반환 → 사용자 선택
  │   └── 없음 + 폐쇄망 → InvalidArgument (관리자 Harbor 사전 등록 필요)
  │   ※ source build / Dockerfile fallback은 이미 고정 — 사전 조회 불필요
  │
  ├── ToolSpecRawSpecFactory.Build(toolDefinition)
  │   (artifact 고정된 상태로 raw_spec 생성 — PascalCase → snake_case)
  │
  ▼
raw_spec (JSON 문자열)
  │ gRPC (GrpcToolSpecClient, BuildSubmissionViewModel이 수명 관리)
  ▼
[NodeVault BuildService]
  ResolveToolSpec → SubmitToolBuild → WatchToolBuild
  → L2(image build) → L3(dry-run) → L4(smoke) → index 등록
  → BuildEvent stream (WatchToolBuild) →
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
| ToolSpec gRPC 전송(ResolveToolSpec/SubmitToolBuild) + WatchToolBuild 스트림 수신 | 완료 | `GrpcToolSpecClient`, `ToolSpecRawSpecFactory`, `BuildSubmissionViewModel` |
| AdminToolList (Catalog REST) | 완료 | `HttpCatalogClient` |
| AdminDataList (Catalog REST) | 완료 | `HttpCatalogClient.ListDataAsync()` |
| Data 초안 모델 | 완료 (도메인 객체만) | `DataDefinition`, `DataRegisterRequest` |

---

## 알려진 미완료 항목

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
| `NodeVault/protos/` | `nodevault.proto` 빌드 시 proto 컴파일 | `ApiProtosRoot` MSBuild 속성 (자동 탐지) |
| `DockGuard` 저장소 | `dockguard.wasm` 번들 생성 | `make policy DOCKGUARD=...` |

api-protos Sprint 1-4 완료. canonical source는 `NodeVault/protos/nodevault/v1/`.

---

## 문서 기준

현재 기준 문서는 `NODEKIT_CLI_FIRST_SPRINT_PLAN.md`와 `CLAUDE.md`이다.
`docs/obsolete/` 아래의 예전 스프린트/조사 문서는 보관용이며, 현재 작업 순서나 API migration 판단에 사용하지 않는다.
