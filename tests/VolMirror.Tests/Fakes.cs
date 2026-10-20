using System.Reflection;
using VolMirror.Interop;

namespace VolMirror.Tests;

/// Reflection access to private state. TrayApp and EndpointWatcher reach COM,
/// WinForms and the registry from their constructors and timers, and production
/// code offers no seams, so these tests drive the private logic directly. Every
/// lookup fails loudly if a member is renamed, rather than silently testing nothing.
internal static class Private
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Set(object target, string field, object? value) =>
        Field(target, field).SetValue(target, value);

    public static T Get<T>(object target, string field) => (T)Field(target, field).GetValue(target)!;

    public static object? Call(object target, string method, params object?[] args)
    {
        MethodInfo info = target.GetType().GetMethod(method, Flags)
            ?? throw new MissingMethodException(target.GetType().Name, method);

        // DoNotWrapExceptions so a test sees the real exception type, not
        // TargetInvocationException.
        return info.Invoke(target, BindingFlags.DoNotWrapExceptions, binder: null, args, culture: null);
    }

    private static FieldInfo Field(object target, string name) =>
        target.GetType().GetField(name, Flags)
            ?? throw new MissingFieldException(target.GetType().Name, name);
}

/// A managed stand-in for the CoreAudio endpoint. Release() on it throws
/// ArgumentException inside Marshal.ReleaseComObject, which EndpointWatcher
/// already swallows for "not an RCW".
internal sealed class FakeEndpoint : IAudioEndpointVolume
{
    public float Scalar { get; set; } = 0.5f;
    public bool Muted { get; set; }
    public int ScalarHr { get; set; }
    public int MuteHr { get; set; }
    public Exception? Throw { get; set; }
    public int Reads { get; private set; }

    public int GetMasterVolumeLevelScalar(out float level)
    {
        Reads++;
        if (Throw is not null) throw Throw;
        level = Scalar;
        return ScalarHr;
    }

    public int GetMute(out bool mute)
    {
        mute = Muted;
        return MuteHr;
    }

    public int RegisterControlChangeNotify(IntPtr cb) => throw new NotSupportedException();
    public int UnregisterControlChangeNotify(IntPtr cb) => throw new NotSupportedException();
    public int GetChannelCount(out uint count) => throw new NotSupportedException();
    public int SetMasterVolumeLevel(float leveldB, ref Guid ctx) => throw new NotSupportedException();
    public int SetMasterVolumeLevelScalar(float level, ref Guid ctx) => throw new NotSupportedException();
    public int GetMasterVolumeLevel(out float leveldB) => throw new NotSupportedException();
    public int SetChannelVolumeLevel(uint ch, float leveldB, ref Guid ctx) => throw new NotSupportedException();
    public int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx) => throw new NotSupportedException();
    public int GetChannelVolumeLevel(uint ch, out float leveldB) => throw new NotSupportedException();
    public int GetChannelVolumeLevelScalar(uint ch, out float level) => throw new NotSupportedException();
    public int SetMute(bool mute, ref Guid ctx) => throw new NotSupportedException();
    public int GetVolumeStepInfo(out uint step, out uint stepCount) => throw new NotSupportedException();
    public int VolumeStepUp(ref Guid ctx) => throw new NotSupportedException();
    public int VolumeStepDown(ref Guid ctx) => throw new NotSupportedException();
    public int QueryHardwareSupport(out uint mask) => throw new NotSupportedException();
    public int GetVolumeRange(out float minDb, out float maxDb, out float incDb) => throw new NotSupportedException();
}
