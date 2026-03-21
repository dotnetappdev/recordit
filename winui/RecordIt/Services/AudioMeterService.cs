using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RecordIt.Services;

/// <summary>
/// Provides real-time audio peak levels using the Windows Core Audio
/// IAudioMeterInformation COM interface. Works for both render (desktop/speakers)
/// and capture (microphone) endpoints.
/// </summary>
public sealed class AudioMeterService : IDisposable
{
    // ── COM GUIDs ─────────────────────────────────────────────────────────
    private static readonly Guid CLSID_MMDeviceEnumerator =
        new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IID_IMMDeviceEnumerator =
        new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid IID_IAudioMeterInformation =
        new("C02216F6-8C67-4B5B-9D00-D008E73E0064");

    // ── COM Interfaces ────────────────────────────────────────────────────
    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask,
            out IMMDeviceCollection ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role,
            out IMMDevice ppEndpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId,
            out IMMDevice ppDevice);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr pClient);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint pcDevices);
        [PreserveSig] int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig] int OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out uint pdwState);
    }

    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int cProps);
        [PreserveSig] int GetAt(int iProp, out PROPERTYKEY pkey);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);
        [PreserveSig] int Commit();
    }

    [ComImport]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float pfPeak);
        [PreserveSig] int GetMeteringChannelCount(out uint pnChannelCount);
        [PreserveSig] int GetChannelsPeakValues(uint u32ChannelCount,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] float[] afPeakValues);
        [PreserveSig] int QueryHardwareSupport(out uint pdwHardwareSupportMask);
    }

    // ── Structs ───────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid FormatId;
        public int PropertyId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pointerVal;
    }

    // PKEY_Device_FriendlyName
    private static readonly PROPERTYKEY PKEY_FriendlyName = new()
    {
        FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        PropertyId = 14
    };

    // DataFlow
    private const int eRender  = 0;
    private const int eCapture = 1;
    private const int eAll     = 2;
    // Role
    private const int eMultimedia = 1;
    // DeviceState
    private const uint DEVICE_STATE_ACTIVE = 0x1;
    // CLSCTX_ALL
    private const uint CLSCTX_ALL = 0x17;

    // ── P/Invoke ──────────────────────────────────────────────────────────
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(
        in Guid rclsid, IntPtr pUnkOuter, uint dwClsCtx,
        in Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

    // ── State ─────────────────────────────────────────────────────────────
    private IMMDeviceEnumerator? _enumerator;
    private bool _disposed;

    public AudioMeterService()
    {
        try
        {
            CoCreateInstance(
                CLSID_MMDeviceEnumerator, IntPtr.Zero, CLSCTX_ALL,
                IID_IMMDeviceEnumerator, out var obj);
            _enumerator = (IMMDeviceEnumerator)obj;
        }
        catch { /* COM not available / sandboxed */ }
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Peak level (0.0–1.0) of the default playback device (desktop audio).</summary>
    public float GetDesktopPeak()
    {
        try
        {
            if (_enumerator == null) return 0f;
            _enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out var dev);
            return GetDevicePeak(dev);
        }
        catch { return 0f; }
    }

    /// <summary>Peak level (0.0–1.0) of the default microphone.</summary>
    public float GetMicPeak()
    {
        try
        {
            if (_enumerator == null) return 0f;
            _enumerator.GetDefaultAudioEndpoint(eCapture, eMultimedia, out var dev);
            return GetDevicePeak(dev);
        }
        catch { return 0f; }
    }

    /// <summary>
    /// Returns all active render endpoints with their current peak level.
    /// </summary>
    public List<AudioEndpointInfo> GetAllRenderEndpoints()
        => GetEndpoints(eRender);

    /// <summary>
    /// Returns all active capture endpoints with their current peak level.
    /// </summary>
    public List<AudioEndpointInfo> GetAllCaptureEndpoints()
        => GetEndpoints(eCapture);

    private List<AudioEndpointInfo> GetEndpoints(int dataFlow)
    {
        var list = new List<AudioEndpointInfo>();
        try
        {
            if (_enumerator == null) return list;
            _enumerator.EnumAudioEndpoints(dataFlow, DEVICE_STATE_ACTIVE, out var col);
            col.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                col.Item(i, out var dev);
                var name = GetFriendlyName(dev);
                var peak = GetDevicePeak(dev);
                list.Add(new AudioEndpointInfo { Name = name, PeakLevel = peak });
            }
        }
        catch { }
        return list;
    }

    private float GetDevicePeak(IMMDevice device)
    {
        try
        {
            var iid = IID_IAudioMeterInformation;
            device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var meterObj);
            var meter = (IAudioMeterInformation)meterObj;
            meter.GetPeakValue(out float peak);
            return Math.Clamp(peak, 0f, 1f);
        }
        catch { return 0f; }
    }

    private string GetFriendlyName(IMMDevice device)
    {
        try
        {
            device.OpenPropertyStore(0 /* STGM_READ */, out var store);
            var key = PKEY_FriendlyName;
            store.GetValue(ref key, out var pv);
            if (pv.vt == 31 /* VT_LPWSTR */ && pv.pointerVal != IntPtr.Zero)
            {
                var name = Marshal.PtrToStringUni(pv.pointerVal);
                return name ?? "Unknown";
            }
        }
        catch { }

        try
        {
            device.GetId(out var id);
            return id ?? "Unknown";
        }
        catch { return "Unknown"; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_enumerator != null)
        {
            Marshal.ReleaseComObject(_enumerator);
            _enumerator = null;
        }
    }
}

public class AudioEndpointInfo
{
    public string Name { get; set; } = "";
    public float PeakLevel { get; set; }
}
