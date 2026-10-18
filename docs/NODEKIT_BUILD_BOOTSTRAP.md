# NodeKit Build Bootstrap

작성일: 2026-04-08  
최종 수정: 2026-06-18

---

## 목적

이 문서는 NodeKit을 현재 개발 환경 외부에서 빌드할 때 필요한 외부 의존성과 경로 설정을 정리한다.

현재 NodeKit은 두 가지 외부 입력을 전제한다.

* `NodeVault/protos/` 의 `nodevault.proto` (proto 컴파일 용도) ← api-protos Sprint 1-4 완료, canonical 이관
* `DockGuard` 저장소의 Dockerfile 정책 디렉터리 (dockguard.wasm 생성 용도)

---

## 1. proto 소스 경로 설정

### 현재 상태 (2026-04-19)

**api-protos Sprint 1-4 완료.** `nodevault.proto` canonical 경로는 `NodeVault/protos/nodevault/v1/`이다.
NodeKit은 그 바이트를 저장소 `protos/`에 벤더링해 쓰며, `NodeVault/protos/`를 자동 탐지하지 않는다.

### 경로 설정

`NodeKit.csproj`는 `ApiProtosRoot` MSBuild 속성 아래에서 `nodevault/v1/nodevault.proto`를 찾는다.
기본값은 저장소에 벤더링된 `protos/` 디렉터리이므로 **NodeVault 체크아웃 없이 빌드된다**
(출처는 `protos/SOURCE.md`).

```bash
dotnet test --solution NodeKit.sln
dotnet build NodeKit.sln
```

필요하면 `/p:ApiProtosRoot=<경로>`로 명시 override할 수 있다.
`ApiProtosRoot`가 비어 있거나 `nodevault.proto`가 없으면(벤더 사본 누락) 빌드가 명확한 에러 메시지와 함께 중단된다
(CLI `src/NodeKit.Cli`는 `NKPROTO004`). 옆에 NodeVault checkout이 있어도 그 경로로 fallback하지 않는다.

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

NodeKit과 NodeVault의 proto 스키마가 일치해야 한다:
- `BuildRequest`, `RegisterToolRequest`, `RegisteredToolDefinition` 필드가 두 저장소 모두 같아야 함
- proto 변경 시 NodeVault `protos/nodevault/v1/nodevault.proto` 수정 후 NodeKit 재빌드 필요

### compiler warning

`CLAUDE.md`의 warning policy를 따른다. 변경 작업은 새 compiler/analyzer warning을
추가하지 않아야 하며, legacy path 안정화 작업 중 CI/lint/test/coverage 기준을
`docs/NODEKIT_CLI_FIRST_SPRINT_PLAN.md`에 맞춰 정리한다.

---

## 4. 권장 부트스트랩 순서

```bash
# 1. 빌드 (저장소 protos/ 벤더 사본 사용 — 경로 지정 불필요)
dotnet build NodeKit.sln

# 2. DockGuard .wasm 번들 생성 (DockGuard 저장소 경로 지정)
make policy DOCKGUARD=/opt/dotnet/src/github.com/HeaInSeo/DockGuard/policy/dockerfile

# 3. 테스트
dotnet test --solution NodeKit.sln
```

현재 개발 환경 경로:
- NodeVault protos: 저장소 내 `protos/` (NodeVault에서 벤더링, 출처는 `protos/SOURCE.md`)
- DockGuard: `/opt/dotnet/src/github.com/HeaInSeo/DockGuard`

---

## 5. CLI 배포 package (linux-x64 self-contained)

지원 profile은 하나다: **linux-x64 / self-contained / loose directory / tar.gz**
(대상 Ubuntu 24.04 x86_64, glibc). framework-dependent, single-file, 다른 OS/RID는
이 범위에 없다. GUI(`NodeKit.csproj`) publish 출력은 CLI 배포물로 쓰지 않는다.

```bash
scripts/package-cli.sh --version 0.9.1            # 기본 출력: artifacts/cli/
scripts/package-cli.sh --version 0.9.1 --output /tmp/nk-cli
```

- restore는 `src/NodeKit.Cli/packages.lock.json`의 linux-x64 섹션으로 `--locked-mode`만 쓴다.
  CLI csproj의 `RuntimeIdentifiers`에 `linux-x64`가 있어서 그 섹션이 생긴다.
