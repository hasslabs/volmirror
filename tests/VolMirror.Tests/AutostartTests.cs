using VolMirror;
using Xunit;

namespace VolMirror.Tests;

public class AutostartTests
{
    [Theory]
    [InlineData(@"""C:\Apps\VolMirror.exe""", @"C:\Apps\VolMirror.exe")]
    [InlineData(@"C:\Apps\VolMirror.exe", @"C:\Apps\VolMirror.exe")]
    [InlineData(@"""C:\Program Files\VolMirror.exe"" --tray", @"C:\Program Files\VolMirror.exe")]
    [InlineData(@"C:\Apps\VolMirror.exe --tray", @"C:\Apps\VolMirror.exe")]
    public void ExtractExecutable_HandlesQuotesAndArguments(string command, string expected)
    {
        Assert.Equal(expected, Autostart.ExtractExecutable(command));
    }

    [Fact]
    public void PointsAt_MatchesTheSameExecutable()
    {
        Assert.True(Autostart.PointsAt(@"""C:\Apps\VolMirror.exe""", @"C:\Apps\VolMirror.exe"));
    }

    [Fact]
    public void PointsAt_IgnoresCaseAndRedundantSegments()
    {
        Assert.True(Autostart.PointsAt(@"""c:\apps\.\VolMirror.exe""", @"C:\Apps\VolMirror.exe"));
    }

    [Fact]
    public void PointsAt_RejectsADifferentBuild()
    {
        // The real trap: enable autostart from the debug build, then publish
        // elsewhere. Checking only that a Run value exists would report enabled
        // while Windows launches the old path.
        Assert.False(Autostart.PointsAt(
            @"""C:\repo\src\bin\Debug\VolMirror.exe""", @"C:\repo\publish\VolMirror.exe"));
    }

    [Fact]
    public void PointsAt_RejectsGarbage()
    {
        Assert.False(Autostart.PointsAt("", @"C:\Apps\VolMirror.exe"));
        Assert.False(Autostart.PointsAt(@"""C:\Apps\VolMirror.exe""", null));
    }
}
