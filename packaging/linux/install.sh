#!/usr/bin/env bash
# Installs a published build of the daemon as a systemd service: creates the
# ax206display system user/group (the same group the udev rule grants USB
# access to), copies the published output to /opt/ax206display, installs the
# udev rule and systemd unit from this directory, then enables and starts
# the service. Safe to re-run (e.g. after publishing a new build) - every
# step is idempotent.
set -euo pipefail

if [[ $EUID -ne 0 ]]; then
  echo "Run as root (sudo $0 <path-to-published-output>)." >&2
  exit 1
fi

PUBLISH_DIR="${1:?Usage: $0 <path-to-published-output>}"
INSTALL_DIR=/opt/ax206display
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [[ ! -x "$PUBLISH_DIR/ax206display-daemon" ]]; then
  echo "No ax206display-daemon executable found under $PUBLISH_DIR - run publish.sh first." >&2
  exit 1
fi

if ! getent group ax206display >/dev/null; then
  groupadd --system ax206display
fi
if ! id ax206display >/dev/null 2>&1; then
  useradd --system --gid ax206display --home-dir /var/lib/ax206display \
    --no-create-home --shell /usr/sbin/nologin ax206display
fi

mkdir -p "$INSTALL_DIR"
cp -a "$PUBLISH_DIR"/. "$INSTALL_DIR"/
chown -R root:root "$INSTALL_DIR"
chmod 755 "$INSTALL_DIR/ax206display-daemon"

install -m 0644 "$SCRIPT_DIR/99-ax206display.rules" /etc/udev/rules.d/99-ax206display.rules
udevadm control --reload-rules
udevadm trigger

install -m 0644 "$SCRIPT_DIR/ax206display-daemon.service" /etc/systemd/system/ax206display-daemon.service
systemctl daemon-reload
systemctl enable --now ax206display-daemon.service

echo "Installed and started. Check status with: systemctl status ax206display-daemon"
echo "Logs: journalctl -u ax206display-daemon -f"
