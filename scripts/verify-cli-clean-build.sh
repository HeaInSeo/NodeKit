#!/usr/bin/env bash
# S4-01: NodeKit CLI 단독 clean build와 dependency closure를 검증한다.
#
# 사용법: scripts/verify-cli-clean-build.sh
#
# HEAD의 추적 파일만(git archive) 빈 임시 디렉터리에 풀어 clean checkout을 만든다.
# 그 옆에는 NodeVault/DockGuard sibling이 없다. 작업 트리의 미커밋 변경은 쓰지 않는다.
#   A. CLI project만 --locked-mode restore + Release build 성공, GUI(NodeKit.csproj)는 빌드되지 않음
#   B. scripts/check-cli-closure.py: lock/runtime closure에 Avalonia/SkiaSharp/Wasmtime/KubernetesClient 없음,
#      Spectre.Console/Grpc.Net.Client/Google.Protobuf는 실제 의존성으로 기록
#   C. 벤더 proto가 없는 clean checkout(nodevault.proto만 없음 / protos/ 전체 없음)에서 Debug·Release build가
#      NKPROTO004로 실패. 옆에 NodeVault/protos를 둬도 그 경로로 fallback하지 않음
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CLI_REL="src/NodeKit.Cli/NodeKit.Cli.csproj"
PROTO_REL="protos/nodevault/v1/nodevault.proto"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

fail() {
    echo "verify-cli-clean-build: FAIL: $*" >&2
    exit 1
}

# clean checkout 하나를 $1/NodeKit에 만든다. 부모 디렉터리에는 NodeKit만 있다.
make_checkout() {
    mkdir -p "$1"
    git -C "$ROOT" archive --format=tar --prefix=NodeKit/ HEAD | tar -x -C "$1"
}

echo "== A. clean checkout: CLI-only locked restore + Release build (HEAD $(git -C "$ROOT" rev-parse HEAD))"
make_checkout "$WORK/a"
A="$WORK/a/NodeKit"
for sibling in NodeVault DockGuard; do
    [ ! -e "$WORK/a/$sibling" ] || fail "sibling $sibling exists next to the clean checkout"
done
dotnet restore "$A/$CLI_REL" --locked-mode
dotnet build "$A/$CLI_REL" --no-restore -c Release /p:TreatWarningsAsErrors=true
DEPS="$A/src/NodeKit.Cli/bin/Release/net10.0/NodeKit.Cli.deps.json"
[ -f "$DEPS" ] || fail "CLI build output has no $DEPS"
# GUI project(저장소 루트 NodeKit.csproj)는 루트 obj/bin에 쓴다. CLI build가 그것을 끌어오면 안 된다.
[ ! -e "$A/obj" ] && [ ! -e "$A/bin" ] || fail "CLI build also built the GUI project (root obj/ or bin/ exists)"

echo "== B. CLI dependency closure"
python3 "$ROOT/scripts/check-cli-closure.py" --lock "$A/src/NodeKit.Cli/packages.lock.json" --deps "$DEPS"

# $1=라벨, $2=checkout 부모 디렉터리. 벤더 proto 없는 build가 NKPROTO004로 실패하고 decoy를 쓰지 않아야 한다.
expect_missing_proto_failure() {
    local label="$1" parent="$2" config log rc
    # 외부 fallback 탐지용 decoy: 예전 기본값이던 sibling NodeVault/protos에 진짜 proto를 둔다.
    mkdir -p "$parent/NodeVault/protos/nodevault/v1"
    cp "$ROOT/$PROTO_REL" "$parent/NodeVault/$PROTO_REL"
    dotnet restore "$parent/NodeKit/$CLI_REL" --locked-mode
    for config in Debug Release; do
        log="$WORK/$label-$config.log"
        rc=0
        dotnet build "$parent/NodeKit/$CLI_REL" --no-restore -c "$config" >"$log" 2>&1 || rc=$?
        [ "$rc" -ne 0 ] || { cat "$log"; fail "$label/$config: build succeeded without the vendored proto"; }
        grep -q "NKPROTO004" "$log" || { cat "$log"; fail "$label/$config: no NKPROTO004 dependency error"; }
        ! grep -q "$parent/NodeVault" "$log" || { cat "$log"; fail "$label/$config: build referenced the sibling NodeVault path"; }
        echo "$label/$config: rc=$rc, $(grep -m1 -o 'error NKPROTO004:[^[]*' "$log")"
    done
}

echo "== C1. clean checkout without $PROTO_REL"
make_checkout "$WORK/c1"
rm "$WORK/c1/NodeKit/$PROTO_REL"
expect_missing_proto_failure c1 "$WORK/c1"

echo "== C2. clean checkout without protos/"
make_checkout "$WORK/c2"
rm -r "$WORK/c2/NodeKit/protos"
expect_missing_proto_failure c2 "$WORK/c2"

echo "verify-cli-clean-build: PASS (S4-01-A/B/C)"
