#!/usr/bin/env bash
# Publishes the Linux daemon as a single self-contained executable (bundles
# the .NET 10 runtime - the target VM needs nothing but libusb-1.0-0
# installed, no separate .NET install). Run from anywhere; output defaults
# to packaging/linux/publish/ next to this script.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DAEMON_DIR="$SCRIPT_DIR/../../src/Ax206Display.Daemon"
OUTPUT_DIR="${1:-$SCRIPT_DIR/publish}"

cd "$DAEMON_DIR"
dotnet publish Ax206Display.Daemon.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o "$OUTPUT_DIR"

echo "Published to $OUTPUT_DIR"
