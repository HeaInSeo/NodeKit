# NodeKit CLI release 안내 — 받기·설치·사용·복구·release 점검

설치된 `nodekit` 명령을 쓰는 사람과 release를 점검하는 사람을 위한 문서다. 명령별 자세한 옵션과 출력
형식은 [`NODEKIT_CLI_USAGE.md`](NODEKIT_CLI_USAGE.md), package를 만드는 방법은
[`NODEKIT_BUILD_BOOTSTRAP.md`](NODEKIT_BUILD_BOOTSTRAP.md) §5–§6에 있다. 저장소 checkout에서 `dotnet run`으로
쓰는 개발용 방법은 `NODEKIT_CLI_USAGE.md` §0–§1이다. 이 문서의 명령은 **설치한 package**만 쓰며 .NET SDK와
저장소 checkout이 필요 없다.

## 1. 대상 profile

지원 profile은 하나다: **linux-x64 / self-contained / loose directory / tar.gz**, 대상은 Ubuntu 24.04 x86_64(glibc).
framework-dependent, single-file, 다른 OS/RID는 범위에 없다. 대상 machine에 필요한 OS package는 manifest의
`native_prerequisites`(libc6, libgcc-s1, libstdc++6, libicu74, libssl3t64, libgssapi-krb5-2, zlib1g, ca-certificates,
tzdata)다. GUI(`NodeKit.csproj`) publish 출력은 CLI 배포물이 아니다.

## 2. 받기와 확인

package는 세 파일이다. `<version>`은 semver다.

| 파일 | 내용 |
|---|---|
| `nodekit-cli-<version>-linux-x64.tar.gz` | 최상위 디렉터리 `nodekit-cli-<version>-linux-x64/` 하나. 실행 파일은 `NodeKit.Cli`, 명령 이름 `nodekit`은 같은 디렉터리의 상대 symlink |
| `nodekit-cli-<version>-linux-x64.tar.gz.sha256` | `sha256sum -c` 형식 checksum |
| `nodekit-cli-<version>-linux-x64.cli-manifest.json` | archive 안 `cli-manifest.json`과 같은 파일 |

받는 방법은 두 가지다.

- **CI가 시험한 archive:** main push의 Verify `Build · Test` run은 published-process 시험이 실제로 실행한
  archive를 artifact `cli-package`로 올린다. 이 archive의 version은 CI용 `0.0.0-ci`다.

  ```bash
  gh run list -R HeaInSeo/NodeKit --workflow Verify --branch main --event push --limit 1
  gh run download <run-id> -R HeaInSeo/NodeKit -n cli-package -D nodekit-cli
  ```

- **version을 붙인 release archive:** release로 정한 source SHA를 checkout해 깨끗한 작업 트리에서 만든다.
  `scripts/package-cli.sh --version 0.9.1 --output <dir>`. 같은 SHA·같은 SDK에서는 archive bytes가 같다(CI
  `CLI package is reproducible`). SDK가 다르면 archive hash가 달라질 수 있으므로 release 기록에는 만든 SDK도 남긴다.

받은 뒤에는 설치 전에 확인한다.

```bash
cd nodekit-cli
sha256sum -c nodekit-cli-0.0.0-ci-linux-x64.tar.gz.sha256
python3 -m json.tool nodekit-cli-0.0.0-ci-linux-x64.cli-manifest.json | head -40
```

manifest에서 볼 값: `version`, `source.sha`와 `source.dirty: false`, `contract_source`(`protos/provenance.json`의
sha256), `build.dotnet_sdk`, `runtime_closure`(Spectre.Console/Grpc.Net.Client/Google.Protobuf 계열, Avalonia/SkiaSharp/
Wasmtime/KubernetesClient 없음), `entrypoint`·`command_alias`, 파일별 크기/sha256/실행 여부. 저장소 checkout이 있으면
`python3 scripts/verify-cli-package.py --dir <dir> --version <version>`이 세 파일·checksum·archive 안 manifest·파일 목록·
실행 권한·`nodekit` alias를 한 번에 확인한다.

## 3. 설치와 빠른 시작

설치 위치는 예시다. 특정 CWD나 저장소가 필요 없다.

```bash
mkdir -p "$HOME/.local/opt" "$HOME/.local/bin"
tar -xzf nodekit-cli-0.0.0-ci-linux-x64.tar.gz -C "$HOME/.local/opt"
ln -sf "$HOME/.local/opt/nodekit-cli-0.0.0-ci-linux-x64/nodekit" "$HOME/.local/bin/nodekit"
nodekit --help
```

