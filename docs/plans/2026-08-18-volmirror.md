# VolMirror Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** A Windows tray app that mirrors the (stored but inert) Windows endpoint volume for the Behringer UCA202 onto an Equalizer APO preamp gain, restoring the native volume slider, media keys and mute over TOSLINK.

**Architecture:** A polling watcher reads `IAudioEndpointVolume` on one endpoint (resolved by device ID), maps the reported dB + mute flag to a `Preamp: <dB> dB` line, and atomically writes it to a `volume.txt` that Equalizer APO `Include:`s. Pure logic (mapping, writing, config editing) is unit-tested; the COM watcher and tray UI are verified manually.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WinForms `NotifyIcon` for the tray, xUnit for tests, Windows Core Audio COM interop (hand-written, already proven in the probe scripts).

**Design spec:** `docs/superpowers/specs/2026-08-18-volmirror-design.md`

---

## Key constants (established by measurement, do not re-derive)

| Thing | Value |
|---|---|
| UCA202 endpoint ID | `{0.0.0.00000000}.{953bc6ad-4278-495a-83c9-22367cb2a16b}` |
| EQ APO config dir | `C:\Program Files\EqualizerAPO\config` |
| App-owned file | `<config dir>\volume.txt` |
| Include line in `config.txt` | `Include: volume.txt` |
| Poll interval | 50 ms |
| Silence / floor gain | −100 dB |

**Culture trap:** the machine runs a Swedish locale, where `double.ToString()` yields `-10,8`. Equalizer APO requires `-10.8`. Every number formatted into the config file MUST use `CultureInfo.InvariantCulture`. This has an explicit test.

---

## Task 1: Solution and project skeleton

**Files:**
- Create: `VolMirror.sln`
- Create: `src/VolMirror/VolMirror.csproj`
- Create: `src/VolMirror/Program.cs` (placeholder, see Step 2)
- Create: `tests/VolMirror.Tests/VolMirror.Tests.csproj`
- Create: `.gitignore`

**Step 1: Create the solution and projects**

Run from the repo root (`C:\Users\Viktors-PC\Documents\Visual Studio Code\VolMirror`):

```bash
dotnet new sln -n VolMirror --format sln
dotnet new classlib -o src/VolMirror
dotnet new xunit -o tests/VolMirror.Tests
```

We start from `classlib` rather than `winforms` so the csproj is written explicitly in Step 2 — the WinForms template varies between SDK versions.

Two SDK 10 quirks, both confirmed on this machine: the templates reject
`-f net10.0-windows` (they offer only `net10.0`/`net8.0`/`netstandard*`), so the
Windows TFM is set afterwards — Step 2 rewrites the app csproj wholesale anyway,
and Step 4 covers the test csproj. And `dotnet new sln` now defaults to the
`.slnx` format, hence `--format sln`.

**Step 2: Replace `src/VolMirror/VolMirror.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AssemblyName>VolMirror</AssemblyName>
    <RootNamespace>VolMirror</RootNamespace>
  </PropertyGroup>

</Project>
```

Delete the template's `Class1.cs`:

```bash
rm src/VolMirror/Class1.cs
```

**`OutputType=WinExe` with no source files fails to build** (`CS5001: Program does
not contain a static 'Main' method`), and the real entry point does not arrive
until Task 8. So add a placeholder `src/VolMirror/Program.cs` — empty `Main`,
shaped like the final version — to keep Step 6 honest. Task 8 overwrites it.

```csharp
namespace VolMirror;

internal static class Program
{
    // Placeholder so WinExe builds; replaced wholesale in Task 8.
    [STAThread]
    private static void Main()
    {
    }
}
```

**Step 3: Create `src/VolMirror/app.manifest`**

