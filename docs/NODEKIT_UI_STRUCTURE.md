# NodeKit UI 구조 및 흐름

버전: 1.0.1
작성일: 2026-04-15 / 갱신: 2026-04-18
상태: **확정**

---

## 1. 전체 레이아웃

```
┌────────────────────────────────────────────────────────────┐
│  상단 타이틀바  NodeKit — Tool Authoring Console          │
├─────────────┬──────────────────────────────────────────────┤
│             │                                              │
│  좌측       │  메인 컨텐츠 패널                           │
│  네비게이션 │  (AuthoringPanel / ToolListPanel /          │
│  (180 px)   │   DataListPanel / PolicyPanel)              │
│             │                                              │
├─────────────┴──────────────────────────────────────────────┤
│  하단 상태바  StatusBar                                   │
└────────────────────────────────────────────────────────────┘
```

---

## 2. 네비게이션 버튼 → 패널 매핑

| 버튼 (`Name`) | 연결 패널 (`Name`) | 역할 |
|--------------|-------------------|------|
| `NavAuthoringButton` | `AuthoringPanel` | Tool 정의 작성 (기본 화면) |
| `NavToolListButton` | `ToolListPanel` | 등록된 Tool 목록 조회 |
| `NavDataListButton` | `DataListPanel` | 등록된 참조 Data 목록 조회 |
| `NavPolicyButton` | `PolicyPanel` | DockGuard 정책 번들 관리 |

패널 전환 구현: `ShowPanel(target)` — 해당 패널만 `IsVisible = true`, 나머지는 `false`.

---

## 3. 패널별 상세 구조

### 3.1 Tool 정의 패널 (`AuthoringPanel`)

Tool authoring 워크플로의 시작점. `ToolDefinition` 초안을 작성하고 L1 검증 후 gRPC로 BuildRequest를 전송한다.

#### 입력 필드

| 컨트롤 (`Name`) | 역할 | 검증 |
|----------------|------|------|
| `ToolNameBox` | 툴 이름 (예: `bwa-mem2`) | L1: 공백 불가 |
| `ToolVersionBox` | 버전 (예: `2.2.1`) | L1: 공백 불가 |
| `ImageUriBox` | 베이스 이미지 URI | L1: `@sha256:` digest 필수, `latest` 차단 |
| `DockerfileBox` | Dockerfile 내용 | L1: `apt-get install` 버전 미고정 차단 |
| `ScriptBox` | 실행 스크립트 | — |
| `CommandBox` | CMD 오버라이드 (JSON 배열) | L1: JSON 형식 검증 |
| `EnvSpecBox` | conda yml / requirements.txt | L1: `pip install` 버전 미고정 차단 |

#### I/O 포트 선언

- **Inputs**: `InputRowsPanel` — 동적으로 행 추가 (`AddInputButton`)
  - 컬럼: 이름 / 역할(role) / 형식(format) / shape
- **Outputs**: `OutputRowsPanel` — 동적으로 행 추가 (`AddOutputButton`)
  - 컬럼: 이름 / 역할(role) / 형식(format) / shape / class

#### Display 메타데이터 섹션

| 컨트롤 (`Name`) | 역할 |
|----------------|------|
| `DisplayLabelBox` | UI 카드 제목 (없으면 `tool_name version` 으로 자동 생성) |
| `DisplayCategoryBox` | 카테고리 (예: `Alignment`) |
| `DisplayDescriptionBox` | 툴팁 설명 |
| `DisplayTagsBox` | 태그 (쉼표 구분) |

#### 연결 주소

| 컨트롤 (`Name`) | 용도 | 기본값 |
|----------------|------|--------|
| `NodeForgeAddressBox` | gRPC 엔드포인트 (Build / Policy) | `http://100.123.80.48:50051` |
| `CatalogAddressBox` | Catalog REST API (Tool/Data 목록) | `http://100.123.80.48:8080` |

#### 액션 버튼

| 버튼 (`Name`) | 동작 |
|--------------|------|
| `ValidateButton` | `WasmPolicyChecker`로 L1 검증 실행 |
| `SendBuildButton` | L1 통과 후 활성화. `BuildRequest` gRPC 전송 |

#### 피드백 패널

| 패널 (`Name`) | 표시 조건 |
|--------------|-----------|
| `ValidationResultPanel` | L1 위반 항목 목록 (`ViolationsList`) |
| `ValidationPassPanel` | L1 전체 통과 |
| `BuildLogPanel` | 빌드 로그 스트림 (`BuildLogBox`) |
| `BuildSuccessPanel` | 빌드 + 등록 완료 (digest 표시) |

#### L1 검증 흐름

```
ValidateButton click
    → WasmPolicyChecker.EvaluateAsync(toolDef)
    → violations.Count > 0: ValidationResultPanel 표시
    → violations.Count == 0: ValidationPassPanel 표시, SendBuildButton 활성화
    → SendBuildButton click
    → BuildRequestFactory.FromToolDefinition(toolDef)
    → GrpcBuildClient.BuildAndRegisterAsync(req)
    → 스트림 수신 → BuildLogBox append
    → 완료 → BuildSuccessPanel 표시
```

---

### 3.2 등록된 Tools 패널 (`ToolListPanel`)

Catalog 서비스 REST API에서 `lifecycle_phase = Active` 툴 목록을 조회해 표시한다.

| 컨트롤 (`Name`) | 역할 |
|----------------|------|
| `RefreshToolListButton` | `LoadToolListAsync()` 호출 |
| `ToolListEmptyPanel` | 목록이 비었을 때 안내 메시지 |
| `ToolListItems` | `RegisteredTool` 텍스트 카드 목록 |

