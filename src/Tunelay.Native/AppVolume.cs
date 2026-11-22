using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Tunelay.Native;

/// <summary>An app's slider in the Windows volume mixer, on the default output.</summary>
public static class AppVolume
{
    public static int? Get(string processName)
    {
        int? percent = null;
        Each(processName, volume =>
        {
            volume.Get(out var level);
            percent ??= (int)Math.Round(level * 100);
        });
        return percent;
    }

    /// <summary>False when the app has no audio session yet (it has played nothing since it started).</summary>
    public static bool Set(string processName, int percent)
    {
        var found = false;
        Each(processName, volume =>
        {
            var context = Guid.Empty;
            volume.Set(Math.Clamp(percent, 0, 100) / 100f, ref context);
            found = true;
        });
        return found;
    }

    private static void Each(string processName, Action<IVolume> action)
    {
        var processes = Process.GetProcessesByName(processName);
        var ids = processes.Select(p => (uint)p.Id).ToHashSet();
        foreach (var process in processes)
        {
            process.Dispose();
        }

        if (ids.Count == 0)
        {
            return;
        }

        try
        {
            ((IDeviceEnumerator)new DeviceEnumerator()).GetDefault(0, 1, out var device);
            var iid = typeof(ISessionManager).GUID;
            device.Activate(ref iid, 23, 0, out var manager);
            ((ISessionManager)manager).GetList(out var list);
            list.GetCount(out var count);
            for (var i = 0; i < count; i++)
            {
                list.Get(i, out var session);
                if (((ISessionControl)session).GetProcessId(out var id) >= 0 && ids.Contains(id))
                {
                    action((IVolume)session);
                }
            }
        }
        catch (COMException)
        {
            // No output device, or it was unplugged just now.
        }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class DeviceEnumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceEnumerator
    {
        void Skip0();

        void GetDefault(int flow, int role, out IDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice
    {
        void Activate(ref Guid iid, int context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionManager
    {
        void Skip0();

        void Skip1();

        void GetList(out ISessionList list);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionList
    {
        void GetCount(out int count);

        void Get(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }

    // Only GetProcessId is used; the eleven members before it just keep the vtable order.
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionControl
    {
        void S0(); void S1(); void S2(); void S3(); void S4(); void S5();
        void S6(); void S7(); void S8(); void S9(); void S10();

        [PreserveSig]
        int GetProcessId(out uint id);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVolume
    {
        void Set(float level, ref Guid context);

        void Get(out float level);
    }
}
