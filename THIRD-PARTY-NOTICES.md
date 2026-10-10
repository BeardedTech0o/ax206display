# Third-party notices

ax206display itself is MIT licensed (see [LICENSE](LICENSE)). It uses and, in
the release downloads, ships the components below under their own licences.
Licences marked "checked" were read from the package metadata on NuGet when
this file was written; re-check them when you update a dependency.

| Component | Licence | Where it is used |
|---|---|---|
| [LibUsbDotNet](https://github.com/LibUsbDotNet/LibUsbDotNet) 3.0.224 | LGPL-3.0-or-later (checked) | USB transport (`Ax206Display.Transport`) |
| [libusb](https://libusb.info/) 1.0.30 | LGPL-2.1-or-later | Native USB library. `libusb-1.0.dll` ships next to the Windows exe; on Linux it is installed from the distribution's `libusb-1.0-0` package |
| [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) 0.9.5 | MPL-2.0 (checked) | Windows sensor readings |
| [SkiaSharp](https://github.com/mono/SkiaSharp) 3.119.4 (and Linux native assets) | MIT (checked); bundles Google Skia (BSD-3-Clause) | Rendering |
| SkiaSharp.Views.WPF 3.119.2 | MIT | Windows designer preview |
| H.NotifyIcon.Wpf 2.3.2 | MIT | Windows tray icon |
| Microsoft.Extensions.* and the .NET runtime | MIT | Hosting, logging, HTTP |
| [Space Mono](https://github.com/googlefonts/spacemono) | SIL OFL 1.1 (`src/Ax206Display.Rendering/Fonts/SpaceMono/OFL.txt`) | Fonts drawn on the display |
| [Inter Tight](https://github.com/rsms/inter) | SIL OFL 1.1 (`src/Ax206Display.Server/wwwroot/fonts/OFL.txt`) | Web UI font |

## Notes on the LGPL components

LibUsbDotNet is LGPL-3.0-or-later and libusb is LGPL-2.1-or-later. Their
source code is available from the project links above. You can swap in a
different build of either library: on Linux, replace the system
`libusb-1.0` package; on Windows, replace `libusb-1.0.dll` next to the exe.

The Windows exe is a self-contained single-file build, so LibUsbDotNet's
managed assembly sits inside that file. If you need to substitute a modified
LibUsbDotNet, build this repository from source (see the README), which
produces an exe with your version of it.

## Protocol references

The AX206 protocol was implemented independently from observed behaviour; no
code was copied from the reverse-engineering projects listed in
[docs/protocol-spec.md](docs/protocol-spec.md).