Runs as a normal user — the app writes only to the EQ APO config folder, which the installer makes writable. Do NOT request elevation.

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="VolMirror.app"/>
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="asInvoker" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
</assembly>
```

**Step 4: Point the test project at `net10.0-windows` and reference the app**

The test project MUST target `net10.0-windows` too — referencing a Windows-specific project from a plain `net10.0` test project fails with NU1201.

Verify `tests/VolMirror.Tests/VolMirror.Tests.csproj` has `<TargetFramework>net10.0-windows</TargetFramework>`, then:

```bash
dotnet add tests/VolMirror.Tests reference src/VolMirror
dotnet sln add src/VolMirror tests/VolMirror.Tests
```

Delete the template's placeholder test:

```bash
rm tests/VolMirror.Tests/UnitTest1.cs
```

**Step 5: Create `.gitignore`**

```gitignore
bin/
obj/
*.user
.vs/
```

**Step 6: Verify the solution builds**

Run: `dotnet build`
Expected: `Build succeeded`, 0 errors.

**Step 7: Commit**

```bash
git add -A
git commit -m "chore: scaffold VolMirror solution and test project"
```

---

## Task 2: PreampMapper — volume reading to config line

The one piece of real logic in the app. Pure function, no I/O.

**Files:**
- Create: `src/VolMirror/PreampMapper.cs`
- Test: `tests/VolMirror.Tests/PreampMapperTests.cs`

**Step 1: Write the failing tests**

Create `tests/VolMirror.Tests/PreampMapperTests.cs`:

```csharp
using System.Globalization;
using VolMirror;
using Xunit;

namespace VolMirror.Tests;

public class PreampMapperTests
{
    [Fact]
    public void FullVolume_IsZeroGain()
    {
        Assert.Equal("Preamp: 0.0 dB", PreampMapper.ToPreampLine(0.0, muted: false));
    }

    [Fact]
    public void MidVolume_PassesWindowsTaperThrough()
    {
        // Measured on the real device: scalar 0.49 reported -10.8 dB.
        Assert.Equal("Preamp: -10.8 dB", PreampMapper.ToPreampLine(-10.8, muted: false));
    }

    [Fact]
    public void Muted_IsSilence()
    {
        Assert.Equal("Preamp: -100.0 dB", PreampMapper.ToPreampLine(-4.5, muted: true));
    }

    [Fact]
    public void Muted_WinsOverFullVolume()
    {
        // Windows reports mute independently of level; mute must not be inferred
        // from level == 0, and level must not override mute.
        Assert.Equal("Preamp: -100.0 dB", PreampMapper.ToPreampLine(0.0, muted: true));
    }

    [Fact]
    public void BottomOfSlider_IsClampedToFloor()
    {
        // Windows reports -128 dB at scalar 0. Never emit that.
        Assert.Equal("Preamp: -100.0 dB", PreampMapper.ToPreampLine(-128.0, muted: false));
    }

    [Fact]
    public void PositiveGain_IsClampedToZero()
    {
        // Defensive: never boost, which would clip.
        Assert.Equal("Preamp: 0.0 dB", PreampMapper.ToPreampLine(3.0, muted: false));
    }

