#!/bin/sh
# Removes the ax206display service and program files.
#
#   sudo ./uninstall.sh           keeps layouts, integrations and passwords
#                                 in /var/lib/ax206display for a reinstall
#   sudo ./uninstall.sh --purge   deletes those too, and the service user
set -eu

PURGE=no
[ "${1:-}" = "--purge" ] && PURGE=yes

if [ "$(id -u)" -ne 0 ]; then
    exec sudo "$0" "$@"
fi

systemctl disable --now ax206display 2>/dev/null || true
rm -f /etc/systemd/system/ax206display.service
systemctl daemon-reload

rm -f /etc/udev/rules.d/60-ax206display.rules /etc/modprobe.d/ax206display-usb-storage.conf /usr/local/bin/ax206display-passwd
command -v udevadm >/dev/null 2>&1 && udevadm control --reload-rules
rm -rf /opt/ax206display

if [ "$PURGE" = yes ]; then
    rm -rf /var/lib/ax206display
    if getent passwd ax206display >/dev/null; then
        userdel ax206display
    fi
    echo "ax206display removed, settings included."
else
    echo "ax206display removed. Settings kept in /var/lib/ax206display (use --purge to delete them)."
fi
