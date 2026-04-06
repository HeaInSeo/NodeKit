# NodeKit 스프린트 기술 문서

작성일: 2026-04-05
최종 수정: 2026-04-06 (builder workload 전략 반영, NodeForge 명명 반영)
상태: 확정

---

## 이 문서에 대해

이 문서는 NodeKit 스프린트의 구현 착수, 검증, 회고에 바로 사용할 수 있는 개발 기준 문서다.

NodeKit은 완성 제품이 아니라 스프린트 프로젝트다. 이번 스프린트에서는 **NodeKit**(authoring client)과 **NodeForge**(Kubernetes-side control plane) 두 프로젝트를 함께 구현한다. 스프린트에서 얻은 결과물(코드, 문서, 발견된 이슈)은 **DagEdit** 등 후속 파이프라인 시스템 설계에 반영된다. DagEdit은 별도 트랙으로 진행 중이며 이번 스프린트의 직접 구현 대상이 아니다.

---

## 1. 범위 / 용어

### 1.1 이번 스프린트 범위

| 구분 | 내용 |
|------|------|
| **MUST** | NodeKit authoring UI (L1) |
| **MUST** | NodeForge (L2~L4) |
| **MUST** | NodeKit ↔ NodeForge gRPC 연동 |
| **MUST** | DockGuard 정책 검사 (WasmPolicyChecker) |
| **MUST** | kind 기반 dry-run 검증 |
| **MUST** | 최소 smoke run (happy-path) |
| **MUST** | 툴 등록 흐름 |
| **STRETCH** | 파이프라인 캔버스 드래그&드롭 |
| **STRETCH** | 노드 연결선 + 숫자 라벨 |
| **STRETCH** | 연결선 클릭 → 매핑 상세창 |

STRETCH 항목이 완성되지 않아도 스프린트는 성공으로 판정한다.

### 1.2 의도적 비범위

| 항목 | 이유 |
|------|------|
| 변수 치환 (`${R1}` → 실제 경로) | DagEdit에서 설계 예정 |
| FileBlock 데이터 바인딩 | tori 서비스 연동 시 설계 |
| BoundPipeline / ScheduledPipeline / ExecutablePipeline | Pipeline Representation Spec v0.1 후속 단계 |
| DockGuard K8s Admission Webhook | 이번 스프린트 비범위, 로드맵 항목 |
| Harbor 레지스트리 연동 | 로컬 레지스트리로 대체 |
| 정책 자동 갱신 (폴링) | STRETCH 이후 로드맵 |
| 파이프라인 전체 실행 | 단일 툴 smoke run까지만 |

### 1.3 핵심 용어 정의

| 용어 | 정의 |
|------|------|
| **Tool** | 파이프라인에서 사용할 수 있는 단일 실행 단위. 컨테이너 이미지 + 쉘 스크립트 + named I/O 선언의 묶음 |
| **ToolDefinition** | 관리자가 NodeKit에서 작성 중인 Tool의 메타데이터 + 실행 스펙 초안. NodeKit 내부에서 사용하는 authoring 모델이다. BuildRequest로 변환되어 NodeForge로 전송된다. |
| **BuildRequest** | NodeKit이 L1 검증을 통과한 후 NodeForge로 전송하는 빌드/등록 요청 단위. ToolDefinition + Dockerfile + build context를 포함한다. |
| **RegisteredToolDefinition** | L2~L4 전체 성공 후 확정된 최종 등록 객체. 이미지 digest, CAS 해시, 검증 결과가 포함된다. 파일 기반 CAS 구조로 저장된다. |
| **AdminToolList** | NodeKit이 관리자에게 보여주는 등록 완료 Tool 목록. RegisteredToolDefinition 기반이며, 관리자 내부 도구다. |
| **PipelineToolPalette** | 최종 사용자 파이프라인 앱(DagEdit 등)이 노출하는 Tool 팔레트. 등록된 Tool 메타데이터를 소비하지만, NodeKit 내부 목록과 다른 계층이다. |
| **Node** | 파이프라인 캔버스 위에 배치된 Tool 인스턴스 |
| **Edge (연결선)** | 노드 간 실행 의존성 + 데이터 전달을 표현하는 방향성 있는 연결 |
| **Policy Bundle** | DockGuard .rego 정책을 OPA가 WASM으로 컴파일한 실행 가능한 번들 파일 |
| **L1~L4** | 이번 스프린트의 4단계 검증 레벨 (섹션 4 참조) |
| **CAS** | Content-Addressable Storage. 파일 내용의 해시를 식별자로 사용하는 저장 방식 |
| **smoke run** | 최소 입력으로 컨테이너가 실제로 한 번 실행되는지 확인하는 최소 검증 |
| **dry-run** | K8s API 서버에 manifest를 제출하되 실제 리소스를 생성하지 않는 schema/admission 검증 |

### 1.4 사용자 계층 구분

이 문서 전체에서 사용자를 두 계층으로 구분한다. 절대로 혼용하지 않는다.

| 계층 | 설명 | 사용하는 앱 |
|------|------|-------------|
| **관리자** | 시스템 구조를 이해하는 내부 인원. Tool을 제작하고 등록한다. | NodeKit |
| **최종 사용자** | 의사 또는 바이오인포매틱스 연구자. 등록된 Tool로 파이프라인을 구성한다. | 파이프라인 앱 (DagEdit 등) |

NodeKit은 관리자 전용 도구다. 최종 사용자는 NodeKit을 사용하지 않는다.

### 1.5 프로젝트 명명

이번 스프린트는 아래 세 프로젝트를 기준으로 설계한다. 이름과 역할 경계는 이 문서 전체에서 고정한다.