    [Fact]
    public void UsesInvariantCulture_EvenUnderSwedishLocale()
    {
        // The machine runs sv-SE, where the default decimal separator is a comma.
        // Equalizer APO would not parse "Preamp: -10,8 dB".
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal("Preamp: -10.8 dB", PreampMapper.ToPreampLine(-10.8, muted: false));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
```

**Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter PreampMapperTests`
Expected: build failure — `PreampMapper` does not exist.

**Step 3: Write the minimal implementation**

Create `src/VolMirror/PreampMapper.cs`:

```csharp
using System.Globalization;

namespace VolMirror;

/// Maps a Windows endpoint volume reading onto an Equalizer APO preamp line.
public static class PreampMapper
{
    /// Gain emitted when muted, and the floor for very low volumes.
    /// Windows reports -128 dB at the bottom of the slider; that is not worth passing on.
    public const double SilenceDb = -100.0;

    public static string ToPreampLine(double levelDb, bool muted)
    {
        double gain = muted ? SilenceDb : Math.Clamp(levelDb, SilenceDb, 0.0);
        return string.Format(CultureInfo.InvariantCulture, "Preamp: {0:F1} dB", gain);
    }
}
```

**Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter PreampMapperTests`
Expected: PASS, 7 tests.

**Step 5: Commit**

```bash
git add src/VolMirror/PreampMapper.cs tests/VolMirror.Tests/PreampMapperTests.cs
git commit -m "feat: map endpoint volume and mute to an Equalizer APO preamp line"
```

---

## Task 3: PreampWriter — atomic write with de-duplication

**Files:**
- Create: `src/VolMirror/PreampWriter.cs`
- Test: `tests/VolMirror.Tests/PreampWriterTests.cs`

**Step 1: Write the failing tests**

Create `tests/VolMirror.Tests/PreampWriterTests.cs`:

```csharp
using VolMirror;
using Xunit;

namespace VolMirror.Tests;

public class PreampWriterTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public PreampWriterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "volmirror-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "volume.txt");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void FirstWrite_CreatesTheFile()
    {
        var writer = new PreampWriter(_path);

        Assert.True(writer.Write("Preamp: -10.0 dB"));
        Assert.Equal("Preamp: -10.0 dB", File.ReadAllText(_path).Trim());
    }

    [Fact]
    public void RepeatedValue_IsNotRewritten()
    {
        var writer = new PreampWriter(_path);
        writer.Write("Preamp: -10.0 dB");

        // A no-op write would make Equalizer APO reload the config for nothing,
        // 20 times a second while the slider sits still.
        Assert.False(writer.Write("Preamp: -10.0 dB"));
    }

    [Fact]
    public void ChangedValue_IsWritten()
    {
        var writer = new PreampWriter(_path);
        writer.Write("Preamp: -10.0 dB");

        Assert.True(writer.Write("Preamp: -20.0 dB"));
        Assert.Equal("Preamp: -20.0 dB", File.ReadAllText(_path).Trim());
    }

    [Fact]
    public void NoTempFileIsLeftBehind()
    {
        var writer = new PreampWriter(_path);
        writer.Write("Preamp: -10.0 dB");

        Assert.Equal(new[] { "volume.txt" }, Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void OverwritesAnExistingFile()
    {
        File.WriteAllText(_path, "Preamp: -50.0 dB\n");
        var writer = new PreampWriter(_path);

        Assert.True(writer.Write("Preamp: -5.0 dB"));
        Assert.Equal("Preamp: -5.0 dB", File.ReadAllText(_path).Trim());
    }
}
```

**Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter PreampWriterTests`
Expected: build failure — `PreampWriter` does not exist.

**Step 3: Write the minimal implementation**

Create `src/VolMirror/PreampWriter.cs`:

```csharp
using System.Text;

namespace VolMirror;

/// Owns exactly one file: the preamp line that Equalizer APO includes.
public sealed class PreampWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _path;
    private string? _lastWritten;

    public PreampWriter(string path) => _path = path;

    /// Writes the line if it differs from the last one written.
    /// Returns true if the file was touched.
    public bool Write(string preampLine)
    {
        if (preampLine == _lastWritten)
            return false;

        // Equalizer APO watches the config directory and reloads on change.
        // Writing in place would let it observe a half-written file during a
        // fast slider drag, which is audible.
        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, preampLine + Environment.NewLine, Utf8NoBom);
        File.Move(tmp, _path, overwrite: true);

        _lastWritten = preampLine;
        return true;
    }

    /// Forgets the cached value so the next Write always hits the disk.
    public void Invalidate() => _lastWritten = null;
}
```

`Invalidate()` is needed by the tray's Resume (Task 8) — after a pause the file may no longer match what we last wrote.

**Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter PreampWriterTests`
Expected: PASS, 5 tests.

**Step 5: Commit**

```bash
git add src/VolMirror/PreampWriter.cs tests/VolMirror.Tests/PreampWriterTests.cs
git commit -m "feat: write the preamp line atomically, skipping unchanged values"
```

---

## Task 4: ApoConfig — ensure the Include line, preserve user filters

**Files:**
- Create: `src/VolMirror/ApoConfig.cs`
- Test: `tests/VolMirror.Tests/ApoConfigTests.cs`

**Step 1: Write the failing tests**

Create `tests/VolMirror.Tests/ApoConfigTests.cs`:

```csharp
using VolMirror;
using Xunit;

namespace VolMirror.Tests;

public class ApoConfigTests : IDisposable
{
    private readonly string _dir;
    private readonly string _configPath;

    public ApoConfigTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "volmirror-apo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _configPath = Path.Combine(_dir, "config.txt");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void MissingConfig_GetsTheIncludeLine()
    {
        Assert.True(ApoConfig.EnsureInclude(_configPath));
        Assert.Contains("Include: volume.txt", File.ReadAllText(_configPath));
    }

    [Fact]
    public void ExistingInclude_IsNotDuplicated()
    {
        File.WriteAllText(_configPath, "Include: volume.txt\n");

        Assert.False(ApoConfig.EnsureInclude(_configPath));
        Assert.Single(File.ReadAllLines(_configPath), l => l.Contains("Include: volume.txt"));
    }

    [Fact]
    public void IncludeWithSurroundingWhitespace_IsRecognised()
    {
        File.WriteAllText(_configPath, "   Include: volume.txt   \n");

        Assert.False(ApoConfig.EnsureInclude(_configPath));
    }

    [Fact]
    public void UserFiltersArePreserved()
    {
        // The whole reason we own a separate file rather than config.txt.
        File.WriteAllText(_configPath, "Filter 1: ON PK Fc 1000 Hz Gain -3 dB Q 1\nFilter 2: ON LS Fc 100 Hz Gain 2 dB\n");

        ApoConfig.EnsureInclude(_configPath);

        string text = File.ReadAllText(_configPath);
        Assert.Contains("Filter 1: ON PK Fc 1000 Hz Gain -3 dB Q 1", text);
        Assert.Contains("Filter 2: ON LS Fc 100 Hz Gain 2 dB", text);
        Assert.Contains("Include: volume.txt", text);
    }
}
```

**Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter ApoConfigTests`
Expected: build failure — `ApoConfig` does not exist.

**Step 3: Write the minimal implementation**

Create `src/VolMirror/ApoConfig.cs`:

```csharp
namespace VolMirror;

/// Touches Equalizer APO's own config.txt as little as possible: one Include line,
/// added once. Everything else in that file belongs to the user.
public static class ApoConfig
{
    public const string VolumeFileName = "volume.txt";
    public const string IncludeLine = "Include: " + VolumeFileName;

    /// Returns true if the line was added.
    public static bool EnsureInclude(string configPath)
    {
        var lines = File.Exists(configPath)
            ? File.ReadAllLines(configPath).ToList()
            : new List<string>();

        if (lines.Any(l => l.Trim().Equals(IncludeLine, StringComparison.OrdinalIgnoreCase)))
            return false;

        lines.Add(IncludeLine);
        File.WriteAllLines(configPath, lines);
        return true;
    }
}
```

**Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter ApoConfigTests`
Expected: PASS, 4 tests.

**Step 5: Commit**

```bash
git add src/VolMirror/ApoConfig.cs tests/VolMirror.Tests/ApoConfigTests.cs
git commit -m "feat: ensure the Include line without disturbing user EQ filters"
```

---

## Task 5: Settings — device ID and paths without a rebuild

**Files:**
- Create: `src/VolMirror/Settings.cs`
- Test: `tests/VolMirror.Tests/SettingsTests.cs`

Stored at `%APPDATA%\VolMirror\settings.json`, written with defaults on first run.

**Step 1: Write the failing tests**

Create `tests/VolMirror.Tests/SettingsTests.cs`:

```csharp
using VolMirror;
using Xunit;

namespace VolMirror.Tests;

public class SettingsTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public SettingsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "volmirror-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "settings.json");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void MissingFile_WritesDefaults()
    {
        var settings = Settings.Load(_path);

        Assert.Equal(Settings.DefaultDeviceId, settings.DeviceId);
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void ExistingFile_IsRoundTripped()
    {
        var written = new Settings { DeviceId = "{0.0.0.00000000}.{deadbeef}", PollIntervalMs = 100 };
        written.Save(_path);

        var read = Settings.Load(_path);

        Assert.Equal("{0.0.0.00000000}.{deadbeef}", read.DeviceId);
        Assert.Equal(100, read.PollIntervalMs);
    }

    [Fact]
    public void CorruptFile_FallsBackToDefaults()
    {
        // A hand-edited settings file must not brick the app on startup.
        File.WriteAllText(_path, "{ not json");

        var settings = Settings.Load(_path);

        Assert.Equal(Settings.DefaultDeviceId, settings.DeviceId);
    }
}
```

**Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter SettingsTests`
Expected: build failure — `Settings` does not exist.

**Step 3: Write the minimal implementation**

Create `src/VolMirror/Settings.cs`:

```csharp
using System.Text.Json;

namespace VolMirror;

public sealed class Settings
{
    /// The Behringer UCA202 on this machine, measured via EndpointVolumeProbe.
    /// Endpoint IDs are stable across reboots.
    public const string DefaultDeviceId = "{0.0.0.00000000}.{953bc6ad-4278-495a-83c9-22367cb2a16b}";
    public const string DefaultConfigDir = @"C:\Program Files\EqualizerAPO\config";

    public string DeviceId { get; set; } = DefaultDeviceId;
    public string ConfigDir { get; set; } = DefaultConfigDir;
    public int PollIntervalMs { get; set; } = 50;

    public string VolumeFilePath => Path.Combine(ConfigDir, ApoConfig.VolumeFileName);
    public string ConfigFilePath => Path.Combine(ConfigDir, "config.txt");

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VolMirror", "settings.json");

    public static Settings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Fall through to defaults rather than refusing to start.
        }

        var settings = new Settings();
        settings.Save(path);
        return settings;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
```

**Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter SettingsTests`
Expected: PASS, 3 tests.

**Step 5: Commit**

```bash
git add src/VolMirror/Settings.cs tests/VolMirror.Tests/SettingsTests.cs
git commit -m "feat: load settings with defaults, surviving a corrupt file"
```

---

## Task 6: Core Audio interop

No tests — this is a declaration of an OS ABI. It is verified by Task 7 running against the real device.

**Files:**
- Create: `src/VolMirror/Interop/CoreAudio.cs`

**Step 1: Write the interop**

This is lifted from the working probe script (`WatchEndpointVolume.ps1`), which already ran successfully against this device. Note `OpenPropertyStore` returns `IntPtr` — we never call it, because `IPropertyStore.GetValue` fails to marshal PROPVARIANT on this machine. Device IDs are the key; friendly names come from the registry if ever needed.

```csharp
using System.Runtime.InteropServices;

namespace VolMirror.Interop;

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int Item(uint index, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams,
                               [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    [PreserveSig] int OpenPropertyStore(uint stgmAccess, out IntPtr store);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out uint state);
}

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IntPtr cb);
    [PreserveSig] int UnregisterControlChangeNotify(IntPtr cb);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float leveldB, ref Guid ctx);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
    [PreserveSig] int GetMasterVolumeLevel(out float leveldB);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint ch, float leveldB, ref Guid ctx);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
    [PreserveSig] int GetChannelVolumeLevel(uint ch, out float leveldB);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
    [PreserveSig] int VolumeStepUp(ref Guid ctx);
    [PreserveSig] int VolumeStepDown(ref Guid ctx);
    [PreserveSig] int QueryHardwareSupport(out uint mask);
    [PreserveSig] int GetVolumeRange(out float minDb, out float maxDb, out float incDb);
}

