# NodeKit

**NodeKit**은 바이오인포매틱스 분석 도구를 컨테이너 이미지로 빌드하고 NodeVault 플랫폼에 등록하는 관리자 데스크탑 애플리케이션입니다.  
C# / Avalonia UI로 작성되어 Windows, macOS, Linux(Ubuntu)에서 동작합니다.

---

## 화면 구성

| 화면 | 설명 |
|------|------|
| **Tool 정의** | 이미지 URI, Dockerfile, 실행 스크립트, I/O 포트 입력 → L1 정책 검증 → NodeVault로 빌드 요청 전송 |
| **등록된 Tools** | NodeVault Catalog에 등록된 도구 목록 조회 (Lifecycle / Health 상태 포함) |
| **등록된 Data** | 등록된 참조 데이터셋 목록 조회 |
| **정책 관리** | DockGuard 정책 번들 버전 확인 및 규칙 목록 조회 |
| **⚙ 서버 설정** | NodeVault / Catalog 서비스 주소 설정 (다른 장비에서 사용 시 필수) |

### 스크린샷

| | |
|---|---|
| ![Tool 정의](docs/screenshots/authoring-ui.svg) | ![L1 검증 통과](docs/screenshots/authoring-ui-validation-pass.svg) |
| ![L1 검증 실패](docs/screenshots/authoring-ui-validation-failure.svg) | ![등록된 Tools](docs/screenshots/registered-tools.svg) |
| ![등록된 Data](docs/screenshots/registered-data.svg) | ![정책 관리](docs/screenshots/policy-management.svg) |
| ![서버 설정](docs/screenshots/server-settings.svg) | |

---

## 설치 및 실행

### 방법 1 — 소스에서 빌드 (개발 환경)

```bash
# 의존성 설치 (dotnet 9 SDK 필요)
dotnet restore

# 실행
dotnet run --project NodeKit.csproj
```

### 방법 2 — Ubuntu 자체 포함 패키지 (다른 장비에서 사용)

다른 Ubuntu 장비에서 .NET 런타임 없이 실행하려면 **자체 포함(self-contained) 패키지**를 빌드합니다.

```bash
# 빌드 (최초 1회 또는 소스 변경 후)
make publish-linux
# 또는 직접 실행
dotnet publish NodeKit.csproj -c Release -r linux-x64 --self-contained true -o publish/linux-x64
```

빌드 결과물은 `publish/linux-x64/` 디렉토리에 생성됩니다.

**Ubuntu 장비로 복사 및 실행:**

```bash
# 1. 파일 복사 (SCP 또는 rsync)
scp -r publish/linux-x64/ user@ubuntu-machine:~/nodekit/

# 2. 실행 권한 부여
chmod +x ~/nodekit/NodeKit

# 3. 실행
~/nodekit/NodeKit
```

> **참고**: Ubuntu에서 Avalonia UI 실행 시 X11 또는 Wayland 디스플레이 서버가 필요합니다.  
> 데스크탑 환경이 설치된 Ubuntu에서 바로 실행됩니다.

---

## 다른 장비에서 NodeVault 서버에 연결하기

NodeKit을 노트북이나 다른 Ubuntu 장비에서 실행할 때, **NodeVault 서버 주소를 변경**해야 합니다.

### 방법 1 — UI에서 설정 (권장)

1. 좌측 메뉴에서 **⚙ 서버 설정** 클릭
2. **NodeVault 주소** 입력: `<서버IP>:50051`  
   예) `100.123.80.48:50051`
3. **Catalog 주소** 입력: `<서버IP>:8080`  
   예) `100.123.80.48:8080`
4. **저장** 버튼 클릭

설정은 `~/.config/NodeKit/settings.json`에 저장되며 다음 실행 시 자동으로 불러옵니다.

### 방법 2 — 설정 파일 직접 편집

```bash
mkdir -p ~/.config/NodeKit
cat > ~/.config/NodeKit/settings.json << 'EOF'
{
  "NodeVaultAddress": "100.123.80.48:50051",
  "CatalogAddress": "100.123.80.48:8080"
}
EOF
```

---

## 핵심 개념

### L1 정책 검증 (로컬 실행)

NodeKit은 빌드 요청 전송 전에 DockGuard 정책을 **로컬에서 WASM으로 실행**합니다.  
네트워크 없이 즉시 피드백을 줍니다.

| 규칙 | 내용 |
|------|------|
| `latest` 태그 금지 | 재현성 보장을 위해 버전 고정 필수 |
| digest `@sha256:` 필수 | 이미지 무결성 pin |
| `apt install` 버전 고정 | 패키지 버전 미지정 시 차단 |
| `AS builder` 별칭 필수 | 멀티스테이지 FROM 표준화 |
| … (총 9개 규칙) | `정책 관리` 화면에서 전체 목록 확인 |

### stableRef vs casHash

| 식별자 | 용도 | 예시 |
|--------|------|------|
| `stableRef` | UI 검색, 사람이 읽는 이름 | `BWA-MEM2@2.2.1` |
| `casHash` | 파이프라인 pin, 불변 식별자 | `d3adbeef0123…` |

파이프라인에 도구를 저장할 때는 반드시 `casHash`를 사용합니다.

---

## 아키텍처 요약

```
NodeKit (이 앱)
  │
  ├─ L1 검증 (로컬 WASM)
  │
  ├─ gRPC :50051 ──► NodeVault
  │                    ├─ L2 이미지 빌드 (buildah/podbridge5)
  │                    ├─ L3 dry-run (K8s)
  │                    └─ L4 smoke run (K8s)
  │
  └─ REST :8080 ──► NodePalette (Catalog)
                       └─ 등록된 Tool / Data 목록 제공
```

상세 설계는 [CLAUDE.md](CLAUDE.md), [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) 참조.

---

## 개발 환경 요구사항

| 항목 | 버전 |
|------|------|
| .NET SDK | 9.0 이상 |
| Avalonia | 11.3.13 |
| OS | Windows 10+, macOS 12+, Ubuntu 22.04+ |
