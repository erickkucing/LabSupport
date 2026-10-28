using System;
using System.Runtime.InteropServices;
using LabSupport.Shared.Protocol;

namespace LabSupport.Shared.Services;

public enum EDataFlow
{
    eRender,
    eCapture,
    eAll,
    EDataFlow_enum_count
}

public enum ERole
{
    eConsole,
    eMultimedia,
    eCommunications,
    ERole_enum_count
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject
{
}

[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IntPtr ppDevices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
}

[Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

    [PreserveSig]
    int OpenPropertyStore(uint stgmAccess, out IntPtr ppProperties);

    [PreserveSig]
    int GetId(out IntPtr ppstrId);

    [PreserveSig]
    int GetState(out uint pdwState);
}

[Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig]
    int RegisterControlChangeNotify(IntPtr pNotify);

    [PreserveSig]
    int UnregisterControlChangeNotify(IntPtr pNotify);

    [PreserveSig]
    int GetChannelCount(out uint pnChannelCount);

    [PreserveSig]
    int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);

    [PreserveSig]
    int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);

    [PreserveSig]
    int GetMasterVolumeLevel(out float pfLevelDB);

    [PreserveSig]
    int GetMasterVolumeLevelScalar(out float pfLevel);

    [PreserveSig]
    int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);

    [PreserveSig]
    int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);

    [PreserveSig]
    int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);

    [PreserveSig]
    int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);

    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
}

public class AudioService
{
    private static readonly Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private const uint CLSCTX_ALL = 23;

    // In-memory simulation fallback for cross-platform / testing on non-Windows OS
    private static bool _simulatedMute = false;
    private static float _simulatedVolume = 0.75f;

    public static bool IsSupported => OperatingSystem.IsWindows();

    private static IAudioEndpointVolume? GetDefaultVolumeEndpoint()
    {
        if (!OperatingSystem.IsWindows()) return null;

        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device);
            if (hr != 0 || device == null) return null;

            var iid = IID_IAudioEndpointVolume;
            hr = device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var volumeObj);
            if (hr != 0 || volumeObj == null) return null;

            return (IAudioEndpointVolume)volumeObj;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (device != null) try { Marshal.ReleaseComObject(device); } catch { }
            if (enumerator != null) try { Marshal.ReleaseComObject(enumerator); } catch { }
        }
    }

    public virtual bool SetMute(bool mute)
    {
        if (!OperatingSystem.IsWindows())
        {
            _simulatedMute = mute;
            return true;
        }

        try
        {
            var endpoint = GetDefaultVolumeEndpoint();
            if (endpoint == null)
            {
                _simulatedMute = mute;
                return true;
            }

            var context = Guid.Empty;
            int hr = endpoint.SetMute(mute, ref context);
            Marshal.ReleaseComObject(endpoint);
            return hr == 0;
        }
        catch
        {
            _simulatedMute = mute;
            return false;
        }
    }

    public virtual bool GetMute(out bool isMuted)
    {
        if (!OperatingSystem.IsWindows())
        {
            isMuted = _simulatedMute;
            return true;
        }

        try
        {
            var endpoint = GetDefaultVolumeEndpoint();
            if (endpoint == null)
            {
                isMuted = _simulatedMute;
                return true;
            }

            int hr = endpoint.GetMute(out isMuted);
            Marshal.ReleaseComObject(endpoint);
            return hr == 0;
        }
        catch
        {
            isMuted = _simulatedMute;
            return false;
        }
    }

    public virtual bool SetMasterVolume(float volume)
    {
        // Normalize 0-100 down to 0.0 - 1.0 if needed
        float scalar = volume;
        if (scalar > 1.0f) scalar /= 100.0f;
        scalar = Math.Clamp(scalar, 0.0f, 1.0f);

        if (!OperatingSystem.IsWindows())
        {
            _simulatedVolume = scalar;
            return true;
        }

        try
        {
            var endpoint = GetDefaultVolumeEndpoint();
            if (endpoint == null)
            {
                _simulatedVolume = scalar;
                return true;
            }

            var context = Guid.Empty;
            int hr = endpoint.SetMasterVolumeLevelScalar(scalar, ref context);
            Marshal.ReleaseComObject(endpoint);
            return hr == 0;
        }
        catch
        {
            _simulatedVolume = scalar;
            return false;
        }
    }

    public virtual bool GetMasterVolume(out float volumePercent)
    {
        if (!OperatingSystem.IsWindows())
        {
            volumePercent = _simulatedVolume * 100.0f;
            return true;
        }

        try
        {
            var endpoint = GetDefaultVolumeEndpoint();
            if (endpoint == null)
            {
                volumePercent = _simulatedVolume * 100.0f;
                return true;
            }

            int hr = endpoint.GetMasterVolumeLevelScalar(out float scalar);
            Marshal.ReleaseComObject(endpoint);
            volumePercent = scalar * 100.0f;
            return hr == 0;
        }
        catch
        {
            volumePercent = _simulatedVolume * 100.0f;
            return false;
        }
    }

    public virtual AudioStateResult GetAudioState()
    {
        GetMute(out bool muted);
        GetMasterVolume(out float vol);
        return new AudioStateResult
        {
            IsMuted = muted,
            MasterVolume = vol
        };
    }
}