public static class CoreAudioGuids
{
    public static readonly Guid MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    public static readonly Guid IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
}
```

**Step 2: Verify it builds**

Run: `dotnet build`
Expected: `Build succeeded`.

**Step 3: Commit**

```bash
git add src/VolMirror/Interop/CoreAudio.cs
git commit -m "feat: add Core Audio interop for endpoint volume"
```

---

## Task 7: EndpointWatcher — poll one endpoint, report changes

**Files:**
- Create: `src/VolMirror/EndpointWatcher.cs`

Polling is the contract (proven at 50 ms). `IAudioEndpointVolumeCallback` is deliberately not implemented — it is an unproven optimization on a hardware-delegated endpoint, and the spec makes it optional.

**Step 1: Write the watcher**

```csharp
using System.Runtime.InteropServices;
using VolMirror.Interop;

namespace VolMirror;

public readonly record struct VolumeReading(double LevelDb, bool Muted);

/// Polls one audio endpoint, resolved by device ID, and raises an event when its
/// volume or mute state changes. Re-attaches by itself if the device goes away.
public sealed class EndpointWatcher : IDisposable
{
    private readonly string _deviceId;
    private readonly int _pollIntervalMs;
    private readonly System.Windows.Forms.Timer _timer;

    private IAudioEndpointVolume? _endpoint;
    private VolumeReading? _last;

