#!/bin/sh
# Installs (or upgrades) ax206display as a systemd service. Run from the
# unpacked release folder:
#
#   sudo ./install.sh                         fresh install or upgrade
#   sudo ./install.sh --import config.json    also bring over a layout file,
#                                             e.g. from the Windows app
#
# Safe to re-run: upgrades replace the program files and keep every setting.
set -eu

HERE=$(cd "$(dirname "$0")" && pwd)
APP_DIR=/opt/ax206display
DATA_DIR=/var/lib/ax206display
SERVICE_USER=ax206display
PORT=8206
IMPORT_CONFIG=""

while [ $# -gt 0 ]; do
    case "$1" in
        --import)
            [ $# -ge 2 ] || { echo "--import needs a path to config.json" >&2; exit 2; }
            IMPORT_CONFIG=$(cd "$(dirname "$2")" && pwd)/$(basename "$2")
            shift 2
            ;;
        -h|--help)
            sed -n '2,10p' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *)
            echo "Unknown option: $1" >&2
            exit 2
            ;;
    esac
done

if [ "$(id -u)" -ne 0 ]; then
    exec sudo "$0" "$@"
fi

say() { printf '\n==> %s\n' "$*"; }

# --- Is this build the right one for this machine? ------------------------
# dpkg's architecture is the userland's, which is what matters: 32-bit
# Raspberry Pi OS often runs on a 64-bit kernel, so uname alone would lie.
if command -v dpkg >/dev/null 2>&1; then
    case "$(dpkg --print-architecture)" in
        arm64) MACHINE_RID=linux-arm64 ;;
        armhf) MACHINE_RID=linux-arm ;;
        amd64) MACHINE_RID=linux-x64 ;;
        armel) MACHINE_RID=unsupported ;;
        *) MACHINE_RID=unknown ;;
    esac
else
    case "$(uname -m)" in
        aarch64|arm64) MACHINE_RID=linux-arm64 ;;
        armv7l|armv8l) MACHINE_RID=linux-arm ;;
        x86_64) MACHINE_RID=linux-x64 ;;
        armv6l) MACHINE_RID=unsupported ;;
        *) MACHINE_RID=unknown ;;
    esac
fi

BUILD_RID=$(cat "$HERE/app/RID" 2>/dev/null || echo unknown)

if [ "$MACHINE_RID" = unsupported ]; then
    echo "This is an ARMv6 board (Pi Zero / Pi 1). .NET 10 doesn't run there, sorry." >&2
    exit 1
fi
if [ "$MACHINE_RID" != unknown ] && [ "$MACHINE_RID" != "$BUILD_RID" ]; then
    echo "This download is for $BUILD_RID but this machine needs $MACHINE_RID." >&2
    echo "Grab the ax206display-*-$MACHINE_RID.tar.gz release instead." >&2
    exit 1
fi

# --- Dependencies -----------------------------------------------------------
say "Installing dependencies"
if command -v apt-get >/dev/null 2>&1; then
    # libusb talks to the panels; fontconfig + a font let SkiaSharp draw text
    # (Raspberry Pi OS Lite ships almost no fonts).
    PACKAGES="libusb-1.0-0 libfontconfig1 fonts-dejavu-core ca-certificates"
    # shellcheck disable=SC2086
    apt-get install -y --no-install-recommends $PACKAGES \
        || { apt-get update && apt-get install -y --no-install-recommends $PACKAGES; }
else
    echo "No apt-get here. Install libusb-1.0, fontconfig and a TrueType font (e.g. DejaVu) with your package manager."
fi

# --- Service account --------------------------------------------------------
if ! getent passwd "$SERVICE_USER" >/dev/null; then
    say "Creating the $SERVICE_USER system user"
    useradd --system --user-group --no-create-home --home-dir "$DATA_DIR" --shell /usr/sbin/nologin "$SERVICE_USER"
fi

