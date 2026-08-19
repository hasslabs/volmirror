using System.Text;

namespace VolMirror;

/// Owns exactly one file: the preamp line that Equalizer APO includes.
public sealed class PreampWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// Equalizer APO holds the file briefly each time it reloads. Four quick
    /// attempts cover that without stalling the poll loop (~36 ms worst case,
    /// inside the 50 ms poll interval).
    private const int MaxAttempts = 4;
    private const int RetryDelayMs = 12;

    private readonly string _path;
    private string? _lastWritten;

    public PreampWriter(string path) => _path = path;

    /// Writes the line if it differs from the last one written.
    /// Returns true if the file was touched. Throws if every attempt fails;
    /// the cached value is left alone so the caller can simply try again.
    public bool Write(string preampLine)
    {
        if (preampLine == _lastWritten)
            return false;

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                // Equalizer APO watches the config directory and reloads on change.
                // Writing in place would let it observe a half-written file during a
                // fast slider drag, which is audible.
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, preampLine + Environment.NewLine, Utf8NoBom);
                File.Move(tmp, _path, overwrite: true);

                _lastWritten = preampLine;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // On Windows a locked destination surfaces from File.Move as
                // UnauthorizedAccessException rather than a sharing violation, so
                // this must not be read as a permissions problem.
                if (attempt >= MaxAttempts)
                    throw;

                Thread.Sleep(RetryDelayMs);
            }
        }
    }

    /// Forgets the cached value so the next Write always hits the disk.
    public void Invalidate() => _lastWritten = null;
}