    /// Raised on every observed change, and once on first successful attach.
    public event Action<VolumeReading>? Changed;

    /// Raised when the device becomes available or unavailable.
    public event Action<bool>? AvailabilityChanged;

    public bool IsAttached => _endpoint is not null;

    public EndpointWatcher(string deviceId, int pollIntervalMs)
    {
        _deviceId = deviceId;
        _pollIntervalMs = pollIntervalMs;
        _timer = new System.Windows.Forms.Timer { Interval = _pollIntervalMs };
        _timer.Tick += (_, _) => Poll();
    }

    public void Start()
    {
        TryAttach();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private void TryAttach()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(
                Type.GetTypeFromCLSID(CoreAudioGuids.MMDeviceEnumerator)!)!;

            if (enumerator.GetDevice(_deviceId, out IMMDevice device) != 0)
                return;

            Guid iid = CoreAudioGuids.IAudioEndpointVolume;
            if (device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out object raw) != 0)
                return;

            _endpoint = (IAudioEndpointVolume)raw;
            _last = null;                       // force a Changed on the next poll
            AvailabilityChanged?.Invoke(true);
        }
        catch (COMException)
        {
            _endpoint = null;
        }
    }

    private void Detach()
    {
        _endpoint = null;
        _last = null;
        AvailabilityChanged?.Invoke(false);
    }

    private void Poll()
    {
        if (_endpoint is null)
        {
            TryAttach();
            return;
        }

        try
        {
            if (_endpoint.GetMasterVolumeLevel(out float db) != 0) { Detach(); return; }
            if (_endpoint.GetMute(out bool muted) != 0) { Detach(); return; }

            var reading = new VolumeReading(db, muted);
            if (_last is { } previous && previous == reading)
                return;

            _last = reading;
            Changed?.Invoke(reading);
        }
        catch (COMException)
        {
            // Device unplugged or driver reloaded mid-poll.
            Detach();
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        if (_endpoint is not null)
            Marshal.ReleaseComObject(_endpoint);
    }
}
```

Using `System.Windows.Forms.Timer` keeps every callback on the UI thread, so the tray can be updated directly with no marshalling and no locking.

**Step 2: Verify it builds**

Run: `dotnet build`
Expected: `Build succeeded`.

**Step 3: Commit**

```bash
git add src/VolMirror/EndpointWatcher.cs
git commit -m "feat: poll the endpoint and report volume and mute changes"
```

---

## Task 8: Tray app and entry point

**Files:**
- Create: `src/VolMirror/TrayApp.cs`
- Create: `src/VolMirror/Autostart.cs`
- Overwrite: `src/VolMirror/Program.cs` (the placeholder from Task 1)

**Step 1: Write `src/VolMirror/Autostart.cs`**

```csharp
using Microsoft.Win32;

