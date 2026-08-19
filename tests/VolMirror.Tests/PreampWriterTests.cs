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

    [Fact]
    public void Invalidate_ForcesTheNextWriteThrough()
    {
        // The tray's Resume relies on this: while paused the file may have drifted
        // from what we last wrote, so the cached value must not suppress the write.
        var writer = new PreampWriter(_path);
        writer.Write("Preamp: -10.0 dB");
        File.WriteAllText(_path, "Preamp: 0.0 dB\n");

        writer.Invalidate();

        Assert.True(writer.Write("Preamp: -10.0 dB"));
        Assert.Equal("Preamp: -10.0 dB", File.ReadAllText(_path).Trim());
    }
}
