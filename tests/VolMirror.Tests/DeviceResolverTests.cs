using VolMirror;
using Xunit;

namespace VolMirror.Tests;

public class DeviceResolverTests
{
    private static readonly AudioEndpointInfo[] Endpoints =
    [
        new("{0.0.0.00000000}.{aaa}", "Hogtalare (USB Audio CODEC )"),
        new("{0.0.0.00000000}.{bbb}", "Hogtalare (HyperX Cloud III S Wireless)"),
        new("{0.0.0.00000000}.{ccc}", "Odyssey G93SC (NVIDIA High Definition Audio)"),
    ];

    [Fact]
    public void MatchesOnNameSubstring()
    {
        Assert.Equal("{0.0.0.00000000}.{aaa}",
            DeviceResolver.PickBestMatch(Endpoints, "USB Audio CODEC", pinnedId: null));
    }

    [Fact]
    public void NameMatchIsCaseInsensitive()
    {
        Assert.Equal("{0.0.0.00000000}.{aaa}",
            DeviceResolver.PickBestMatch(Endpoints, "usb audio codec", pinnedId: null));
    }

    [Fact]
    public void PinnedIdWins_WhenItIsPresent()
    {
        // Escape hatch for two identical DACs, where the name cannot disambiguate.
        Assert.Equal("{0.0.0.00000000}.{bbb}",
            DeviceResolver.PickBestMatch(Endpoints, "USB Audio CODEC", pinnedId: "{0.0.0.00000000}.{bbb}"));
    }

    [Fact]
    public void PinnedIdIsIgnored_WhenTheDeviceIsGone()
    {
        // The whole reason this class exists: endpoint IDs change when Windows
        // re-enumerates a device, e.g. after installing an APO. A stale pin must
        // not strand the app.
        Assert.Equal("{0.0.0.00000000}.{aaa}",
            DeviceResolver.PickBestMatch(Endpoints, "USB Audio CODEC", pinnedId: "{0.0.0.00000000}.{stale}"));
    }

    [Fact]
    public void NoMatch_ReturnsNull()
    {
        Assert.Null(DeviceResolver.PickBestMatch(Endpoints, "Focusrite Scarlett", pinnedId: null));
    }

    [Fact]
    public void EmptyEndpointList_ReturnsNull()
    {
        Assert.Null(DeviceResolver.PickBestMatch([], "USB Audio CODEC", pinnedId: null));
    }

    [Fact]
    public void SeveralNameMatches_FirstInEnumerationOrderWins()
    {
        AudioEndpointInfo[] twins =
        [
            new("{0.0.0.00000000}.{one}", "Speakers (USB Audio CODEC )"),
            new("{0.0.0.00000000}.{two}", "Speakers (USB Audio CODEC )"),
        ];

        Assert.Equal("{0.0.0.00000000}.{one}", DeviceResolver.PickBestMatch(twins, "USB Audio CODEC", pinnedId: null));
    }

    [Fact]
    public void EmptyPinnedId_IsTreatedAsUnpinned()
    {
        Assert.Equal("{0.0.0.00000000}.{aaa}",
            DeviceResolver.PickBestMatch(Endpoints, "USB Audio CODEC", pinnedId: ""));
    }

    [Fact]
    public void PinnedId_IsMatchedExactly_IncludingCase()
    {
        // Ordinal comparison: a differently-cased pin is not "present", so the
        // name match is used instead.
        Assert.Equal("{0.0.0.00000000}.{aaa}",
            DeviceResolver.PickBestMatch(Endpoints, "USB Audio CODEC", pinnedId: "{0.0.0.00000000}.{BBB}"));
    }

    [Fact]
    public void PinnedId_WinsEvenWhenNoNameMatches()
    {
        Assert.Equal("{0.0.0.00000000}.{ccc}",
            DeviceResolver.PickBestMatch(Endpoints, "Focusrite Scarlett", pinnedId: "{0.0.0.00000000}.{ccc}"));
    }

    [Fact]
    public void EmptyName_MatchesTheFirstEndpoint()
    {
        // string.Contains("") is always true. Settings.Normalize replaces a blank
        // name with the default, which is what keeps this from attaching to an
        // arbitrary device.
        Assert.Equal("{0.0.0.00000000}.{aaa}", DeviceResolver.PickBestMatch(Endpoints, "", pinnedId: null));
    }

    [Fact]
    public void EndpointWithEmptyName_IsSkippedByANonEmptyQuery()
    {
        // ReadNameFromRegistry returns "" when the registry key is missing.
        AudioEndpointInfo[] list =
        [
            new("{0.0.0.00000000}.{noname}", ""),
            new("{0.0.0.00000000}.{aaa}", "Hogtalare (USB Audio CODEC )"),
        ];

        Assert.Equal("{0.0.0.00000000}.{aaa}", DeviceResolver.PickBestMatch(list, "USB Audio CODEC", pinnedId: null));
    }
}
