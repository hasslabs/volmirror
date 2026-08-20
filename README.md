# VolMirror

Makes the Windows volume control work again for USB DACs that quietly ignore it.

## The problem

Some USB DACs declare a volume control they do not actually apply to the output
you are using. The Behringer UCA202 is the case this was built for: its
PCM2902 chip advertises a USB Audio Feature Unit, so Windows hands volume off to
the hardware and does **not** insert its own software volume. But the chip's
attenuator sits on the analog DAC path only — the S/PDIF encoder is not
downstream of it. Over TOSLINK the slider moves, Windows stores the value,
`GetMasterVolumeLevelScalar` reads it back correctly, and the audio never
changes. (The chip also stalls master-channel volume requests outright; only
per-channel ones are honoured.)

Per-application volume keeps working, because session volume is applied in
software in the audio engine's 32-bit float mixer, upstream of the USB stream.
That asymmetry is the whole basis of the fix.

Counter-intuitively, a DAC that exposes **no** volume control at all does not
have this problem: Microsoft's driver docs are explicit that when a driver does
not advertise `KSPROPERTY_AUDIO_VOLUMELEVEL`, "the Windows audio engine creates
a software volume control APO". A dumb bit pipe gets a working slider; a
half-clever one does not.

## What it does

VolMirror reads that stored-but-inert endpoint volume and writes a matching
`Preamp: <dB> dB` line into a file [Equalizer APO](https://sourceforge.net/projects/equalizerapo/)
includes. Equalizer APO applies the gain inside the audio engine, where it
reaches the S/PDIF output.

```
Windows slider / media keys / mute
        │  (stored on the endpoint, but inert)
        ▼
IAudioEndpointVolume ──watched by──▶ VolMirror
                                        │
                                        ▼
                          Preamp: -12.5 dB  →  volume.txt
                                        │
                                        ▼
                    Equalizer APO applies it in the audio engine
                                        ▼
                          attenuated PCM → USB → TOSLINK → speakers
```

The native controls keep working — slider, media keys, mute, the Windows OSD.
There is no second control surface to learn, and no hotkeys are intercepted.

### Why not just use the dB Windows reports?

Because Windows derives it from the device's own volume range, and some devices
declare a nonsensical one. The UCA202 reports a minimum of −128 dB, which
stretches the taper so badly that the top of the slider stops doing anything
audible:

| Slider range | dB covered | Per 2% keypress |
|---|---|---|
| 90–100% | 1.6 dB | 0.32 dB |
| 50–60% | 2.7 dB | 0.54 dB |
| 10–20% | 10.5 dB | 2.1 dB |

At 0.32 dB a keypress is below the threshold of audibility, so near the top it
takes three or four presses before anything happens — while the bottom of the
slider is six times more sensitive.

VolMirror therefore reads the slider *position* and applies its own curve,
linear in dB. Every press is the same size: with the default −60 dB range and
Windows' 2% step, that is 1.2 dB everywhere. Adjust with `MinDb` — a wider
range makes each step coarser, a narrower one limits how quiet it can go.

## Install

Requires [Equalizer APO](https://sourceforge.net/projects/equalizerapo/),
enabled on your DAC's endpoint (its Configurator asks, then wants a reboot),
and the .NET 10 runtime.

```powershell
.\install.ps1
```

Publishes to `%LOCALAPPDATA%\VolMirror`, registers autostart, and launches it.
Deliberately not run from the repo — `publish\` is gitignored, so a `git clean`
would delete the executable Windows starts.

```powershell
.\install.ps1 -Uninstall
```

## Configuration

`%APPDATA%\VolMirror\settings.json`, written with defaults on first run:

| Key | Default | Notes |
|---|---|---|
| `DeviceNameContains` | `USB Audio CODEC` | Matched against the endpoint's friendly name, case-insensitively |
| `PinnedDeviceId` | `null` | Exact endpoint ID, for two identically named devices. Ignored when that endpoint is absent |
| `ConfigDir` | `C:\Program Files\EqualizerAPO\config` | |
| `PollIntervalMs` | `50` | Clamped to 10–5000 |
| `MinDb` | `-60` | Gain at the bottom of the slider. Clamped to −100…−6 |

Matching is by **name**, not ID, because endpoint IDs are not stable — installing
Equalizer APO made Windows re-enumerate the device and issue it a fresh GUID,
which stranded an earlier hard-coded ID.

VolMirror owns `volume.txt` and nothing else. It adds one `Include:` line to the
top of `config.txt` and never touches the rest, so your own filters are safe;
that file is read and written as raw bytes so a non-UTF-8 config cannot be
mangled.

## Limitations

- **WASAPI-exclusive and ASIO streams are not attenuated.** They bypass the
  audio engine, so nothing in this design can reach them. Turn off "allow
  applications to take exclusive control" on the endpoint if that matters.
- **Digital attenuation costs resolution** — roughly one bit per 6 dB. On
  16-bit S/PDIF, −20 dB leaves about 12.7 effective bits. Very unlikely to be
  audible, but it is real; attenuating downstream in analog does not have this
  cost.
- A hard kill (not Quit, not logout) leaves the last gain in `volume.txt`. It
  self-corrects on next launch, which re-mirrors the current volume.

## Build

```powershell
dotnet test
dotnet build
```

C# / .NET 10, WinForms tray icon, hand-written Core Audio COM interop.
The pure logic — volume mapping, atomic file writing, config editing, settings
validation, autostart path matching — is unit-tested; the COM watcher and tray
are verified by running them.

## License

MIT
