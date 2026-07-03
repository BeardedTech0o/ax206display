# AX206 USB Protocol Specification

This document describes the reverse-engineered USB protocol used to drive
AX206-based USB LCD displays (Appotech AX206 chipset), as implemented in
`Ax206Display.Protocol` and `Ax206Display.Transport`.

**No vendor SDK or NDA documentation exists for this protocol.** Everything
below is derived from reading the source of four independent, publicly
available reverse-engineering projects, cross-checked against each other:

| Reference | Language | License | Role |
|---|---|---|---|
| [dreamlayers/dpf-ax](https://github.com/dreamlayers/dpf-ax) | C | No LICENSE/COPYING file in the GitHub mirror; informal per-file copyright headers only | The most complete reverse-engineering effort; also contains firmware-replacement tooling we do not use |
| [ukoda/lcd4linux-ax206](https://github.com/ukoda/lcd4linux-ax206) | C | GPLv2 (`COPYING`) | An `lcd4linux` driver plugin talking to stock/commercial firmware |
| [wjohnsaunders/Client-DPF-AX206](https://github.com/wjohnsaunders/Client-DPF-AX206) | C++ | BSD-2-Clause | A minimal standalone client, notably documents a full-frame-blit-only quirk |
| [plumbum/go2dpf](https://github.com/plumbum/go2dpf) | Go | MIT | A third independent host implementation, useful for cross-checking |

All four independently agree on the wire format described below, which gives
high confidence in the parts marked "confirmed." Where they disagree or where
none of them exercise a documented feature, this is called out explicitly in
[§7 Known gaps and uncertainties](#7-known-gaps-and-uncertainties) - do not
treat those parts as reliable.

None of this project's code is copied from the references above; only the
observed protocol *behavior* (opcodes, byte layouts, sequencing) is
reimplemented independently in C#.

## 1. Transport layer: a private reuse of USB Mass-Storage Bulk-Only Transport

The AX206 runtime protocol is carried over raw USB bulk transfers shaped like
USB Mass Storage's Bulk-Only Transport (BOT) - a Command Block Wrapper (CBW),
an optional data phase, and a Command Status Wrapper (CSW) - but with a
**vendor-private 16-byte command block**, not a real SCSI CDB. It is driven
directly over bulk endpoints (e.g. via libusb), with the application claiming
the interface itself; it is not serviced through the OS's mass-storage/SCSI
stack in normal use.

- **Bulk OUT endpoint:** `0x01`
- **Bulk IN endpoint:** `0x81`
- **Interface:** `0` (claimed directly by the application)

These endpoint numbers are identical across all four reference
implementations (`Ax206Display.Protocol.Transport.BulkOnlyTransport`).

> **Gap:** none of the four references print or assert the interface's
> `bInterfaceClass`/`bInterfaceSubClass`/`bInterfaceProtocol`. The CBW/CSW
> shape strongly resembles Mass Storage/Bulk-Only Transport (class `0x08`,
> subclass `0x06`, protocol `0x50`), but that specific inference is not
> confirmed by any cited source - treat it as a structural inference, not a
> documented fact.

### 1.1 Command Block Wrapper (CBW) - 31 bytes, sent to the bulk OUT endpoint

| Offset | Size | Field | Value |
|---|---|---|---|
| 0 | 4 | `dCBWSignature` | `55 53 42 43` ("USBC") |
| 4 | 4 | `dCBWTag` | `DE AD BE EF`, fixed - no reference implementation increments it per request |
| 8 | 4 | `dCBWDataTransferLength` | little-endian byte count of the data phase (0 if none) |
| 12 | 1 | `bmCBWFlags` | `0x80` = data phase is IN (device→host); `0x00` = OUT or no data |
| 13 | 1 | `bCBWLUN` | `0x00` |
| 14 | 1 | `bCBWCBLength` | `0x10` (16) |
| 15 | 16 | CBWCB | the vendor command block, see §2 |

Implemented by `Ax206Display.Protocol.Transport.CommandBlockWrapper`.

### 1.2 Data phase

A plain bulk transfer of `dCBWDataTransferLength` bytes, in the direction
`bmCBWFlags` indicates, on EP `0x01` (OUT) or EP `0x81` (IN). Omitted
entirely when the length is 0 (e.g. `SetProperty`).

### 1.3 Command Status Wrapper (CSW) - 13 bytes, read from the bulk IN endpoint

Always read after the CBW and any data phase.

| Offset | Size | Field | Value |
|---|---|---|---|
| 0 | 4 | `dCSWSignature` | `55 53 42 53` ("USBS") |
| 4 | 4 | `dCSWTag` | should echo the CBW tag; only Client-DPF-AX206 checks this, the others only check the signature |
| 8 | 4 | `dCSWDataResidue` | not checked by any reference implementation |
| 12 | 1 | `bCSWStatus` | `0x00` = success |

Implemented by `Ax206Display.Protocol.Transport.CommandStatusWrapper`. This
project checks the signature and status byte, matching the majority
behavior; it does not require the tag to round-trip.

### 1.4 Timeouts

Reference implementations disagree: `dpf-ax`/`lcd4linux-ax206` use 3s
(data-out) / 5s with retries (CSW read); `Client-DPF-AX206` uses a flat 1s
for everything. A full 480x320 (or larger) RGB565 frame is a few hundred KB,
so a short timeout risks spurious failures on slow hosts/hubs.
`BulkOnlyTransport.DefaultTransferTimeout` uses a conservative 5 seconds
rather than copying any one reference's literal constant.

## 2. The 16-byte vendor command block (CDB)

Byte 0 is always `0xCD` for every display-runtime command
(`Ax206CdbSelector.VendorOpcode`). Byte 5 selects between one leaf command
and a "user command" dispatch table:

```
byte:  0     1  2  3  4  5     6         7..14           15
       0xCD  0  0  0  0  SEL  [OPCODE]  [args...]        0
```

- `SEL = 0x02` (`Ax206CdbSelector.GetLcdParameters`): a leaf command, no
  opcode byte - queries screen width/height (§3).
- `SEL = 0x06` (`Ax206CdbSelector.UserCommand`): byte 6 holds an opcode from
  the table in §4.
- `SEL = 0x03` appears in `dpf-ax`'s device probing as an undocumented
  firmware-variant check (see §7); not used by this project.

There is also a separate `0xCB`-prefixed command family in `dpf-ax` used
exclusively for raw SPI flash access (firmware dump/reflash). It is unrelated
to displaying images and is not implemented here.

## 3. GetLcdParameters - resolution auto-detection

CDB: `CD 00 00 00 00 02 00 00 00 00 00 00 00 00 00 00`, with a 5-byte data-IN
phase.

| Offset | Meaning |
|---|---|
| 0-1 | width, little-endian `uint16` |
| 2-3 | height, little-endian `uint16` |
| 4 | validity marker; Client-DPF-AX206 requires this to equal `0xFF`, the other three read it but don't check it |

**All four references query this at open time and none hardcodes a
resolution.** This project follows the same rule: `IAx206DeviceDiscovery`
implementations must never filter by a hardcoded VID/PID or assume a
resolution - a candidate USB device is only accepted as an AX206 display
after this probe returns plausible dimensions
(`LcdParametersResponse.HasPlausibleDimensions`: both dimensions in `(0,
4096]`, deliberately not tied to 480x320 or any other specific panel).

Implemented by `Ax206Display.Protocol.Commands.LcdParametersResponse` /
`Ax206CommandBuilder.GetLcdParameters()`.

## 4. User commands (`SEL = 0x06`, opcode at CDB byte 6)

From `dpf-ax/include/usbuser.h`'s `USBCMD_*` enum. "Exercised" means at least
one of the four reference *host* implementations actually calls it against
real/stock firmware, as opposed to only being declared in a header.

| Opcode | Name | Purpose | Exercised by |
|---|---|---|---|
| `0x00` | GetProperty | Mirror of SetProperty | None - declared only |
| `0x01` | **SetProperty** | Set a device property, see §5 | All four |
| `0x04` | MemRead | Read microcontroller RAM (firmware-debug) | dpf-ax only, firmware tooling |
| `0x05` | AppLoad | Load/jump to RAM code (firmware-debug) | dpf-ax only, firmware tooling |
| `0x11` | FillRect | Fill a rectangle with the current foreground color | None - declared only, wire format beyond the opcode is not demonstrated anywhere |
| `0x12` | **Blit** | Upload a rectangle of RGB565 pixels, see §6 | All four - this is the core "draw" operation |
| `0x13` | CopyRect | Screen-to-screen rectangle copy | None - declared only |
| `0x20` | FlashLock | Lock/unlock flash during reflashing | dpf-ax only, firmware tooling |
| `0xFF` | Probe | "Get version code" | Declared only; the probe path actually used is CDB byte 5, not this opcode |

Implemented by `Ax206Display.Protocol.Commands.Ax206UserCommand`. This
project only builds CDBs for `SetProperty` and `Blit` - the only two with a
demonstrated wire format against real hardware.

## 5. SetProperty (opcode `0x01`)

CDB bytes 7-8: property token, little-endian `uint16`. CDB bytes 9-10: value,
little-endian `uint16`. No data phase.

| Token | Name | Notes |
|---|---|---|
| `0x01` | **Brightness** | 0-7 (0 = min/off, 7 = max). Used by all four references - the only property this project currently exposes. |
| `0x02` | ForegroundColor | RGB565. Only `dpf-ax` calls it (`dpf_setcol`); not used by the other three. |
| `0x03` | BackgroundColor | RGB565. Declared only, no call site in any of the four repos. |
| `0x10` | Orientation | Values (per `dpf-ax` Changelog v0.41): 0=landscape, 1=portrait, 2=reverse landscape, 3=reverse portrait. **Only documented against `dpf-ax`'s own replacement firmware.** None of the three clients that talk to stock/commercial firmware ever send it - they all rotate pixels in software before blitting instead. **Do not assume hardware rotation works on a real device;** treat it as untested against stock firmware. |

Implemented by `Ax206Display.Protocol.Commands.Ax206Property` /
`Ax206CommandBuilder.SetProperty()`.

## 6. Blit (opcode `0x12`) - uploading pixels

CDB layout:

| Offset | Field |
|---|---|
| 7-8 | left (x0), little-endian `uint16` |
| 9-10 | top (y0), little-endian `uint16` |
| 11-12 | right - 1 (last column, **inclusive**), little-endian `uint16` |
| 13-14 | bottom - 1 (last row, **inclusive**), little-endian `uint16` |
| 15 | `0x00` (unused) |

Data phase (OUT): `(right-left) * (bottom-top) * 2` bytes of pixel data, row
major, no stride padding. `Ax206CommandBuilder.Blit()` takes exclusive
right/bottom bounds (like a normal .NET rectangle) and does the -1 adjustment
internally.

### 6.1 Pixel format: RGB565, **big-endian** - a critical gotcha

Every pixel is transmitted as **[high byte: `RRRRRGGG`][low byte:
`GGGBBBBB`]** - confirmed identically in `dpf-ax/dpflib/dpf.h`'s
`RGB565_0`/`RGB565_1` macros (duplicated in `lcd4linux-ax206`) and in
`go2dpf/image.go`'s `ImageRGB565.Set()`, whose own doc comment calls it "big
endian format" explicitly.

This is the **opposite** of what you get by casting a 16-bit RGB565 value to
bytes on a little-endian x86/x64/ARM64 machine (which is every platform this
app runs on), and the opposite of `SkiaSharp`'s native
`SKColorType.Rgb565` in-memory layout. **The high byte must be written
first.** `Ax206Display.Rendering.PixelFormats.FrameBufferExtractor.ToRgb565Bytes(bitmap,
swapBytes: true)` performs this swap before any pixel data reaches
`IAx206Transport.BlitAsync`.

### 6.2 Full-frame vs. partial-rectangle blits - a real hardware/firmware quirk

`lcd4linux-ax206` actively tracks a dirty rectangle and sends partial blits,
and this works on the hardware the author tested. But `Client-DPF-AX206`'s
README states plainly that partial blits crash its unit, and its
`MustBlitFullScreen()` unconditionally returns `true`. **This is a genuine,
unresolved disagreement between reference implementations** - most likely a
firmware/hardware-revision difference, not a protocol bug on either side.

**This project defaults to full-frame blits.** Partial-rectangle blitting as
a bandwidth optimization is future work and should be probed for at runtime
(e.g. try a small partial blit and watch for a failure/hang) rather than
assumed safe.

## 7. Known gaps and uncertainties

Listed honestly so nobody mistakes an inference for a confirmed fact:

1. USB interface class/subclass/protocol bytes are never printed/asserted by
   any of the four references (§1).
2. `PROPERTY_ORIENTATION` (`0x10`) is only documented against `dpf-ax`'s own
   replacement firmware, not stock/commercial firmware (§5).
3. `FillRect` (`0x11`) and `CopyRect` (`0x13`) are declared but never called
   by any reference host implementation - their wire format beyond the
   opcode byte is undemonstrated. Not implemented in this project.
4. Partial-rectangle blit reliability is contradicted across references
   (§6.2) - full-frame blit is the safe default.
5. No checksum/CRC exists anywhere in the runtime protocol (the firmware
   *flashing* path has its own unrelated Intel-HEX checksums).
6. The CBW tag is a hardcoded constant, never incremented, and only one of
   four reference clients validates the CSW tag echoes it. Whether
   incrementing it would also work is untested by any source - this project
   keeps it fixed to match the field-tested behavior.
7. No reference documents an upper bound on a single Blit transfer's size;
   all of them hand a full-rectangle buffer (up to hundreds of KB) to a
   single bulk transfer call and let the host controller fragment it. Very
   large single blits (e.g. a hypothetical bigger panel) are unverified
   territory.
8. `GetProperty` (`0x00`) response format is not documented anywhere - never
   called or answered in any of the four repos.
9. The `SEL = 0x03` "lock capability probe" mentioned in §2 has only a loose
   code-comment description (`0` = original firmware, `1` = "improved
   hack") and is irrelevant to normal display operation.
10. Confirmed resolutions across the four repos are 128x128, 160x128,
    320x240, 240x320, and 800x480. Common 3.5" panels are widely reported in
    the hobbyist community as 480x320, but that specific figure is not
    directly confirmed in any of the four cited repos' text or code. Since
    resolution is always auto-queried (§3), this is a low-risk gap.
11. Timeout values vary 5x across references (§1.4); pick a conservative
    value rather than trusting any single one.

## 8. Known VID/PID pairs (for compatibility documentation only)

| VID:PID | Description |
|---|---|
| `1908:0102` | AX206 display, normal runtime mode - the only pair any of the four references match against, including commercially sold "USB LCD monitor" panels (e.g. GEMBIRD-branded units bundled for AIDA64/HWiNFO) which ship pre-flashed with compatible firmware |
| `1908:3318` | AX206 mask-ROM bootloader mode - firmware recovery only, not a display; listed for completeness |

**This list (`Ax206Display.Protocol.Discovery.KnownUsbIdentifiers`) is
informational only.** Per this project's design requirement, device
discovery never filters USB enumeration by VID/PID - it probes every USB
device with the §3 `GetLcdParameters` command and accepts whichever ones
answer plausibly, so unlisted/rebadged clones are still found. See
`IAx206DeviceDiscovery`.

## 9. Init/handshake sequence

There is no special handshake beyond standard USB setup:

1. Open the device, claim interface `0` (see
   `Ax206Display.Transport.LibUsb.LibUsbAx206DeviceDiscovery` / `WinUsb`
   fallback).
2. Send `GetLcdParameters` (§3). This doubles as both resolution discovery
   and a liveness/compatibility check - a device that doesn't answer
   sensibly here is rejected as "not an AX206 display."
3. Optionally call `SetProperty(Brightness, ...)`.
4. Call `Blit(...)` whenever a frame needs to be pushed. There is no
   session/sequence-number requirement between calls.

No reference implementation performs a reset or "wake" command before the
first blit.