`$HOME/.local/bin`이 `PATH`에 없으면 추가하거나 `"$HOME/.local/opt/nodekit-cli-0.0.0-ci-linux-x64/nodekit"`를 직접 실행한다.

ToolSpec Recipe 하나를 만들고 검증·render한다. 아래는 네트워크 조회가 필요 없는 container 방식이다. 대화형으로
만들려면 `nodekit recipe create recipe.json`만 실행하고 질문을 따라간다(`NODEKIT_CLI_USAGE.md` §2).

```bash
mkdir -p ~/nodekit-work && cd ~/nodekit-work
nodekit recipe create recipe.json --non-interactive --method container \
  --field ToolName=bwa \
  --field ToolVersion=0.7.17 \
  --field "Script=bwa mem" \
  --field ImageRef=quay.io/biocontainers/bwa:0.7.17--h7132678_9 \
  --field ImageDigest=sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
nodekit validate recipe.json
nodekit render recipe.json --out build-request.json
```

세 명령 모두 exit 0이면 `recipe.json`과 `build-request.json`이 생긴다. `render --format raw-spec`은 `submit`이
`ResolveToolSpec`에 보낼 `raw_spec` 값만 쓴다. 다른 method(package/mirror/source/dockerfile)의 필수 field는
`NODEKIT_CLI_USAGE.md` §2-10과 §7에 있다. 위 digest는 예시 값이다. 실제 이미지를 빌드할 때는 실제 digest를 쓴다.

NodeVault에 제출하려면 주소를 준다. 실제 NodeVault 연결은 CLI 단독 release 범위 밖이다(§9).

```bash
nodekit submit recipe.json --url http://<nodevault-host>:<port> --format jsonl
```

## 4. Recipe 예시와 동반 파일 배치

**ToolSpec Recipe**(§3의 `recipe.json`)는 파일 하나로 완결된다. `Script`와 Dockerfile 내용은 Recipe 안에 저장되고,
dockerfile 방식에서 경로로 받은 Dockerfile도 내용으로 고정된다. 원본 Dockerfile 파일은 다시 필요 없다.

**ToolFunctionRecipe**는 `ScriptPath`가 외부 파일을 가리킨다. 경로는 **Recipe 파일이 있는 디렉터리 기준 상대 경로**로
쓰고, 그 파일을 같은 상대 배치로 함께 둔다. `Command.Executable`·`Command.WorkingDirectory`·port 경로는 **이미지 안의
경로**이며 host 경로로 해석하지 않는다.

```text
project/
├── function-recipe.json      # ScriptPath = ./scripts/sort.sh
├── scripts/
│   └── sort.sh               # 동반 파일(필수)
└── fixtures/
    └── reads.fastq           # FixtureReferences[0].LocalPath = ./fixtures/reads.fastq (선택)
```

```bash
cd project
nodekit function-recipe create function-recipe.json \
  --tool-spec-digest <C1: 기반 ToolSpec digest, sha256: 없는 소문자 hex 64자리> \
  --base-tool-image-digest <A: 기반 이미지 digest, sha256:...> \
  --non-interactive \
  --field FunctionId=sort-tool.sort \
  --field Revision=v1 \
  --field ScriptPath=./scripts/sort.sh \
  --field Command.Executable=/usr/local/bin/sort-tool \
  --field Command.WorkingDirectory=/work \
  --field Command.Arguments=--threads \
  --field "Command.Arguments={param.threads}" \
  --field "InputPorts[0].Name=reads" \
  --field "InputPorts[0].Required=true" \
  --field "InputPorts[0].PathOrGlob=/work/in/*.fastq" \
  --field "OutputPorts[0].Name=sorted" \
  --field "OutputPorts[0].PathOrGlob=/work/out/sorted.fastq" \
  --field "Parameters[0].Name=threads" \
  --field "Parameters[0].Type=Integer" \
  --field "Parameters[0].DefaultValue=4" \
  --field "FixtureReferences[0].LocalPath=./fixtures/reads.fastq" \
  --field EnforcedResources.CpuRequest=500m \
  --field EnforcedResources.CpuLimit=2000m \
  --field EnforcedResources.MemoryRequest=256Mi \
  --field EnforcedResources.MemoryLimit=1Gi
nodekit function-recipe validate function-recipe.json
nodekit function-recipe render function-recipe.json --out function-render.json
```