namespace VolMirror;

public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VolMirror";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is not null;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)!;
        if (enabled)
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
```

**Step 2: Write `src/VolMirror/TrayApp.cs`**

```csharp
using System.Globalization;
using System.Windows.Forms;

namespace VolMirror;

public sealed class TrayApp : ApplicationContext
{
    private readonly Settings _settings;
    private readonly EndpointWatcher _watcher;
    private readonly PreampWriter _writer;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _autostartItem;

    private bool _paused;
    private VolumeReading? _latest;

    public TrayApp(Settings settings)
    {
        _settings = settings;
        _writer = new PreampWriter(settings.VolumeFilePath);
        _watcher = new EndpointWatcher(settings.DeviceId, settings.PollIntervalMs);

        _pauseItem = new ToolStripMenuItem("Pause mirroring", null, (_, _) => TogglePause());
        _autostartItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleAutostart())
        {
            Checked = Autostart.IsEnabled
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open config folder", null,
            (_, _) => OpenConfigFolder()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => Quit()));

        _icon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "VolMirror",
            ContextMenuStrip = menu,
            Visible = true
        };

        _watcher.Changed += OnVolumeChanged;
        _watcher.AvailabilityChanged += OnAvailabilityChanged;

        if (!Directory.Exists(settings.ConfigDir))
        {
            // Keep running and re-check; the user may install Equalizer APO later.
            _icon.ShowBalloonTip(10000, "VolMirror",
                "Equalizer APO config folder not found. Mirroring is idle.", ToolTipIcon.Warning);
        }
        else
        {
            ApoConfig.EnsureInclude(settings.ConfigFilePath);
        }

        _watcher.Start();
    }

    private void OnVolumeChanged(VolumeReading reading)
    {
        _latest = reading;
        if (!_paused)
            WriteCurrent();
        UpdateTooltip();
    }

    private void OnAvailabilityChanged(bool available) => UpdateTooltip();

    private void WriteCurrent()
    {
        if (_latest is not { } reading) return;

        try
        {
            _writer.Write(PreampMapper.ToPreampLine(reading.LevelDb, reading.Muted));
        }
        catch (IOException)
        {
            // Transient: Equalizer APO may hold the file for an instant while reloading.
            // The next change rewrites it.
        }
        catch (UnauthorizedAccessException)
        {
            _icon.ShowBalloonTip(10000, "VolMirror",
                $"Cannot write to {_settings.VolumeFilePath}. Check folder permissions.",
                ToolTipIcon.Error);
        }
    }

    private void TogglePause()
    {
        _paused = !_paused;
        _pauseItem.Text = _paused ? "Resume mirroring" : "Pause mirroring";
        _pauseItem.Checked = _paused;

        if (!_paused)
        {
            // The file may have drifted while paused; force the next write through.
            _writer.Invalidate();
            WriteCurrent();
        }
        // On pause: deliberately leave volume.txt alone. Resetting to 0 dB would
        // make pausing at a low volume produce a sudden loud jump.

        UpdateTooltip();
    }

    private void ToggleAutostart()
    {
        bool enabled = !Autostart.IsEnabled;
        Autostart.SetEnabled(enabled);
        _autostartItem.Checked = enabled;
    }

    private void OpenConfigFolder()
    {
        if (Directory.Exists(_settings.ConfigDir))
            System.Diagnostics.Process.Start("explorer.exe", _settings.ConfigDir);
    }

    private void UpdateTooltip()
    {
        string state = !_watcher.IsAttached ? "device not present"
            : _paused ? "paused"
            : _latest is { } r
                ? (r.Muted ? "muted" : string.Format(CultureInfo.InvariantCulture, "{0:F1} dB", Math.Clamp(r.LevelDb, PreampMapper.SilenceDb, 0.0)))
                : "waiting";

        // NotifyIcon.Text is capped at 63 characters.
        _icon.Text = $"VolMirror — {state}";
    }

    private void Quit()
    {
        _icon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _watcher.Dispose();
            _icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
```

**Step 3: Overwrite `src/VolMirror/Program.cs`**

This file already exists as the empty placeholder from Task 1. Replace it
wholesale — do not merge into it.

```csharp
using System.Windows.Forms;

namespace VolMirror;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, "VolMirror.SingleInstance", out bool isFirst);
        if (!isFirst)
            return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp(Settings.Load(Settings.DefaultPath)));
    }
}
```

**Step 4: Verify the whole suite still passes and it builds**

Run: `dotnet test`
Expected: PASS, 19 tests.

Run: `dotnet build`
Expected: `Build succeeded`.

**Step 5: Commit**

```bash
git add src/VolMirror/TrayApp.cs src/VolMirror/Autostart.cs src/VolMirror/Program.cs
git commit -m "feat: add tray UI, autostart toggle and single-instance entry point"
```

---

## Task 9: Manual acceptance test

The unit tests cover the logic; this is where the app is actually proven. No code — run it and listen.

**Step 1: Publish a single file and run it**

```bash
dotnet publish src/VolMirror -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Run `publish\VolMirror.exe`. A tray icon appears.

