# NodeKit 사용 가이드

버전: 1.0  
작성일: 2026-04-20  
대상: UX 테스트 담당자, 관리자

---

## 목차

1. [설치 (Ubuntu)](#1-설치-ubuntu)
2. [첫 실행 — 서버 주소 설정](#2-첫-실행--서버-주소-설정)
3. [Tool 정의 및 빌드 요청](#3-tool-정의-및-빌드-요청)
4. [등록된 Tool 목록 조회](#4-등록된-tool-목록-조회)
5. [등록된 Data 목록 조회](#5-등록된-data-목록-조회)
6. [정책 관리](#6-정책-관리)
7. [트러블슈팅](#7-트러블슈팅)

---

## 1. 설치 (Ubuntu)

### 1-1. 빌드 장비에서 배포 패키지 생성

NodeKit 소스가 있는 장비에서 실행합니다.

```bash
cd /opt/dotnet/src/github.com/HeaInSeo/NodeKit
make publish-linux
```

출력 디렉토리: `publish/linux-x64/` (약 126 MB, .NET 런타임 포함)

### 1-2. 대상 Ubuntu 장비로 복사

```bash
rsync -av --delete publish/linux-x64/ user@target-host:~/nodekit/
```

또는 scp를 사용하는 경우:

```bash
scp -r publish/linux-x64/ user@target-host:~/nodekit/
```

### 1-3. 대상 장비에서 실행 권한 부여 및 실행

```bash
chmod +x ~/nodekit/NodeKit
~/nodekit/NodeKit
```

### 1-4. 시스템 라이브러리 요구사항

Ubuntu 22.04 이상을 기준으로 아래 패키지가 필요합니다.
없는 경우 한 번만 설치하면 됩니다.

```bash
sudo apt-get install -y \
  libx11-6 libxcursor1 libxi6 libxrandr2 libxext6 \
  libgl1 libfontconfig1 libice6 libsm6
```

---

## 2. 첫 실행 — 서버 주소 설정

앱을 처음 실행하면 기본 주소(seoy 서버)로 설정되어 있습니다.
다른 환경에서 테스트하는 경우 서버 주소를 변경해야 합니다.

### 2-1. 설정 패널 열기

좌측 네비게이션 하단 **⚙ 서버 설정** 버튼을 클릭합니다.

### 2-2. 주소 입력

| 항목 | 설명 | 기본값 |
|------|------|--------|
| NodeVault 주소 | 빌드 요청 및 정책 서비스 (gRPC) | `http://100.123.80.48:50051` |
| Catalog 주소 | Tool / Data 목록 조회 (REST) | `http://100.123.80.48:8080` |

주소 형식: `http://호스트:포트` (현재 TLS 미사용)

### 2-3. 저장

**저장** 버튼을 클릭합니다.
녹색 `✓ 설정이 저장되었습니다.` 메시지가 나타나면 완료입니다.

하단에 설정 파일 경로가 표시됩니다.
설정은 해당 파일에 저장되므로 다음 실행 시 다시 입력할 필요가 없습니다.

> Linux에서 설정 파일 위치: `~/.config/NodeKit/settings.json`

### 2-4. 기본값으로 초기화

**기본값으로 초기화** 버튼을 클릭하면 seoy 기본 주소로 되돌아갑니다.

---

## 3. Tool 정의 및 빌드 요청

좌측 네비게이션 **Tool 정의** 버튼을 클릭합니다.

### 3-1. 기본 정보 입력

| 항목 | 설명 | 예시 |
|------|------|------|
| Tool 이름 | 영문 소문자, 하이픈 허용 | `bwa-mem2` |
| 버전 | Semantic Versioning 권장 | `2.2.1` |
| 컨테이너 이미지 URI | `@sha256:` digest 포함 필수 | `registry.example.com/bwa-mem2:2.2.1@sha256:abc123...` |

> `latest` 태그와 digest 없는 URI는 L1 검증에서 차단됩니다.

### 3-2. Dockerfile 입력 (선택)

실행 환경을 정의하는 Dockerfile을 입력합니다.
DockGuard 정책에 따라 아래 항목은 차단됩니다.

- `apt-get install` 버전 미고정 (예: `apt-get install bwa` → 차단, `apt-get install bwa=0.7.17-*` → 허용)
- 멀티스테이지 빌드 구조 미준수

### 3-3. 실행 스크립트 (선택)

컨테이너 내에서 실행할 셸 스크립트를 입력합니다.

### 3-4. 런타임 커맨드 오버라이드 (선택)

Dockerfile `CMD`를 재정의할 경우 JSON 배열 형식으로 입력합니다.

```json
["/bin/sh", "-c", "/app/executor.sh"]
```

### 3-5. I/O 포트 선언

**+ Input 추가** / **+ Output 추가** 버튼으로 포트를 추가합니다.

**Input 포트 항목:**

| 항목 | 설명 | 예시 |
|------|------|------|
| 이름 | 포트 식별자 | `reads` |
| 역할(role) | 데이터 의미 | `sample-fastq` |
| 형식(format) | 파일 형식 | `fastq` |
| shape | 단일/쌍 | `single` / `pair` |

**Output 포트 항목:**

| 항목 | 설명 | 예시 |
|------|------|------|
| 이름 | 포트 식별자 | `aligned_bam` |
| 역할(role) | 데이터 의미 | `aligned-bam` |
| 형식(format) | 파일 형식 | `bam` |
| shape | 단일/쌍 | `single` |
| class | 주/보조 출력 | `primary` / `secondary` |

행을 삭제하려면 행 오른쪽 **×** 버튼을 클릭합니다.
마지막 행은 삭제 대신 내용이 초기화됩니다.

### 3-6. 환경 스펙 (선택)

conda yml 또는 requirements.txt 내용을 입력합니다.
버전 미고정 패키지는 L1 검증에서 차단됩니다.

```
# 허용: numpy==1.26.4
# 차단: numpy
```

### 3-7. UI 팔레트 표시 정보

Catalog에서 사용자에게 보이는 표시 정보입니다.

| 항목 | 설명 | 예시 |
|------|------|------|
| 레이블 | UI 카드 제목 | `BWA-MEM2 2.2.1` |
| 카테고리 | 분류 | `Alignment` |
| 설명 | 툴팁 텍스트 | `paired-end FASTQ → BAM` |
| 태그 | 쉼표 구분 | `dna, alignment, short-read` |

### 3-8. L1 검증 실행

**L1 검증** 버튼을 클릭합니다.

- **통과**: 녹색 `✓ L1 검증 통과` 배너 표시, **빌드 요청 전송** 버튼 활성화
- **실패**: 빨간색 위반 항목 목록 표시 — 각 항목에 규칙 ID와 수정 방법이 표시됩니다

> 입력값을 수정하면 검증 상태가 초기화됩니다. L1 통과 후 폼을 수정하면 재검증이 필요합니다.

### 3-9. 빌드 요청 전송

**빌드 요청 전송 (NodeVault)** 버튼을 클릭합니다.

- 하단에 빌드 로그가 실시간으로 표시됩니다
- 완료 시 `✓ 빌드 및 등록 완료`와 image digest가 표시됩니다
- 실패 시 상태바에 오류 메시지가 표시됩니다

---

## 4. 등록된 Tool 목록 조회

좌측 네비게이션 **등록된 Tools** 버튼을 클릭합니다.

Catalog 서비스에서 `lifecycle_phase = Active` 상태인 Tool 목록을 불러옵니다.

각 항목에 표시되는 정보:
- 레이블, 카테고리, lifecycle phase
- CAS hash (앞 12자), 등록 일시

**새로 고침** 버튼으로 최신 목록을 다시 불러올 수 있습니다.

---

## 5. 등록된 Data 목록 조회

좌측 네비게이션 **등록된 Data** 버튼을 클릭합니다.

Catalog 서비스에서 `lifecycle_phase = Active` 상태인 참조 데이터 목록을 불러옵니다.

각 항목에 표시되는 정보:
- 레이블, 형식, 카테고리, lifecycle phase, integrity health
- CAS hash (앞 12자), 등록 일시

---

## 6. 정책 관리

좌측 네비게이션 **정책 관리** 버튼을 클릭합니다.

### 6-1. 정책 목록 확인

**목록 새로 고침** 버튼을 클릭하면 현재 적용 중인 DockGuard 정책 규칙과 번들 버전이 표시됩니다.

### 6-2. 번들 갱신

NodeVault에 새 정책 번들이 배포된 경우 **번들 갱신** 버튼을 클릭하면
최신 `.wasm` 번들을 가져와 즉시 적용합니다.
이후 L1 검증에 새 정책이 반영됩니다.

---

## 7. 트러블슈팅

### 실행 시 화면이 나타나지 않거나 즉시 종료되는 경우

디스플레이 환경이 없는 서버(headless)에서는 GUI 앱이 실행되지 않습니다.
X11 또는 Wayland 세션이 있는 데스크톱 환경이 필요합니다.

SSH 원격 접속의 경우 X forwarding을 사용합니다:

```bash
ssh -X user@target-host
~/nodekit/NodeKit
```

### 라이브러리 오류 (`libX11.so`, `libGL.so` 등)

[1-4. 시스템 라이브러리 요구사항](#1-4-시스템-라이브러리-요구사항)의 패키지를 설치합니다.

### `gRPC 오류: ...` 메시지가 상태바에 표시되는 경우

1. **⚙ 서버 설정**에서 NodeVault 주소가 올바른지 확인합니다
2. 대상 서버가 실행 중인지 확인합니다 (`curl http://host:50051` 응답 여부)
3. 방화벽에서 해당 포트(기본 50051)가 열려 있는지 확인합니다

### `목록 조회 오류: ...` 메시지가 표시되는 경우

1. **⚙ 서버 설정**에서 Catalog 주소가 올바른지 확인합니다
2. `curl http://host:8080/v1/catalog/tools` 로 직접 응답을 확인합니다

### L1 검증은 통과하지만 빌드가 실패하는 경우

NodeVault 서버 측 L2/L3/L4 검증 실패입니다.
빌드 로그 패널의 `[ERROR]` 메시지를 확인하고 NodeVault 담당자에게 공유합니다.

### DockGuard 정책 번들을 로드할 수 없습니다 (POLICY-UNAVAIL)

`assets/policy/dockguard.wasm` 파일이 배포 패키지에 포함되지 않았거나 손상된 경우입니다.
`~/nodekit/assets/policy/dockguard.wasm` 파일 존재 여부를 확인합니다.

이 경고가 표시되어도 Dockerfile이 없는 Tool은 L1 검증을 통과할 수 있습니다.

---

## 참고

- **설정 파일 위치** (Linux): `~/.config/NodeKit/settings.json`
- **아키텍처 문서**: `docs/ARCHITECTURE.md`
- **UI 구조 상세**: `docs/NODEKIT_UI_STRUCTURE.md`
