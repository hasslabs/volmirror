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

    private readonly System.Windows.Forms.Timer _retryTimer;
    private int _consecutiveWriteFailures;
    private bool _writeErrorReported;

    public TrayApp(Settings settings)
    {
        _settings = settings;
        _writer = new PreampWriter(settings.VolumeFilePath);
        _watcher = new EndpointWatcher(settings.DeviceNameContains, settings.PinnedDeviceId, settings.PollIntervalMs);

        _pauseItem = new ToolStripMenuItem("Pause mirroring", null, (_, _) => TogglePause());
        _autostartItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleAutostart())
        {
            Checked = Autostart.IsEnabled
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open config folder", null, (_, _) => OpenConfigFolder()));
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

        // A write can fail while Equalizer APO holds the file. Changed only fires
        // on movement, so without this a failure landing on the last change of a
        // drag would strand the volume at the wrong level until it is touched again.
        _retryTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _retryTimer.Tick += (_, _) =>
        {
            if (_consecutiveWriteFailures > 0 && !_paused)
                WriteCurrent();
        };
        _retryTimer.Start();

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
            _consecutiveWriteFailures = 0;
            _writeErrorReported = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Already retried inside the writer. A collision with Equalizer APO's
            // own reload is normal and self-corrects on the retry timer, so warn
            // only once the failure has persisted for a couple of seconds - and
            // only once, rather than on every poll.
            _consecutiveWriteFailures++;

            if (_consecutiveWriteFailures >= 4 && !_writeErrorReported)
            {
                _writeErrorReported = true;
                _icon.ShowBalloonTip(10000, "VolMirror",
                    $"Cannot write to {_settings.VolumeFilePath}. Check folder permissions.",
                    ToolTipIcon.Error);
            }
        }

        UpdateTooltip();
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
            : _consecutiveWriteFailures >= 4 ? "write failing"
            : _paused ? "paused"
            : _latest is { } r
                ? (r.Muted ? "muted" : string.Format(CultureInfo.InvariantCulture, "{0:F1} dB",
                    Math.Clamp(r.LevelDb, PreampMapper.SilenceDb, 0.0)))
                : "waiting";

        // NotifyIcon.Text is capped at 63 characters.
        _icon.Text = $"VolMirror - {state}";
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
            _retryTimer.Stop();
            _retryTimer.Dispose();
            _watcher.Dispose();
            _icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
