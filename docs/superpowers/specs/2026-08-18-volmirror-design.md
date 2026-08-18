# VolMirror — Design

- **Date:** 2026-08-18
- **Status:** Draft for review
- **Working name:** VolMirror (changeable)

## Problem

The Behringer UCA202 (TI PCM2902, 16-bit) is connected by TOSLINK optical into
Argon Audio FENRIS A4 active speakers. The Windows master volume slider is
effectively dead for this device: the PCM2902 declares a USB Feature Unit, so
Windows delegates volume to hardware and does *not* substitute a software volume
APO — but the chip's attenuator sits only on its analog DAC path, and the S/PDIF
encoder is not downstream of it. The slider moves, the value is stored, the
audio never changes. (The chip also stalls master-channel volume requests
outright; only per-channel L/R are honored.)

Per-application volume still works because session volume (`ISimpleAudioVolume`)
is applied in software in the audio engine's 32-bit float mixer, upstream of the
USB stream — so it reaches the optical output. That asymmetry is the whole basis
of the fix.

### Two facts established by measurement

Both gates were validated on the actual machine before this design was written:

1. **The signal exists.** Windows stores the endpoint volume for the UCA202 even
   though the device ignores it. A polling probe recorded 65 distinct changes
   while dragging the slider, pressing media keys, and toggling mute.
   `GetMasterVolumeLevelScalar` reads back exactly what was set. No spontaneous
   jump to 1.000 was observed (Microsoft's `AudioEndpointBuilder` 100%-bug did
   not manifest here).
2. **Software attenuation reaches TOSLINK.** With Equalizer APO installed on the
   UCA202 endpoint, `Preamp: -20 dB` produced an audible drop over the optical
   link. Because EQ APO attenuates the float samples inside the audio engine,
   the attenuated PCM is what reaches the chip's S/PDIF encoder.

The fix is therefore: **mirror the (stored but inert) Windows endpoint volume
onto an Equalizer APO preamp gain**, which the audio engine actually applies.

## Goals

- Make the native Windows volume control — slider, media keys, and mute — audibly
  work for the UCA202 over TOSLINK.
- No parallel/extra control surface. The user keeps using Windows as normal.
- Run quietly in the background, start with Windows, survive reboots and
  device hot-plug.
- Preserve any EQ filters the user may add to the same device.

## Non-goals

- Exclusive-mode / ASIO / WASAPI-exclusive volume. These bypass the audio engine
  and thus EQ APO; out of scope by architecture, not omission.
- Controlling any device other than the UCA202. The HyperX headset already has a
  working native volume and must not be touched.
- A custom OSD or hotkey handling. Windows' own OSD and media keys are reused.
- Fixing the 16-bit ceiling or the fidelity cost of digital attenuation (see
  Known limitations).

## Architecture

One sentence: **VolMirror watches the UCA202's endpoint volume and writes a
matching preamp gain to that device's Equalizer APO config.**

```
Windows volume slider / media keys / mute
        │  (value stored on the UCA202 endpoint, but inert)
        ▼
IAudioEndpointVolume  ──watched by──▶  VolMirror
                                          │  reads scalar + dB + mute
                                          ▼
                              atomic write of "Preamp: <dB> dB"
                                          │
                                          ▼
                        EqualizerAPO\config\volume.txt
                                          │  (Include:'d from config.txt)
                                          ▼
                        EQ APO applies gain in the audio engine
                                          ▼
                        attenuated PCM → USB → TOSLINK → FENRIS
```

### The key simplification: no device awareness needed

An earlier sketch had VolMirror track the default device and selectively
intercept media keys. That is unnecessary. Because EQ APO is installed **only**
on the UCA202, and VolMirror mirrors **only** the UCA202 endpoint, the coupling
falls out for free:

- UCA202 is default, user presses volume-down → Windows lowers the UCA202
  endpoint → VolMirror mirrors → audio drops.
- User switches to HyperX, presses volume-down → Windows lowers the *HyperX*
  endpoint (which works natively) → the UCA202 endpoint is untouched →
  VolMirror does nothing → the preamp is unchanged.

The user never touches the UCA202's control while listening on the headset, so
the preamp is never moved spuriously. VolMirror needs no hooks, no OSD, and no
default-device tracking. It listens to one endpoint and writes one file.

## Components

### 1. Endpoint watcher

- Resolves the UCA202 by **device ID**, not name:
  `{0.0.0.00000000}.{953bc6ad-4278-495a-83c9-22367cb2a16b}`.
  (Name lookup via `IPropertyStore.GetValue` failed on this machine — PROPVARIANT
  marshalling returns nothing — so the ID is both the robust key and the only
  reliable one. The friendly name, if needed for the tray tooltip, is read from
  the registry under `MMDevices\Audio\Render\{guid}\Properties`.)
- Reads three things per change: master scalar, master level in dB, and mute.
- **Primary mechanism: polling at ~50 ms.** Proven sufficient — the tightest
  real interval observed was ~60 ms, and 50 ms tracks a fast slider drag without
  visible lag.
- **Optimization: `IAudioEndpointVolumeCallback`.** Event-driven, no CPU spin.
  But it is *unproven* on a hardware-delegated endpoint that ignores the value,
  so it is strictly an optimization layered on top of polling: register the
  callback, and if it does not fire reliably, fall back to the polling loop.
  Polling is the contract; the callback only reduces wakeups.