- dirty 작업 트리면 거부한다. `--allow-dirty`는 manifest에 `source.dirty: true`를
  남기는 시험용이며 release로 쓰지 않는다.
- 산출물:
  - `nodekit-cli-<version>-linux-x64.tar.gz`: 최상위 디렉터리 `nodekit-cli-<version>-linux-x64/` 하나.
    실행 파일(apphost)은 그 안의 `NodeKit.Cli`이고, 문서와 CLI 안내문이 쓰는 command 이름 `nodekit`은
    같은 디렉터리의 상대 symlink(`nodekit -> NodeKit.Cli`)다.
  - `nodekit-cli-<version>-linux-x64.tar.gz.sha256`: `sha256sum -c`로 확인한다.
  - `nodekit-cli-<version>-linux-x64.cli-manifest.json`: archive 안 `cli-manifest.json`과 같다.
- manifest(`release/cli-manifest.template.json`에서 생성, `scripts/write-cli-manifest.py`)에는
  version, source SHA/dirty, `protos/provenance.json` sha256(contract source), dotnet SDK,
  native 필요조건, `NodeKit.Cli.deps.json`의 package closure, 파일별 크기/sha256/실행 여부,
  `entrypoint`(`NodeKit.Cli`)와 `command_alias`(`nodekit` → `NodeKit.Cli`)가 들어간다. symlink는 `files`에 넣지 않는다.
  closure에 Avalonia/SkiaSharp/Wasmtime/KubernetesClient가 있으면 manifest를 쓰지 않고 실패한다.
  `nodekit` alias가 없거나 다른 대상을 가리키거나 다른 symlink가 있어도 실패한다.
- 대상 machine에는 .NET SDK/runtime이 필요 없다. 필요한 OS package는 manifest의
  `native_prerequisites`를 따른다.

설치·실행(대상 machine, 설치 위치는 예시다. repository checkout이나 특정 CWD가 필요 없다):

```bash
sha256sum -c nodekit-cli-0.9.1-linux-x64.tar.gz.sha256
tar -xzf nodekit-cli-0.9.1-linux-x64.tar.gz -C "$HOME/.local/opt"
ln -s "$HOME/.local/opt/nodekit-cli-0.9.1-linux-x64/nodekit" "$HOME/.local/bin/nodekit"   # 또는 그 디렉터리를 PATH에 추가
nodekit --help
```

`scripts/verify-cli-package.py --dir <output> --version <version>`는 세 산출물, checksum, archive 안 manifest,
파일 목록, 실행 권한과 `nodekit` alias symlink를 확인한다(CI `Build · Test`도 실행).

여기서 만든 package는 준비용이다. 최종 release package는 모든 기능 변경이 들어간
target source SHA에서 다시 만든다.

---

## 6. CLI 단독 clean build 검증 (S4-01)

```bash
scripts/verify-cli-clean-build.sh
```

HEAD의 추적 파일만 빈 임시 디렉터리에 풀어(NodeVault/DockGuard sibling 없음) 아래를 확인한다.
미커밋 변경은 검증하지 않으므로 commit 뒤에 실행한다. CI `Build · Test`도 같은 script를 돌린다.

- A: `dotnet restore src/NodeKit.Cli/NodeKit.Cli.csproj --locked-mode`와 CLI Release build가 성공하고,
  GUI project(`NodeKit.csproj`)는 빌드되지 않는다.
- B: `scripts/check-cli-closure.py`가 lock 파일(linux-x64 섹션 포함)과 `NodeKit.Cli.deps.json`을 본다.
  Avalonia/SkiaSharp/Wasmtime/KubernetesClient나 project 참조가 있으면 실패하고,
  Spectre.Console/Grpc.Net.Client/Google.Protobuf는 Direct 의존성이자 runtime closure에 있어야 한다.
  통과하면 runtime closure를 출력한다.
- C: `protos/nodevault/v1/nodevault.proto`만 없거나 `protos/` 전체가 없는 checkout에서 Debug·Release build가
  `NKPROTO004`로 실패한다. 옆에 `NodeVault/protos`를 둬도 그 경로를 쓰지 않는다.
