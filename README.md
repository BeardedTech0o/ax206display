# ax206display

A Windows system-tray app that drives multiple USB AX206-based LCD screens
(the common 3.5" 480x320 "USB LCD monitor" panels and similar), each with its
own independently configured widget layout: system monitoring, images/GIFs,
a clock, weather (Open-Meteo), UniFi, and Proxmox status.

The AX206 USB protocol is not publicly documented by its vendor; this project
reverse-derives it from public reference implementations - see
[`docs/protocol-spec.md`](docs/protocol-spec.md) for the full write-up,
citations, and known gaps.

## Download

Prebuilt Windows builds are published on the
[Releases page](https://github.com/BeardedTech0o/ax206display/releases) - no
separate .NET install required either way:

- `Ax206Display-*-win-x64.msi` - the installer (recommended): adds a Start
  Menu shortcut and an Add/Remove Programs entry, and cleanly replaces the
  previous version on your next update instead of you hand-swapping exes.
- `Ax206Display-*-win-x64.zip` - the same app as a portable exe, for running
  without installing anything. It's a zip rather than a bare exe because
  Ax206Display.exe needs its bundled libusb-1.0.dll sitting right next to
  it - keep both files together wherever you unzip it.

## Status

Currently at the **M1 scaffold** milestone: solution structure, the USB
protocol layer, a mock transport for hardware-free development/testing, and a
minimal tray app that renders a live clock widget end-to-end through the real
compositor. The widget designer, multi-device widget persistence, and the
UniFi/Proxmox/weather widgets themselves are future milestones - the
underlying data-source clients already exist and are unit tested, but aren't
yet wired into a widget UI. 

## Solution layout

| Project | TFM | Purpose |
|---|---|---|
| `Ax206Display.Protocol` | net8.0 | AX206 command/CBW/CSW byte-level protocol, no I/O |
| `Ax206Display.Transport` | net8.0 | `IAx206Transport` + a mock, a LibUsbDotNet-based transport, and a WinUSB P/Invoke fallback |
| `Ax206Display.Rendering` | net8.0 | SkiaSharp-based widget compositor and pixel-format conversion |
| `Ax206Display.DataSources` | net8.0 | System sensors (LibreHardwareMonitorLib), Open-Meteo weather, UniFi, Proxmox clients |
| `Ax206Display.Config` | net8.0 | JSON config models/service, DPAPI-backed secret store |
| `Ax206Display.App` | net8.0-windows | The WPF tray app: DI host, tray icon/menu, Task Scheduler auto-start, widget-designer window |
| `Ax206Display.Tests` | net8.0 | xUnit tests for every project above except `App` |

All USB I/O goes through the `IAx206Transport` interface so
rendering/config/data-source code is fully testable without hardware (see
`Ax206Display.Transport.Mock`). Device discovery never hardcodes a USB
VID/PID: it probes candidate devices with the protocol's own
`GetLcdParameters` command and accepts whichever respond plausibly.

## Building

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```sh
# Everything except the WPF app (works on Linux/macOS/Windows):
dotnet build Ax206Display.CrossPlatform.slnf
dotnet test Ax206Display.CrossPlatform.slnf

[![Buy Me a Coffee](https://img.buymeacoffee.com/button-api/?text=Buy%20me%20a%20coffee&emoji=&slug=nullobj&button_colour=FFDD00&font_colour=000000&font_family=Cookie&outline_colour=000000&coffee_colour=ffffff)](https://www.buymeacoffee.com/nullobj)

# Everything, including the WPF app (Windows only):
dotnet build Ax206Display.sln
```

CI (`.github/workflows/ci.yml`) mirrors this split: a Linux job builds and
tests the cross-platform projects, and a `windows-latest` job builds the full
solution including the WPF app.

## Security

TLS to UniFi/Proxmox uses certificate pinning
(`IntegrationConfig.PinnedCertificateSha256Thumbprint`), not a blanket
"accept any certificate" bypass, since both commonly serve self-signed
certs on a LAN. Secrets are DPAPI-encrypted at rest (`Ax206Display.Config.Secrets`)
and their in-memory buffers are zeroed after use. The app currently runs
elevated (`requireAdministrator`) for USB/Task Scheduler access; see
[`docs/privilege-separation.md`](docs/privilege-separation.md) for a proposed
design to shrink that to a minimal elevated broker process in a future
milestone.

UniFi login needs a **local-access-only** admin account - a cloud/SSO-linked
account can fail to authenticate through the API in confusing ways (e.g. a
generic "invalid credentials" response even with the right password) that
have nothing to do with this app. If Test & Save keeps failing, check
Settings &rarr; Admins &rarr; the account in question and confirm it's set to
local access rather than linked to a Ubiquiti cloud account. If you'd rather
keep 2FA enabled on that account instead of switching it to local-only, the
app supports that too (`IntegrationConfig.TotpSecretKey`, computed via
`Ax206Display.DataSources.Auth.TotpGenerator`) - enter the base32 setup
key/secret from when you enabled 2FA, not a live 6-digit code.

The build enforces `TreatWarningsAsErrors` with
`AnalysisLevel=latest-recommended`, pins the full dependency graph via
`packages.lock.json` + `Directory.Packages.props`, and gates CI on
`NuGetAudit`/`dotnet list package --vulnerable`.

[![Buy Me a Coffee](https://img.buymeacoffee.com/button-api/?text=Buy%20me%20a%20coffee&emoji=&slug=nullobj&button_colour=FFDD00&font_colour=000000&font_family=Cookie&outline_colour=000000&coffee_colour=ffffff)](https://www.buymeacoffee.com/nullobj)
