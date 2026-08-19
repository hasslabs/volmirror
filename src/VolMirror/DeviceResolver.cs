using Microsoft.Win32;
using VolMirror.Interop;

namespace VolMirror;

public readonly record struct AudioEndpointInfo(string Id, string Name);

/// Finds an audio endpoint by name rather than by ID.
///
/// Endpoint IDs are NOT stable: installing Equalizer APO made Windows
/// re-enumerate the UCA202 and issue it a fresh GUID, which stranded a
/// hard-coded ID. The friendly name survives that, so it is the better key.
///
/// Names come from the registry because IPropertyStore.GetValue fails to
/// marshal PROPVARIANT on this machine.
public static class DeviceResolver
{
    private const string RenderKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render";

    // PKEY_Device_DeviceDesc and PKEY_DeviceInterface_FriendlyName, as the
    // registry spells them.
    private const string DescValue = "{a45c254e-df1c-4efd-8020-67d146a850e0},2";
    private const string InterfaceValue = "{b3f8fa53-0004-438e-9003-51a46e139bfc},6";

    /// Picks an endpoint: a pinned ID if it is still present, else the first
    /// whose name contains <paramref name="nameContains"/>. Null if none match.
    public static string? PickBestMatch(
        IEnumerable<AudioEndpointInfo> endpoints, string nameContains, string? pinnedId)
    {
        var list = endpoints.ToList();

        if (!string.IsNullOrEmpty(pinnedId) && list.Any(e => e.Id == pinnedId))
            return pinnedId;

        foreach (var endpoint in list)
        {
            if (endpoint.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
                return endpoint.Id;
        }

        return null;
    }

    /// Active render endpoints, with names read from the registry.
    public static List<AudioEndpointInfo> ListActiveRenderEndpoints()
    {
        var result = new List<AudioEndpointInfo>();

        Type? comType = Type.GetTypeFromCLSID(CoreAudioGuids.MMDeviceEnumerator);
        if (comType is null || Activator.CreateInstance(comType) is not IMMDeviceEnumerator enumerator)
            return result;

        if (enumerator.EnumAudioEndpoints(0 /* eRender */, 1 /* DEVICE_STATE_ACTIVE */,
                out IMMDeviceCollection collection) != 0)
            return result;

        if (collection.GetCount(out uint count) != 0)
            return result;

        for (uint i = 0; i < count; i++)
        {
            if (collection.Item(i, out IMMDevice device) != 0)
                continue;
            if (device.GetId(out string id) != 0)
                continue;

            result.Add(new AudioEndpointInfo(id, ReadNameFromRegistry(id)));
        }

        return result;
    }

    private static string ReadNameFromRegistry(string endpointId)
    {
        // Endpoint IDs look like "{0.0.0.00000000}.{guid}"; the registry is keyed
        // on the trailing brace-wrapped GUID alone.
        int lastBrace = endpointId.LastIndexOf('{');
        if (lastBrace < 0)
            return string.Empty;

        string guid = endpointId[lastBrace..];

        using var key = Registry.LocalMachine.OpenSubKey($@"{RenderKey}\{guid}\Properties");
        if (key is null)
            return string.Empty;

        string? desc = key.GetValue(DescValue) as string;
        string? iface = key.GetValue(InterfaceValue) as string;

        return (desc, iface) switch
        {
            (not null, not null) => $"{desc} ({iface})",
            (null, not null) => iface,
            (not null, null) => desc,
            _ => string.Empty
        };
    }
}