이 field 묶음은 published-process 시험(`PublishedCliPortabilityTests`)이 쓰는 입력과 같다. 동반 script가 없으면
validate는 exit 2, 빈 script는 exit 1이며 어느 쪽도 RPC를 보내지 않는다. 명령 계약 전체는
[`specs/001-toolfunctionspec-authoring/contracts/cli-function-recipe-commands.md`](../specs/001-toolfunctionspec-authoring/contracts/cli-function-recipe-commands.md)다.

## 5. 다른 설치(B)에서 다시 쓰기

작성 환경 A에서 저장한 Recipe를 다른 NodeKit 설치 B에서 다시 입력하지 않고 쓴다.

1. B에 §2–§3대로 package를 설치한다. 설치 경로·HOME·작업 디렉터리는 A와 달라도 된다.
2. A에서 **Recipe 파일과 Recipe 디렉터리 기준 동반 파일만** 같은 상대 배치로 복사한다.
   ToolSpec Recipe는 파일 하나, ToolFunctionRecipe는 `function-recipe.json` + `scripts/…` (+ 선택 fixture)다.
3. B에서 `validate`/`render`(ToolFunctionRecipe는 `function-recipe validate`/`render`)를 다시 실행한다. B는 현재
   파일 내용으로 다시 검사하며, 저장된 `Ready` 표지만 믿지 않는다.

옮기지 않는 것: A의 endpoint 설정, 설치 경로, credential, `.nodekit/` 아래 receipt·journal. 과거 요청 기록은 A의 것이고
B의 새 요청은 B에서 새로 기록된다. `ScriptPath`를 절대 경로로 쓰면 B에서 그 경로가 없어 실패하므로 상대 경로로
쓴다. 이 흐름은 같은 archive를 따로 푼 두 설치로 시험했다(`PublishedCliPortabilityTests`). 같은 host·같은 OS 사용자에서
돈 시험이며 별도 기계 증거는 아니다.

## 6. 현재 의존성·payload·식별자 경계

- **의존성:** CLI는 NuGet package를 쓴다. `Spectre.Console`, `Grpc.Net.Client`, `Google.Protobuf`가 runtime
  dependency이고 `Grpc.Tools`는 빌드 전용이다. 버전은 `src/NodeKit.Cli/packages.lock.json`으로 고정된다. 이전 문서의
  "NuGet 패키지를 쓰지 않는다"는 사실이 아니다. self-contained package라 대상 machine에 .NET runtime이 필요 없다.
- **ToolSpec 첫 이미지 payload:** `submit`은 `ResolveToolSpec → SubmitToolBuild → WatchToolBuild`를 쓴다.
  `render --format raw-spec`이 보낼 `raw_spec`을 그대로 보여 준다.
- **ToolFunction 두 번째 이미지 payload:** `raw_spec`은 `nodevault.build.raw_spec.v1`의 **정확한 4키**(kind 2)다.
  `function-recipe render` 출력은 사람이 보는 미리보기이며 실제로 보낼 4키 payload와 같지 않다.
- **typed 등록:** `RegisterToolFunctionRequest`는 `canonicalization_version="w2-set-v1"`(request field 8)을 보내고,
  응답 `canonicalization_version`(response field 4)이 비었거나 다르면 결과 불명 `REGISTRATION_VERSION_MISMATCH`다.
  대소문자만 다른 값이나 길이가 같은 다른 version도 받지 않는다.
- **상류 gate:** 현재 NodeVault 빌드 경로가 kind 2를 받지 않으므로 `function-recipe submit`은 유효한 `Ready`도 요청 없이
  `TOOLFUNCTION_BUILD_GATE_CLOSED`(exit 1)로 끝난다. 이것은 **현재 상태**이며 영구 차단이 아니다. NodeVault가 kind 2를
  받으면 client 경로(두 번째 이미지 빌드 → typed 등록)가 이미 구현돼 있다.
- **식별자:** A(첫 이미지 digest), C1(첫 이미지 ToolSpec digest, 등록 `base_tool_spec_digest`), B(두 번째 이미지 digest,
  등록 `image_digest`), C2(두 번째 빌드의 ToolSpec digest, 기록만 하고 C1 자리에 쓰지 않음), F/H/P
  (`tool_function_digest`/`cas_hash`/`presentation_revision_id`, NodeVault 응답 그대로 저장). **pipeline pin은 서버가
  돌려준 H(`cas_hash`)다.** NodeKit은 F/H/P를 계산하거나 추정하지 않는다. 전체 표는 `NODEKIT_CLI_USAGE.md` §9에 있다.
  C1→`base_tool_spec_digest` mapping은 v0.9.1 추천 계약이며, 바뀌면 mapper와 문서를 함께 고친다.
