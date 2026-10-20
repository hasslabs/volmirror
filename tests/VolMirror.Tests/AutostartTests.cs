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

    [Theory]
    [InlineData(@"  ""C:\Apps\VolMirror.exe""  ", @"C:\Apps\VolMirror.exe")]   // surrounding whitespace
    [InlineData(@"""C:\Program Files\VolMirror.exe", @"C:\Program Files\VolMirror.exe")] // unclosed quote
    [InlineData(@"""""", "")]                                                // empty quotes
    [InlineData("   ", "")]                                                  // blank
    public void ExtractExecutable_EdgeCases(string command, string expected)
    {
        Assert.Equal(expected, Autostart.ExtractExecutable(command));
    }

    [Fact]
    public void ExtractExecutable_UnquotedPathWithSpaces_StopsAtTheFirstSpace()
    {
        // Why Quote exists: an unquoted Run value under Program Files is read as
        // "C:\Program" plus arguments.
        Assert.Equal(@"C:\Program", Autostart.ExtractExecutable(@"C:\Program Files\VolMirror.exe"));
    }

    [Fact]
    public void Quote_WrapsThePathSoItSurvivesExtraction()
    {
        const string path = @"C:\Program Files\Vol Mirror\VolMirror.exe";

        string command = Autostart.Quote(path);

        Assert.Equal("\"" + path + "\"", command);
        Assert.True(Autostart.PointsAt(command, path));
    }

    [Fact]
    public void Quote_OfNull_IsEmptyQuotes_WhichPointsAtNothing()
    {
        // Environment.ProcessPath can be null; the resulting Run value must never
        // be reported as pointing at this executable.
        Assert.Equal("\"\"", Autostart.Quote(null));
        Assert.False(Autostart.PointsAt(Autostart.Quote(null), @"C:\Apps\VolMirror.exe"));
    }

    [Fact]
    public void PointsAt_IgnoresArgumentsAfterTheExecutable()
    {
        Assert.True(Autostart.PointsAt(@"""C:\Apps\VolMirror.exe"" --minimized", @"C:\Apps\VolMirror.exe"));
    }

    [Fact]
    public void PointsAt_ResolvesParentSegments()
    {
        Assert.True(Autostart.PointsAt(@"""C:\Apps\old\..\VolMirror.exe""", @"C:\Apps\VolMirror.exe"));
    }

    [Fact]
    public void PointsAt_InvalidPathCharacters_ReturnFalseRatherThanThrow()
    {
        // GetFullPath throws ArgumentException on an embedded NUL; a corrupt Run
        // value must not crash the tray menu that reads IsEnabled.
        Assert.False(Autostart.PointsAt("\"C:\\Apps\\Vol\0Mirror.exe\"",@"C:\Apps\VolMirror.exe"));
    }
}