**Step 2: Work through the checklist**

With audio playing on the UCA202 (TOSLINK into the FENRIS):

| # | Action | Expected |
|---|---|---|
| 1 | Drag the Windows slider top to bottom | Volume tracks smoothly and audibly, no zipper noise |
| 2 | Press the keyboard volume keys | Volume changes; Windows' own OSD appears |
| 3 | Press mute, then unmute | Silence, then the previous level returns |
| 4 | Drag the slider fully to 0 and back up | Mutes at the bottom, recovers on the way up |
| 5 | Hover the tray icon | Shows the current dB, matching the slider |
| 6 | Switch to HyperX (Ctrl+Alt+F11), change volume, switch back | Headset volume works natively; the UCA202's level is unchanged on return |
| 7 | Pause from the tray, move the slider | Volume freezes — no jump, no change |
| 8 | Resume | The current slider position takes effect |
| 9 | Unplug the UCA202's USB, replug | Tooltip shows "device not present", then re-attaches and resumes |
| 10 | Enable "Start with Windows", reboot | The tray icon returns; volume still works |

**Step 3: Check the config file is clean**

```bash
cat "C:\Program Files\EqualizerAPO\config\volume.txt"
cat "C:\Program Files\EqualizerAPO\config\config.txt"
```

Expected: `volume.txt` holds one `Preamp: <n>.<n> dB` line with a **decimal point**, not a comma. `config.txt` holds the `Include: volume.txt` line and nothing else lost.

**Step 4: Commit any fixes, then tag**

```bash
git tag v0.1.0
```

---

## Deferred (explicitly not in v1)

- `IAudioEndpointVolumeCallback` instead of polling — an optimization, unproven on this endpoint.
- Multiple devices — the design generalizes to `{device ID → config file}` pairs, but there is one device today.
- An exclusive-mode warning balloon.
- A custom tray icon — `SystemIcons.Application` is a placeholder.
- Repo hosting (hasslabs public vs GrimSQL private) — decided before the first push.