- Handles device disappearance (unplug, driver reload) and re-attaches when the
  endpoint returns, via `IMMNotificationClient` device-state events or a
  re-resolve on read failure.

### 2. Volume → preamp mapping

- **The taper is free.** `GetMasterVolumeLevel` already returns dB on Windows'
  own curve. Feed it straight into `Preamp:`. No custom scalar→dB mapping to
  design or tune — this is what makes the control *feel* native. Measured points:
  0.49 → −10.8 dB, 0.31 → −17.8 dB, 0.10 → −35.0 dB.
- **Clamp the bottom.** Windows reports −128 dB at scalar 0. Clamp to a sane
  floor (e.g. −100 dB) or let mute own the bottom; do not emit −128.
- **Mute is independent.** Read the mute flag separately; never infer mute from
  volume == 0. When muted, emit a silencing preamp (≤ −100 dB). Note Windows
  auto-sets the mute flag when the slider is dragged fully to 0 and clears it on
  the way back up — mirror the flag as-is.

### 3. Config writer

- VolMirror owns exactly one file: `EqualizerAPO\config\volume.txt`, containing
  a single `Preamp: <dB> dB` line.
- `config.txt` references it via an `Include: volume.txt` line. VolMirror never
  edits `config.txt` beyond ensuring that one Include line exists (added once at
  first run if missing). This keeps any user EQ filters in `config.txt`
  untouched.
- **Atomic write:** write to a temp file, then move-with-overwrite onto
  `volume.txt`. EQ APO watches the config directory and hot-reloads on change; a
  half-written file during a fast drag would otherwise cause audible glitches.
- De-dupe: skip the write if the dB value (rounded to EQ APO's resolution) is
  unchanged, to avoid needless reloads.

### 4. Tray UI

- A `NotifyIcon` (WinForms is sufficient; no full WPF needed).
- Tooltip / menu shows current level in dB and mute state — confirms the app is
  alive and mirroring.
- Menu: **Pause/Resume** mirroring, **Open config folder**, **Start with
  Windows** (toggle), **Quit**.
- Pause **stops writing and leaves `volume.txt` as-is** (volume freezes at the
  current level; the Windows slider stops taking effect until resume). It must
  not jump to `Preamp: 0 dB` — pausing at a low volume would then cause a sudden
  loud jump, the worst failure mode for a volume tool. Resume re-syncs from the
  current endpoint value.

### 5. Startup & lifecycle

- **Autostart:** registry `Run` key or a Startup-folder shortcut (decided at
  implementation; `Run` key is simplest). Toggleable from the tray.
- **Sync on launch:** immediately read the current endpoint volume and write the
  matching preamp, so the file is correct from the first moment rather than
  stale until the first change.
- **Single instance:** a named mutex; a second launch just exits.

## Error handling / robustness

- **EQ APO not installed / config folder missing:** detect at startup, show a
  tray balloon explaining the prerequisite, and idle (keep running, re-check)
  rather than crashing.
- **`Include:` line missing:** add it once; if `config.txt` is not writable
  (permissions), surface a clear message — the app needs write access to the
  config folder (installer grants it, or run once elevated).
- **Endpoint absent at startup:** idle and wait for the device via notification,
  then resolve and sync.
- **Exclusive-mode warning (optional, nice-to-have):** if the endpoint's "allow
  exclusive control" is on, mirroring won't affect exclusive streams. A one-time
  tray hint could suggest disabling it. Not required for v1.

## Known limitations (by design)

- **Exclusive-mode / ASIO streams are not attenuated.** They bypass the audio
  engine. Mitigation is a user setting ("disable exclusive mode on this
  endpoint"), documented, not enforced.
- **Digital attenuation on 16-bit S/PDIF costs resolution.** ~1 bit per 6 dB;
  at −20 dB roughly 12.7 effective bits remain. Very unlikely to be audible on
  these speakers, but it is the one real fidelity argument for the hardware
  alternatives (analog attenuation downstream, e.g. an IR blaster driving the
  FENRIS remote, or a DAC with no Feature Unit).

## Test strategy

- **Manual acceptance (the real test):** play audio on the UCA202, drag the
  Windows slider top to bottom, confirm smooth audible tracking; press media
  keys; toggle mute; switch to HyperX and back and confirm the preamp is
  untouched while on the headset.
- **Unit-testable in isolation:** the volume→preamp mapping (scalar/dB/mute →
  preamp string, including clamp and mute), and the atomic-write + de-dupe logic
  (against a temp directory). These are the pieces with real logic; the COM
  watcher and tray are integration-tested manually.
- **Glitch check:** fast repeated drags must not produce audible zipper noise —
  validates the atomic write and de-dupe.

## Prerequisites (one-time setup)

- Equalizer APO installed, enabled on **only** the `Högtalare (USB Audio CODEC )`
  endpoint.
- `config.txt` contains `Include: volume.txt` (VolMirror adds it if absent).

## Open questions / future

- **Multiple volumeless DACs:** the design generalizes to a list of
  `{device-ID → config file}` pairs. Out of scope now (one device), but the
  watcher/writer should not hard-assume a single device internally.
- **Repo hosting:** public (hasslabs) vs private (GrimSQL) — decided before the
  first commit.
- **Config of the device ID:** hard-coded constant vs a small settings file. A
  settings file makes it portable to another machine/DAC without a rebuild;
  likely worth it even for v1.
