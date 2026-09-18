# Running the daemon on Linux (Proxmox VM)

`Ax206Display.Daemon` (`src/Ax206Display.Daemon/`) is a headless Linux port
of the app: the same display-manager and integration-pump logic as the WPF
tray app, with a systemd service standing in for the tray icon/Task
Scheduler auto-start. It's built and tested against **Debian 13 (Trixie)**,
running as a VM guest with the AX206 panel's USB device passed through from
the Proxmox host - the setup below assumes that.

It also serves a small web UI (on port 8080) with a live preview of each
display's current layout and config export/import - see
[Web UI](#web-ui) below. Widget layouts themselves (add/move/resize/delete)
are still edited by hand in `config.json` for now (see
`AppConfig`/`DeviceProfileConfig` in `Ax206Display.Config.Models`) - an
editable designer is the next milestone.

## 1. Pass the USB display through to the VM

The panel is a physical USB device on the Proxmox **host**; the VM only sees
it once you've passed it through. In the Proxmox web UI: select the VM ->
**Hardware** -> **Add** -> **USB Device**. Prefer **Use USB Vendor/Device
ID** (`1908:0102` - see `docs/protocol-spec.md` §8) over **Use USB Port**:
the vendor/device ID keeps matching the panel across host reboots and port
changes, where a port-based mapping breaks if you ever move it to a
different physical port. If you're running Proxmox 8's Resource Mappings
instead (**Datacenter** -> **Resource Mappings** -> **USB Devices**), map it
there once and reuse the mapping across VMs/reinstalls.

## 2. Prepare the Debian 13 VM

```sh
sudo apt update
sudo apt install -y libusb-1.0-0
```

That's the only runtime dependency - the daemon publishes as a
self-contained single-file binary (below), so no separate .NET install is
needed on the VM.

## 3. Build and publish

From a machine with the **.NET 10 SDK** (the build machine doesn't have to
be the VM itself - cross-compiling `-r linux-x64` works from any OS):

```sh
./packaging/linux/publish.sh
```

This runs `dotnet publish` against `src/Ax206Display.Daemon/` (which pins
its own `global.json` to the .NET 10 SDK, independent of the repo root's
.NET 8 pin used by the rest of the solution) and writes a self-contained
`linux-x64` build to `packaging/linux/publish/`. Copy that directory to the
VM (`scp -r packaging/linux/publish debian-vm:/tmp/ax206-publish`).

## 4. Install as a systemd service

On the VM, as root:

```sh
sudo ./packaging/linux/install.sh /tmp/ax206-publish
```

This is idempotent - safe to re-run after publishing an updated build. It:

- Creates the `ax206display` system user/group (no login shell, no home
  directory - it only needs USB access, granted via the udev rule below).
- Copies the published build to `/opt/ax206display/`.
- Installs `packaging/linux/99-ax206display.rules` to
  `/etc/udev/rules.d/`, granting the `ax206display` group read/write access
  to the panel's USB device (VID:PID `1908:0102`/`1908:3318` - see the rule
  file's comments if you have a clone reporting a different ID).
- Installs `packaging/linux/ax206display-daemon.service` to
  `/etc/systemd/system/`, enables it, and starts it.

The service uses systemd's `StateDirectory=`/`ConfigurationDirectory=` to
own `/var/lib/ax206display` (secrets + the key that protects them) and
`/etc/ax206display` (`config.json`) - no config exists on first boot, and
the daemon auto-provisions a default full-screen-clock layout for whatever
display it finds, same as the Windows app on first run.

## 5. Verify

```sh
systemctl status ax206display-daemon
journalctl -u ax206display-daemon -f
```

You should see a log line for each display found
(`Auto-provisioned a default layout for ...`) and the clock/CPU/RAM layout
appear on the panel within a few seconds. If it logs `No AX206 displays
found`, double check the USB passthrough in step 1 - from inside the VM,
`lsusb` should list a `1908:0102` device.

## Web UI

Open `http://<vm-address>:8080` in a browser. It shows a card per connected
display with a live preview (refreshed every 2 seconds, rendered
server-side with the exact same compositor/widgets the physical panel
uses - what you see in the browser is what's on the panel), plus two
buttons:

- **Export Config** - downloads a `.zip` containing `config.json` and every
  device's background image, if any.
- **Import Config** - upload a `.zip` from either this daemon or the
  Windows app (see below) to adopt its devices/integrations. Matching
  device/integration `Id`s are replaced; anything else is added alongside
  what's already configured, not wiped.

If port 8080 isn't reachable, check the VM's firewall (`ufw`/`nftables`) -
the systemd unit itself doesn't restrict which interfaces it listens on.

### Migrating from the Windows app

The Windows tray app has matching **Export Config...**/**Import Config...**
tray menu items using the same `.zip` format, so moving your layouts over is
a straight export-on-Windows, import-on-Linux (or the reverse):

1. On Windows: tray icon -> **Export Config...** -> save the `.zip`.
2. Copy it to wherever you're browsing the Linux daemon's web UI from.
3. Open `http://<vm-address>:8080`, use **Import Config**, pick the file.

**Integration passwords/API tokens don't travel in the package** - they're
encrypted with a key tied to the machine that created them (DPAPI on
Windows, the AES key file described below on Linux), which can't be
recreated on the other machine. Both the export and import steps say this
in the UI; re-enter passwords for any imported integrations afterward. USB
device `Id`s (serial-number-based) are the same regardless of which OS the
panel is plugged into, so an imported device profile matches up with the
physical panel automatically once you reconnect it.

## Updating

Re-run `publish.sh`, copy the new output over, and re-run `install.sh` -
it'll overwrite `/opt/ax206display/` and restart the service (systemd
restarts it as part of `systemctl enable --now` re-running against an
already-enabled unit... to force a restart on a version bump specifically,
run `systemctl restart ax206display-daemon` after `install.sh`).

## Config and secrets locations

Unlike the Windows app (which uses per-machine `%ProgramData%`), the daemon
follows the FHS:

| What | Path | Overridable via |
|---|---|---|
| `config.json` | `/etc/ax206display/config.json` | `AX206DISPLAY_CONFIG_PATH` |
| Encrypted secrets | `/var/lib/ax206display/secrets.dat` | `AX206DISPLAY_SECRETS_PATH` |
| Secret encryption key | `/var/lib/ax206display/protector.key` | `AX206DISPLAY_SECRET_KEY_PATH` |

The encryption key is Linux's answer to Windows DPAPI: an AES-256 key
generated on first run and locked to owner-only (`0600`) file permissions -
see `Ax206Display.Daemon.Secrets.LinuxFileSecretProtector`. Anyone who can
read that key file and `secrets.dat` can decrypt the stored
integration passwords, so keep both under `/var/lib/ax206display` with
their default permissions rather than relocating them somewhere
world-readable.

## Known gaps vs. the Windows app

- **CPU temperature** reads as unavailable (`--`) on most Proxmox VMs:
  `LibreHardwareMonitorLib` (the Windows app's sensor library) ships no
  Linux runtime assets at all in the pinned version, so the daemon reads
  `/sys/class/thermal` directly instead - and KVM guests typically don't
  expose a thermal zone to begin with. CPU load and memory usage (from
  `/proc/stat`/`/proc/meminfo`) work normally.
- **GPU stats** are always unavailable - there's no dependency-free way to
  read them for an arbitrary GPU vendor on Linux; left as a future
  milestone if there's demand.
- **No editable widget designer yet** - the web UI (see above) shows a live
  preview and handles config export/import, but adding/moving/resizing
  widgets is still done by hand-editing `/etc/ax206display/config.json`
  (the daemon polls it every few seconds and hot-reloads changes, same as
  the Windows app). An editable designer is the next milestone.