| 프로젝트 | 역할 | 이번 스프린트 |
|----------|------|---------------|
| **NodeKit** | 관리자용 Tool authoring 클라이언트 (C# / Avalonia). ToolDefinition 초안 작성, L1 검증, BuildRequest 생성, 등록 결과 조회. | 주 구현 대상 |
| **NodeForge** | Kubernetes-side build/register control plane (Go). BuildRequest 수신, builder Job orchestration, 정책/레지스트리/카탈로그 관리, RegisteredToolDefinition 확정. | 주 구현 대상 |
| **DagEdit** | 별도 트랙의 파이프라인 편집기. 최종 사용자가 등록된 Tool로 파이프라인을 구성한다. NodeKit/NodeForge와 동일한 naming set으로 묶지 않는다. | 본 스프린트 비범위. 별도 구현 중 |

> **NodeKit은 authoring client, NodeForge는 control plane이다.** 이 문서 전체에서 이 두 역할을 혼용하지 않는다. DagEdit은 별도 프로젝트로 유지한다.

---

## 2. 전체 아키텍처 흐름

### 2.1 세 단계 흐름

이 시스템은 크게 세 단계로 구성된다.

```
[단계 1: 관리자 Authoring]
관리자가 NodeKit에서
  - Dockerfile 작성
  - build context 구성
  - 쉘 스크립트 작성
  - 입력/출력(I/O) 선언
  - 메타데이터 입력
  - 재현성 규칙 검증 (L1)
  - DockGuard 정책 검사 (L1)
  → 검증 통과 시 build request 생성

         ↓ gRPC

[단계 2: NodeForge — Build/Register]
NodeForge가 (Kubernetes-side control plane)
  - build request 수신 및 DockGuard 재검증 (L2)
  - builder Job 생성 — 실제 빌드를 별도 builder workload에 위임 (L2)

      builder workload (클러스터 내 독립 실행 단위)
        - Dockerfile / build context 수신
        - OCI 호환 이미지 빌드
        - 내부 레지스트리 push 후 종료

  - builder Job 상태 추적 + 로그 수집 (L2)
  - registry push 성공 확인 + digest 확보 (L2)
  - RegisteredToolDefinition + manifest 생성 (L2)
  - kind dry-run 검증 (L3)
  - smoke run 실행 (L4)
  → 성공 시 RegisteredToolDefinition 확정 + AdminToolList 등록

         ↓

[단계 3: 최종 사용자 파이프라인 구성]  ← DagEdit 등 별도 프로젝트 트랙
최종 사용자가 파이프라인 앱(DagEdit 등)에서
  - RegisteredToolDefinition 메타데이터를 PipelineToolPalette에서 선택
  - 캔버스에 배치하고 연결
  - 파이프라인 실행
  → Pipeline Representation Spec v0.1 흐름으로 연결
```

### 2.2 전체 아키텍처 다이어그램

```
┌─────────────────────────────────────────────────────┐
│                  관리자 영역                          │
│                                                     │
│  ┌──────────────────────────────────────┐           │
│  │          NodeKit (Avalonia / C#)     │           │
│  │                                     │           │
│  │  ToolDefinition Authoring UI        │           │
│  │  ├─ Dockerfile / build context      │           │
│  │  ├─ Script 편집기                   │           │
│  │  ├─ I/O 선언                        │           │
│  │  └─ 메타데이터                       │           │
│  │                                     │           │
│  │  L1 검증                            │           │
│  │  ├─ 정적 검증 (필드/재현성 규칙)    │           │
│  │  └─ WasmPolicyChecker               │           │
│  │      └─ policy.wasm 로드            │           │
│  │         (gRPC로 NodeForge에서 수신)  │           │
│  │                                     │           │
│  │  정책 관리 UI                       │           │
│  │  ├─ 현재 정책 목록/버전             │           │
│  │  └─ 수동 갱신 버튼                 │           │
│  └──────────────────┬───────────────────┘           │
│                     │ gRPC                          │
└─────────────────────┼───────────────────────────────┘
                      │
┌─────────────────────┼───────────────────────────────┐
│                     │    K8s 영역 (kind)             │
│  ┌──────────────────▼───────────────────┐           │
│  │    NodeForge (Go)                    │           │
│  │    [Kubernetes-side control plane]   │           │
│  │                                     │           │
│  │  PolicyService                      │           │
│  │  ├─ .rego 파일 저장                 │           │
│  │  ├─ opa build → policy.wasm         │           │
│  │  └─ GetPolicyBundle() RPC           │           │
│  │                                     │           │
│  │  BuildService                       │           │
│  │  ├─ builder Job 생성 / 상태 추적    │           │
│  │  └─ digest 확인 + 결과 수집 (L2)    │           │
│  │                                     │           │
│  │  ValidateService                    │           │
│  │  ├─ kind dry-run (L3)               │           │
│  │  └─ smoke run (L4)                  │           │
│  │                                     │           │
│  │  ToolRegistryService                │           │
│  │  └─ RegisteredToolDefinition CAS 저장│          │
│  └──────────────────┬───────────────────┘           │
│                     │ Job 생성 / 위임                │
│  ┌──────────────────▼───────────────────┐           │
│  │  builder workload (Job-per-build)    │           │
│  │  ├─ Dockerfile / build context 수신 │           │
│  │  ├─ OCI 호환 이미지 빌드 실행        │           │
│  │  └─ 내부 레지스트리 push 후 종료     │           │
│  └──────────────────────────────────────┘           │
│                                                     │
│  ┌────────────────┐   ┌──────────────────┐         │
│  │  내부 레지스트리│   │  kind 클러스터   │         │
│  └────────────────┘   └──────────────────┘         │
└─────────────────────────────────────────────────────┘
                      │
┌─────────────────────┼───────────────────────────────┐
│                     │    최종 사용자 영역             │
│  ┌──────────────────▼───────────────────┐           │
│  │  DagEdit 등 (별도 프로젝트 트랙)     │           │
│  │  - PipelineToolPalette 소비          │           │
│  │  - 파이프라인 캔버스 구성            │           │
│  │  - Pipeline Representation Spec 연동 │           │
│  └──────────────────────────────────────┘           │
└─────────────────────────────────────────────────────┘
```

### 2.3 재현성 철학

이 시스템 전체를 관통하는 핵심 원칙은 **재현성(Reproducibility)** 이다.

> **same data + same method = same result**

유전체 분석 결과는 환자의 치료 방향에 직접 영향을 미친다. 소프트웨어 버전 하나가 달라져도 변이 분석 결과가 달라질 수 있다. 따라서 이 시스템은 재현성을 깨는 행동을 사전에 차단하도록 설계한다.

NodeKit이 강제하는 재현성 규칙:

| 규칙 | 허용 | 거부 |
|------|------|------|
| 이미지 태그 | `ubuntu:22.04@sha256:abc123...` | `ubuntu:latest`, digest 없는 태그 |
| 패키지 버전 | `bwa=0.7.17=h5bf99c6_8` (버전+빌드 문자열) | `bwa`, `bwa=0.7.17` |
| 베이스 이미지 | 정확한 버전 + SHA256 digest | `latest`, 버전 없음 |

개발 편의를 위해 이 규칙을 우회하는 플래그는 만들지 않는다. 개발 중 테스트가 필요하면 사전 검증된 fixture/sample profile을 사용한다.

---

## 3. 구성요소

### 3.1 NodeKit (C# / Avalonia)

**역할**: 관리자가 Tool을 정의하고 등록 요청을 준비하는 authoring 클라이언트. 이미지를 직접 빌드하거나 실행하지 않는다.

**주요 책임**:
- Tool 정의 입력 UI (Dockerfile, script, I/O, 메타데이터)
- L1 정적 검증
- DockGuard 정책 검사 (WasmPolicyChecker)
- BuildRequest 생성 및 NodeForge로 gRPC 전송
- 정책 관리 UI (정책 목록 조회, 버전 확인, 수동 갱신)
- 등록 결과 수신 및 AdminToolList 갱신

**핵심 인터페이스**:

```csharp
// 정책 번들을 어디서 가져오는지 추상화
interface IPolicyBundleProvider
{
    Task<PolicyBundle> GetLatestBundleAsync();
}

// 스프린트 초기: 로컬 파일에서 로드
class LocalFilePolicyBundleProvider : IPolicyBundleProvider { }

// NodeForge PolicyService 완성 후 교체
class GrpcPolicyBundleProvider : IPolicyBundleProvider { }

// 정책 검사 추상화
interface IPolicyChecker
{
    PolicyResult Check(string dockerfileContent);
}

// WasmPolicyChecker: bundle 로드 후 Wasmtime으로 실행
class WasmPolicyChecker : IPolicyChecker { }
```

### 3.2 NodeForge (Go)

**역할**: NodeKit에서 받은 BuildRequest를 처리하는 Kubernetes-side control plane.
NodeForge는 직접 이미지를 빌드하지 않는다. 실제 이미지 빌드는 클러스터 내부의 별도 builder workload에 위임한다.
NodeForge는 builder Job의 lifecycle 전체를 책임지며, 그 결과를 후속 검증(L3, L4)과 등록 흐름에 연결한다.

**주요 책임**:
- BuildRequest 수신 및 DockGuard 서버 측 재검증
- builder Job 생성 → 상태 추적 → 로그 수집 → 성공/실패 판정
- registry push 성공 확인 및 이미지 digest 확보
- RegisteredToolDefinition 생성 (CAS 파일)
- manifest / YAML 생성
- kind dry-run 검증 (L3)
- smoke run 실행 (L4)
- Tool 등록 완료 처리 (RegisteredToolDefinition 확정)
- PolicyService를 통해 NodeKit에 정책 bundle 제공

> **설계 기준**: NodeForge Pod는 Docker socket을 mount하지 않으며, privileged 권한을 요구하지 않는다. 이미지 빌드는 반드시 별도 builder workload가 수행한다.

**gRPC 서비스 구성**:

```protobuf
service PolicyService {
  rpc GetPolicyBundle(GetPolicyBundleRequest) returns (PolicyBundle);
  rpc ListPolicies(ListPoliciesRequest) returns (ListPoliciesResponse);
}

service BuildService {
  rpc BuildAndRegister(BuildRequest) returns (stream BuildEvent);
}

service ValidateService {
  rpc DryRun(DryRunRequest) returns (DryRunResult);
  rpc SmokeRun(SmokeRunRequest) returns (SmokeRunResult);
}

service ToolRegistryService {
  rpc RegisterTool(RegisterToolRequest) returns (RegisterToolResponse);
  rpc GetTool(GetToolRequest) returns (RegisteredToolDefinition);  // 등록 완료 객체 반환
  rpc ListTools(ListToolsRequest) returns (ListToolsResponse);     // 목록도 RegisteredToolDefinition 기반
}
```

**PolicyService 동작**:
```
관리자가 .rego 파일 추가/수정
    ↓
PolicyService: opa build 실행 → policy.wasm 생성
    ↓
NodeKit의 GetPolicyBundle() 호출 시 최신 bundle 반환
```

**NodeForge 내부 구성 (예시)**:
- `cmd/controlplane` — gRPC 서버 진입점
- `pkg/policy` — DockGuard 정책 관리
- `pkg/registry` — 내부 레지스트리 연동
- `pkg/catalog` — RegisteredToolDefinition CAS 저장

**BuildService 동작 흐름**:
```
NodeKit에서 BuildRequest 수신 (→ NodeForge)
    ↓
DockGuard 정책 재검증
    ↓
builder Job 생성 (Kubernetes Job API)
    ↓
Job 상태 watch + 로그 스트리밍
    ↓
builder workload: 이미지 빌드 → 내부 레지스트리 push → 종료
    ↓
registry push 성공 확인 + digest 확보
    ↓
RegisteredToolDefinition / manifest 생성
    ↓
L3 (dry-run) → L4 (smoke run) → RegisteredToolDefinition 확정
```

**ToolDefinition 저장 (CAS)**:
```
ToolDefinition 내용 → SHA256 해시 계산
    ↓
{hash}.tooldefinition 파일로 저장
    ↓
향후 Harbor / 정책 서버로 확장 가능한 구조
```

### 3.3 builder workload

**역할**: 실제 이미지 빌드를 수행하는 클러스터 내부의 독립 실행 단위.
NodeForge의 위임을 받아 Dockerfile과 build context로 OCI 호환 이미지를 빌드하고, 내부 레지스트리에 push한 뒤 종료한다.

**초기 구현 전략 — Job-per-build**:
- 빌드 요청 1건당 builder Job 1개를 생성한다.
- builder Job은 빌드 완료 후 자동 종료된다.
- Job-per-build를 선택한 이유:
  - 빌드 간 격리가 자연스럽다.
  - K8s가 Job lifecycle(재시도, 정리)을 대신 관리해준다.
  - NodeForge의 orchestration 코드가 단순해진다.
  - 스프린트에서 검증하는 것은 "성능"이 아니라 "전체 흐름의 신뢰성"이다.

**주요 책임**:
- Dockerfile / build context 수신
- OCI 호환 이미지 빌드 실행
- 내부 레지스트리 push
- 빌드 완료 후 종료 (상태 코드로 성공/실패 전달)

**빌더 구현 기술**:
초기 후보로 BuildKit 계열이 자연스럽다. 단, 이번 스프린트에서 특정 구현체에 완전히 고정하지 않는다. 설계 단위는 "OCI-compatible builder"이며, 빌더 교체는 builder Job spec 수정만으로 가능하도록 격리한다.

> 지속형 빌더 서비스(Deployment / StatefulSet 기반)로의 전환은 스프린트 이후 빌드 캐시 효율이 실제 문제가 됐을 때 검토한다.

**명시적 제약**:
- builder workload는 클러스터 내부에서 분리된 권한으로 실행된다.
- builder workload의 최종 산출물은 내부 레지스트리에 push된 이미지 + digest다.
  builder Pod 내부에 산출물이 남아있는 상태는 완료 상태로 취급하지 않는다.
- NodeForge와 builder workload 사이의 결과 확인 기준은
  내부 레지스트리 push 성공과 digest 확보다. 그 외의 채널로 결과를 전달하지 않는다.

### 3.4 DockGuard

**역할**: Dockerfile이 재현성 및 빌드 정책을 지키는지 검사하는 OPA/Rego 기반 정책 도구.

**현재 정책 규칙**:

| 규칙 ID | 내용 |
|---------|------|
| DFM001 | FROM 명령은 정확히 한 번만 사용 |
| DFM002 | FROM에 `AS builder` 별칭 필수 |
| DFM003 | `AS final` 스테이지 금지 (시스템이 자동 생성) |
| DFM004 | `COPY --from=builder` 금지 (시스템이 자동 처리) |

**정책 관리 흐름**:
```
NodeForge PolicyService가 .rego 파일 관리
    ↓
opa build → policy.wasm 번들 생성
    ↓
NodeKit이 gRPC로 bundle 수신
    ↓
WasmPolicyChecker가 bundle 로드 후 Dockerfile 검사
```

이번 스프린트에서 DockGuard는 **Dockerfile authoring/build 단계의 정책 게이트** 역할만 한다. K8s Admission Webhook으로의 확장은 로드맵 항목이다.

### 3.5 kind + kubeconfig

**kind**: 로컬에 설치된 Docker 기반 K8s 클러스터. 이미 설치됨.

**kubeconfig 기반 연결**:
```
kind 클러스터 생성
    ↓
~/.kube/config 자동 생성
    ↓
NodeForge가 kubeconfig 읽어 K8s API 직접 호출
    ↓
dry-run, smoke run 실행
```

Ingress나 서비스 메시 없이 로컬 kubeconfig로 직접 접근한다. Ingress 설계는 이후 운영 환경 전환 시 별도 진행한다.

### 3.6 내부 레지스트리

kind 클러스터 내부 또는 로컬에서 실행하는 Docker 레지스트리. 빌드된 이미지를 저장하고 kind 클러스터에서 pull할 수 있도록 한다.

```bash
# 로컬 레지스트리 실행 예시
docker run -d -p 5001:5000 --name local-registry registry:2
```

향후 Harbor로 교체 가능하도록 레지스트리 주소를 설정 파일로 분리한다.

### 3.7 AdminToolList (NodeKit 내부)

등록 완료된 RegisteredToolDefinition 목록을 관리하는 NodeKit 내부 구성요소. 관리자가 등록 결과를 확인하는 도구다. 스프린트에서는 인메모리 + 로컬 파일로 구현한다.

> **PipelineToolPalette와의 구분**: AdminToolList는 관리자 전용 NodeKit 내부 목록이다. 최종 사용자 파이프라인 앱(DagEdit 등)이 Tool을 소비하는 PipelineToolPalette와 같은 계층이 아니다. 두 목록은 같은 RegisteredToolDefinition 데이터를 기반으로 하지만, 사용 주체와 앱이 다르다.

---

## 4. 검증 단계 (L1~L4)

### 전체 요약

| 단계 | 이름 | 수행 주체 | 핵심 목적 |
|------|------|-----------|-----------|
| L1 | 관리자 입력 / 정적 검증 | NodeKit | 잘못된 입력을 빠르게 차단 |
| L2 | 빌드 / 등록 준비 | NodeForge | 이미지 빌드 및 레지스트리 등록 성공 여부 |
| L3 | K8s 제출 가능성 검증 | NodeForge | manifest가 K8s API 수준에서 수용 가능한지 |
| L4 | 최소 smoke run | NodeForge | 실제로 한 번 실행 가능한지 |

---

### L1. 관리자 입력 / 정적 검증

**수행 주체**: NodeKit

**목적**: build/register 경로로 넘기기 전에 즉시 잡을 수 있는 오류를 차단한다.

**주요 입력**:
- Dockerfile
- build context (파일 목록)
- Tool 이름, 버전, 식별자
- 베이스 이미지 정보 (버전 + digest 필수)
- command / entrypoint 정의
- 쉘 스크립트
- I/O 슬롯 정의 (이름, 방향, 타입)
- 메타데이터 (설명, 카테고리 등)

**확인하는 것**:
- 필수 필드 누락 여부
- `latest` 태그 사용 금지
- 이미지에 SHA256 digest 포함 여부
- 패키지 버전 + 빌드 문자열 명시 여부 — 검사 대상은 Dockerfile 내 `micromamba install` / `conda install` 구문, 또는 build context에 포함된 `environment.yml` 같은 environment spec 파일이다. 두 경로 모두 버전+빌드 문자열 없이 패키지 이름만 있는 경우 차단한다.
- Tool name / version 형식 유효성
- I/O 슬롯 기본 형식 유효성
- Dockerfile 존재 여부 및 기본 구조
- build context 경로 및 파일 존재 여부
- 입력 간 상호 모순
- **DockGuard 정책 검사** (WasmPolicyChecker)

**확인하지 않는 것**:
- 실제 이미지 빌드 성공 여부
- 컨테이너 런타임 진입 가능성
- K8s 제출 가능성
- 실제 실행 결과

**실패 피드백 구분**:

| 유형 | 설명 | 동작 |
|------|------|------|
| 차단 오류 | 다음 단계 진행 불가 (필수 필드 누락, 정책 위반 등) | "다음" 버튼 비활성화, 오류 메시지 표시 |
| 경고 | 권장 수정 (선택적) | 경고 아이콘, 진행은 가능 |

**다음 단계 진입 조건**:
- 차단급 오류 없음
- DockGuard 정책 전체 통과
- build request 생성 가능한 최소 입력 갖춤

---

### L2. 빌드 / 등록 준비

**수행 주체**: NodeForge (orchestration) + builder workload (빌드 실행)

**목적**: NodeForge가 builder workload를 통해 이미지를 빌드하고, 내부 레지스트리에 등록하여 digest를 확보한다.

**주요 동작**:
1. NodeKit에서 build request 수신 (gRPC)
2. DockGuard 정책 검사 (서버 측 재검증)
3. builder Job 생성 — builder workload에 실제 빌드 위임
4. builder workload가 이미지 빌드 후 내부 레지스트리 push → 종료
5. builder Job 상태 추적 및 로그 수집
6. registry push 성공 확인 + 이미지 digest 확보
7. RegisteredToolDefinition 생성 (CAS 파일)
8. manifest / YAML 생성

> **orchestration 루프 주의**: 이 단계의 핵심 난점은 빌더 기술 선택이 아니다.
> Job 생성 → 상태 watch → 로그 수집 → push 결과 확인 → digest 확보 → 후속 단계 연결
> 루프를 신뢰성 있게 구현하는 것이 L2의 가장 중요한 설계 과제다.

**확인하는 것**:
- Dockerfile이 DockGuard 정책을 통과하는지
- builder Job이 정상적으로 생성되고 실행되는지
- builder workload가 이미지 빌드를 실제로 완료하는지
- 내부 레지스트리 push 성공 여부
- 이미지 digest 확보 가능 여부
- RegisteredToolDefinition 생성에 필요한 값이 모두 채워지는지
- manifest / YAML 생성 일관성

**확인하지 않는 것**:
- K8s API 서버 제출 가능성 (L3에서 확인)
- 실제 Job / Pod 실행 성공 여부 (L4에서 확인)
- 빌드 캐시 효율 / 빌드 성능

**실패 피드백 구분**:

| 유형 | 예시 |
|------|------|
| DockGuard 정책 위반 | DFM002: AS builder 별칭 없음 |
| builder Job 생성 실패 | K8s API 오류, 권한 부족 |
| 빌드 실패 | Dockerfile 문법 오류, 패키지 설치 실패 |
| 레지스트리 push 실패 | 내부 레지스트리 미실행, 인증 오류 |
| digest 확보 실패 | push 결과에서 digest 추출 불가 |
| RegisteredToolDefinition 생성 실패 | 필수 값 누락 |

**다음 단계 진입 조건**:
- DockGuard 정책 통과
- builder Job 완료 성공
- 내부 레지스트리 등록 성공
- digest 확보 성공
- RegisteredToolDefinition / manifest 생성 성공

---

### L3. Kubernetes 제출 가능성 검증

**수행 주체**: NodeForge

**목적**: 생성된 manifest가 Kubernetes API 서버 수준에서 수용 가능한지 확인한다.

> **중요**: dry-run은 manifest/schema/admission 수준의 검증이다. 이미지가 실제로 pull 가능한지, 클러스터에 자원이 충분한지는 확인하지 않는다.

**주요 동작**:
- kubeconfig로 kind 클러스터 연결
- `--dry-run=server` 방식으로 Job manifest 제출
- API 서버 응답 확인

**확인하는 것**:
- manifest / schema 유효성
- API 서버가 해당 리소스 수용 가능 여부
- 필수 필드 누락 여부 (API 서버 기준)
- 기본값 적용 수준에서 문제 없는지

**확인하지 않는 것** (자주 혼동되는 항목):
- 실제 노드 자원 여유 (CPU, 메모리)
- 이미지 pull 가능 여부
- 컨테이너 시작 성공
- 애플리케이션 동작 성공
- 실제 스케줄링 성공

**실패 피드백 구분**:

| 유형 | 예시 |
|------|------|
| manifest 구조 오류 | 필수 필드 누락, 잘못된 필드 타입 |
| API 거부 | 지원하지 않는 리소스 버전 |
| dry-run 거부 | admission 수준 정책 위반 |

**다음 단계 진입 조건**:
- kind 대상 server-side dry-run 성공
- 제출 가능성 수준에서 차단 오류 없음

---

### L4. 최소 happy-path smoke run

**수행 주체**: NodeForge

**목적**: 빌드된 이미지가 실제로 한 번 이상 실행 가능한지 확인한다. 운영 수준의 종합 검증이 아니라, "등록 가능한 최소 실행 가능성"을 보는 단계다.

**주요 동작**:
- kind 환경에서 최소 입력으로 Job 1회 실행
- 컨테이너 시작 여부 확인
- entrypoint / command 진입 여부 확인
- 종료 코드 확인
- 최소 기대 출력 또는 생성 산출물 존재 여부 확인

**확인하는 것**:
- 빌드된 이미지가 실제로 pull/run 가능한지
- command / entrypoint가 실제로 시작되는지
- 최소 happy-path 입력에서 오류 없이 종료하는지
- 종료 코드 또는 최소 산출물이 기대와 일치하는지

**확인하지 않는 것**:
- 대규모 입력 처리 성능
- 다양한 입력 조합 전체 검증
- 다중 샘플 / 고부하 상황
- 운영 환경 네트워크/레지스트리 조건
- 장기 안정성

**실패 피드백 구분**:

| 유형 | 예시 |
|------|------|
| pull 실패 | 이미지가 레지스트리에 없음, 인증 오류 |
| container start 실패 | entrypoint 경로 오류, 권한 문제 |
| command 실패 | 실행 파일 없음, runtime dependency 누락 |
| 최소 출력 미생성 | 기대한 파일이 생성되지 않음 |

**등록 조건**:
- smoke run 성공
- 최소 실행 가능성 확인
- 등록 가능한 최소 기준 충족

---

## 5. 테스트

### 5.1 단위 테스트 (NodeKit)

| 테스트 | 내용 | MUST / STRETCH |
|--------|------|----------------|
| L1 필수 필드 검증 | 각 필드별 누락/형식 오류 케이스 | MUST |
| latest 태그 차단 | `ubuntu:latest` 등 입력 시 차단 확인 | MUST |
| digest 형식 검증 | `@sha256:` 없으면 차단 | MUST |
| 패키지 버전 고정 검증 | Dockerfile install 구문 또는 environment spec 파일에 버전+빌드 문자열 없으면 차단 | MUST |
| WasmPolicyChecker | DFM001~DFM004 각 규칙 통과/실패 케이스 | MUST |
| IPolicyBundleProvider 교체 | LocalFile → Grpc 교체 후 동일 동작 확인 | MUST |
| ToolDefinition 직렬화 | 생성 및 파일 저장/로드 | MUST |

### 5.2 단위 테스트 (NodeForge)

> **테스트 우선순위 기준**: 이 단계 테스트의 핵심은 빌더 구현체 자체가 아니라,
> Job 생성 → 상태 추적 → 로그 수집 → registry push 성공 확인 → digest 확보 → 후속 단계 연결
> orchestration 루프의 신뢰성이다.
> **아래 표에서 [루프 핵심] 표시 항목을 먼저 통과해야 나머지 항목으로 진행한다.**

| 테스트 | 내용 | MUST / STRETCH |
|--------|------|----------------|
| PolicyService | .rego 파일 로드, opa build 실행, bundle 반환 | MUST |
| **[루프 핵심]** BuildService — Job 생성 | builder Job이 정상 생성되는지 | MUST |
| **[루프 핵심]** BuildService — Job 상태 추적 | Pending → Running → Succeeded / Failed 전환 감지 | MUST |
| **[루프 핵심]** BuildService — 로그 수집 | Job 실행 중/완료 후 로그 수집 가능 여부 | MUST |
| **[루프 핵심]** BuildService — push 성공 판정 | registry push 성공 시 digest 확보 확인 | MUST |
| **[루프 핵심]** BuildService — digest 확보 실패 처리 | push 후 digest 추출 불가 시 오류 반환 | MUST |
| **[루프 핵심]** BuildService — Job 실패 처리 | builder Job 실패 시 오류 로그와 함께 실패 반환 | MUST |
| RegisteredToolDefinition CAS | 동일 내용 → 동일 hash, 다른 내용 → 다른 hash | MUST |
| manifest 생성 | 필수 필드 포함 여부 | MUST |
| dry-run 결과 파싱 | 성공/실패 응답 파싱 | MUST |

### 5.3 통합 테스트

| 테스트 | 내용 | MUST / STRETCH |
|--------|------|----------------|
| L1 → L2 연동 | NodeKit build request → NodeForge 수신 확인 | MUST |
| **[Phase 2 핵심 검증]** orchestration loop happy-path | Job 생성 → Running → push → Succeeded → digest 확보까지 1회 완전 성공 | MUST |
| builder Job lifecycle 추적 | Job 생성 → 실행 → 완료까지 상태 전환 정상 감지 | MUST |
| builder Job 실패 시 오류 전파 | builder Job 실패 → NodeForge → NodeKit UI 오류 표시 | MUST |
| digest 확보 후 L3 자동 연결 | digest 확보 성공 시 dry-run 자동 진입 | MUST |
| L2 → L3 연동 | 빌드 성공 후 dry-run 자동 진행 | MUST |
| L3 → L4 연동 | dry-run 성공 후 smoke run 자동 진행 | MUST |
| 전체 흐름 | L1 입력 → 툴 등록까지 end-to-end | MUST |
| 정책 업데이트 반영 | .rego 수정 → bundle 갱신 → NodeKit 반영 | MUST |
| 팔레트 등록 후 캔버스 배치 | 드래그&드롭 노드 생성 | STRETCH |
| 노드 연결선 생성 | 두 노드 연결 후 선 표시 | STRETCH |
| 연결선 라벨 표시 | I/O 수 숫자 표시 | STRETCH |

### 5.4 실패 케이스 테스트

| 실패 케이스 | 기대 동작 |
|------------|-----------|
| latest 태그 입력 | L1에서 즉시 차단, 오류 메시지 표시 |
| DockGuard 정책 위반 Dockerfile | L1에서 위반 규칙 번호와 이유 표시 |
| builder Job 생성 실패 | L2 실패, 오류 원인(K8s API / 권한) NodeKit에 표시 |
| builder Job Pending 지속 | 타임아웃 후 실패 처리, 오류 메시지 표시 |
| builder workload 빌드 실패 | L2 실패, builder Job 로그 NodeKit에 전달 |
| registry push 성공 후 digest 추출 불가 | L2 실패, digest 확보 불가 오류 명시 |
| 레지스트리 미실행 | L2 push 실패, 원인 명시 |
| manifest 스키마 오류 | L3 dry-run 거부, 오류 필드 표시 |
| 이미지 pull 실패 | L4 실패, pull 오류 로그 표시 |
| smoke run command 실패 | L4 실패, 종료 코드와 로그 표시 |

### 5.5 MUST / STRETCH 완료 판정 기준

**MUST — 아래 항목이 모두 동작해야 스프린트 MUST 달성**

| # | 항목 | 확인 방법 |
|---|------|-----------|
| 1 | NodeKit에서 Tool 정보 (Dockerfile, script, I/O) 입력 가능 | 직접 입력 후 저장 확인 |
| 2 | latest 태그, digest 미포함, 버전 미고정 입력이 L1에서 차단됨 | 각 케이스 직접 입력 테스트 |
| 3 | DockGuard 정책 위반 시 어떤 규칙인지 UI에 표시됨 | DFM001~DFM004 각각 테스트 |
| 4 | NodeKit → NodeForge gRPC build request 전송 성공 | 로그 또는 UI 상태 확인 |
| 5 | NodeForge가 builder Job을 생성하고, builder workload가 이미지를 빌드하여 내부 레지스트리에 등록하며, digest가 확보됨 | 레지스트리에서 이미지 확인, NodeForge 로그에서 digest 확인 |
| 6 | kind dry-run이 실행되고 성공/실패 결과가 NodeKit에 표시됨 | NodeKit UI에서 결과 확인 |
| 7 | smoke run이 실행되고 결과가 NodeKit에 표시됨 | NodeKit UI에서 결과 확인 |
| 8 | 전체 흐름 성공 시 RegisteredToolDefinition이 확정되고 AdminToolList에 등록됨 | NodeKit AdminToolList에서 Tool 확인 |
| 9 | NodeKit에서 정책 목록 조회 및 수동 갱신 가능 | 정책 관리 UI에서 확인 |
| 10 | 발견된 이슈와 DagEdit 반영 사항이 문서화됨 | 이슈 문서 파일 존재 확인 |

**STRETCH — 가능하면 달성, 못 해도 스프린트 실패 아님**

| # | 항목 |
|---|------|
| S-1 | 등록된 Tool을 캔버스에 드래그&드롭으로 배치 가능 |
| S-2 | 두 노드를 연결하면 방향성 있는 연결선이 표시됨 |
| S-3 | 연결선 위에 I/O 수 숫자 라벨 표시 |
| S-4 | 연결선 클릭 시 매핑 정보 상세창 표시 |

---

## 6. 스프린트 일정

### 전체 일정 개요

**전체 기간**: 2026-04-07(월) ~ 2026-05-02(토) — **4주**

```
Week 1  04-07 ~ 04-11   환경 준비 + 프로젝트 구조 + L1 정적 검증
Week 2  04-14 ~ 04-18   WasmPolicyChecker + gRPC 설계 + NodeForge 기초
Week 3  04-21 ~ 04-25   NodeForge L2~L4 구현 + NodeKit-NodeForge 연동
Week 4  04-28 ~ 05-02   연동 통합 테스트 + STRETCH + 마무리
```

---

### Phase 0 — 환경 준비 + 프로젝트 구조
**기간**: 2026-04-07(월) ~ 2026-04-08(화) / 2일

**달성 목표**: 두 앱이 각자 빌드되고, gRPC로 hello-world 수준의 통신이 동작한다.

| 날짜 | 작업 | 완료 기준 |
|------|------|-----------|
| 04-07 (월) | kind 클러스터 상태 확인 및 로컬 레지스트리 실행 | `kubectl get nodes` 정상, registry:2 컨테이너 실행 |
| 04-07 (월) | NodeKit 프로젝트 구조 재설계 | 새 폴더/클래스 구조로 솔루션 재구성 |
| 04-08 (화) | NodeForge Go 프로젝트 초기화 | `go build` 성공, 기본 gRPC 서버 실행 |
| 04-08 (화) | proto 파일 정의 + gRPC 코드 생성 | NodeKit ↔ NodeForge ping/pong RPC 동작 |

**리스크 및 예상 이슈**:

| 리스크 | 가능성 | 영향 | 대응 |
|--------|--------|------|------|
| kind 로컬 레지스트리 연동 설정 복잡 | 중 | L2 이미지 push 불가 | kind 공식 문서의 local registry 설정 참조 |
| proto 파일에서 C# / Go 코드 생성 환경 구성 | 중 | gRPC 연동 지연 | protoc + grpc 플러그인 설치 스크립트 먼저 작성 |
| .NET 10 + Avalonia 조합 빌드 이슈 | 낮 | 개발 환경 세팅 지연 | 기존 NodeKit 코드 컴파일 확인 후 진행 |

---

### Phase 1 — L1 정적 검증 + WasmPolicyChecker
**기간**: 2026-04-09(수) ~ 2026-04-16(수) / 6일

**달성 목표**: NodeKit UI에서 Tool 정의를 입력하고 L1 검증(정적 + DockGuard 정책)이 통과/차단 동작한다.

| 날짜 | 작업 | 완료 기준 |
|------|------|-----------|
| 04-09 (수) | ToolDefinition 모델 설계 확정 | 모든 필드 정의, 직렬화 테스트 통과 |
| 04-09 (수) | Tool authoring UI 레이아웃 | 입력 폼 화면 틀 완성 |
| 04-10 (목) | 이미지 URI 입력 + latest/digest 검증 | latest 입력 시 차단, digest 없으면 차단 |
| 04-10 (목) | 패키지 버전 고정 검증 | 버전+빌드 문자열 없으면 저장 불가 |
| 04-11 (금) | 쉘 스크립트 편집기 UI | 여러 줄 입력, 저장 가능 |
| 04-14 (월) | named I/O 선언 UI | 입력/출력 이름 추가/삭제 가능 |
| 04-14 (월) | IPolicyBundleProvider + IPolicyChecker 인터페이스 확정 | 인터페이스 코드 작성 완료 |
| 04-15 (화) | LocalFilePolicyBundleProvider 구현 | 로컬 .wasm 파일 로드 성공 |
| 04-15 (화) | WasmPolicyChecker 구현 (Wasmtime .NET SDK) | DFM001~DFM004 각 규칙 테스트 통과 |
| 04-16 (수) | L1 검증 UI 통합 | 차단 오류/경고 UI 표시 확인 |

**리스크 및 예상 이슈**:

| 리스크 | 가능성 | 영향 | 대응 |
|--------|--------|------|------|
| Wasmtime .NET SDK의 .NET 10 호환성 | 중 | WasmPolicyChecker 구현 불가 | 사전에 NuGet 패키지 설치 및 간단한 WASM 로드 테스트 먼저 실행 |
| DockGuard .rego → .wasm 빌드 환경 구성 | 중 | policy.wasm 파일 생성 불가 | OPA CLI 설치, `opa build` 실행 확인 먼저 |
| Avalonia에서 동적 행 추가/삭제 UI | 중 | I/O 선언 UI 구현 지연 | ItemsControl + ObservableCollection 패턴 사용 |
| Wasmtime에서 OPA WASM API 호출 방식 파악 | 높 | 구현 시간 초과 | OPA WASM ABI 문서 및 예제 코드 먼저 분석 |

---

### Phase 2 — NodeForge L2~L3 + gRPC PolicyService
**기간**: 2026-04-17(목) ~ 2026-04-23(수) / 5일

**달성 목표**: Phase 2의 핵심 목표는 하나다. builder Job orchestration loop를 happy-path 기준으로 1회 완전히 닫는 것.

```
Job 생성 → Running → 이미지 빌드 → 내부 레지스트리 push → Succeeded → digest 확보 → 후속 단계 연결
```

이 루프가 실제로 닫히면 나머지 세부 구현(RegisteredToolDefinition 생성, manifest 생성, dry-run, NodeKit UI)을 이어간다. 루프가 닫히기 전에 세부 구현을 낙관적으로 확장하지 않는다.

> **⚠ Phase 2 선행 게이트 — 통과 전 세부 구현 금지**
>
> 아래 조건이 모두 충족될 때까지 RegisteredToolDefinition 생성 / manifest 생성 / dry-run 구현 / NodeKit UI 작업을 시작하지 않는다.
>
> - builder Job 1개 생성 성공
> - Job이 Running 상태로 진입하고 로그 수집 가능
> - 내부 레지스트리 push 성공 확인
> - digest 확보 성공
>
> 게이트 통과 기준: 위 4가지를 포함하는 happy-path 1회 완전 성공.

**작업 분류**:

| 분류 | 작업 |
|------|------|
| **[게이트 — 최우선]** | builder Job spec 확정 + happy-path 1회 성공 |
| **[핵심]** | BuildService orchestration 구현, builder workload 빌드/push/로그 |
| **[게이트 통과 후]** | RegisteredToolDefinition 생성, manifest 생성, dry-run, PolicyService |
| **[후순위]** | NodeKit UI 반영, L2/L3 결과 표시, 통합 테스트 |

**세부 일정**:

| 날짜 | 분류 | 작업 | 완료 기준 |
|------|------|------|-----------|
| 04-17 (목) | **[게이트]** | builder Job spec 확정 + happy-path 1회 성공 | Job 생성 → push → digest 확보까지 1회 성공. **미달 시 04-18 이후 작업 전면 보류** |
| 04-17 (목) | 병렬 | PolicyService gRPC 구현 | .rego 로드, opa build, GetPolicyBundle() RPC 동작 |
| 04-18 (금) | **[핵심]** | BuildService — builder Job 생성 + 상태 추적 구현 | Job 생성 → Succeeded / Failed 전환 감지 동작 |
| 04-18 (금) | 병렬 | NodeKit GrpcPolicyBundleProvider 구현 | gRPC로 bundle 수신, WasmPolicyChecker 재로드 성공 |
| 04-21 (월) | **[핵심]** | builder workload — 이미지 빌드 + push + 로그 수집 | 빌드 이미지 push, digest 확보, 로그 수집 확인 |
| 04-21 (월) | 게이트 통과 후 | RegisteredToolDefinition 생성 + CAS 파일 저장 | hash 기반 파일명으로 저장, 로드 확인 |
| 04-22 (화) | 게이트 통과 후 | manifest / YAML 생성 로직 | K8s Job spec YAML 정상 생성 |
| 04-22 (화) | 게이트 통과 후 | ValidateService — kind dry-run 구현 | kubeconfig로 kind 연결, dry-run 성공/실패 반환 |
| 04-23 (수) | **[후순위]** | NodeKit — L2/L3 결과 UI 표시 + Phase 2 통합 테스트 | 빌드/dry-run 진행 상태 NodeKit 표시, 전체 흐름 확인 |

**리스크 및 예상 이슈**:

| 리스크 | 가능성 | 영향 | 실패 시 대응 |
|--------|--------|------|--------------|
| 게이트(happy-path) 미달 — 04-17 내 미완료 | 높 | 이후 모든 세부 작업 블로킹 | 04-18 작업 전면 보류. 게이트 완료를 04-18~04-21로 연장하고 나머지를 Phase 3 초반으로 이동 |
| builder Job 결과 회수 방식 설계 미흡 | 높 | digest 확보 불가 → orchestration loop 미완성 | 게이트 단계에서 registry API 직접 조회 방식 확정. 미해결 시 게이트 통과 불가로 처리 |
| kind ↔ 내부 레지스트리 pull 설정 실패 | 높 | L4 smoke run에서 ImagePullBackOff | kind config containerdConfigPatches 설정. 미해결 시 L4를 Phase 3으로 이동 |
| opa build 실행 환경 (OPA CLI 경로) | 중 | PolicyService 동작 불가 | OPA CLI 경로를 설정 파일로 분리. PolicyService는 게이트와 독립이므로 별도 처리 가능 |
| YAML 생성 시 `${}` 변수 처리 | 중 | YAML 파싱 오류 | 스크립트 내용을 ConfigMap 또는 base64 인코딩으로 처리 |
| async gRPC streaming UI 처리 | 높 | NodeKit UI 멈춤 | async/await + 진행 표시기. **미완료 시 UI polish를 Phase 3으로 이동해도 핵심 흐름에 영향 없음** |
| Phase 2 작업 과밀 (5일에 9개 항목) | 높 | 게이트 지연 시 연쇄 블로킹 | 후순위 항목(NodeKit UI, 통합 테스트)을 Phase 3으로 이동. 핵심 흐름 닫기에 집중 |

---

### Phase 3 — L4 smoke run + 전체 연동 + 툴 등록
**기간**: 2026-04-24(목) ~ 2026-04-29(화) / 4일

**달성 목표**: smoke run까지 포함한 전체 L1~L4 흐름이 end-to-end로 동작하고, RegisteredToolDefinition이 확정되어 AdminToolList에 등록된다.

| 날짜 | 작업 | 완료 기준 |
|------|------|-----------|
| 04-24 (목) | ValidateService — smoke run 구현 | kind에서 최소 Job 1회 실행, 결과 반환 |
| 04-24 (목) | ToolRegistryService — 등록 완료 처리 | smoke run 성공 시 RegisteredToolDefinition 확정 + CAS 저장 |
| 04-25 (금) | NodeKit — AdminToolList UI 구현 | 등록된 RegisteredToolDefinition 목록 표시 |
| 04-25 (금) | NodeKit — L4 결과 및 등록 완료 UI | smoke run 결과 + 등록 성공 메시지 표시 |
| 04-28 (월) | 전체 흐름 end-to-end 테스트 | L1→L2→L3→L4→등록 전체 동작 확인 |
| 04-28 (월) | 실패 케이스 테스트 | 각 단계별 실패 시 올바른 피드백 표시 확인 |
| 04-29 (화) | 발견된 이슈 문서화 | 이슈 목록 + DagEdit 반영 사항 문서 작성 |

**리스크 및 예상 이슈**:

| 리스크 | 가능성 | 영향 | 대응 |
|--------|--------|------|------|
| smoke run Job이 kind에서 Pending 상태 지속 | 높 | L4 결과 확인 불가 | kind 노드 자원 확인, 테스트용 최소 리소스 요청으로 설정 |
| 이미지 pull 실패 (ImagePullBackOff) | 높 | smoke run 진행 불가 | kind ↔ 로컬 레지스트리 연동 설정 재확인 |
| 전체 연동 타이밍 문제 (gRPC 비동기) | 중 | 진행 상태 UI 불일치 | gRPC streaming으로 실시간 이벤트 전달 |

---

### Phase 4 — STRETCH + 마무리
**기간**: 2026-04-30(수) ~ 2026-05-02(금) / 3일

**달성 목표**: MUST 전체 완료를 전제로, 파이프라인 캔버스 기초를 최소 구현한다.

| 날짜 | 작업 | 완료 기준 |
|------|------|-----------|
| 04-30 (수) | 팔레트 → 캔버스 드래그&드롭 | 드롭 시 노드 생성 |
| 04-30 (수) | 방향성 연결선 + 숫자 라벨 | 두 노드 연결 후 선 + 숫자 표시 |
| 05-01 (목) | 연결선 클릭 → 매핑 상세창 | 상세창 표시 (최소 구현) |
| 05-01 (목) | 연결 규칙 검증 (MVP 임시: 출력 수 == 입력 수) | 불일치 시 오류 표시 |
| 05-02 (금) | 스프린트 회고 + DagEdit 반영 사항 정리 | 회고 문서 작성 완료 |

> **연결 규칙 주의**: "부모 출력 수 == 자식 입력 수" 규칙은 이번 스프린트의 임시 단순화다. DagEdit에서는 이름 기반 I/O 매핑 검증으로 교체될 예정이다.

**리스크 및 예상 이슈**:

| 리스크 | 가능성 | 영향 | 대응 |
|--------|--------|------|------|
| Avalonia 드래그&드롭 컨트롤 간 구현 복잡 | 높 | 구현 시간 초과 | DragDrop API 사전 검토, 안 되면 버튼 방식 대체 |
| Phase 3이 지연될 경우 Phase 4 시간 부족 | 높 | STRETCH 전부 미완성 | Phase 4는 STRETCH이므로 스프린트 실패 아님 |

---

### 전체 일정 요약

```
04-07 ━━┓ Phase 0: 환경 준비 + 프로젝트 구조   (2일)
04-08 ━━┛

04-09 ━━┓
04-10    │
04-11    │ Phase 1: L1 검증 + WasmPolicyChecker (6일)
04-14    │
04-15    │
04-16 ━━┛

04-17 ━━┓
04-18    │
04-21    │ Phase 2: NodeForge L2~L3 + PolicyService (5일)
04-22    │
04-23 ━━┛

04-24 ━━┓
04-25    │ Phase 3: L4 smoke run + 전체 연동 (4일)
04-28    │
04-29 ━━┛

04-30 ━━┓
05-01    │ Phase 4: STRETCH + 마무리 (3일)
05-02 ━━┛ ← 스프린트 종료
```

---

## 7. 운영 체크리스트

### 스프린트 착수 전 확인

- [ ] `kubectl get nodes` — kind 클러스터 정상 동작 확인
- [ ] `docker run -d -p 5001:5000 --name local-registry registry:2` — 내부 레지스트리 실행
- [ ] `opa version` — OPA CLI 설치 확인
- [ ] `opa build` 로 DockGuard .rego → .wasm 빌드 테스트
- [ ] Wasmtime .NET 패키지 설치 및 간단한 WASM 로드 테스트
- [ ] proto 파일 기반 C# / Go 코드 생성 환경 확인
- [ ] kubeconfig 경로 확인 (`~/.kube/config`)
- [ ] kind config에 내부 레지스트리 연동 설정 추가
- [ ] `kubectl create job` 간단 테스트 — kind 클러스터에서 Job 생성/실행 가능 여부 확인
- [ ] builder workload가 내부 레지스트리에 push할 수 있는지 인증/연결 확인
- [ ] builder Job용 ServiceAccount / RBAC 권한 구성 확인

### 매일 확인 포인트

- [ ] kind 클러스터 실행 중 (`kubectl get nodes`)
- [ ] 로컬 레지스트리 실행 중 (`docker ps | grep registry`)
- [ ] NodeForge gRPC 서버 실행 중
- [ ] NodeKit 빌드 성공 여부

### 실패 시 디버깅 순서

| 증상 | 먼저 확인할 것 |
|------|----------------|
| L1 DockGuard 항상 통과 | policy.wasm이 실제로 로드되는지 확인, WasmPolicyChecker 로그 |
| gRPC 연결 실패 | NodeForge 서버 실행 중인지, 포트 확인 |
| builder Job 생성 안 됨 | `kubectl describe job <builder-job>` → Events 확인. K8s API 오류 또는 RBAC 권한 확인 |
| builder Job Pending 지속 | `kubectl describe pod` → Events 확인. 이미지 pull 오류 또는 노드 리소스 부족 여부 |
| builder Job 실패 | `kubectl logs job/<builder-job>` → 빌드 오류 로그 확인 |
| digest 확보 실패 | 레지스트리 API로 push된 이미지와 tag 직접 확인 |
| NodeForge가 Job 상태 감지 못할 때 | NodeForge K8s watch 연결 상태 확인, kubeconfig 권한 확인 |
| 내부 레지스트리 push 실패 | 레지스트리 컨테이너 실행 중인지, 5001 포트 확인 |
| dry-run 거부 | manifest YAML 내용 확인, `kubectl apply --dry-run=server -f manifest.yaml` 직접 실행 |
| smoke run ImagePullBackOff | kind ↔ 내부 레지스트리 연동 설정 확인 |
| smoke run Pending | kind 노드 자원 확인, 리소스 요청 줄이기 |

---

## 8. 한계 및 로드맵

### 이번 스프린트의 의도적 한계

| 항목 | 현재 구현 | 향후 방향 |
|------|-----------|-----------|
| PolicyBundleProvider | 로컬 파일 → gRPC 전환 (스프린트 중 진행) | 정책 서버 자동 폴링 |
| ToolDefinition 저장 | 로컬 CAS 파일 | Harbor 또는 중앙 레지스트리 |
| 레지스트리 | 로컬 registry:2 | Harbor |
| builder workload 구현 | Job-per-build (초기 기본선) | Deployment / StatefulSet 기반 지속형 빌더 (빌드 캐시 효율이 실제 문제가 됐을 때 전환) |
| 빌더 구현체 | OCI-compatible builder (BuildKit 계열 초기 후보) | 빌더 교체 가능한 추상화 구조로 발전 |
| K8s 접근 방식 | kubeconfig 로컬 직접 접근 | Ingress + gRPC gateway router |
| DockGuard 적용 범위 | Dockerfile authoring 단계 | K8s Admission Webhook으로 확장 |
| 연결 규칙 | 출력 수 == 입력 수 (임시) | 이름 기반 I/O 매핑 검증 |
| 캔버스/연결선 | STRETCH 최소 구현 | DagEdit에서 완전 구현 |

### DagEdit에 반영될 사항

이번 스프린트에서 발견한 이슈와 검증 결과는 DagEdit 설계에 직접 반영된다.

- 변수(`${R1}`) 선언 방식 — UI 직접 선언 vs 스크립트 자동 파싱
- 연결선의 이름 기반 I/O 매핑 검증
- tori FileBlock 데이터 바인딩 UI
- 파이프라인 전체 실행 및 상태 추적
- Pipeline Representation Spec v0.1의 4단계 표현 모델 완전 지원

### 로드맵 항목

| 항목 | 설명 |
|------|------|
| DockGuard K8s Admission Webhook | K8s Job 제출 시점에도 정책 검사 |
| 정책 서버 + 자동 갱신 | OPA Bundle Server, 폴링 기반 자동 업데이트 |
| Harbor 레지스트리 연동 | 엔터프라이즈급 이미지 관리 |
| gRPC gateway router | 외부 클라이언트 → K8s 접근 표준화 |
| 지속형 builder service | Job-per-build에서 Deployment / StatefulSet 기반 builder로 전환 (빌드 캐시 지속, 병렬 빌드 지원) |
| builder workload 추상화 | 빌더 구현체(BuildKit / Buildah 등) 교체 가능한 인터페이스 레이어 |
| 이름 기반 I/O 매핑 검증 | "출력 수 == 입력 수" 규칙을 이름 기반으로 교체 |
| ToolDefinition 버전 관리 | CAS 기반에서 semantic versioning 또는 content hash 전략 추가 |

---

## 관련 문서

| 문서 | 설명 |
|------|------|
| `Pipeline Representation Spec v0.1` (2026-04-04) | 전체 파이프라인 표현 모델 (AuthoredPipeline → ExecutablePipeline) |
| `PIPELINE_LITE_SPEC.md` (2026-03-27) | 10일 PoC 전용 최소 명세 (이전 문서) |
| dockhouse | 재현 가능한 툴 이미지 제작 방법의 참조 구현 |
| DockGuard | Dockerfile build 단계 정책 검사 도구 (.rego 기반) |