#### 로드 흐름

```
NavToolListButton 클릭 (또는 RefreshToolListButton 클릭)
    → HttpCatalogClient.ListToolsAsync()
    → GET {CatalogAddressBox}/v1/catalog/tools
    → JSON → List<RegisteredTool>
    → ToolListItems 업데이트
    → 비어있으면 ToolListEmptyPanel 표시
```

#### 표시 항목 (카드 한 줄)

```
{DisplayLabel}  [{LifecyclePhase}]  {IntegrityHealth}
cas: {CasHash}  @{StableRef}  ({RegisteredAt})
```

---

### 3.3 등록된 Data 패널 (`DataListPanel`)

Catalog 서비스 REST API에서 `lifecycle_phase = Active` 참조 데이터 목록을 조회해 표시한다.

| 컨트롤 (`Name`) | 역할 |
|----------------|------|
| `RefreshDataListButton` | `LoadDataListAsync()` 호출 |
| `DataListEmptyPanel` | 목록이 비었을 때 안내 메시지 |
| `DataListItems` | `RegisteredData` 텍스트 카드 목록 |

#### 로드 흐름

```
NavDataListButton 클릭 (또는 RefreshDataListButton 클릭)
    → HttpCatalogClient.ListDataAsync()
    → GET {CatalogAddressBox}/v1/catalog/data
    → JSON → List<RegisteredData>
    → DataListItems 업데이트
```

#### 표시 항목 (카드 한 줄)

```
{DisplayLabel}  [{LifecyclePhase}]  {IntegrityHealth}
{Format}  {Description}  cas: {CasHash}  ({RegisteredAt})
```

---

### 3.4 정책 관리 패널 (`PolicyPanel`)

DockGuard .wasm 번들의 버전 조회 및 재로드를 담당한다.

| 컨트롤 (`Name`) | 역할 |
|----------------|------|
| `PolicyBundleVersionLabel` | 현재 로드된 번들 버전 표시 |
| `PolicyListItems` | 적용 중인 정책 규칙 목록 |
| `RefreshPolicyListButton` | 정책 목록 새로 고침 |
| `ReloadBundleButton` | `WasmPolicyChecker` 번들 재로드 |

#### 번들 재로드 흐름

```
ReloadBundleButton 클릭
    → IPolicyBundleProvider.GetBundleAsync()
    → WasmPolicyChecker.ReloadAsync(bundle)
    → PolicyBundleVersionLabel 업데이트
    → PolicyListItems 업데이트
```

---

## 4. 상태바 (`StatusBar`)

하단 1줄. 작업 상태를 문자열로 표시한다.

| 상태 | 표시 예시 |
|------|-----------|
| 기본 | `준비` |
| 검증 중 | `L1 검증 중...` |
| 빌드 전송 | `BuildRequest 전송 중...` |
| 로드 중 | `툴 목록 로드 중...` |
| 오류 | `오류: {message}` |

---

## 5. 의존성 경계

| 의존 대상 | 방향 | 용도 |
|-----------|------|------|
| `WasmPolicyChecker` | NodeKit → DockGuard .wasm | L1 정책 실행 |
| `GrpcBuildClient` | NodeKit → NodeForge BuildService gRPC | BuildRequest 전송 + 빌드 이벤트 수신 |
| `GrpcPolicyBundleProvider` | NodeKit → NodeForge PolicyService gRPC | 정책 번들 동적 로드 및 목록 조회 |
| `HttpCatalogClient` | NodeKit → Catalog REST | Tool/Data 목록 조회 (read-only) |
| `GrpcToolRegistryClient` | — | **레거시 — MainWindow에서 미사용**. `HttpCatalogClient`로 대체됨 |

**금지**: NodeKit은 NodeVault 내부 저장 구조(index, CAS 파일)를 직접 알지 않는다.
**금지**: NodeKit은 K8s API / Job 스케줄링 / 이미지 빌드 로직을 가지지 않는다.

---

## 6. 주요 C# 타입 참조

| 타입 | 파일 | 역할 |
|------|------|------|
| `ToolDefinition` | `src/Authoring/ToolDefinition.cs` | Tool 작성 초안 모델 |
| `DataDefinition` | `src/Authoring/DataDefinition.cs` | Data 작성 초안 모델 |
| `BuildRequest` | `src/Grpc/BuildRequest.cs` | gRPC 전송 모델 |
| `DataRegisterRequest` | `src/Grpc/DataRegisterRequest.cs` | Data gRPC 전송 모델 |
| `RegisteredTool` | `src/Grpc/GrpcToolRegistryClient.cs` | Catalog 조회 결과 |
| `RegisteredData` | `src/Grpc/HttpCatalogClient.cs` | Catalog 조회 결과 |
| `IToolRegistryClient` | `src/Grpc/GrpcToolRegistryClient.cs` | Catalog 클라이언트 인터페이스 (별도 파일 없음) |
| `HttpCatalogClient` | `src/Grpc/HttpCatalogClient.cs` | Catalog REST 클라이언트 (`IToolRegistryClient` 구현) |
| `GrpcBuildClient` | `src/Grpc/GrpcBuildClient.cs` | BuildService gRPC 클라이언트 |
| `IBuildClient` | `src/Grpc/IBuildClient.cs` | Build 클라이언트 인터페이스 |
| `WasmPolicyChecker` | `src/Policy/WasmPolicyChecker.cs` | L1 정책 실행기 |
| `MainWindow` | `UI/MainWindow.axaml(.cs)` | 메인 윈도우 code-behind |
