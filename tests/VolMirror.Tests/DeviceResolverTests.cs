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
}
