#!/usr/bin/env bash
# NodeKit CLI 배포 package를 만든다. 지원 profile은 하나뿐이다:
# linux-x64 / self-contained / loose directory / tar.gz (Ubuntu 24.04 x86_64, glibc).
#
# 사용법: scripts/package-cli.sh --version <semver> [--output <dir>] [--allow-dirty]
#
# 산출물(<dir>, 기본 artifacts/cli):
#   nodekit-cli-<version>-linux-x64.tar.gz            최상위 디렉터리 하나를 담은 archive
#   nodekit-cli-<version>-linux-x64.tar.gz.sha256     sha256sum -c 형식
#   nodekit-cli-<version>-linux-x64.cli-manifest.json archive 안 cli-manifest.json과 같은 파일
#
# restore는 packages.lock.json에 고정된 --locked-mode로만 한다. 작업 트리가 dirty면
# 거부한다(--allow-dirty는 manifest에 source.dirty=true를 기록하며 release로 쓰지 않는다).
set -euo pipefail

RID="linux-x64"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/NodeKit.Cli/NodeKit.Cli.csproj"

usage() {
    echo "usage: $0 --version <semver> [--output <dir>] [--allow-dirty]" >&2
    exit 2
}

VERSION=""
OUTPUT="$ROOT/artifacts/cli"
ALLOW_DIRTY="false"
while [ $# -gt 0 ]; do
    case "$1" in
        --version) [ $# -ge 2 ] || usage; VERSION="$2"; shift 2 ;;
        --output) [ $# -ge 2 ] || usage; OUTPUT="$2"; shift 2 ;;
        --allow-dirty) ALLOW_DIRTY="true"; shift ;;
        -h|--help) usage ;;
        *) echo "package-cli: unknown option: $1" >&2; usage ;;
    esac
done

if ! [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
    echo "package-cli: --version must be semver (e.g. 0.9.1 or 0.9.1-rc.1), got '$VERSION'" >&2
    exit 2
fi

SOURCE_SHA="$(git -C "$ROOT" rev-parse HEAD)"
SOURCE_DIRTY="false"
if [ -n "$(git -C "$ROOT" status --porcelain)" ]; then
    if [ "$ALLOW_DIRTY" != "true" ]; then
        echo "package-cli: working tree is dirty; commit first or pass --allow-dirty" >&2
        exit 1
    fi
    SOURCE_DIRTY="true"
fi

NAME="nodekit-cli-${VERSION}-${RID}"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
PUBLISH_DIR="$STAGE/$NAME"

dotnet restore "$PROJECT" --locked-mode -r "$RID"
dotnet publish "$PROJECT" --no-restore -c Release -r "$RID" --self-contained true \
    -p:PublishSingleFile=false -p:Version="$VERSION" -p:SourceRevisionId="$SOURCE_SHA" \
    -o "$PUBLISH_DIR"

python3 "$ROOT/scripts/write-cli-manifest.py" \
    --template "$ROOT/release/cli-manifest.template.json" \
    --publish-dir "$PUBLISH_DIR" \
    --version "$VERSION" \
    --source-sha "$SOURCE_SHA" \
    --source-dirty "$SOURCE_DIRTY" \
    --contract-source "$ROOT/protos/provenance.json" \
    --dotnet-sdk "$(dotnet --version)" \
    --out "$PUBLISH_DIR/cli-manifest.json"

mkdir -p "$OUTPUT"
OUTPUT="$(cd "$OUTPUT" && pwd)"
tar --sort=name --owner=0 --group=0 --numeric-owner -C "$STAGE" -czf "$OUTPUT/$NAME.tar.gz" "$NAME"
(cd "$OUTPUT" && sha256sum "$NAME.tar.gz" > "$NAME.tar.gz.sha256")
cp "$PUBLISH_DIR/cli-manifest.json" "$OUTPUT/$NAME.cli-manifest.json"

echo "package-cli: wrote $OUTPUT/$NAME.tar.gz"
cat "$OUTPUT/$NAME.tar.gz.sha256"