# --- Program files ----------------------------------------------------------
say "Installing to $APP_DIR"
if systemctl is-active --quiet ax206display 2>/dev/null; then
    systemctl stop ax206display
fi
rm -rf "$APP_DIR.new"
cp -a "$HERE/app" "$APP_DIR.new"
chown -R root:root "$APP_DIR.new"
find "$APP_DIR.new" -type d -exec chmod 755 {} +
find "$APP_DIR.new" -type f -exec chmod 644 {} +
chmod 755 "$APP_DIR.new/ax206display"
if [ -d "$APP_DIR" ]; then
    rm -rf "$APP_DIR.old"
    mv "$APP_DIR" "$APP_DIR.old"
fi
mv "$APP_DIR.new" "$APP_DIR"
rm -rf "$APP_DIR.old"

install -d -m 700 -o "$SERVICE_USER" -g "$SERVICE_USER" "$DATA_DIR"

if [ -n "$IMPORT_CONFIG" ]; then
    say "Importing $IMPORT_CONFIG"
    if [ -f "$DATA_DIR/config.json" ]; then
        cp -a "$DATA_DIR/config.json" "$DATA_DIR/config.json.before-import"
        echo "Previous config kept as $DATA_DIR/config.json.before-import"
    fi
    install -m 600 -o "$SERVICE_USER" -g "$SERVICE_USER" "$IMPORT_CONFIG" "$DATA_DIR/config.json"
    echo "Layouts come across as-is. Integration passwords don't (Windows encrypted them"
    echo "to that PC), so re-enter them under Integrations in the web UI."
fi

# --- USB access -------------------------------------------------------------
say "Setting up USB access"
install -m 644 "$HERE/system/60-ax206display.rules" /etc/udev/rules.d/60-ax206display.rules
install -m 644 "$HERE/system/ax206display-usb-storage.conf" /etc/modprobe.d/ax206display-usb-storage.conf

# The modprobe option only applies when usb-storage next loads; set it on the
# running kernel too. The parameter holds the whole list, so append to it.
QUIRKS=/sys/module/usb_storage/parameters/quirks
if [ -w "$QUIRKS" ] && ! grep -q '1908:0102' "$QUIRKS"; then
    CURRENT=$(cat "$QUIRKS")
    if [ -n "$CURRENT" ]; then
        echo "$CURRENT,1908:0102:i" > "$QUIRKS"
    else
        echo "1908:0102:i" > "$QUIRKS"
    fi
fi

if command -v udevadm >/dev/null 2>&1; then
    udevadm control --reload-rules
    udevadm trigger --subsystem-match=usb --attr-match=idVendor=1908 || true
fi

install -m 755 "$HERE/system/ax206display-passwd" /usr/local/bin/ax206display-passwd

# --- Service ----------------------------------------------------------------
say "Starting the service"
install -m 644 "$HERE/system/ax206display.service" /etc/systemd/system/ax206display.service
systemctl daemon-reload
systemctl enable ax206display >/dev/null
systemctl restart ax206display

# Wait for the first start to write its one-time password.
i=0
while [ $i -lt 30 ] && ! [ -f "$DATA_DIR/web-password.json" ]; do
    sleep 1
    i=$((i + 1))
done

if ! systemctl is-active --quiet ax206display; then
    echo
    echo "The service didn't stay up. See why with: journalctl -u ax206display -n 50"
    exit 1
fi

echo
echo "ax206display is running."
for ip in $(hostname -I 2>/dev/null); do
    case "$ip" in
        *:*) ;;
        *) echo "  Web UI: http://$ip:$PORT" ;;
    esac
done
echo "  Web UI: http://$(hostname).local:$PORT"
if [ -f "$DATA_DIR/initial-password.txt" ]; then
    echo "  First-time password: $(cat "$DATA_DIR/initial-password.txt")"
    echo "  (Change it under Settings. Forgot it later? sudo ax206display-passwd)"
fi
echo "  Logs: journalctl -u ax206display -f"
