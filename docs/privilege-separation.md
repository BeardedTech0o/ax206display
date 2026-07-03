# Privilege separation: proposed design (not yet implemented)

**Status: deferred.** This document proposes an architecture change to address
security-audit finding F2 (the app currently runs entirely as
`requireAdministrator`). It is intentionally *not* implemented yet: the
widget designer, config UI, and integration wiring this would need to plug
into don't exist yet, and designing the IPC contract against real UI
requirements later will produce a better result than guessing at them now.
Treat this as a starting point for a dedicated future milestone, not a spec
to build against today.

## Problem

`Ax206Display.App`'s manifest requests `requireAdministrator` for the entire
process because two things need elevation:

1. USB device access (WinUSB/libusb need admin rights to open the AX206's
   raw device handle on most systems).
2. Registering/unregistering the Task Scheduler auto-start entry.

Everything else the process does - rendering, JSON parsing of config and of
three external HTTP APIs (Open-Meteo, UniFi, Proxmox), holding decrypted
credentials in memory, running the widget designer UI - does **not** need
elevation, but inherits it anyway because it's the same process. A bug
anywhere in that larger surface (a malformed API response, a compromised
dependency, a crafted USB device response) runs as full local admin.
(CWE-250, Execution with Unnecessary Privileges.)

## Goals

- The only code that ever runs elevated is USB I/O and Task Scheduler
  registration - nothing else.
- No UAC prompt in the common case: the elevated component should be a
  Windows Service (starts with the machine, no interactive elevation dialog),
  not a second `requireAdministrator` executable the user has to approve.
- The non-elevated process should be able to run and be developed/tested
  exactly as today when there's no broker available (e.g. `MockAx206Transport`
  continues to work standalone) - the split should not make the mock-first
  testing story worse.

## Non-goals (for this document)

- Multi-user / fast-user-switching support for the broker (assume one
  interactive session for now; revisit if that changes).
- Any change to the wire protocol in `docs/protocol-spec.md` - the broker
  still talks to devices exactly as `Ax206Display.Transport` does today.

## Proposed architecture

```
┌─────────────────────────────┐        named pipe        ┌──────────────────────────────┐
│  Ax206Display.App            │  <-------------------->  │  Ax206Display.Broker          │
│  (per-user, NOT elevated)    │                           │  (Windows Service, elevated)  │
│                               │                           │                                │
│  - WPF tray + designer UI     │                           │  - Ax206Display.Transport      │
│  - Ax206Display.Rendering     │                           │    (LibUsb / WinUSB)           │
│    (composites frames)        │                           │  - AutoStartService            │
│  - Ax206Display.Config        │                           │    (Task Scheduler reg/unreg)  │
│    (config + secrets, DPAPI   │                           │                                │
│    CurrentUser-scoped)        │                           │                                │
│  - Ax206Display.DataSources   │                           │                                │
│    (weather/UniFi/Proxmox)    │                           │                                │
└─────────────────────────────┘                           └──────────────────────────────┘
```

`Ax206Display.Transport`'s existing `IAx206Transport` abstraction is exactly
the seam this split needs: the Broker hosts the real implementations
(`LibUsbAx206Transport`, `WinUsbAx206Transport`), and the App talks to a new
`RemoteAx206Transport : IAx206Transport` that forwards every call over the
pipe. No rendering/config/data-source code changes at all, because none of
it depends on `IAx206Transport` being local.

## IPC contract sketch

- **Transport:** a named pipe (`\\.\pipe\Ax206Display.Broker`), created by
  the Broker with an explicit ACL granting connect access only to the
  interactive user's SID (not `Everyone`/`Authenticated Users`) - this is the
  IPC-layer equivalent of F8's directory ACL hardening.
- **Framing:** length-prefixed messages, `System.Text.Json`-serialized, one
  request/response pair per call - this mirrors the request/response shape
  `IAx206Transport` already has (`GetLcdParametersAsync`, `SetPropertyAsync`,
  `BlitAsync`), so the contract is close to a direct RPC pass-through:
  ```csharp
  // App -> Broker
  record DiscoverDevicesRequest;
  record BlitRequest(string DeviceId, ushort Left, ushort Top, ushort Right, ushort Bottom, byte[] PixelsBigEndianRgb565);
  record SetPropertyRequest(string DeviceId, Ax206Property Property, ushort Value);
  record RegisterAutoStartRequest(bool Enabled);

  // Broker -> App
  record DeviceDescriptor(string DeviceId, ushort Width, ushort Height);
  record OperationResult(bool Success, string? Error);
  ```
- **What crosses the boundary:** device discovery results and composed pixel
  buffers/property values going one way, operation results the other. No
  credentials, no config, no rendering ever needs to cross into the Broker -
  it only ever sees pixel bytes and protocol-level values, which keeps its
  attack surface limited to exactly what it has today (a USB device's
  responses) plus a small, fully-typed IPC contract instead of arbitrary
  code.
- **Backpressure/timeouts:** the Broker should reject (not queue
  indefinitely) if the App is producing frames faster than USB can drain
  them, matching `BulkOnlyTransport.DefaultTransferTimeout`'s existing
  conservative-timeout philosophy.

## Deployment implications

- The Broker installs as a Windows Service (`sc create` / a proper MSI
  service entry), running as `LocalSystem` or a dedicated low-rights service
  account with just the device-access rights it needs - not as an
  interactively-elevated app the user has to click "Yes" for.
- `Ax206Display.App`'s Task Scheduler entry (registered by the Broker on the
  App's behalf, since only the Broker is elevated) becomes a normal, **non**-
  elevated ONLOGON task - a nice side effect is the user no longer sees a UAC
  prompt at every logon, only once at install time when the service itself is
  installed.
- `app.manifest`'s `requestedExecutionLevel` drops from
  `requireAdministrator` to `asInvoker`.

## Threat model improvement

| Today | After the split |
|---|---|
| Malformed UniFi/Proxmox/Open-Meteo response is parsed by an admin process | Parsed by a non-elevated process; worst case is a non-elevated compromise |
| A vulnerable NuGet dependency anywhere in the App runs as admin | Only a vulnerable dependency *inside the Broker* (much smaller: just `LibUsbDotNet` + BCL) runs elevated |
| Elevation boundary = the whole app | Elevation boundary = one small, typed IPC contract + USB I/O |

## Open questions for the future milestone that implements this

1. Pipe framing library: hand-rolled length-prefixed JSON (simple, no new
   dependency) vs. gRPC/named-pipe transport (more tooling, more surface).
   Leaning toward hand-rolled given the contract is small and fixed.
2. Service installer: WiX/MSI vs. a simple `sc.exe`-based first-run
   installer step. Affects how `AutoStartService` moves into the Broker.
3. Should the Broker also own `ISystemMonitorSource`
   (LibreHardwareMonitorLib) since some sensors are also elevation-sensitive
   on Windows? Out of scope for the *first* cut of this split - revisit if
   it turns out sensor reads silently degrade without elevation.
4. Versioning the IPC contract so a Broker service update doesn't require
   the App to be closed/reopened mid-session.
