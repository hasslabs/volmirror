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