- **지원 경계:** `--registration-timeout`은 opt-in이며 기본은 등록 deadline 없음이다. 파일 저장(create/validate/render)은
  임시 파일 → 교체 방식이라 실패·취소 시 기존 파일 bytes가 그대로 남는다. 빌드 성공과 등록 성공은 어느 쪽도
  승인(`Approved`)이 아니다.

## 7. 실행·복구 가이드

| exit | 뜻 | 다음 행동 |
|---|---|---|
| 0 | 성공 | — |
| 1 | 검증 실패·확정 거부·결과 불명 | stderr 진단을 보고 Recipe를 고친다. jsonl의 `remote_build_state`/`remote_registration_state`가 `unknown`이면 실패로 단정하지 말고 아래 확인 명령을 쓴다 |
| 2 | 사용법·인자·파일 IO 오류 | 명령/경로/권한을 고친다. 업무 RPC는 보내지 않았다 |
| 124 | `--connect-timeout`(build ID 전) 또는 `--registration-timeout`(등록 RPC 뒤) 만료 | 원격 상태는 불명. 등록이면 `register-replay` |
| 125 | `--watch-timeout`(build ID 뒤) 만료 | 서버 빌드는 계속될 수 있다. `receipt watch` |
| 130 | 사용자 취소(Ctrl-C, `/cancel`) | 저장 전이면 파일 변경 없음. 보낸 뒤면 원격 상태 불명 |

복구 명령은 새 요청을 만들지 않고 저장된 기록만 쓴다.

```bash
# ToolSpec 빌드: submit이 Recipe 옆 .nodekit/receipts/<request-id>.json에 남긴 receipt
nodekit receipt watch .nodekit/receipts/<request-id>.json --format jsonl    # 다시 관찰
nodekit receipt cancel .nodekit/receipts/<request-id>.json --format jsonl   # 취소 요청 한 번

# ToolFunction typed 등록: 결과 불명(124/130/응답 유실)일 때 같은 요청 bytes만 다시 보낸다
# (기본 기록 위치는 Recipe 옆 .nodekit/registrations/, --receipt를 줬으면 그 receipt의 record root)
nodekit function-recipe register-replay .nodekit/registrations/<request-id>.json --format jsonl
```

`register-replay`는 저장된 같은 endpoint·request ID·version·요청 bytes만 보낸다. 다른 `--url`은 exit 2이고, Recipe/script를
다시 읽거나 새 이미지를 빌드하지 않는다. 이미 registered/rejected인 기록은 RPC 없이 저장된 결과를 보여 준다.
client 쪽 replay는 서버 멱등성의 증거가 아니다. 상태별 `error_code`·`recovery` 값은 `NODEKIT_CLI_USAGE.md` §3·§6·§9에 있다.

**published-process 실행 기록:** CI artifact `published-cli-transcript`(`published-cli-transcript.jsonl`)는 설치한
package를 저장소 밖에서 별도 process로 실행한 각 호출을 한 줄씩 남긴다. 필드는 `package_sha256`, `source_sha`,
`install_dir`, `cwd`, `args`, `stdin`, `env`, `exit`, `stdout`, `stderr`, loopback fake가 받은 `rpc`(method, 요청 type,
요청 sha256, 요청 JSON), `record_dir`과 그 아래 `files` hash다. ToolSpec 제출/관찰·별도 process 재진입, ToolFunction
두 번째 이미지 빌드→typed 등록, ACK 유실 뒤 replay가 loopback HTTP/2 fake server를 상대로 기록된다. fake 응답은 실제
NodeVault 빌드나 등록의 증거가 아니다.

## 8. Release checklist와 evidence manifest

release를 점검하는 사람은 release로 정한 commit의 main push Verify run에서 아래 값을 채운다. 모든 항목이 같은 commit을
가리켜야 한다. 이전 commit의 PASS 기록을 새 release에 그대로 쓰지 않는다.

- [ ] **source SHA**: release commit, `source.dirty: false` (manifest `source`)
- [ ] **contract source SHA**: `protos/provenance.json` sha256 (manifest `contract_source.sha256`), CI
      `Proto provenance (pinned producer)`의 pinned NodeVault commit과 vendored proto sha256
