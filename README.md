# ax206display

A Windows system-tray app that drives multiple USB AX206-based LCD screens
(the common 3.5" 480x320 "USB LCD monitor" panels and similar), each with its
own independently configured widget layout: system monitoring, images/GIFs,
a clock, weather (Open-Meteo), UniFi, and Proxmox status.

The AX206 USB protocol is not publicly documented by its vendor; this project
reverse-derives it from public reference implementations - see
[`docs/protocol-spec.md`](docs/protocol-spec.md) for the full write-up,
citations, and known gaps.

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

# Everything, including the WPF app (Windows only):
dotnet build Ax206Display.sln
```

CI (`.github/workflows/ci.yml`) mirrors this split: a Linux job builds and
tests the cross-platform projects, and a `windows-latest` job builds the full
solution including the WPF app.
