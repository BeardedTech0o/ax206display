# Running on a Raspberry Pi (or any Linux box)

On Linux, ax206display runs as a background service with a web page for
managing your screens. There's no tray icon and no desktop needed, so
Raspberry Pi OS Lite works fine. It drives the same panels, uses the same
layout format and shows the same widgets and integrations as the Windows app.

## What you need

- A Raspberry Pi 3, 4 or 5 (or Zero 2 W) on Raspberry Pi OS Bookworm or
  newer, 64-bit or 32-bit. Other Debian-based distros on arm64, armhf or x64
  work too.
- The Pi Zero, Zero W and Pi 1 won't work. They're ARMv6, and .NET 8 doesn't
  run there.
- One or more AX206 USB panels. A powered hub helps if you run several off a
  Pi, since the backlights draw real current.

## Install

Grab the tarball for your OS from the
[Releases page](https://github.com/BeardedTech0o/ax206display/releases).
Run `dpkg --print-architecture` if you're not sure which one: `arm64` means
`linux-arm64`, `armhf` means `linux-arm`.

```sh
tar xzf ax206display-v*-linux-arm64.tar.gz
cd ax206display-v*-linux-arm64
sudo ./install.sh
```

The installer:

- installs `libusb-1.0-0`, `libfontconfig1` and a font (Pi OS Lite ships
  almost none)
- creates an `ax206display` system user and puts the program in
  `/opt/ax206display`
- adds a udev rule so that user can open the panels, and tells the kernel's
  USB storage driver to leave them alone (the firmware pretends to be a disk)
- installs and starts the `ax206display` systemd service

At the end it prints the web address and a one-time password. Open
`http://<your-pi>:8206`, sign in, then change the password under
**Settings**.

Upgrading is the same command with the newer tarball. Settings are kept.

## Using the web UI

**Displays** lists every panel the Pi has seen. A green dot means it's
connected and running. Pick one and you get the layout editor:

- Add widgets from the toolbar: clock, text, system stat, gauge.
- Drag to move, drag the corner handle to resize. Arrow keys nudge by 1 px,
  Shift+arrow by 10. Edges snap to the screen and to other widgets; hold Alt
  to place freely.
- The preview is rendered by the Pi with the same code that drives the panel,
  colour banding included, so what you see is what you get.
- Nothing reaches the panel until you press **Save to display**. It updates
  within about three seconds.
- Brightness, frame rate and the background image live under **Display** on
  the right. Uploaded backgrounds are scaled to the panel's resolution.

Plugged in a new panel? Press **Scan USB**. It gets a default clock-and-stats
layout straight away.

**On your phone or desktop.** The page works as an installable app, with its own
icon and no browser bars. On an iPhone, open it in Safari and use **Share, Add to
Home Screen**. Chrome only offers a proper install over HTTPS, so on a plain
`http://` address it usually adds a shortcut instead. Put the page behind a
reverse proxy with HTTPS (see Security notes) if you want the full install.

**Integrations** sets up Pi-hole, Proxmox and UniFi. Each one is tested
against the live service before it's saved. For a self-signed HTTPS
certificate, press **Detect**, check the fingerprint matches what your server
shows, and accept it.

## Moving over from Windows

Your layouts can come with you. Copy `C:\ProgramData\Ax206Display\config.json`
to the Pi, then:

```sh
sudo ./install.sh --import config.json
```

Panels are matched by USB serial number, so each one picks up its old layout.
Two things don't carry over:

- **Integration passwords.** Windows encrypts them with a key tied to that PC.
  Re-enter them under Integrations.
- **Background images.** The config points at Windows paths. Upload them again
  in the web UI.

## Day to day

| Task | Command |
|---|---|
| Live logs | `journalctl -u ax206display -f` |
| Restart | `sudo systemctl restart ax206display` |
| Forgot the web password | `sudo ax206display-passwd` |
| Change the port | Put `ASPNETCORE_URLS=http://0.0.0.0:9000` in `/etc/default/ax206display`, then restart |
| Uninstall, keep settings | `sudo ./uninstall.sh` |
| Uninstall everything | `sudo ./uninstall.sh --purge` |

Everything the service stores is in `/var/lib/ax206display`: `config.json`
(layouts and integration settings), `secrets.dat` plus `secret.key`
(integration passwords), the web login, and uploaded backgrounds. Back up the
folder and you've backed up the install.

## Security notes

- The web UI speaks plain HTTP on port 8206 and is reachable from your LAN.
  Don't forward that port to the internet. For HTTPS, put it behind a
  reverse proxy (Caddy, nginx) and bind the service to localhost with
  `ASPNETCORE_URLS=http://127.0.0.1:8206`. Also add
  `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` to `/etc/default/ax206display`,
  so the service trusts the proxy's `X-Forwarded-For` and `X-Forwarded-Proto`
  headers (from localhost only). Without it, every visitor looks like the
  proxy's address, so the sign-in limit below is shared by everyone, and the
  login cookie isn't marked Secure even though the browser is on HTTPS.
  Without a proxy, your password and session cookie cross the network in
  clear text, so only use the plain-HTTP address on a network you trust.
- Sign-in is a single password, stored as a salted PBKDF2 hash. Each address
  gets ten sign-in attempts per five minutes. Changing the password signs out
  every other browser. Signing out only clears that browser's cookie; the
  cookie itself stays valid until it expires (30 days, sliding) or the
  password changes, so change the password if a browser or device is lost.
- Integration passwords are encrypted with AES-256-GCM. The key sits in its
  own file readable only by the service account. That stops someone who
  copies `secrets.dat` alone, but not someone with root on the Pi. That's the
  same trade any headless service makes.
- The service runs as its own unprivileged user under systemd sandboxing
  (read-only system, no home directory access, no capabilities). USB access
  comes from the udev rule, not from root.

## Troubleshooting

**No displays found.** Check the panel shows up with `lsusb | grep 1908`. If
it does, look at `journalctl -u ax206display -n 50`. A line about libusb
means `sudo apt install libusb-1.0-0` and a restart. If your panel uses a
different USB ID, add a matching line to
`/etc/udev/rules.d/60-ax206display.rules`, run
`sudo udevadm control --reload-rules`, and replug it.

**The panel shows garbage or goes blank after a while.** Usually power.
Try a powered hub, or lower the brightness.

**Text looks different from Windows.** Fonts like Segoe UI and Consolas don't
exist on Linux, so they fall back to DejaVu Sans. Space Mono is bundled and
looks identical everywhere.

**Screens drop out and reconnect every few minutes.** Check `vcgencmd get_throttled`
first: anything other than `0x0` means the Pi's power supply is too weak, and
that needs fixing before anything else. If it's `0x0`, look at the Pi's own log
with `sudo dmesg | grep -i usb | tail -40`. Screens that vanish together
(`USB disconnect` on several ports in the same instant) point to the shared USB
bus rather than one bad cable. The service sends a screen new pixels only when
the picture changes, and one screen at a time, so a clock showing seconds
(`HH:mm:ss`) sends far more data than one showing `HH:mm`.

**Trying the UI without a panel.** Run the binary with `AX206_DEMO=1` and it
pretends two panels (480×320 and 320×480) are plugged in.

## Building it yourself

You need the .NET 8 SDK on any machine (it doesn't have to be the Pi):

```sh
packaging/linux/build-release.sh linux-arm64 1.0.0
```

That writes `artifacts/ax206display-1.0.0-linux-arm64.tar.gz`, the same
tarball the release pipeline publishes. Copy it to the Pi and install as
above.