- [ ] **package checksum**: `.tar.gz.sha256` 값, 두 번 만든 package의 hash가 같음(CI `CLI package is reproducible`),
      `verify-cli-package: PASS`, 만든 dotnet SDK
- [ ] **CI run**: Verify `Build · Test` run ID·job ID·head SHA·event `push`·conclusion `success`, CodeQL run
- [ ] **case IDs + expected/results**: `check-cli-test-report: PASS total=… failed=0 errors=0 skipped=2`(skip은 실제 NodeVault
      연결 opt-in 2개 class뿐), artifact `cli-test-report`(xunit). 판정 ID와 시험의 연결은
      [`NODEKIT_CLI_ACCEPTANCE_EVIDENCE.md`](NODEKIT_CLI_ACCEPTANCE_EVIDENCE.md)
- [ ] **#109 반복**: `sha=<release SHA> runs=20`, 20/20 통과
- [ ] **artifact**: `cli-package`, `published-cli-transcript`, `cli-test-report`, `coverage`의 ID·sha256
- [ ] **후속 표시**: 실제 NodeVault 연결·Harbor 등록·Gateway·VM/Kubernetes 경로는 미실행 후속(§9)

**기준 기록 — main `327abe2f` (2026-10-20, 이 문서 추가 전 main):**

| 항목 | 값 |
|---|---|
| source SHA | `327abe2fd33b98f3b4d04f617dfed412b5112d26` |
| contract source | `protos/provenance.json` sha256 `fd3130b052c0e5c51507291f66520dadf9343f8941d5e4fa03d908226ad6faab`; vendored `protos/nodevault/v1/nodevault.proto` sha256 `88b138abb9e9b93e361cc754f6e84f20e32b742aeb4a038263617e6c87d54f11` == NodeVault@`b640f967522198199566bdfde7735c4ef052445d` |
| package checksum | `nodekit-cli-0.0.0-ci-linux-x64.tar.gz` sha256 `d0f6cbc30e787560c99e246301537e3eb9ec1ee98cacd7cb25a1adb9c01be7aa` (pkg-a == pkg-b), `verify-cli-package: PASS` |
| CI run | Verify [39431992889](https://github.com/HeaInSeo/NodeKit/actions/runs/39431992889) job 118659101603 `Build · Test`, push, success; CodeQL 39431992938 success |
| test report | `check-cli-test-report: PASS total=1179 failed=0 errors=0 skipped=2` |
| #109 | `sha=327abe2fd33b98f3b4d04f617dfed412b5112d26 runs=20` |
| artifact | `cli-test-report` 12268318117 (`sha256:ca58d9df7d86e85a0f63e8dffcb9eabb889ad1b59a1be91a57ac17e3c19f03af`), `published-cli-transcript` 12268527540 (`sha256:20f5664980fac599a4eee772ecb095ffd0395db6c17e0e9389cd1c3ec6699e9a`), `coverage` 12268362703 (`sha256:30c71e60dbba30419e402906b8a6b46a1abeccdea2b3f36205e744f43c7bf91c`). `cli-package` artifact는 이 commit 뒤에 추가됐다 |
| 후속 | 실제 NodeVault 연결 미실행 |

이 기준 기록은 checklist의 채움 예시다. release 인수는 release commit의 run으로 다시 채운 기록으로 한다.

## 9. CLI 단독 release가 주장하지 않는 것

기본 CLI test suite와 CI는 local fixture와 loopback fake server 범위다. `NODEKIT_NODEVAULT_URL`을 설정했을 때만 실제
NodeVault에 연결하는 opt-in 시험 2개 class가 있으며 기본은 skip이다. 아래는 CLI 단독 release의 완료 범위가 아니며 별도
연결 스프린트에서 인수한다.

- 실제 NodeVault 이미지 빌드·Harbor 등록, ToolFunction 두 번째 이미지의 실제 빌드와 typed 등록
- 실제 환경의 재수용·중복 효과 방지·watch 재연결·응답 유실 복구, 서버 멱등성
- gRPC Gateway, seoy·infra-lab VM·Kubernetes 경로, 인증/권한
- NodeSentinel 관찰, Console/GUI, 다른 저장소의 개발, 승인(`Approved`)
- 생성된 Dockerfile의 실제 `docker build` 검증, 다른 OS/RID·별도 기계에서의 설치
