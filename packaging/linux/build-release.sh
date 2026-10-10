#!/bin/sh
# Builds a self-contained Linux release tarball: no .NET install needed on
# the target. Runs on any machine with the .NET 10 SDK (cross-publishing for
# ARM from x64 works fine).
#
#   packaging/linux/build-release.sh linux-arm64 [version] [output-dir]
#
# RIDs: linux-arm64 (64-bit Raspberry Pi OS), linux-arm (32-bit), linux-x64.
set -eu

RID=${1:?usage: build-release.sh <rid> [version] [output-dir]}
VERSION=${2:-0.0.0-dev}
OUT=${3:-artifacts}

ROOT=$(cd "$(dirname "$0")/../.." && pwd)
case "$OUT" in
    /*) ;;
    *) OUT="$ROOT/$OUT" ;;
esac
NAME="ax206display-$VERSION-$RID"
STAGE="$OUT/$NAME"

rm -rf "$STAGE"
mkdir -p "$STAGE/app" "$STAGE/system"

# A RID-specific publish rewrites every library's packages.lock.json with a
# section for that RID (and the release version), which CI's locked-mode
# restore then rejects. Put the committed files back however this exits.
LOCKS=$(mktemp -d)
(cd "$ROOT" && find src tests -name packages.lock.json -print0 | xargs -0 tar -cf "$LOCKS/locks.tar")
trap 'tar -C "$ROOT" -xf "$LOCKS/locks.tar"; rm -rf "$LOCKS"' EXIT

dotnet publish "$ROOT/src/Ax206Display.Server/Ax206Display.Server.csproj" \
    --configuration Release \
    --runtime "$RID" \
    --self-contained true \
    -p:Version="${VERSION#v}" \
    --output "$STAGE/app"

echo "$RID" > "$STAGE/app/RID"

cp "$ROOT/packaging/linux/ax206display.service" \
   "$ROOT/packaging/linux/60-ax206display.rules" \
   "$ROOT/packaging/linux/ax206display-usb-storage.conf" \
   "$ROOT/packaging/linux/ax206display-passwd" \
   "$STAGE/system/"
cp "$ROOT/packaging/linux/install.sh" "$ROOT/packaging/linux/uninstall.sh" "$STAGE/"
cp "$ROOT/LICENSE" "$ROOT/THIRD-PARTY-NOTICES.md" "$STAGE/"
chmod 755 "$STAGE/install.sh" "$STAGE/uninstall.sh" "$STAGE/system/ax206display-passwd"

tar -C "$OUT" -czf "$OUT/$NAME.tar.gz" "$NAME"
(cd "$OUT" && sha256sum "$NAME.tar.gz" > "$NAME.tar.gz.sha256")
echo "Built $OUT/$NAME.tar.gz"
