using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("VolumeMixer")]
[assembly: AssemblyDescription("Lightweight Windows tray volume mixer")]
[assembly: AssemblyProduct("VolumeMixer")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]
[assembly: ComVisible(false)]

namespace VolumeMixer
{
    // ============================================================
    // Property keys + variants
    // ============================================================
    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    // The native union includes a counted pointer (16 bytes on x64), even
    // when we only read strings/integers. PropVariantClear writes all 24 bytes.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pwszVal;
        [FieldOffset(8)] public uint uintVal;
        [FieldOffset(8)] public int intVal;
    }

    internal static class PKey
    {
        public static readonly PROPERTYKEY Device_FriendlyName = new PROPERTYKEY
        {
            fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
            pid = 14
        };
        public static readonly PROPERTYKEY Device_DeviceDesc = new PROPERTYKEY
        {
            fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
            pid = 2
        };
        public static readonly PROPERTYKEY AudioEndpoint_FormFactor = new PROPERTYKEY
        {
            fmtid = new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"),
            pid = 0
        };
    }

    // ============================================================
    // Windows Core Audio API COM interop
    // ============================================================
    internal static class CoreAudio
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        public class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int Item(int index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
            [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore propertyStore);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string deviceId);
            [PreserveSig] int GetState(out int state);
        }

        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IPropertyStore
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetAt(int index, out PROPERTYKEY key);
            [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
            int NotImpl0();
            int NotImpl1();
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioSessionManager2
        {
            int NotImpl0();
            int NotImpl1();
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
            [PreserveSig] int RegisterSessionNotification(IAudioSessionNotification SessionNotification);
            [PreserveSig] int UnregisterSessionNotification(IAudioSessionNotification SessionNotification);
        }

        [ComImport, Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioSessionNotification
        {
            [PreserveSig] int OnSessionCreated(IAudioSessionControl newSession);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int sessionCount);
            [PreserveSig] int GetSession(int sessionCount, out IAudioSessionControl session);
        }

        [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioSessionControl
        {
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
            int NotImpl0();
            [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
        }

        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioSessionControl2
        {
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string n);
            int NotImpl_SetDisplayName();
            [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
            int NotImpl_SetIconPath();
            int NotImpl_GetGroupingParam();
            int NotImpl_SetGroupingParam();
            int NotImpl_RegisterAudioSessionNotification();
            int NotImpl_UnregisterAudioSessionNotification();
            [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetProcessId(out uint pid);
            [PreserveSig] int IsSystemSoundsSession();
        }

        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ISimpleAudioVolume
        {
            [PreserveSig] int SetMasterVolume(float level, ref Guid eventContext);
            [PreserveSig] int GetMasterVolume(out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioEndpointVolume
        {
            int NotImpl_RegisterControlChangeNotify();
            int NotImpl_UnregisterControlChangeNotify();
            [PreserveSig] int GetChannelCount(out int channelCount);
            [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid eventContext);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
            [PreserveSig] int GetMasterVolumeLevel(out float level);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            int NotImpl_SetChannelVolumeLevel();
            int NotImpl_SetChannelVolumeLevelScalar();
            int NotImpl_GetChannelVolumeLevel();
            int NotImpl_GetChannelVolumeLevelScalar();
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
            [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
            [PreserveSig] int VolumeStepUp(ref Guid eventContext);
            [PreserveSig] int VolumeStepDown(ref Guid eventContext);
            [PreserveSig] int QueryHardwareSupport(out uint hardwareSupportMask);
            [PreserveSig] int GetVolumeRange(out float volumeMindB, out float volumeMaxdB, out float volumeIncrementdB);
        }

        public static readonly Guid IID_IAudioSessionManager2 = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
        public static readonly Guid IID_IAudioEndpointVolume  = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
    }

    // ============================================================
    // IPolicyConfig (undocumented but stable on Win10/11)
    // ============================================================
    internal static class PolicyConfig
    {
        [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
        public class CPolicyConfigClient { }

        [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IPolicyConfig
        {
            int NotImpl0(); int NotImpl1(); int NotImpl2(); int NotImpl3();
            int NotImpl4(); int NotImpl5(); int NotImpl6(); int NotImpl7();
            int NotImpl8(); int NotImpl9();
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
            int NotImpl10();
        }

        public static void SetDefault(string deviceId)
        {
            var pc = (IPolicyConfig)new CPolicyConfigClient();
            try
            {
                pc.SetDefaultEndpoint(deviceId, 0); // eConsole
                pc.SetDefaultEndpoint(deviceId, 1); // eMultimedia
                pc.SetDefaultEndpoint(deviceId, 2); // eCommunications
            }
            finally { Marshal.ReleaseComObject(pc); }
        }
    }

    // ============================================================
    // Per-app output device routing via WinRT IAudioPolicyConfig
    // (undocumented Windows API; works on Win10 1803+ / Win11)
    // ============================================================
    internal static class AudioPolicyConfig
    {
        [DllImport("combase.dll", PreserveSig = true)]
        private static extern int WindowsCreateString(
            [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
            int length, out IntPtr hstring);

        [DllImport("combase.dll", PreserveSig = true)]
        private static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("combase.dll", PreserveSig = true)]
        private static extern int RoGetActivationFactory(
            IntPtr activatableClassId, ref Guid iid,
            [MarshalAs(UnmanagedType.IUnknown)] out object factory);

        // The IInspectable-derived factory — 19 unused method slots before the
        // three methods we care about. Order matches the published vtable.
        [Guid("ab3d4648-e242-459f-b02f-541c70306324")]
        [InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
        private interface IAudioPolicyConfigFactory
        {
            int Stub01(); int Stub02(); int Stub03(); int Stub04();
            int Stub05(); int Stub06(); int Stub07(); int Stub08();
            int Stub09(); int Stub10(); int Stub11(); int Stub12();
            int Stub13(); int Stub14(); int Stub15(); int Stub16();
            int Stub17(); int Stub18(); int Stub19();

            [PreserveSig] int SetPersistedDefaultAudioEndpoint(uint processId, int flow, int role, IntPtr deviceIdHstring);
            [PreserveSig] int GetPersistedDefaultAudioEndpoint(uint processId, int flow, int role, out IntPtr deviceIdHstring);
            [PreserveSig] int ClearAllPersistedApplicationDefaultEndpoints();
        }

        private static IAudioPolicyConfigFactory _cached;
        private static bool _initFailed;

        private static IAudioPolicyConfigFactory Factory
        {
            get
            {
                if (_cached != null) return _cached;
                if (_initFailed) return null;

                IntPtr classNameH = IntPtr.Zero;
                try
                {
                    string className = "Windows.Media.Internal.AudioPolicyConfig";
                    int hr = WindowsCreateString(className, className.Length, out classNameH);
                    if (hr != 0) { _initFailed = true; return null; }

                    object factory;
                    Guid iid = new Guid("ab3d4648-e242-459f-b02f-541c70306324");
                    hr = RoGetActivationFactory(classNameH, ref iid, out factory);
                    if (hr != 0) { _initFailed = true; return null; }
                    _cached = (IAudioPolicyConfigFactory)factory;
                    return _cached;
                }
                catch
                {
                    _initFailed = true;
                    return null;
                }
                finally
                {
                    if (classNameH != IntPtr.Zero) WindowsDeleteString(classNameH);
                }
            }
        }

        public static bool IsSupported { get { return Factory != null; } }

        // Per-app endpoint IDs need a "persisted" prefix that Windows recognizes.
        // The format from IMMDevice.GetId is e.g. "{0.0.0.00000000}.{guid}";
        // we wrap it as "\\?\SWD#MMDEVAPI#{0.0.0.00000000}.{guid}#{e6327cad-dcec-4949-ae8a-991e976a79d2}".
        private static string MakePersistedId(string endpointId, bool render)
        {
            if (string.IsNullOrEmpty(endpointId)) return "";
            return @"\\?\SWD#MMDEVAPI#" + endpointId + "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
        }

        public static bool SetDefaultForApp(uint processId, string endpointId)
        {
            var f = Factory;
            if (f == null) return false;

            string persistedId = string.IsNullOrEmpty(endpointId)
                ? ""
                : MakePersistedId(endpointId, true);

            IntPtr h = IntPtr.Zero;
            try
            {
                int hr = WindowsCreateString(persistedId, persistedId.Length, out h);
                if (hr != 0) return false;
                // role: 0=eConsole, 1=eMultimedia, 2=eCommunications. Set all three so
                // the app gets routed regardless of which role it requests.
                f.SetPersistedDefaultAudioEndpoint(processId, 0 /*eRender*/, 0, h);
                f.SetPersistedDefaultAudioEndpoint(processId, 0, 1, h);
                f.SetPersistedDefaultAudioEndpoint(processId, 0, 2, h);
                return true;
            }
            catch { return false; }
            finally { if (h != IntPtr.Zero) WindowsDeleteString(h); }
        }

        public static string GetDefaultForApp(uint processId)
        {
            var f = Factory;
            if (f == null) return null;
            try
            {
                IntPtr outH;
                int hr = f.GetPersistedDefaultAudioEndpoint(processId, 0 /*eRender*/, 0 /*eConsole*/, out outH);
                if (hr != 0 || outH == IntPtr.Zero) return null;
                try
                {
                    uint length;
                    IntPtr buf = WindowsGetStringRawBuffer(outH, out length);
                    if (buf == IntPtr.Zero || length == 0) return null;
                    string s = Marshal.PtrToStringUni(buf, (int)length);
                    return ExtractEndpointId(s);
                }
                finally { WindowsDeleteString(outH); }
            }
            catch { return null; }
        }

        [DllImport("combase.dll", PreserveSig = true)]
        private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

        // Strip the "\\?\SWD#MMDEVAPI#...#{interfaceguid}" wrapper to get the raw endpoint ID.
        private static string ExtractEndpointId(string persisted)
        {
            if (string.IsNullOrEmpty(persisted)) return null;
            int start = persisted.IndexOf("MMDEVAPI#", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return persisted;
            start += "MMDEVAPI#".Length;
            int end = persisted.IndexOf('#', start);
            if (end < 0) end = persisted.Length;
            return persisted.Substring(start, end - start);
        }
    }

    // ============================================================
    // Domain models + audio engine
    // ============================================================
    internal sealed class AudioSession
    {
        public uint ProcessId;
        public string DisplayName;
        public string ProcessName;
        public string ExecutablePath;
        public Image IconImage;
        public bool IsSystemSounds;
        public CoreAudio.ISimpleAudioVolume Volume;
        public CoreAudio.IAudioSessionControl2 Control;
    }

    internal sealed class AudioMaster
    {
        public CoreAudio.IAudioEndpointVolume Endpoint;
        public string DeviceId;

        public float Volume
        {
            get
            {
                float v;
                int hr = Endpoint.GetMasterVolumeLevelScalar(out v);
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                return v;
            }
            set
            {
                float clamped = Math.Max(0f, Math.Min(1f, value));
                Guid g = Guid.Empty;
                int hr = Endpoint.SetMasterVolumeLevelScalar(clamped, ref g);
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
            }
        }

        public bool Mute
        {
            get
            {
                bool m;
                int hr = Endpoint.GetMute(out m);
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                return m;
            }
            set
            {
                Guid g = Guid.Empty;
                int hr = Endpoint.SetMute(value, ref g);
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
            }
        }

        public bool TryGetVolume(out float volume)
        {
            volume = 0f;
            try { return Endpoint != null && Endpoint.GetMasterVolumeLevelScalar(out volume) == 0; }
            catch { volume = 0f; return false; }
        }

        public bool TryGetMute(out bool muted)
        {
            muted = false;
            try { return Endpoint != null && Endpoint.GetMute(out muted) == 0; }
            catch { muted = false; return false; }
        }

        public bool TrySetVolume(float volume)
        {
            try
            {
                Guid g = Guid.Empty;
                float clamped = Math.Max(0f, Math.Min(1f, volume));
                return Endpoint != null && Endpoint.SetMasterVolumeLevelScalar(clamped, ref g) == 0;
            }
            catch { return false; }
        }

        public bool TrySetMute(bool muted)
        {
            try
            {
                Guid g = Guid.Empty;
                return Endpoint != null && Endpoint.SetMute(muted, ref g) == 0;
            }
            catch { return false; }
        }

        public bool TryStepVolume(int notches)
        {
            if (Endpoint == null || notches == 0) return false;
            try
            {
                uint step;
                uint stepCount;
                int hr = Endpoint.GetVolumeStepInfo(out step, out stepCount);
                int stepsPerNotch = 2;
                if (hr == 0 && stepCount > 1)
                    stepsPerNotch = Math.Max(1, (int)Math.Round((stepCount - 1) * 0.02));

                int total = Math.Min(40, Math.Abs(notches) * stepsPerNotch);
                Guid g = Guid.Empty;
                for (int i = 0; i < total; i++)
                {
                    hr = notches > 0 ? Endpoint.VolumeStepUp(ref g) : Endpoint.VolumeStepDown(ref g);
                    if (hr != 0) return false;
                }

                float v;
                bool muted;
                if (TryGetVolume(out v) && v > 0.001f && TryGetMute(out muted) && muted)
                    TrySetMute(false);
                return true;
            }
            catch { return false; }
        }
    }

    internal sealed class DeviceInfo
    {
        public string Id;
        public string Name;
        public string Subtitle;
        public uint FormFactor;
        public bool IsDefault;
    }

    internal static class AudioEngine
    {
        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PROPVARIANT pvar);

        private static void ReleaseCom(object o)
        {
            try
            {
                if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
            }
            catch { }
        }

        public static void ReleaseMaster(AudioMaster master)
        {
            if (master == null) return;
            object endpoint = master.Endpoint;
            master.Endpoint = null;
            ReleaseCom(endpoint);
        }

        public static void ReleaseSessions(IEnumerable<AudioSession> sessions)
        {
            if (sessions == null) return;
            foreach (var session in sessions)
            {
                if (session == null) continue;
                object volume = session.Volume;
                object control = session.Control;
                session.Volume = null;
                session.Control = null;
                ReleaseCom(volume);
                if (!ReferenceEquals(control, volume)) ReleaseCom(control);
            }
        }

        public static void AdoptSessionInterfaces(AudioSession target, AudioSession source)
        {
            if (target == null || source == null) return;
            object oldVolume = target.Volume;
            object oldControl = target.Control;
            target.Volume = source.Volume;
            target.Control = source.Control;
            source.Volume = null;
            source.Control = null;
            ReleaseCom(oldVolume);
            if (!ReferenceEquals(oldControl, oldVolume)) ReleaseCom(oldControl);
        }

        public static AudioMaster GetMaster()
        {
            return GetMasterForRole(1 /*eMultimedia*/);
        }

        public static List<AudioMaster> GetDefaultRoleMasters()
        {
            var result = new List<AudioMaster>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int[] roles = { 1 /*eMultimedia*/, 0 /*eConsole*/, 2 /*eCommunications*/ };

            for (int i = 0; i < roles.Length; i++)
            {
                try
                {
                    var m = GetMasterForRole(roles[i]);
                    string key = !string.IsNullOrEmpty(m.DeviceId) ? m.DeviceId : ("role:" + roles[i]);
                    if (seen.Add(key)) result.Add(m);
                    else ReleaseCom(m.Endpoint);
                }
                catch { }
            }

            return result;
        }

        private static AudioMaster GetMasterForRole(int role)
        {
            CoreAudio.IMMDeviceEnumerator enumerator = null;
            CoreAudio.IMMDevice device = null;
            try
            {
                enumerator = (CoreAudio.IMMDeviceEnumerator)new CoreAudio.MMDeviceEnumerator();
                int hr = enumerator.GetDefaultAudioEndpoint(0, role, out device);
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);

                string id = null;
                try { device.GetId(out id); } catch { }

                object o;
                Guid iid = CoreAudio.IID_IAudioEndpointVolume;
                hr = device.Activate(ref iid, 23, IntPtr.Zero, out o);
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                return new AudioMaster { Endpoint = (CoreAudio.IAudioEndpointVolume)o, DeviceId = id };
            }
            finally
            {
                ReleaseCom(device);
                ReleaseCom(enumerator);
            }
        }

        // Weak, bounded LRU cache: visible sessions keep images alive, while apps that
        // disappear can be collected without an unbounded process-lifetime cache.
        private sealed class IconCacheEntry
        {
            public WeakReference Image;
            public LinkedListNode<string> Node;
        }
        private const int IconCacheLimit = 64;
        private static readonly Dictionary<string, IconCacheEntry> _iconCache =
            new Dictionary<string, IconCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<string> _iconCacheLru = new LinkedList<string>();
        private static readonly object _iconCacheLock = new object();

        private static Image GetIconImageForExe(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return null;
            lock (_iconCacheLock)
            {
                IconCacheEntry entry;
                if (_iconCache.TryGetValue(exePath, out entry))
                {
                    Image cached = entry.Image.Target as Image;
                    if (cached != null)
                    {
                        _iconCacheLru.Remove(entry.Node);
                        _iconCacheLru.AddFirst(entry.Node);
                        return cached;
                    }
                    _iconCacheLru.Remove(entry.Node);
                    _iconCache.Remove(exePath);
                }
            }
            Image img = null;
            try
            {
                if (File.Exists(exePath))
                {
                    using (var ico = Icon.ExtractAssociatedIcon(exePath))
                    {
                        if (ico != null) img = ico.ToBitmap();
                    }
                }
            }
            catch { }
            lock (_iconCacheLock)
            {
                IconCacheEntry existing;
                if (_iconCache.TryGetValue(exePath, out existing))
                {
                    Image cached = existing.Image.Target as Image;
                    if (cached != null)
                    {
                        if (img != null) img.Dispose();
                        _iconCacheLru.Remove(existing.Node);
                        _iconCacheLru.AddFirst(existing.Node);
                        return cached;
                    }
                    _iconCacheLru.Remove(existing.Node);
                    _iconCache.Remove(exePath);
                }

                if (img != null)
                {
                    var node = _iconCacheLru.AddFirst(exePath);
                    _iconCache[exePath] = new IconCacheEntry
                    {
                        Image = new WeakReference(img),
                        Node = node,
                    };
                    while (_iconCache.Count > IconCacheLimit)
                    {
                        var last = _iconCacheLru.Last;
                        if (last == null) break;
                        _iconCache.Remove(last.Value);
                        _iconCacheLru.RemoveLast();
                    }
                }
            }
            return img;
        }

        public static List<AudioSession> EnumerateSessions()
        {
            // Phase 1 — pull session metadata sequentially (the COM enumerator is single-threaded).
            // Don't touch Process or icons here; defer to phase 2.
            var prelim = new List<AudioSession>();
            var seenPid = new HashSet<uint>();
            CoreAudio.IMMDeviceEnumerator enumerator = null;
            CoreAudio.IMMDevice device = null;
            CoreAudio.IAudioSessionManager2 mgr = null;
            CoreAudio.IAudioSessionEnumerator sessEnum = null;

            try
            {
                enumerator = (CoreAudio.IMMDeviceEnumerator)new CoreAudio.MMDeviceEnumerator();
                enumerator.GetDefaultAudioEndpoint(0, 1, out device);

                object o;
                Guid iid = CoreAudio.IID_IAudioSessionManager2;
                device.Activate(ref iid, 23, IntPtr.Zero, out o);
                mgr = (CoreAudio.IAudioSessionManager2)o;

                mgr.GetSessionEnumerator(out sessEnum);

                int count;
                sessEnum.GetCount(out count);

                for (int i = 0; i < count; i++)
                {
                    CoreAudio.IAudioSessionControl ctrl = null;
                    bool retained = false;
                    try
                    {
                        sessEnum.GetSession(i, out ctrl);
                        var ctrl2 = ctrl as CoreAudio.IAudioSessionControl2;
                        var vol = ctrl as CoreAudio.ISimpleAudioVolume;
                        if (ctrl2 == null || vol == null) continue;

                        int state;
                        ctrl.GetState(out state);
                        if (state == 2) continue;

                        uint pid;
                        ctrl2.GetProcessId(out pid);
                        bool isSystem = ctrl2.IsSystemSoundsSession() == 0;
                        if (!seenPid.Add(pid)) continue;

                        var s = new AudioSession
                        {
                            ProcessId = pid,
                            IsSystemSounds = isSystem,
                            Volume = vol,
                            Control = ctrl2,
                        };
                        if (isSystem)
                        {
                            s.DisplayName = Strings.SystemSounds;
                            s.ProcessName = "system";
                        }
                        prelim.Add(s);
                        retained = true;
                    }
                    finally
                    {
                        if (!retained) ReleaseCom(ctrl);
                    }
                }
            }
            finally
            {
                ReleaseCom(sessEnum);
                ReleaseCom(mgr);
                ReleaseCom(device);
                ReleaseCom(enumerator);
            }

            // Phase 2 — resolve process info + icon in PARALLEL (these are I/O bound:
            // Process.GetProcessById, MainModule.FileName, Icon.ExtractAssociatedIcon).
            // Sequential = N×40ms; parallel scales with cores. Big improvement on cold start.
            var nonSystem = prelim.Where(s => !s.IsSystemSounds).ToList();
            if (nonSystem.Count > 0)
            {
                System.Threading.Tasks.Parallel.ForEach(nonSystem, s =>
                {
                    string exe = null, procName = null, name = null;
                    try
                    {
                        using (var p = Process.GetProcessById((int)s.ProcessId))
                        {
                            exe = SafeGetMainModulePath(p);
                            procName = p.ProcessName;
                            name = !string.IsNullOrEmpty(exe)
                                ? Path.GetFileNameWithoutExtension(exe)
                                : p.ProcessName;
                        }
                    }
                    catch
                    {
                        try { s.Control.GetDisplayName(out name); } catch { }
                        if (string.IsNullOrEmpty(name)) name = "PID " + s.ProcessId;
                    }
                    s.ExecutablePath = exe;
                    s.ProcessName = procName;
                    s.DisplayName = TitleCase(name);
                    s.IconImage = GetIconImageForExe(exe); // cached on first miss, instant after
                });
            }

            return prelim;
        }

        public static List<DeviceInfo> EnumerateOutputDevices()
        {
            var result = new List<DeviceInfo>();
            CoreAudio.IMMDeviceEnumerator enumerator = null;
            CoreAudio.IMMDeviceCollection col = null;

            try
            {
                enumerator = (CoreAudio.IMMDeviceEnumerator)new CoreAudio.MMDeviceEnumerator();

                string defaultId = null;
                CoreAudio.IMMDevice def = null;
                try
                {
                    enumerator.GetDefaultAudioEndpoint(0, 1, out def);
                    if (def != null) def.GetId(out defaultId);
                }
                catch { }
                finally { ReleaseCom(def); }

                enumerator.EnumAudioEndpoints(0 /*eRender*/, 1 /*ACTIVE*/, out col);
                int count;
                col.GetCount(out count);

                for (int i = 0; i < count; i++)
                {
                    CoreAudio.IMMDevice d = null;
                    CoreAudio.IPropertyStore ps = null;
                    try
                    {
                        col.Item(i, out d);

                        string id;
                        d.GetId(out id);

                        string name = "(Bilinmeyen)";
                        string subtitle = "";
                        uint formFactor = 10;

                        if (d.OpenPropertyStore(0 /*STGM_READ*/, out ps) == 0 && ps != null)
                        {
                            name = ReadStringProp(ps, PKey.Device_FriendlyName) ?? name;
                            subtitle = ReadStringProp(ps, PKey.Device_DeviceDesc) ?? "";
                            formFactor = ReadUIntProp(ps, PKey.AudioEndpoint_FormFactor);
                        }

                        result.Add(new DeviceInfo
                        {
                            Id = id,
                            Name = CleanDeviceName(name, subtitle),
                            Subtitle = subtitle,
                            FormFactor = formFactor,
                            IsDefault = string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase)
                        });
                    }
                    finally
                    {
                        ReleaseCom(ps);
                        ReleaseCom(d);
                    }
                }
            }
            finally
            {
                ReleaseCom(col);
                ReleaseCom(enumerator);
            }

            // Default first, then alphabetical
            result.Sort((a, b) =>
            {
                if (a.IsDefault != b.IsDefault) return a.IsDefault ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return result;
        }

        public static void SetDefaultDevice(string deviceId)
        {
            PolicyConfig.SetDefault(deviceId);
        }

        // Per Microsoft docs, RegisterSessionNotification only delivers events AFTER
        // GetSessionEnumerator has been called on the SAME manager instance — and the
        // manager + listener references must stay alive for the subscription to hold.
        private static CoreAudio.IAudioSessionManager2 _notifMgr;
        private static SessionNotificationListener _notifListener;

        private sealed class SessionNotificationListener : CoreAudio.IAudioSessionNotification
        {
            private readonly Action _onCreated;
            public SessionNotificationListener(Action onCreated) { _onCreated = onCreated; }
            public int OnSessionCreated(CoreAudio.IAudioSessionControl newSession)
            {
                try { if (_onCreated != null) _onCreated(); } catch { }
                return 0; // S_OK
            }
        }

        public static void RegisterSessionListener(Action onSessionCreated)
        {
            UnregisterSessionListener();
            CoreAudio.IMMDeviceEnumerator enumerator = null;
            CoreAudio.IMMDevice device = null;
            try
            {
                enumerator = (CoreAudio.IMMDeviceEnumerator)new CoreAudio.MMDeviceEnumerator();
                enumerator.GetDefaultAudioEndpoint(0, 1, out device);
                object o;
                Guid iid = CoreAudio.IID_IAudioSessionManager2;
                device.Activate(ref iid, 23, IntPtr.Zero, out o);
                _notifMgr = (CoreAudio.IAudioSessionManager2)o;

                // Quirk: notifications stay silent until GetSessionEnumerator has been
                // invoked at least once on this manager. Discard the result, we just
                // need the side-effect of "priming" the manager.
                CoreAudio.IAudioSessionEnumerator enm;
                _notifMgr.GetSessionEnumerator(out enm);
                if (enm != null) Marshal.ReleaseComObject(enm);

                _notifListener = new SessionNotificationListener(onSessionCreated);
                _notifMgr.RegisterSessionNotification(_notifListener);
            }
            catch
            {
                UnregisterSessionListener();
            }
            finally
            {
                ReleaseCom(device);
                ReleaseCom(enumerator);
            }
        }

        public static void UnregisterSessionListener()
        {
            if (_notifMgr != null && _notifListener != null)
            {
                try { _notifMgr.UnregisterSessionNotification(_notifListener); } catch { }
            }
            _notifListener = null;
            if (_notifMgr != null)
            {
                try { Marshal.ReleaseComObject(_notifMgr); } catch { }
                _notifMgr = null;
            }
        }

        // Friendly name often = "Headphones (USB) Hardware Name". Pull a clean leading title.
        private static string CleanDeviceName(string friendly, string desc)
        {
            if (string.IsNullOrEmpty(friendly)) return desc ?? "";
            return friendly;
        }

        private static string ReadStringProp(CoreAudio.IPropertyStore ps, PROPERTYKEY key)
        {
            PROPVARIANT pv;
            int hr = ps.GetValue(ref key, out pv);
            if (hr != 0) return null;
            string s = null;
            try
            {
                if (pv.vt == 31 /*VT_LPWSTR*/ && pv.pwszVal != IntPtr.Zero)
                    s = Marshal.PtrToStringUni(pv.pwszVal);
            }
            finally { PropVariantClear(ref pv); }
            return s;
        }

        private static uint ReadUIntProp(CoreAudio.IPropertyStore ps, PROPERTYKEY key)
        {
            PROPVARIANT pv;
            int hr = ps.GetValue(ref key, out pv);
            if (hr != 0) return 10;
            uint v = 10;
            try
            {
                if (pv.vt == 19 /*VT_UI4*/) v = pv.uintVal;
                else if (pv.vt == 3  /*VT_I4*/)  v = (uint)pv.intVal;
            }
            finally { PropVariantClear(ref pv); }
            return v;
        }

        private static string TitleCase(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpper(s[0]) + s.Substring(1);
        }

        private static string SafeGetMainModulePath(Process p)
        {
            try { return p.MainModule != null ? p.MainModule.FileName : null; }
            catch { return null; }
        }
    }

    // ============================================================
    // Win32 helpers
    // ============================================================
    internal static class Win32
    {
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE  = 20;
        public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        public const int DWMSBT_TRANSIENTWINDOW = 3;
        public const int DWMWCP_ROUND = 2;
        public const int WM_SETREDRAW = 0x000B;
        public const int WH_MOUSE_LL = 14;
        public const int WM_MOUSEWHEEL = 0x020A;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_MBUTTONDOWN = 0x0207;
        public const int MONITOR_DEFAULTTONEAREST = 2;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_NOOWNERZORDER = 0x0200;

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NOTIFYICONIDENTIFIER
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public Guid guidItem;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("shell32.dll", SetLastError = true)]
        public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetColorizationColor(out uint colorizationColor, [MarshalAs(UnmanagedType.Bool)] out bool opaqueBlend);

        public const int WCA_ACCENT_POLICY = 19;
        public const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

        [StructLayout(LayoutKind.Sequential)]
        public struct ACCENT_POLICY
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWCOMPOSITIONATTRIBDATA
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll")]
        public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("Shell32.dll", CharSet = CharSet.Auto)]
        public static extern int ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[] phiconLarge, IntPtr[] phiconSmall, int nIcons);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        public static Icon LoadIconFromFile(string file, int index, bool small)
        {
            var large = new IntPtr[1];
            var smallArr = new IntPtr[1];
            int n = ExtractIconEx(file, index, large, smallArr, 1);
            if (n <= 0) return null;
            IntPtr handle = small ? smallArr[0] : large[0];
            IntPtr other  = small ? large[0]    : smallArr[0];
            if (other != IntPtr.Zero) DestroyIcon(other);
            if (handle == IntPtr.Zero) return null;
            try
            {
                using (var src = Icon.FromHandle(handle)) return (Icon)src.Clone();
            }
            finally { DestroyIcon(handle); }
        }
    }

    // ============================================================
    // Theme + drawing helpers
    // ============================================================
    internal static class Theme
    {
        public static bool IsDark;
        public static Color WindowBg;
        public static Color HeaderBg;
        public static Color CardBg;
        public static Color CardBgHover;
        public static Color TileBg;
        public static Color BtnBg;
        public static Color BtnHover;
        public static Color SliderTrack;
        public static Color BorderSubtle;
        public static Color TextPrimary;
        public static Color TextMuted;
        public static Color SectionLabel;
        public static Color MuteRed;
        public static Color PillText;
        public static Color AccentBlue;

        static Theme() { Reload(); }

        // Re-read Windows app theme + accent color. Safe to call after WM_SETTINGCHANGE.
        public static void Reload()
        {
            IsDark = ReadAppsUseDarkTheme();
            Color? acc = ReadAccent();
            AccentBlue = acc.HasValue ? acc.Value : Color.FromArgb(59, 130, 246);

            if (IsDark)
            {
                // Let DWM/backdrop carry the Windows material. These are only calm
                // overlay surfaces; do not paint the raw accent palette over the app.
                WindowBg     = Tint(Color.FromArgb(32, 32, 32), AccentBlue, 0.13);
                HeaderBg     = Tint(Color.FromArgb(43, 43, 43), AccentBlue, 0.11);
                CardBg       = Tint(Color.FromArgb(50, 50, 50), AccentBlue, 0.09);
                CardBgHover  = Tint(Color.FromArgb(62, 62, 62), AccentBlue, 0.12);
                TileBg       = Tint(Color.FromArgb(64, 64, 64), AccentBlue, 0.09);
                BtnBg        = Tint(Color.FromArgb(54, 54, 54), AccentBlue, 0.09);
                BtnHover     = Tint(Color.FromArgb(72, 72, 72), AccentBlue, 0.11);
                SliderTrack  = Tint(Color.FromArgb(76, 76, 76), AccentBlue, 0.16);
                BorderSubtle = Tint(Color.FromArgb(64, 64, 64), AccentBlue, 0.08);
                TextPrimary  = Color.White;
                TextMuted    = Color.FromArgb(180, 180, 180);
                SectionLabel = Color.FromArgb(150, 150, 150);
                MuteRed      = Color.FromArgb(239, 68, 68);
                PillText     = Color.FromArgb(235, 245, 255);
            }
            else
            {
                WindowBg     = Tint(Color.FromArgb(243, 243, 243), AccentBlue, 0.055);
                HeaderBg     = Tint(Color.FromArgb(250, 250, 250), AccentBlue, 0.035);
                CardBg       = Tint(Color.FromArgb(255, 255, 255), AccentBlue, 0.025);
                CardBgHover  = Tint(Color.FromArgb(239, 239, 239), AccentBlue, 0.055);
                TileBg       = Tint(Color.FromArgb(229, 229, 229), AccentBlue, 0.045);
                BtnBg        = Tint(Color.FromArgb(235, 235, 235), AccentBlue, 0.045);
                BtnHover     = Tint(Color.FromArgb(222, 222, 222), AccentBlue, 0.065);
                SliderTrack  = Tint(Color.FromArgb(218, 218, 218), AccentBlue, 0.075);
                BorderSubtle = Tint(Color.FromArgb(216, 216, 216), AccentBlue, 0.045);
                TextPrimary  = Color.FromArgb(32, 32, 32);
                TextMuted    = Color.FromArgb(94, 94, 94);
                SectionLabel = Color.FromArgb(104, 104, 104);
                MuteRed      = Color.FromArgb(220, 38, 38);
                PillText     = Color.FromArgb(40, 40, 40);
            }
        }

        public static int AcrylicGradientColor()
        {
            int alpha = IsDark ? 205 : 225;
            return unchecked((int)(((uint)alpha << 24)
                | ((uint)WindowBg.B << 16)
                | ((uint)WindowBg.G << 8)
                | WindowBg.R));
        }

        private static bool ReadAppsUseDarkTheme()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("AppsUseLightTheme");
                        if (v is int) return ((int)v) == 0;
                    }
                }
            } catch { }
            return true;
        }

        private static Color? ReadAccent()
        {
            Color? dwm = ReadDwmAccent();
            if (dwm.HasValue) return dwm.Value;

            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\DWM"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("AccentColor");
                        if (v is int)
                        {
                            int abgr = (int)v;
                            int r = (abgr >>  0) & 0xFF;
                            int g = (abgr >>  8) & 0xFF;
                            int b = (abgr >> 16) & 0xFF;
                            return Color.FromArgb(255, r, g, b);
                        }
                    }
                }
            } catch { }
            return null;
        }

        private static Color? ReadDwmAccent()
        {
            try
            {
                uint raw;
                bool opaque;
                int hr = Win32.DwmGetColorizationColor(out raw, out opaque);
                if (hr != 0) return null;

                int r = (int)((raw >> 16) & 0xFF);
                int g = (int)((raw >> 8) & 0xFF);
                int b = (int)(raw & 0xFF);
                if (r == 0 && g == 0 && b == 0) return null;
                return Color.FromArgb(255, r, g, b);
            }
            catch { return null; }
        }

        private static Color Tint(Color baseColor, Color accent, double amount)
        {
            amount = Math.Max(0.0, Math.Min(1.0, amount));
            int r = (int)Math.Round(baseColor.R + (accent.R - baseColor.R) * amount);
            int g = (int)Math.Round(baseColor.G + (accent.G - baseColor.G) * amount);
            int b = (int)Math.Round(baseColor.B + (accent.B - baseColor.B) * amount);
            return Color.FromArgb(baseColor.A, r, g, b);
        }

    }

    // ============================================================
    // Localized strings (system UI culture: tr or fallback en)
    // ============================================================
    internal static class Strings
    {
        public static readonly string VolumeMixer;
        public static readonly string MainSound;
        public static readonly string Apps;
        public static readonly string SystemSounds;
        public static readonly string SoundSettings;
        public static readonly string OutputDevice;
        public static readonly string SpatialSound = "Spatial Sound";
        public static readonly string SpatialSoundSubtitle;
        public static readonly string DefaultPill;
        public static readonly string Speakers;
        public static readonly string Headphones;
        public static readonly string Headset;
        public static readonly string Spdif = "SPDIF";
        public static readonly string HdmiDp = "HDMI / DisplayPort";
        public static readonly string AudioOutput;
        public static readonly string NoOutputs;
        public static readonly string MusicPill;
        public static readonly string MeetingPill;
        public static readonly string OpenMixer;
        public static readonly string ExitItem;
        public static readonly string VolumePrefix;
        public static readonly string MutedText;
        public static readonly string SystemDefault;
        public static readonly string OutputDeviceLabel;
        public static readonly string AutoStart;

        static Strings()
        {
            bool tr = false;
            try
            {
                tr = string.Equals(
                    System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                    "tr", StringComparison.OrdinalIgnoreCase);
            }
            catch { }

            if (tr)
            {
                VolumeMixer = "Ses Karıştırıcısı";
                MainSound = "Ana Ses";
                Apps = "UYGULAMALAR";
                SystemSounds = "Sistem Sesleri";
                SoundSettings = "Ses Ayarları";
                OutputDevice = "ÇIKIŞ AYGITI";
                SpatialSound = "Uzamsal Ses";
                SpatialSoundSubtitle = "Windows aygıt özelliklerinde yönetilir";
                DefaultPill = "Varsayılan";
                Speakers = "Hoparlörler";
                Headphones = "Kulaklık";
                Headset = "Kulaklıklı mikrofon";
                AudioOutput = "Ses çıkışı";
                NoOutputs = "Aktif çıkış aygıtı bulunamadı.";
                MusicPill = "müzik";
                MeetingPill = "toplantı";
                OpenMixer = "Karıştırıcıyı Aç";
                ExitItem = "Çıkış";
                VolumePrefix = "Ses: ";
                MutedText = "Sessiz";
                SystemDefault = "Sistem varsayılanı";
                OutputDeviceLabel = "Çıkış aygıtı";
                AutoStart = "Başlangıçta Çalıştır";
            }
            else
            {
                VolumeMixer = "Volume Mixer";
                MainSound = "Master";
                Apps = "APPS";
                SystemSounds = "System Sounds";
                SoundSettings = "Sound Settings";
                OutputDevice = "OUTPUT DEVICE";
                SpatialSoundSubtitle = "Managed in Windows Sound Settings";
                DefaultPill = "Default";
                Speakers = "Speakers";
                Headphones = "Headphones";
                Headset = "Headset";
                AudioOutput = "Audio output";
                NoOutputs = "No active output devices.";
                MusicPill = "music";
                MeetingPill = "meeting";
                OpenMixer = "Open Mixer";
                ExitItem = "Exit";
                VolumePrefix = "Volume: ";
                MutedText = "Muted";
                SystemDefault = "System default";
                OutputDeviceLabel = "Output device";
                AutoStart = "Run at startup";
            }
        }
    }

    internal static class Draw
    {
        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            if (d <= 0 || r.Width <= 0 || r.Height <= 0) { p.AddRectangle(r); return p; }
            d = Math.Min(d, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    internal static class UiFonts
    {
        public static readonly Font HeaderGlyph = new Font("Segoe UI Symbol", 11f);
        public static readonly Font BackGlyph = new Font("Segoe UI", 14f, FontStyle.Bold);
        public static readonly Font HeaderTitle = new Font("Segoe UI Semibold", 11f);
        public static readonly Font RowTitle = new Font("Segoe UI Semibold", 9.5f);
        public static readonly Font RowPercent = new Font("Segoe UI", 8.5f);
        public static readonly Font CardTitle = new Font("Segoe UI Semibold", 9f);
        public static readonly Font CardSubtitle = new Font("Segoe UI", 8f);
        public static readonly Font Section = new Font("Segoe UI Semibold", 7.5f);
        public static readonly Font Body = new Font("Segoe UI", 9f);
        public static readonly Font Pill = new Font("Segoe UI Semibold", 7.5f);
    }

    // ============================================================
    // App brand registry
    // ============================================================
    internal sealed class AppBrand
    {
        public Color Color;
        public string Pill;
        public Color PillColor;
    }

    internal static class AppRegistry
    {
        private static readonly Dictionary<string, AppBrand> Map = new Dictionary<string, AppBrand>(StringComparer.OrdinalIgnoreCase)
        {
            { "chrome",       new AppBrand { Color = Color.FromArgb(66, 133, 244) } },
            { "msedge",       new AppBrand { Color = Color.FromArgb(0, 120, 215) } },
            { "firefox",      new AppBrand { Color = Color.FromArgb(255, 113, 57) } },
            { "spotify",      new AppBrand { Color = Color.FromArgb(29, 185, 84),  Pill = Strings.MusicPill,    PillColor = Color.FromArgb(29, 185, 84) } },
            { "discord",      new AppBrand { Color = Color.FromArgb(88, 101, 242) } },
            { "zoom",         new AppBrand { Color = Color.FromArgb(45, 140, 255), Pill = Strings.MeetingPill, PillColor = Color.FromArgb(45, 140, 255) } },
            { "vlc",          new AppBrand { Color = Color.FromArgb(255, 136, 0) } },
            { "slack",        new AppBrand { Color = Color.FromArgb(74, 21, 75) } },
            { "teams",        new AppBrand { Color = Color.FromArgb(98, 100, 167) } },
            { "ms-teams",     new AppBrand { Color = Color.FromArgb(98, 100, 167) } },
            { "obs64",        new AppBrand { Color = Color.FromArgb(80, 100, 130) } },
            { "obs",          new AppBrand { Color = Color.FromArgb(80, 100, 130) } },
            { "steam",        new AppBrand { Color = Color.FromArgb(102, 192, 244) } },
            { "system",       new AppBrand { Color = Color.FromArgb(110, 120, 138) } },
        };

        public static AppBrand Lookup(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return null;
            AppBrand b;
            return Map.TryGetValue(processName, out b) ? b : null;
        }

        public static Color ColorOrFallback(string processName)
        {
            var b = Lookup(processName);
            if (b != null) return b.Color;
            return ColorFromName(processName ?? "");
        }

        private static Color ColorFromName(string name)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in name) h = h * 31 + c;
                int hue = ((h & 0xFFFF) % 360 + 360) % 360;
                return ColorFromHsl(hue, 0.55, 0.55);
            }
        }

        private static Color ColorFromHsl(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = l - c / 2;
            double r = 0, g = 0, b = 0;
            if (h < 60)       { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else              { r = c; b = x; }
            return Color.FromArgb(
                (int)Math.Round((r + m) * 255),
                (int)Math.Round((g + m) * 255),
                (int)Math.Round((b + m) * 255));
        }
    }

    // ============================================================
    // Panel that applies the Win11 dark Explorer theme so its native
    // scrollbar matches the dark UI instead of showing system gray.
    // ============================================================
    internal sealed class DarkScrollPanel : Panel
    {
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { Win32.SetWindowTheme(Handle, Theme.IsDark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
        }
    }

    // ============================================================
    // Slider
    // ============================================================
    internal sealed class FlatSlider : Control
    {
        private int _min = 0, _max = 100, _value = 0;
        private bool _dragging;
        private const int TrackHeight = 14;
        private const int ThumbRadius = 9;

        public Color BarColor = Theme.AccentBlue;
        public bool Faded;
        public event Action<int> ValueChanged;

        public FlatSlider()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, true); // accept focus → MouseWheel routes here
            BackColor = Color.Transparent;
            Height = 22;
            TabStop = false;
        }

        public int Minimum { get { return _min; } set { _min = value; Invalidate(); } }
        public int Maximum { get { return _max; } set { _max = value; Invalidate(); } }
        public bool IsDragging { get { return _dragging; } }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = Math.Max(_min, Math.Min(_max, value));
                if (v == _value) return;
                _value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(v);
            }
        }

        public void SetValueSilent(int v)
        {
            int clamped = Math.Max(_min, Math.Min(_max, v));
            if (clamped == _value) return;
            _value = clamped;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int yMid = Height / 2;
            int xLeft = ThumbRadius + 1;
            int xRight = Width - ThumbRadius - 1;
            int trackY = yMid - TrackHeight / 2;
            int trackW = Math.Max(1, xRight - xLeft);

            var trackRect = new Rectangle(xLeft, trackY, trackW, TrackHeight);
            using (var path = Draw.Rounded(trackRect, TrackHeight / 2))
            using (var br = new SolidBrush(Theme.SliderTrack))
                g.FillPath(br, path);

            float frac = _max > _min ? (float)(_value - _min) / (_max - _min) : 0f;
            int filledW = (int)Math.Round(trackW * frac);
            if (filledW > 0)
            {
                var filledRect = new Rectangle(xLeft, trackY, filledW, TrackHeight);
                Color barC = Faded ? Color.FromArgb(120, BarColor) : BarColor;
                using (var path = Draw.Rounded(filledRect, TrackHeight / 2))
                using (var br = new LinearGradientBrush(filledRect,
                    Color.FromArgb(barC.A, Math.Min(255, barC.R + 35), Math.Min(255, barC.G + 35), Math.Min(255, barC.B + 35)),
                    barC, LinearGradientMode.Horizontal))
                    g.FillPath(br, path);
            }

            int thumbX = xLeft + filledW;
            var thumbRect = new RectangleF(thumbX - ThumbRadius, yMid - ThumbRadius, ThumbRadius * 2, ThumbRadius * 2);
            using (var br = new SolidBrush(Color.White))
                g.FillEllipse(br, thumbRect);
            using (var pen = new Pen(Color.FromArgb(80, 0, 0, 0)))
                g.DrawEllipse(pen, thumbRect);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            // Steal focus from any TabStop control so subsequent OnMouseWheel fires here.
            if (CanFocus && !Focused) Focus();
            _dragging = true;
            Capture = true;
            SetFromX(e.X);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            // Take focus on hover so the wheel scrolls THIS slider.
            if (CanFocus && !Focused) Focus();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int notches = e.Delta / 120;
            int step = 2 * Math.Sign(notches) * Math.Max(1, Math.Abs(notches));
            int next = Math.Max(_min, Math.Min(_max, _value + step));
            if (next != _value) Value = next;
            // Mark handled so the parent scroll panel doesn't ALSO scroll the apps area.
            var hme = e as HandledMouseEventArgs;
            if (hme != null) hme.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) SetFromX(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Capture = false;
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) _dragging = false;
        }

        private void SetFromX(int x)
        {
            int xLeft = ThumbRadius + 1;
            int xRight = Width - ThumbRadius - 1;
            int trackW = xRight - xLeft;
            if (trackW <= 0) return;
            float frac = Math.Max(0, Math.Min(1, (float)(x - xLeft) / trackW));
            Value = _min + (int)Math.Round(frac * (_max - _min));
        }
    }

    // ============================================================
    // Icon tile
    // ============================================================
    internal sealed class IconTile : Control
    {
        private readonly Image _img;
        private readonly string _glyph;
        private readonly Font _glyphFont;
        private readonly bool _ownsImage;
        private readonly bool _ownsFont;
        private Color _bg;

        public IconTile(Image img, Color bg) : this(img, bg, true) { }
        public IconTile(Image img, Color bg, bool ownsImage) { _img = img; _bg = bg; _ownsImage = ownsImage; Init(); }
        public IconTile(string glyph, Font font, Color bg) { _glyph = glyph; _glyphFont = font; _bg = bg; _ownsFont = true; Init(); }

        public void SetBg(Color bg) { _bg = bg; Invalidate(); }

        private void Init()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(36, 36);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var path = Draw.Rounded(new Rectangle(0, 0, Width, Height), 8))
            using (var br = new SolidBrush(_bg))
                g.FillPath(br, path);

            if (_img != null)
            {
                int s = Math.Min(Width, Height) - 12;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(_img, (Width - s) / 2, (Height - s) / 2, s, s);
            }
            else if (!string.IsNullOrEmpty(_glyph))
            {
                using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var br = new SolidBrush(GlyphColorFor(_bg)))
                    g.DrawString(_glyph, _glyphFont, br, new RectangleF(0, 0, Width, Height), fmt);
            }
        }

        private static Color GlyphColorFor(Color bg)
        {
            double luminance = (0.2126 * bg.R + 0.7152 * bg.G + 0.0722 * bg.B) / 255.0;
            return luminance > 0.62 ? Color.FromArgb(32, 32, 32) : Color.White;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_ownsImage && _img != null) _img.Dispose();
                if (_ownsFont && _glyphFont != null) _glyphFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // ============================================================
    // Mute button
    // ============================================================
    internal sealed class MuteButton : Control
    {
        private bool _muted;
        private bool _hover;
        private static readonly Font GlyphFont = new Font("Segoe UI Symbol", 11.5f);
        public event Action<bool> Toggled;

        public bool Muted
        {
            get { return _muted; }
            set { if (_muted != value) { _muted = value; Invalidate(); } }
        }

        public MuteButton()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Size = new Size(36, 36);
            Cursor = Cursors.Hand;
            Click += (s, e) => { Muted = !Muted; if (Toggled != null) Toggled(_muted); };
            MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            MouseLeave += (s, e) => { _hover = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Draw.Rounded(new Rectangle(0, 0, Width, Height), 8))
            using (var br = new SolidBrush(_hover ? Theme.BtnHover : Theme.BtnBg))
                g.FillPath(br, path);

            string glyph = _muted ? "🔇" : "🔊";
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var br = new SolidBrush(_muted ? Theme.MuteRed : Theme.TextPrimary))
                g.DrawString(glyph, GlyphFont, br, new RectangleF(0, 0, Width, Height), fmt);
        }
    }

    // ============================================================
    // Generic header button (gear / back / refresh)
    // ============================================================
    internal sealed class IconHeaderButton : Control
    {
        private bool _hover;
        public string Glyph;
        public Font GlyphFont = UiFonts.HeaderGlyph;
        public event Action Clicked;

        public IconHeaderButton(string glyph)
        {
            Glyph = glyph;
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Size = new Size(36, 36);
            Cursor = Cursors.Hand;
            Click += (s, e) => { if (Clicked != null) Clicked(); };
            MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            MouseLeave += (s, e) => { _hover = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Draw.Rounded(new Rectangle(0, 0, Width, Height), 8))
            using (var br = new SolidBrush(_hover ? Theme.BtnHover : Theme.BtnBg))
                g.FillPath(br, path);
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var br = new SolidBrush(Theme.TextPrimary))
                g.DrawString(Glyph, GlyphFont, br, new RectangleF(0, 0, Width, Height), fmt);
        }
    }

    // ============================================================
    // Pill badge
    // ============================================================
    internal sealed class PillLabel : Control
    {
        private readonly Color _color;
        private static readonly Font PillFont = UiFonts.Pill;

        public PillLabel(string text, Color color)
        {
            Text = text; _color = color;
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            using (var g = CreateGraphics())
            {
                var sz = g.MeasureString(text, PillFont);
                Size = new Size((int)Math.Ceiling(sz.Width) + 14, 18);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Draw.Rounded(new Rectangle(0, 0, Width, Height), Height / 2))
            using (var br = new SolidBrush(Color.FromArgb(60, _color)))
                g.FillPath(br, path);
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var br = new SolidBrush(Theme.PillText))
                g.DrawString(Text, PillFont, br, new RectangleF(0, 0, Width, Height), fmt);
        }
    }

    // ============================================================
    // Compact device option for the per-app device picker (in AppRow expand).
    // ============================================================
    internal sealed class DeviceOptionButton : Control
    {
        // Font instances are GDI-handle-backed; allocating them per OnPaint causes
        // GDI handle churn under fast scroll. Cache once, share across all instances.
        private static readonly Font GlyphFont = new Font("Segoe UI Symbol", 11f);
        private static readonly Font NameFont = new Font("Segoe UI", 9f);
        private static readonly Font CheckFont = new Font("Segoe UI Symbol", 11f, FontStyle.Bold);

        private bool _hover;
        private bool _selected;
        private readonly string _name;
        private readonly string _glyph;
        public Action Clicked;

        public DeviceOptionButton(string name, string glyph, bool selected)
        {
            _name = name; _glyph = glyph; _selected = selected;
            Height = 32;
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Click += (s, e) => { if (Clicked != null) Clicked(); };
            MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            MouseLeave += (s, e) => { _hover = false; Invalidate(); };
        }

        public void SetSelected(bool selected)
        {
            if (_selected == selected) return;
            _selected = selected;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color bg = _selected
                ? Color.FromArgb(50, Theme.AccentBlue.R, Theme.AccentBlue.G, Theme.AccentBlue.B)
                : (_hover ? Theme.CardBgHover : Color.Transparent);

            using (var path = Draw.Rounded(new Rectangle(0, 0, Width, Height), 6))
            using (var br = new SolidBrush(bg))
                g.FillPath(br, path);

            // Glyph (form factor icon)
            using (var br = new SolidBrush(_selected ? Theme.AccentBlue : Theme.TextMuted))
            {
                using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(_glyph, GlyphFont, br, new RectangleF(2, 0, 26, Height), fmt);
            }

            // Name
            using (var br = new SolidBrush(Theme.TextPrimary))
            {
                using (var fmt = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
                {
                int rightPad = _selected ? 22 : 6;
                g.DrawString(_name, NameFont, br, new RectangleF(30, 0, Width - 30 - rightPad, Height), fmt);
                }
            }

            // Selection check on the right
            if (_selected)
            {
                using (var br = new SolidBrush(Theme.AccentBlue))
                {
                    using (var fmt = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
                    g.DrawString("✓", CheckFont, br, new RectangleF(0, 0, Width - 8, Height), fmt);
                }
            }
        }
    }

    // ============================================================
    // Volume row
    // ============================================================
    internal sealed class AppRow : Panel
    {
        private const int CollapsedHeight = 64;
        private const int OptionRowH = 32;
        private const int OptionRowGap = 4;

        private readonly FlatSlider _slider;
        private readonly Label _pct;
        private readonly MuteButton _mute;
        private readonly Label _name;
        private readonly PillLabel _pill;
        private readonly IconTile _tile;

        private readonly bool _canRoute;
        private readonly uint _processId;
        private readonly Func<List<DeviceInfo>> _getDevices;
        private Panel _expandPanel;
        private bool _expanded;
        private System.Windows.Forms.Timer _expandTimer;
        private System.Windows.Forms.Timer _scrollTimer;
        private int _savedScrollY = -1;
        private readonly List<DeviceOptionButton> _deviceButtons = new List<DeviceOptionButton>();
        private readonly MouseEventHandler _rightClickHandler;
        public bool IsDragging { get { return _slider.IsDragging; } }

        public AppRow(string name, IconTile tile, Color brandColor, AppBrand brand,
                      float volume, bool muted,
                      uint processId, bool canRoute,
                      Func<List<DeviceInfo>> getDevices,
                      Action<float> onVolume, Action<bool> onMute)
        {
            _processId = processId;
            _canRoute = canRoute;
            _getDevices = getDevices;

            Height = CollapsedHeight;
            Dock = DockStyle.Top;
            BackColor = Theme.WindowBg;

            _tile = tile;
            // Vertically center the tile on the SLIDER row (slider center = y 43,
            // tile is 36 tall → top at 25). Looks more balanced than centering on
            // the whole collapsed row (which placed it visually above the slider).
            _tile.Location = new Point(14, 43 - _tile.Height / 2);
            Controls.Add(_tile);

            _name = new Label
            {
                Text = name,
                AutoSize = true,
                ForeColor = Theme.TextPrimary,
                BackColor = Color.Transparent,
                Font = UiFonts.RowTitle,
                Location = new Point(60, 9),
            };
            Controls.Add(_name);

            if (brand != null && !string.IsNullOrEmpty(brand.Pill))
            {
                _pill = new PillLabel(brand.Pill, brand.PillColor);
                _pill.Location = new Point(_name.Right + 6, 10);
                Controls.Add(_pill);
            }

            _slider = new FlatSlider
            {
                BarColor = brandColor,
                Faded = muted,
                Minimum = 0,
                Maximum = 100,
                Location = new Point(60, 32),
                Height = 22,
            };
            _slider.SetValueSilent((int)Math.Round(volume * 100));
            Controls.Add(_slider);

            _pct = new Label
            {
                Text = ((int)Math.Round(volume * 100)) + "%",
                AutoSize = true,
                ForeColor = Theme.TextMuted,
                BackColor = Color.Transparent,
                Font = UiFonts.RowPercent,
            };
            Controls.Add(_pct);

            _mute = new MuteButton { Muted = muted };
            Controls.Add(_mute);

            _slider.ValueChanged += (v) =>
            {
                _pct.Text = v + "%";
                if (onVolume != null) onVolume(v / 100f);
                LayoutRow();
            };
            _mute.Toggled += (m) =>
            {
                _slider.Faded = m;
                _slider.Invalidate();
                if (onMute != null) onMute(m);
            };

            // Right-click on the row (and any child) toggles the per-app device picker.
            _rightClickHandler = (s, e) =>
            {
                if (e.Button == MouseButtons.Right) ToggleExpand();
            };
            if (_canRoute) HookRightClickRecursive(this);

            Resize += (s, e) => LayoutRow();
            LayoutRow();
        }

        private void HookRightClickRecursive(Control root)
        {
            root.MouseUp += _rightClickHandler;
            foreach (Control c in root.Controls) HookRightClickRecursive(c);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_expandTimer != null)
                {
                    _expandTimer.Stop();
                    _expandTimer.Dispose();
                    _expandTimer = null;
                }
                if (_scrollTimer != null)
                {
                    _scrollTimer.Stop();
                    _scrollTimer.Dispose();
                    _scrollTimer = null;
                }
            }
            base.Dispose(disposing);
        }

        private void ToggleExpand()
        {
            if (_expanded) Collapse();
            else Expand();
        }

        private void Expand()
        {
            if (_expanded) return;
            _expanded = true;
            BuildExpandPanel();
            int target = CollapsedHeight + (_expandPanel != null ? _expandPanel.Height : 0);

            var scroll = Parent as Panel;
            if (scroll != null && scroll.AutoScroll)
            {
                _savedScrollY = -scroll.AutoScrollPosition.Y;
                AnimateScroll(scroll, this.Top);
            }

            AnimateHeight(target, null);
        }

        private void Collapse()
        {
            if (!_expanded) return;
            _expanded = false;

            var scroll = Parent as Panel;
            int restoreScroll = _savedScrollY;
            _savedScrollY = -1;

            AnimateHeight(CollapsedHeight, () =>
            {
                if (_expandPanel != null)
                {
                    Controls.Remove(_expandPanel);
                    _expandPanel.Dispose();
                    _expandPanel = null;
                }
                _deviceButtons.Clear();
            });

            if (scroll != null && scroll.AutoScroll && restoreScroll >= 0)
                AnimateScroll(scroll, restoreScroll);
        }

        private void AnimateScroll(Panel scroll, int targetY)
        {
            if (_scrollTimer != null) { _scrollTimer.Stop(); _scrollTimer.Dispose(); _scrollTimer = null; }
            int startY = -scroll.AutoScrollPosition.Y;
            if (startY == targetY) return;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            _scrollTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _scrollTimer.Tick += (s, e) =>
            {
                if (scroll.IsDisposed)
                {
                    _scrollTimer.Stop();
                    _scrollTimer.Dispose();
                    _scrollTimer = null;
                    return;
                }
                double t = Math.Min(1.0, sw.Elapsed.TotalMilliseconds / 220.0);
                double eased = 1.0 - Math.Pow(1.0 - t, 3);
                int y = (int)Math.Round(startY + (targetY - startY) * eased);
                // AutoScroll clamps automatically as content size changes during height anim.
                scroll.AutoScrollPosition = new Point(0, y);
                if (t >= 1.0)
                {
                    _scrollTimer.Stop();
                    _scrollTimer.Dispose();
                    _scrollTimer = null;
                }
            };
            _scrollTimer.Start();
        }

        private void AnimateHeight(int target, Action onDone)
        {
            if (_expandTimer != null) { _expandTimer.Stop(); _expandTimer.Dispose(); _expandTimer = null; }
            int start = Height;
            if (start == target) { if (onDone != null) onDone(); return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _expandTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _expandTimer.Tick += (s, e) =>
            {
                double t = Math.Min(1.0, sw.Elapsed.TotalMilliseconds / 220.0);
                double eased = 1.0 - Math.Pow(1.0 - t, 3);
                int h = (int)Math.Round(start + (target - start) * eased);
                if (Height != h) Height = h;
                if (t >= 1.0)
                {
                    _expandTimer.Stop();
                    _expandTimer.Dispose();
                    _expandTimer = null;
                    if (onDone != null) onDone();
                }
            };
            _expandTimer.Start();
        }

        private void BuildExpandPanel()
        {
            var devices = _getDevices != null ? _getDevices() : new List<DeviceInfo>();
            string current = null;
            try { current = AudioPolicyConfig.GetDefaultForApp(_processId); } catch { }

            _expandPanel = new Panel
            {
                Location = new Point(0, CollapsedHeight),
                Width = Width,
                BackColor = Theme.WindowBg,
            };

            var title = new Label
            {
                Text = Strings.OutputDeviceLabel,
                Font = UiFonts.Section,
                ForeColor = Theme.SectionLabel,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(60, 6),
            };
            _expandPanel.Controls.Add(title);

            int y = 28;
            int btnWidth = Math.Max(60, Width - 28);

            // System default (clears per-app routing override)
            _deviceButtons.Clear();
            var sysDefault = new DeviceOptionButton(Strings.SystemDefault, "★", string.IsNullOrEmpty(current));
            sysDefault.Size = new Size(btnWidth, OptionRowH);
            sysDefault.Location = new Point(14, y);
            sysDefault.Clicked = () => SelectEndpoint("");
            _expandPanel.Controls.Add(sysDefault);
            _deviceButtons.Add(sysDefault);
            y += OptionRowH + OptionRowGap;

            // Each enumerated output device
            foreach (var d in devices)
            {
                bool selected = !string.IsNullOrEmpty(current) &&
                                string.Equals(d.Id, current, StringComparison.OrdinalIgnoreCase);
                var btn = new DeviceOptionButton(d.Name, GlyphForFactor(d.FormFactor), selected);
                btn.Size = new Size(btnWidth, OptionRowH);
                btn.Location = new Point(14, y);
                string capId = d.Id;
                btn.Clicked = () => SelectEndpoint(capId);
                _expandPanel.Controls.Add(btn);
                _deviceButtons.Add(btn);
                y += OptionRowH + OptionRowGap;
            }

            _expandPanel.Height = y + 8;
            Controls.Add(_expandPanel);

            // Hook right-click on expand panel + its children for the toggle.
            HookRightClickRecursive(_expandPanel);
        }

        private void SelectEndpoint(string endpointId)
        {
            // Update visual selection immediately.
            for (int i = 0; i < _deviceButtons.Count; i++)
            {
                bool sel;
                if (i == 0) sel = string.IsNullOrEmpty(endpointId);
                else sel = false;
                _deviceButtons[i].SetSelected(sel);
            }
            // Find the matching device button (skip index 0 = system default)
            if (!string.IsNullOrEmpty(endpointId))
            {
                var devices = _getDevices != null ? _getDevices() : new List<DeviceInfo>();
                for (int i = 0; i < devices.Count && (i + 1) < _deviceButtons.Count; i++)
                {
                    if (string.Equals(devices[i].Id, endpointId, StringComparison.OrdinalIgnoreCase))
                    {
                        _deviceButtons[i + 1].SetSelected(true);
                    }
                }
            }

            // Apply routing in background (the WinRT call can be slow).
            uint pid = _processId;
            string id = endpointId;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try { AudioPolicyConfig.SetDefaultForApp(pid, id); } catch { }
            });
        }

        private static string GlyphForFactor(uint ff)
        {
            switch (ff)
            {
                case 1: return "🔊";
                case 3:
                case 5: return "🎧";
                case 8: return "♪";
                case 9: return "🖥";
                default: return "🔊";
            }
        }

        private void LayoutRow()
        {
            int rightPad = 14;
            // Pin the main row controls to the COLLAPSED area (top 64px), not the
            // current Height — otherwise they'd drift downward as the row expands.
            _mute.Location = new Point(Width - rightPad - _mute.Width, (CollapsedHeight - _mute.Height) / 2);
            int pctX = _mute.Left - 8 - _pct.PreferredWidth;
            _pct.Location = new Point(pctX, 36);
            int sliderRight = pctX - 6;
            int sliderLeft = 60;
            _slider.Location = new Point(sliderLeft, 32);
            _slider.Width = Math.Max(40, sliderRight - sliderLeft);

            if (_expandPanel != null)
            {
                _expandPanel.Width = Width;
                int btnWidth = Math.Max(60, Width - 28);
                foreach (var btn in _deviceButtons) btn.Width = btnWidth;
            }
        }

        // Sync slider+mute visual to a value/state read from the audio system.
        // Used by the popup's live-poll timer so external changes (media keys,
        // other apps, etc.) reflect in the UI without firing onChange callbacks.
        public void UpdateExternalState(float volume, bool muted)
        {
            if (_slider.IsDragging) return;
            int pct = (int)Math.Round(volume * 100);
            _slider.SetValueSilent(pct);
            if (_slider.Faded != muted)
            {
                _slider.Faded = muted;
                _slider.Invalidate();
            }
            if (_mute.Muted != muted) _mute.Muted = muted;
            string newPctText = pct + "%";
            if (_pct.Text != newPctText) _pct.Text = newPctText;
            LayoutRow();
        }
    }

    // ============================================================
    // Device card (for Settings view)
    // ============================================================
    internal sealed class DeviceCard : Panel
    {
        private bool _hover;
        private bool _isDefault;
        private readonly Action _onClick;
        private readonly Label _nameLbl;
        private readonly Label _subLbl;
        private readonly IconTile _tile;
        private PillLabel _pill;

        public DeviceCard(string title, string subtitle, uint formFactor, bool isDefault, Action onClick)
        {
            _isDefault = isDefault;
            _onClick = onClick;
            Height = 50;
            Margin = new Padding(0);
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.WindowBg;

            string glyph = GlyphForFactor(formFactor);
            Color iconBg = isDefault ? Theme.AccentBlue : Theme.TileBg;
            _tile = new IconTile(glyph, new Font("Segoe UI Symbol", 12f), iconBg);
            _tile.Size = new Size(30, 30);
            _tile.Location = new Point(10, 10);
            Controls.Add(_tile);

            _nameLbl = new Label
            {
                Text = title,
                AutoSize = false,
                AutoEllipsis = true,
                ForeColor = Theme.TextPrimary,
                BackColor = Color.Transparent,
                Font = UiFonts.CardTitle,
                Location = new Point(48, 5),
                Height = 18,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            Controls.Add(_nameLbl);

            _subLbl = new Label
            {
                Text = subtitle ?? "",
                AutoSize = false,
                AutoEllipsis = true,
                ForeColor = Theme.TextMuted,
                BackColor = Color.Transparent,
                Font = UiFonts.CardSubtitle,
                Location = new Point(48, 25),
                Height = 16,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            Controls.Add(_subLbl);

            if (isDefault) AddPill();

            HookHover(this);
            foreach (Control c in Controls) HookHover(c);

            Resize += (s, e) => LayoutChildren();
            LayoutChildren();
        }

        private void HookHover(Control c)
        {
            c.MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            c.MouseLeave += (s, e) => { var pt = PointToClient(Cursor.Position); if (!ClientRectangle.Contains(pt)) { _hover = false; Invalidate(); } };
            c.Click += (s, e) => { if (_onClick != null) _onClick(); };
        }

        private void AddPill()
        {
            _pill = new PillLabel(Strings.DefaultPill, Theme.AccentBlue);
            Controls.Add(_pill);
            HookHover(_pill);
        }

        public void UpdateIsDefault(bool isDefault)
        {
            if (_isDefault == isDefault) return;
            _isDefault = isDefault;
            _tile.SetBg(isDefault ? Theme.AccentBlue : Theme.TileBg);
            if (isDefault && _pill == null)
            {
                AddPill();
            }
            else if (!isDefault && _pill != null)
            {
                Controls.Remove(_pill);
                _pill.Dispose();
                _pill = null;
            }
            LayoutChildren();
            Invalidate();
        }

        private void LayoutChildren()
        {
            int rightEdge = Width - 10;
            if (_pill != null)
            {
                _pill.Location = new Point(Width - _pill.Width - 10, (Height - _pill.Height) / 2);
                rightEdge = _pill.Left - 6;
            }
            int textWidth = Math.Max(20, rightEdge - 48);
            _nameLbl.Width = textWidth;
            if (_subLbl != null) _subLbl.Width = textWidth;
        }

        private static string GlyphForFactor(uint ff)
        {
            switch (ff)
            {
                case 1: return "🔊";   // Speakers
                case 3: return "🎧";   // Headphones
                case 5: return "🎧";   // Headset
                case 8: return "♪";    // SPDIF
                case 9: return "🖥";   // HDMI / DisplayDevice
                default: return "🔊";
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color bg = _isDefault ? Color.FromArgb(40, Theme.AccentBlue.R, Theme.AccentBlue.G, Theme.AccentBlue.B)
                                  : (_hover ? Theme.CardBgHover : Theme.CardBg);
            using (var path = Draw.Rounded(new Rectangle(0, 0, Width, Height), 10))
            using (var br = new SolidBrush(bg))
                g.FillPath(br, path);

            if (_isDefault)
            {
                using (var path = Draw.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 10))
                using (var pen = new Pen(Theme.AccentBlue))
                    g.DrawPath(pen, path);
            }
        }
    }

    // ============================================================
    // Section label
    // ============================================================
    internal sealed class SectionLabel : Panel
    {
        public SectionLabel(string text)
        {
            Dock = DockStyle.Top;
            Height = 32;
            BackColor = Theme.WindowBg;

            var lbl = new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = Theme.SectionLabel,
                BackColor = Color.Transparent,
                Font = UiFonts.Section,
                Location = new Point(16, 12),
            };
            Controls.Add(lbl);
        }
    }

    // ============================================================
    // Header
    // ============================================================
    internal sealed class HeaderPanel : Panel
    {
        public HeaderPanel(string title, Image titleIcon, IconHeaderButton leftBtn, IconHeaderButton rightBtn)
        {
            Dock = DockStyle.Top;
            Height = 56;
            BackColor = Theme.HeaderBg;

            int titleX = 16;

            if (leftBtn != null)
            {
                leftBtn.Location = new Point(12, (Height - leftBtn.Height) / 2);
                Controls.Add(leftBtn);
                titleX = leftBtn.Right + 10;
            }
            else if (titleIcon != null)
            {
                var icon = new PictureBox
                {
                    Image = titleIcon,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Size = new Size(22, 22),
                    Location = new Point(16, 17),
                    BackColor = Color.Transparent,
                };
                Controls.Add(icon);
                titleX = icon.Right + 8;
            }

            var titleLbl = new Label
            {
                Text = title,
                AutoSize = true,
                ForeColor = Theme.TextPrimary,
                BackColor = Color.Transparent,
                Font = UiFonts.HeaderTitle,
                Location = new Point(titleX, 17),
            };
            Controls.Add(titleLbl);

            if (rightBtn != null)
            {
                Controls.Add(rightBtn);
                Resize += (s, e) =>
                {
                    rightBtn.Location = new Point(Width - 16 - rightBtn.Width, (Height - rightBtn.Height) / 2);
                };
                rightBtn.Location = new Point(Width - 16 - rightBtn.Width, (Height - rightBtn.Height) / 2);
            }
        }
    }

    // ============================================================
    // Mixer popup form (with view switching)
    // ============================================================
    internal sealed class MixerForm : Form
    {
        private enum View { Mixer, Settings }
        private View _view = View.Mixer;

        private readonly Panel _content;
        private AudioMaster _master;
        private List<AudioSession> _sessions = new List<AudioSession>();
        private List<DeviceInfo> _devices = new List<DeviceInfo>();
        private static Image _speakerImage;
        private bool _preloaded;

        // Device list animation
        private const int DevCardH = 50;
        private const int DevCardGap = 6;
        private const int FormFixedHeight = 420;
        private bool _switchInProgress;
        private System.Windows.Forms.Timer _deviceAnimTimer;
        private Dictionary<string, DeviceCard> _deviceCardsById = new Dictionary<string, DeviceCard>();
        private Panel _devicesScroll;

        // Live volume sync — pulls current values from the audio system every 250ms
        // while the popup is visible, so external changes (media keys, other apps)
        // are reflected in sliders without firing onVolume callbacks.
        private sealed class RowSync
        {
            public AppRow Row;
            public AudioMaster Master;
            public AudioSession Session;
        }
        private readonly List<RowSync> _rowSyncs = new List<RowSync>();
        private System.Windows.Forms.Timer _syncTimer;
        private int _audioReloadSeq;
        private int _deviceReloadSeq;
        private int _audioReloadPending;
        private int _syncTicks;

        public MixerForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Theme.WindowBg;
            ForeColor = Theme.TextPrimary;
            Size = new Size(380, FormFixedHeight);

            _content = new Panel { Dock = DockStyle.Fill, AutoScroll = false, BackColor = Theme.WindowBg };
            Controls.Add(_content);

            // .NET's canonical hook for system theme/accent changes. More reliable than
            // catching WM_SETTINGCHANGE on a hidden form (broadcast delivery is finicky).
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged; } catch { }
                StopSyncTimer();
                StopDeviceAnimation();
                if (_slideAnimTimer != null) { _slideAnimTimer.Stop(); _slideAnimTimer.Dispose(); _slideAnimTimer = null; }
                if (_sessionRefreshTimer != null) { _sessionRefreshTimer.Stop(); _sessionRefreshTimer.Dispose(); _sessionRefreshTimer = null; }
                if (_delayedCloseTimer != null) { _delayedCloseTimer.Stop(); _delayedCloseTimer.Dispose(); _delayedCloseTimer = null; }
                AudioEngine.ReleaseMaster(_master);
                AudioEngine.ReleaseSessions(_sessions);
                _master = null;
                _sessions = new List<AudioSession>();
            }
            base.Dispose(disposing);
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // Listen broadly — Windows uses Color/General/VisualStyle/Window for theme
            // and accent changes, and the categories aren't always consistent.
            if (e.Category != UserPreferenceCategory.Color
                && e.Category != UserPreferenceCategory.General
                && e.Category != UserPreferenceCategory.VisualStyle
                && e.Category != UserPreferenceCategory.Window)
                return;
            RefreshThemeIfChanged();
        }

        // Called when the audio system signals a new session was created. Apps like
        // Chrome and Discord can create many transient sessions in rapid succession,
        // so we debounce: only re-enumerate ~500ms AFTER the last event lands.
        private System.Windows.Forms.Timer _sessionRefreshTimer;

        public void OnExternalSessionChange()
        {
            if (!Visible || _view != View.Mixer) return;
            try
            {
                if (!IsHandleCreated || IsDisposed) return;
                BeginInvoke((Action)(() =>
                {
                    if (_view != View.Mixer || !Visible) return;
                    // Restart the debounce timer on every new event.
                    if (_sessionRefreshTimer == null)
                    {
                        _sessionRefreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
                        _sessionRefreshTimer.Tick += (s, e) =>
                        {
                            _sessionRefreshTimer.Stop();
                            DoSessionRefresh();
                        };
                    }
                    _sessionRefreshTimer.Stop();
                    _sessionRefreshTimer.Start();
                }));
            }
            catch { }
        }

        private void DoSessionRefresh()
        {
            if (!Visible || _view != View.Mixer) return;
            int seq = Interlocked.Increment(ref _audioReloadSeq);
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                List<AudioSession> s = null;
                try { s = AudioEngine.EnumerateSessions(); } catch { }
                if (s == null) return;
                try
                {
                    if (IsHandleCreated && !IsDisposed)
                        BeginInvoke((Action)(() =>
                        {
                            if (seq != _audioReloadSeq || _view != View.Mixer || !Visible)
                            {
                                AudioEngine.ReleaseSessions(s);
                                return;
                            }
                            ApplySessionReload(s);
                        }));
                    else
                        AudioEngine.ReleaseSessions(s);
                }
                catch { AudioEngine.ReleaseSessions(s); }
            });
        }

        private void ApplySessionReload(List<AudioSession> freshSessions)
        {
            if (freshSessions == null) return;
            if (IsDisposed || _isClosing || _rowSyncs.Any(x => x.Row.IsDragging))
            {
                AudioEngine.ReleaseSessions(freshSessions);
                return;
            }
            var oldPids = new HashSet<uint>(_sessions.Select(x => x.ProcessId));
            bool listChanged = _sessions.Count != freshSessions.Count
                || !oldPids.SetEquals(freshSessions.Select(x => x.ProcessId));

            if (!listChanged)
            {
                foreach (var fresh in freshSessions)
                {
                    var existing = _sessions.FirstOrDefault(x => x.ProcessId == fresh.ProcessId);
                    if (existing != null) AudioEngine.AdoptSessionInterfaces(existing, fresh);
                }
                AudioEngine.ReleaseSessions(freshSessions);
                return;
            }

            var oldSessions = _sessions;
            _sessions = freshSessions;
            Render();
            AudioEngine.ReleaseSessions(oldSessions);
        }

        // Public so TrayApp's poll timer can also call this as a fallback in case
        // SystemEvents misses an accent change.
        public void RefreshThemeIfChanged()
        {
            try
            {
                if (!IsHandleCreated || IsDisposed) return;
                BeginInvoke((Action)(() =>
                {
                    Color old = Theme.AccentBlue;
                    Color oldBg = Theme.WindowBg;
                    bool wasDark = Theme.IsDark;
                    Theme.Reload();
                    if (old.ToArgb() == Theme.AccentBlue.ToArgb()
                        && oldBg.ToArgb() == Theme.WindowBg.ToArgb()
                        && wasDark == Theme.IsDark) return; // no change
                    ApplyWindowChromeTheme();
                    BackColor = Theme.WindowBg;
                    ForeColor = Theme.TextPrimary;
                    _content.BackColor = Theme.WindowBg;
                    // Cached per-control colors (IconTile bg, FlatSlider.BarColor, labels)
                    // need a re-render to pick up the new accent or app theme.
                    if (Visible) Render();
                }));
            }
            catch { }
        }

        // Pre-load audio data on a background thread so the popup is instant on first show.
        public void PreloadAsync()
        {
            int seq = Interlocked.Increment(ref _audioReloadSeq);
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                AudioMaster m = null;
                List<AudioSession> s = null;
                List<DeviceInfo> d = null;
                try { m = AudioEngine.GetMaster(); } catch { }
                try { s = AudioEngine.EnumerateSessions(); } catch { }
                try { d = AudioEngine.EnumerateOutputDevices(); } catch { }
                try
                {
                    if (IsHandleCreated && !IsDisposed)
                        BeginInvoke((Action)(() =>
                        {
                            if (IsDisposed || seq != _audioReloadSeq || _preloaded)
                            {
                                AudioEngine.ReleaseMaster(m);
                                AudioEngine.ReleaseSessions(s);
                                return;
                            }
                            _devices = d ?? new List<DeviceInfo>();
                            if (Visible)
                            {
                                ApplyAudioReload(m, s);
                                return;
                            }
                            AudioEngine.ReleaseMaster(_master);
                            AudioEngine.ReleaseSessions(_sessions);
                            _master = m;
                            _sessions = s ?? new List<AudioSession>();
                            _preloaded = true;
                            WarmUpRender();
                        }));
                    else
                    {
                        AudioEngine.ReleaseMaster(m);
                        AudioEngine.ReleaseSessions(s);
                    }
                }
                catch
                {
                    AudioEngine.ReleaseMaster(m);
                    AudioEngine.ReleaseSessions(s);
                }
            });
        }

        // Build cached controls without showing or activating the window. Showing
        // off-screen still steals focus and DoEvents can re-enter tray handlers.
        private bool _warmingUp;

        private void WarmUpRender()
        {
            if (Visible || _warmingUp) return;
            _warmingUp = true;
            try
            {
                Render();
            }
            catch { }
            finally { _warmingUp = false; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyWindowChromeTheme();
        }

        private void ApplyWindowChromeTheme()
        {
            try
            {
                int round = Win32.DWMWCP_ROUND;
                Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
                int dark = Theme.IsDark ? 1 : 0;
                Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
                int backdrop = Win32.DWMSBT_TRANSIENTWINDOW;
                int hr = Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, 4);
                if (hr != 0) ApplyAcrylicFallback();
            } catch { }
        }

        private void ApplyAcrylicFallback()
        {
            IntPtr accentPtr = IntPtr.Zero;
            try
            {
                var accent = new Win32.ACCENT_POLICY
                {
                    AccentState = Win32.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                    AccentFlags = 2,
                    GradientColor = Theme.AcrylicGradientColor(),
                    AnimationId = 0,
                };
                int size = Marshal.SizeOf(typeof(Win32.ACCENT_POLICY));
                accentPtr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(accent, accentPtr, false);
                var data = new Win32.WINDOWCOMPOSITIONATTRIBDATA
                {
                    Attribute = Win32.WCA_ACCENT_POLICY,
                    Data = accentPtr,
                    SizeOfData = size,
                };
                Win32.SetWindowCompositionAttribute(Handle, ref data);
            }
            catch { }
            finally
            {
                if (accentPtr != IntPtr.Zero) Marshal.FreeHGlobal(accentPtr);
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Win32.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        public void ShowNearTray(Win32.RECT? trayRect = null)
        {
            CancelDelayedClose();
            bool alreadyVisible = Visible && !_warmingUp;
            bool viewChanged = _view != View.Mixer;
            _view = View.Mixer;
            _lastTrayRect = trayRect;

            if (!alreadyVisible)
            {
                // If preloaded, use cached data instantly. Otherwise render an empty skeleton
                // and let the background loader populate it.
                if (!_preloaded)
                {
                    AudioEngine.ReleaseMaster(_master);
                    AudioEngine.ReleaseSessions(_sessions);
                    _master = null;
                    _sessions = new List<AudioSession>();
                }
                Render();
            }
            else if (viewChanged) Render();

            // Position slightly below the final spot so we can slide up into place.
            Point finalPos = CalculatePopupLocation(_lastTrayRect);
            int finalX = finalPos.X;
            _finalY = finalPos.Y;
            int hiddenY = _finalY + SlideOffset;
            int startY = alreadyVisible ? Location.Y : hiddenY;

            _isOpening = true;
            _isClosing = false;
            Location = new Point(finalX, startY);

            if (!alreadyVisible) Show();
            try { Activate(); } catch { }
            BringToFront();
            StartSyncTimer();

            // Defer the background refresh until the slide-in animation finishes —
            // otherwise BeginInvoke from the worker thread can land mid-animation
            // and the Render() call stalls the slide. The cached _sessions / _master
            // (from the previous open) are shown instantly; the sync timer keeps
            // their values live until the refresh lands.
            AnimateSlide(startY, _finalY, ScaledDuration(startY, _finalY, OpenDurationMs), /*opening:*/ true,
                onComplete: ReloadAudioInBackground);
        }

        public void ToggleNearTray(Win32.RECT? trayRect = null)
        {
            if (_warmingUp) return;

            if (Visible && _isClosing)
            {
                ShowNearTray(trayRect);
            }
            else if (Visible)
                BeginAnimatedClose();
            else
                ShowNearTray(trayRect);
        }

        public void CloseIfPointOutside(Win32.POINT pt)
        {
            if (!Visible || _warmingUp || _isClosing) return;
            if (Bounds.Contains(new Point(pt.X, pt.Y))) return;
            // The same mouse press also reaches NotifyIcon.MouseDown. Its toggle
            // owns tray clicks, regardless of which queued callback arrives first.
            if (_lastTrayRect.HasValue)
            {
                var tray = _lastTrayRect.Value;
                if (pt.X >= tray.Left && pt.X < tray.Right && pt.Y >= tray.Top && pt.Y < tray.Bottom) return;
            }
            ScheduleDelayedClose();
        }

        private const int SlideOffset = 14;
        private const double OpenDurationMs = 150.0;
        private const double CloseDurationMs = 120.0;
        private System.Windows.Forms.Timer _slideAnimTimer;
        private bool _isOpening;
        private bool _isClosing;
        private System.Windows.Forms.Timer _delayedCloseTimer;
        private int _finalY;
        private Win32.RECT? _lastTrayRect;

        private void AnimateSlide(int startY, int endY, double durationMs, bool opening, Action onComplete = null)
        {
            if (_slideAnimTimer != null) { _slideAnimTimer.Stop(); _slideAnimTimer.Dispose(); _slideAnimTimer = null; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _slideAnimTimer = new System.Windows.Forms.Timer { Interval = 12 };
            _slideAnimTimer.Tick += (s, e) =>
            {
                double t = Math.Min(1.0, sw.Elapsed.TotalMilliseconds / durationMs);
                // Smoothstep distributes this short movement across every frame. The
                // old cubic curves rounded several early frames to the same pixel and
                // then jumped near the end, which looked like UI-thread stutter.
                double eased = t * t * (3.0 - 2.0 * t);
                int y = (int)Math.Round(startY + (endY - startY) * eased);
                SetAnimatedWindowY(y);
                if (t >= 1.0)
                {
                    SetAnimatedWindowY(endY);
                    _slideAnimTimer.Stop();
                    _slideAnimTimer.Dispose();
                    _slideAnimTimer = null;
                    if (opening) _isOpening = false;
                    else
                    {
                        _isClosing = false;
                        // Actually hide the form once the slide-down completes.
                        // Using SetVisibleCore via Hide() — won't re-trigger Deactivate.
                        base.Hide();
                    }
                    if (onComplete != null) { try { onComplete(); } catch { } }
                }
            };
            _slideAnimTimer.Start();
        }

        private void SetAnimatedWindowY(int y)
        {
            uint flags = Win32.SWP_NOSIZE | Win32.SWP_NOZORDER
                | Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER;
            if (!IsHandleCreated || !Win32.SetWindowPos(Handle, IntPtr.Zero, Left, y, 0, 0, flags))
                Location = new Point(Left, y);
        }

        private static double ScaledDuration(int startY, int endY, double fullDurationMs)
        {
            double distance = Math.Abs(endY - startY);
            double fullDistance = Math.Max(1.0, SlideOffset);
            return Math.Max(55.0, fullDurationMs * Math.Min(1.0, distance / fullDistance));
        }

        private void ReloadAudioInBackground()
        {
            if (Interlocked.CompareExchange(ref _audioReloadPending, 1, 0) != 0) return;
            int seq = Interlocked.Increment(ref _audioReloadSeq);
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                AudioMaster m = null;
                List<AudioSession> s = null;
                try { m = AudioEngine.GetMaster(); } catch { }
                try { s = AudioEngine.EnumerateSessions(); } catch { }
                try
                {
                    if (IsHandleCreated && !IsDisposed)
                        BeginInvoke((Action)(() =>
                        {
                            if (seq != _audioReloadSeq)
                            {
                                AudioEngine.ReleaseMaster(m);
                                AudioEngine.ReleaseSessions(s);
                                return;
                            }
                            ApplyAudioReload(m, s);
                        }));
                    else
                    {
                        AudioEngine.ReleaseMaster(m);
                        AudioEngine.ReleaseSessions(s);
                    }
                }
                catch
                {
                    AudioEngine.ReleaseMaster(m);
                    AudioEngine.ReleaseSessions(s);
                }
                finally { Interlocked.Exchange(ref _audioReloadPending, 0); }
            });
        }

        // Diff the freshly-enumerated audio state against what's already on screen.
        // If the app list is identical (same PIDs), we keep the existing AppRows
        // intact — only the underlying session references swap, so the next sync
        // timer tick reads from fresh COM pointers. This means a re-open with the
        // same apps playing has ZERO re-render flicker.
        private void ApplyAudioReload(AudioMaster m, List<AudioSession> s)
        {
            if (IsDisposed || _view != View.Mixer || !Visible || _isClosing
                || _rowSyncs.Any(x => x.Row.IsDragging))
            {
                AudioEngine.ReleaseMaster(m);
                AudioEngine.ReleaseSessions(s);
                return;
            }
            _preloaded = true;
            if (s == null) s = new List<AudioSession>();

            bool listChanged;
            if (_sessions.Count != s.Count)
            {
                listChanged = true;
            }
            else
            {
                var oldPids = new HashSet<uint>(_sessions.Select(x => x.ProcessId));
                listChanged = !oldPids.SetEquals(s.Select(x => x.ProcessId));
            }

            AudioMaster oldMaster = _master;
            List<AudioSession> oldSessions = _sessions;
            _master = m;

            // A disconnected cached endpoint may have been omitted by RenderMixer
            // despite a non-null wrapper. Recovery must restore that missing row.
            bool masterChanged = _rowSyncs.Any(x => x.Master != null) != (m != null);
            if (listChanged)
            {
                _sessions = s;
            }
            else
            {
                foreach (var fresh in s)
                {
                    var existing = oldSessions.FirstOrDefault(x => x.ProcessId == fresh.ProcessId);
                    if (existing != null) AudioEngine.AdoptSessionInterfaces(existing, fresh);
                }
                _sessions = oldSessions;
                AudioEngine.ReleaseSessions(s);
            }

            // Swap session/master references inside _rowSyncs so the sync timer
            // reads from fresh COM pointers even when we skip a full Render.
            for (int i = 0; i < _rowSyncs.Count; i++)
            {
                var rs = _rowSyncs[i];
                if (rs.Master != null) rs.Master = m;
                else if (rs.Session != null)
                {
                    var match = _sessions.FirstOrDefault(x => x.ProcessId == rs.Session.ProcessId);
                    if (match != null) rs.Session = match;
                }
            }

            if (listChanged || masterChanged)
            {
                Render();
                if (listChanged) AudioEngine.ReleaseSessions(oldSessions);
            }
            AudioEngine.ReleaseMaster(oldMaster);
            // else: nothing to repaint. SyncFromSystem keeps values fresh.
        }

        private void PositionAtTray()
        {
            Location = CalculatePopupLocation(_lastTrayRect);
        }

        private Point CalculatePopupLocation(Win32.RECT? trayRect)
        {
            Screen screen = Screen.PrimaryScreen;
            int centerX = screen.WorkingArea.Right - 24;
            int centerY = screen.WorkingArea.Bottom + 24;

            if (trayRect.HasValue)
            {
                var r = trayRect.Value;
                centerX = r.Left + Math.Max(1, r.Right - r.Left) / 2;
                centerY = r.Top + Math.Max(1, r.Bottom - r.Top) / 2;
                screen = Screen.FromPoint(new Point(centerX, centerY));
            }

            Rectangle wa = screen.WorkingArea;
            const int margin = 8;

            int x;
            if (centerX <= wa.Left)
                x = wa.Left + margin;
            else if (centerX >= wa.Right)
                x = wa.Right - Width - margin;
            else
                x = centerX - Width / 2;
            x = Clamp(x, wa.Left + margin, wa.Right - Width - margin);

            int y;
            if (centerY <= wa.Top)
                y = wa.Top + margin;
            else if (centerY >= wa.Bottom)
                y = wa.Bottom - Height - margin;
            else
                y = centerY - Height / 2;
            y = Clamp(y, wa.Top + margin, wa.Bottom - Height - margin);

            return new Point(x, y);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (max < min) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (_warmingUp) return; // ignore the spurious deactivate during off-screen warm-up
            ScheduleDelayedClose();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            CancelDelayedClose();
        }

        private void ScheduleDelayedClose()
        {
            if (!Visible || _warmingUp || _isClosing) return;
            if (_delayedCloseTimer == null)
            {
                _delayedCloseTimer = new System.Windows.Forms.Timer { Interval = 100 };
                _delayedCloseTimer.Tick += (s, e) =>
                {
                    _delayedCloseTimer.Stop();
                    BeginAnimatedClose();
                };
            }
            _delayedCloseTimer.Stop();
            _delayedCloseTimer.Start();
        }

        private void CancelDelayedClose()
        {
            if (_delayedCloseTimer != null) _delayedCloseTimer.Stop();
        }

        private void BeginAnimatedClose()
        {
            if (!Visible || _isClosing) return;
            CancelDelayedClose();
            if (_isOpening)
            {
                if (_slideAnimTimer != null) { _slideAnimTimer.Stop(); _slideAnimTimer.Dispose(); _slideAnimTimer = null; }
                _isOpening = false;
            }
            _isClosing = true;
            StopSyncTimer();
            int startY = Location.Y;
            int endY = (_finalY != 0 ? _finalY : startY) + SlideOffset;
            AnimateSlide(startY, endY, ScaledDuration(startY, endY, CloseDurationMs), /*opening:*/ false);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (_warmingUp) return; // off-screen warm-up shouldn't start sync poll
            if (Visible) StartSyncTimer();
            else
            {
                StopSyncTimer();
                Interlocked.Increment(ref _audioReloadSeq);
                Interlocked.Increment(ref _deviceReloadSeq);
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_SETTINGCHANGE = 0x001A;
            if (m.Msg == WM_SETTINGCHANGE && m.LParam != IntPtr.Zero)
            {
                try
                {
                    string param = Marshal.PtrToStringUni(m.LParam);
                    if (string.Equals(param, "ImmersiveColorSet", StringComparison.OrdinalIgnoreCase))
                    {
                        RefreshThemeIfChanged();
                    }
                }
                catch { }
            }
            base.WndProc(ref m);
        }

        private void StartSyncTimer()
        {
            if (_syncTimer != null) return;
            _syncTicks = 0;
            _syncTimer = new System.Windows.Forms.Timer { Interval = 250 };
            _syncTimer.Tick += (s, e) => SyncFromSystem();
            _syncTimer.Start();
        }

        private void StopSyncTimer()
        {
            if (_syncTimer == null) return;
            _syncTimer.Stop();
            _syncTimer.Dispose();
            _syncTimer = null;
        }

        private void SyncFromSystem()
        {
            foreach (var rs in _rowSyncs)
            {
                try
                {
                    float v = 0;
                    bool muted = false;
                    if (rs.Master != null)
                    {
                        v = rs.Master.Volume;
                        muted = rs.Master.Mute;
                    }
                    else if (rs.Session != null && rs.Session.Volume != null)
                    {
                        rs.Session.Volume.GetMasterVolume(out v);
                        rs.Session.Volume.GetMute(out muted);
                    }
                    else continue;
                    rs.Row.UpdateExternalState(v, muted);
                }
                catch { }
            }

            // Creation notifications do not cover expired sessions or default-device
            // changes, and may be unavailable on some Windows configurations.
            // Reconcile while open; never enumerate on the UI thread or during a drag.
            if (Visible && _view == View.Mixer && !_isOpening && !_isClosing
                && !_rowSyncs.Any(x => x.Row.IsDragging) && ++_syncTicks >= 8)
            {
                _syncTicks = 0;
                ReloadAudioInBackground();
            }
        }

        private void ReloadAudio()
        {
            AudioMaster oldMaster = _master;
            List<AudioSession> oldSessions = _sessions;
            try { _master = AudioEngine.GetMaster(); } catch { _master = null; }
            try { _sessions = AudioEngine.EnumerateSessions(); } catch { _sessions = new List<AudioSession>(); }
            AudioEngine.ReleaseMaster(oldMaster);
            AudioEngine.ReleaseSessions(oldSessions);
        }

        private void ReloadDevices()
        {
            try { _devices = AudioEngine.EnumerateOutputDevices(); } catch { _devices = new List<DeviceInfo>(); }
        }

        private void GoToSettings()
        {
            _view = View.Settings;
            // Use cached _devices if we have any (from preload or previous visit). Render instantly.
            Render();

            // Refresh in background to pick up any device changes.
            int seq = Interlocked.Increment(ref _deviceReloadSeq);
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                List<DeviceInfo> devs = null;
                try { devs = AudioEngine.EnumerateOutputDevices(); } catch { }
                try
                {
                    if (IsHandleCreated && !IsDisposed)
                        BeginInvoke((Action)(() =>
                        {
                            if (seq != _deviceReloadSeq) return;
                            if (_view != View.Settings || !Visible) return;
                            devs = devs ?? new List<DeviceInfo>();
                            if (DeviceListsEqual(_devices, devs)) return;
                            _devices = devs;
                            Render();
                        }));
                }
                catch { }
            });
        }

        private void GoToMixer()
        {
            _view = View.Mixer;
            Render();
            ReloadAudioInBackground();
        }

        private static bool DeviceListsEqual(List<DeviceInfo> a, List<DeviceInfo> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                var x = a[i];
                var y = b[i];
                if (!string.Equals(x.Id, y.Id, StringComparison.OrdinalIgnoreCase)) return false;
                if (!string.Equals(x.Name, y.Name, StringComparison.CurrentCulture)) return false;
                if (!string.Equals(x.Subtitle, y.Subtitle, StringComparison.CurrentCulture)) return false;
                if (x.FormFactor != y.FormFactor || x.IsDefault != y.IsDefault) return false;
            }
            return true;
        }

        private void Render()
        {
            StopDeviceAnimation();
            _deviceCardsById.Clear();
            _rowSyncs.Clear();

            // Suppress paint during bulk control changes to avoid flicker.
            bool redrawSuppressed = false;
            if (_content.IsHandleCreated)
            {
                Win32.SendMessage(_content.Handle, Win32.WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
                redrawSuppressed = true;
            }

            _content.SuspendLayout();
            try
            {
                var oldControls = _content.Controls.Cast<Control>().ToArray();
                _content.Controls.Clear();
                foreach (var control in oldControls) control.Dispose();
                if (_view == View.Mixer) RenderMixer();
                else RenderSettings();
            }
            finally
            {
                _content.ResumeLayout(true);
                if (redrawSuppressed)
                {
                    Win32.SendMessage(_content.Handle, Win32.WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                    // Invalidate(true) schedules paint via WM_PAINT (async). Refresh() forces
                    // a synchronous paint which blocks the UI thread for ~30-50ms with many
                    // child controls. Async paint feels snappier on first show.
                    _content.Invalidate(true);
                }
            }

            // Skip position reset while a slide animation is in flight — otherwise
            // the form would snap to its final spot mid-animation (or to the closed
            // spot during close).
            if (Visible && !_isOpening && !_isClosing) PositionAtTray();
        }

        private void StopDeviceAnimation()
        {
            if (_deviceAnimTimer != null)
            {
                _deviceAnimTimer.Stop();
                _deviceAnimTimer.Dispose();
                _deviceAnimTimer = null;
            }
        }

        // ----- Mixer view -----
        // Layout (top→bottom): Header, Master, "APPS" label, scrollable app rows.
        // Form Height stays fixed (~4 visible app rows; rest scrolls).
        private void RenderMixer()
        {
            // 1) Build apps scrollable area first; it'll Dock=Fill the bottom.
            var appsScroll = new DarkScrollPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Theme.WindowBg,
            };

            var apps = _sessions.Where(s => !s.IsSystemSounds).ToList();
            var sysSession = _sessions.FirstOrDefault(s => s.IsSystemSounds);
            if (sysSession != null) apps.Add(sysSession);

            // Add rows in reverse so first session ends up visually at top within scrollable.
            for (int i = apps.Count - 1; i >= 0; i--)
            {
                var s = apps[i];
                float v;
                bool m;
                try
                {
                    if (s.Volume == null) continue;
                    s.Volume.GetMasterVolume(out v);
                    s.Volume.GetMute(out m);
                }
                catch
                {
                    continue;
                }

                string key = s.IsSystemSounds ? "system" : (s.ProcessName ?? "");
                var brand = AppRegistry.Lookup(key);
                var brandColor = AppRegistry.ColorOrFallback(key);

                Image img = s.IconImage;
                IconTile tile;
                if (s.IsSystemSounds)
                    tile = new IconTile("🖥", new Font("Segoe UI Symbol", 14f), Theme.TileBg);
                else if (img != null)
                    tile = new IconTile(img, Theme.TileBg, false);
                else
                {
                    string glyph = !string.IsNullOrEmpty(s.DisplayName) ? s.DisplayName.Substring(0, 1).ToUpper() : "?";
                    tile = new IconTile(glyph, new Font("Segoe UI Semibold", 13f), brandColor);
                }

                var captured = s;
                bool canRoute = !s.IsSystemSounds && AudioPolicyConfig.IsSupported && s.ProcessId != 0;
                var row = new AppRow(s.DisplayName, tile, brandColor, brand, v, m,
                    s.ProcessId, canRoute,
                    () => _devices,
                    (val) => { try { Guid g = Guid.Empty; captured.Volume.SetMasterVolume(val, ref g); } catch { } },
                    (mute) => { try { Guid g = Guid.Empty; captured.Volume.SetMute(mute, ref g); } catch { } });
                appsScroll.Controls.Add(row);
                _rowSyncs.Add(new RowSync { Row = row, Session = captured });
            }

            _content.Controls.Add(appsScroll);

            // 2) Apps section label
            if (apps.Count > 0)
            {
                var sec = new SectionLabel(Strings.Apps);
                _content.Controls.Add(sec);
            }

            // 3) Master row
            float masterVolume;
            bool masterMuted;
            if (_master != null && _master.TryGetVolume(out masterVolume) && _master.TryGetMute(out masterMuted))
            {
                var masterTile = new IconTile("🔊", new Font("Segoe UI Symbol", 14f), Theme.AccentBlue);
                var row = new AppRow(Strings.MainSound, masterTile, Theme.AccentBlue, null, masterVolume, masterMuted,
                    0, false, null,
                    (val) => { try { _master.Volume = val; } catch { } },
                    (mute) => { try { _master.Mute = mute; } catch { } });
                _content.Controls.Add(row);
                _rowSyncs.Add(new RowSync { Row = row, Master = _master });
            }

            // 4) Header (added LAST → docks at TOP)
            var settingsBtn = new IconHeaderButton("⚙");
            settingsBtn.Clicked += () => GoToSettings();
            var header = new HeaderPanel(Strings.VolumeMixer, GetSpeakerImage(), null, settingsBtn);
            _content.Controls.Add(header);
        }

        // ----- Settings view -----
        // Layout (top→bottom): Header, "OUTPUT DEVICE" label, Devices (scrollable),
        //                      "SPATIAL SOUND" label, Spatial card.
        private void RenderSettings()
        {
            // 1) Devices scrollable — Dock=Fill, takes the middle area between Top and Bottom siblings.
            _devicesScroll = new DarkScrollPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Theme.WindowBg,
            };

            if (_devices.Count > 0)
            {
                int totalContentH = _devices.Count * (DevCardH + DevCardGap) + 8;
                _devicesScroll.AutoScrollMinSize = new Size(0, totalContentH);

                // Fixed-size cards centered with comfortable right margin. No Anchor —
                // initial container size is unsized and Anchor=Right would expand the
                // card to absurd widths once the container docks/Fills the parent.
                const int cardW = 320;
                int leftPad = (Width - cardW) / 2;
                for (int i = 0; i < _devices.Count; i++)
                {
                    var d = _devices[i];
                    var captured = d;
                    var card = new DeviceCard(d.Name, FormFactorLabel(d.FormFactor), d.FormFactor, d.IsDefault,
                        () => OnDeviceClicked(captured));
                    card.Size = new Size(cardW, DevCardH);
                    card.Location = new Point(leftPad, 4 + i * (DevCardH + DevCardGap));
                    _devicesScroll.Controls.Add(card);
                    _deviceCardsById[d.Id] = card;
                }
            }
            else
            {
                var emptyLbl = new Label
                {
                    Text = Strings.NoOutputs,
                    Dock = DockStyle.Fill,
                    ForeColor = Theme.TextMuted,
                    BackColor = Theme.WindowBg,
                    Font = UiFonts.Body,
                    TextAlign = ContentAlignment.MiddleCenter,
                };
                _devicesScroll.Controls.Add(emptyLbl);
            }

            _content.Controls.Add(_devicesScroll);

            // Bottom spatial sound shortcut. Windows owns the actual spatial format UI,
            // so this opens the selected/default output device properties page.
            var spatialPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 92,
                BackColor = Theme.WindowBg,
            };
            var spatialLabel = new SectionLabel(Strings.SpatialSound);
            spatialPanel.Controls.Add(spatialLabel);
            const int spatialCardW = 320;
            int spatialLeft = (Width - spatialCardW) / 2;
            var spatialCard = new DeviceCard(Strings.SpatialSound, Strings.SpatialSoundSubtitle, 10, false,
                OpenSpatialSoundSettings);
            spatialCard.Size = new Size(spatialCardW, DevCardH);
            spatialCard.Location = new Point(spatialLeft, 34);
            spatialPanel.Controls.Add(spatialCard);
            _content.Controls.Add(spatialPanel);

            // Top siblings: "OUTPUT DEVICE" label first (inner), header last (outermost top).
            var devLabel = new SectionLabel(Strings.OutputDevice);
            _content.Controls.Add(devLabel);

            var backBtn = new IconHeaderButton("←") { GlyphFont = UiFonts.BackGlyph };
            backBtn.Clicked += () => GoToMixer();
            var header = new HeaderPanel(Strings.SoundSettings, null, backBtn, null);
            _content.Controls.Add(header);
        }

        private void OpenSpatialSoundSettings()
        {
            try
            {
                string id = null;
                var current = _devices.FirstOrDefault(d => d.IsDefault) ?? _devices.FirstOrDefault();
                if (current != null) id = current.Id;

                string uri = !string.IsNullOrEmpty(id)
                    ? "ms-settings:sound-properties?endpointId=" + Uri.EscapeDataString(id)
                    : "ms-settings:sound-defaultoutputproperties";

                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            }
            catch
            {
                try { Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true }); } catch { }
            }
        }

        private void OnDeviceClicked(DeviceInfo clicked)
        {
            if (clicked.IsDefault || _switchInProgress) return;
            _switchInProgress = true;

            // Update model
            foreach (var d in _devices) d.IsDefault = false;
            clicked.IsDefault = true;
            _devices = _devices.OrderBy(x => x.IsDefault ? 0 : 1)
                               .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                               .ToList();

            // In-place update: change pill/tile on each existing card AND compute new Y targets.
            // No Render() = no flicker; only the affected cards repaint, then animate to new positions.
            var anim = new List<DeviceCardAnimItem>();
            for (int i = 0; i < _devices.Count; i++)
            {
                var d = _devices[i];
                DeviceCard card;
                if (!_deviceCardsById.TryGetValue(d.Id, out card)) continue;
                card.UpdateIsDefault(d.IsDefault);
                int targetY = 4 + i * (DevCardH + DevCardGap);
                int startY = card.Top;
                if (startY != targetY)
                    anim.Add(new DeviceCardAnimItem { Card = card, StartY = startY, EndY = targetY });
            }

            // Scroll back to top so user sees the newly-default card after animation.
            if (_devicesScroll != null) _devicesScroll.AutoScrollPosition = new Point(0, 0);

            if (anim.Count > 0)
            {
                StopDeviceAnimation();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                _deviceAnimTimer = new System.Windows.Forms.Timer { Interval = 16 };
                _deviceAnimTimer.Tick += (s, e) =>
                {
                    double t = Math.Min(1.0, sw.Elapsed.TotalMilliseconds / 260.0);
                    double eased = 1.0 - Math.Pow(1.0 - t, 3);
                    foreach (var item in anim)
                    {
                        if (item.Card.IsDisposed) continue;
                        int y = (int)Math.Round(item.StartY + (item.EndY - item.StartY) * eased);
                        if (item.Card.Top != y) item.Card.Top = y;
                    }
                    if (t >= 1.0) StopDeviceAnimation();
                };
                _deviceAnimTimer.Start();
            }

            // Background COM call (slow! must not block UI).
            string id = clicked.Id;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try { AudioEngine.SetDefaultDevice(id); } catch { }
                try
                {
                    if (IsHandleCreated && !IsDisposed)
                        BeginInvoke((Action)(() => { _switchInProgress = false; }));
                    else
                        _switchInProgress = false;
                }
                catch { _switchInProgress = false; }
            });
        }

        private sealed class DeviceCardAnimItem
        {
            public DeviceCard Card;
            public int StartY;
            public int EndY;
        }

        private static string FormFactorLabel(uint ff)
        {
            switch (ff)
            {
                case 1: return Strings.Speakers;
                case 3: return Strings.Headphones;
                case 5: return Strings.Headset;
                case 8: return Strings.Spdif;
                case 9: return Strings.HdmiDp;
                default: return Strings.AudioOutput;
            }
        }

        private static Image GetSpeakerImage()
        {
            if (_speakerImage != null) return _speakerImage;
            try
            {
                string mmres = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mmres.dll");
                if (File.Exists(mmres))
                {
                    using (var ico = Win32.LoadIconFromFile(mmres, 0, false))
                    {
                        if (ico != null) { _speakerImage = ico.ToBitmap(); return _speakerImage; }
                    }
                }
                string sndvol = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "SndVol.exe");
                if (File.Exists(sndvol))
                {
                    using (var ico = Icon.ExtractAssociatedIcon(sndvol))
                    {
                        if (ico != null) { _speakerImage = ico.ToBitmap(); return _speakerImage; }
                    }
                }
            } catch { }
            _speakerImage = SystemIcons.Application.ToBitmap();
            return _speakerImage;
        }
    }

    // ============================================================
    // Tray app with dynamic volume icon
    // ============================================================
    internal sealed class TrayApp : ApplicationContext
    {
        private readonly NotifyIcon _icon;
        private readonly MixerForm _form;
        private readonly System.Windows.Forms.Timer _pollTimer;
        private Icon[] _iconSet; // [mute, zero, low, mid, high]
        private int _lastBucket = -1;
        private AudioMaster _master;

        // Low-level hook is armed only while the popup is visible or the pointer is
        // over our icon. Keeping it installed globally during games is unnecessary.
        private IntPtr _hookId;
        private Win32.HookProc _hookProc; // keep ref alive (GC would collect a stack-only delegate)
        private SynchronizationContext _uiCtx;
        private IntPtr _formHandle;

        private DateTime _lastTrayToggle = DateTime.MinValue;
        private DateTime _lastNotifyIconMouseMove = DateTime.MinValue;
        private Win32.POINT _lastNotifyIconMousePoint;
        private volatile bool _popupVisible;

        public TrayApp()
        {
            _form = new MixerForm();

            // Force the form's Win32 handle to be created so background-thread BeginInvoke
            // calls during PreloadAsync work (the form stays invisible).
            if (!_form.IsHandleCreated) { var _h = _form.Handle; }
            _formHandle = _form.Handle;

            // Pre-warm audio data so the first popup-show is instant.
            _form.PreloadAsync();

            _iconSet = LoadVolumeIcons();

            _icon = new NotifyIcon
            {
                Icon = _iconSet[2],
                Visible = true,
                Text = Strings.VolumeMixer,
            };
            _form.VisibleChanged += (s, e) =>
            {
                _popupVisible = _form.Visible;
                RefreshMouseHookState();
            };

            _icon.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                TogglePopupFromTray();
            };
            _icon.MouseMove += (s, e) =>
            {
                // Shell_NotifyIconGetRect can point at the overflow chevron while
                // this icon is hidden. MouseMove is delivered only for our real icon,
                // so remember it as proof before accepting a wheel event.
                _lastNotifyIconMouseMove = DateTime.UtcNow;
                Point cursor = Cursor.Position;
                _lastNotifyIconMousePoint = new Win32.POINT { X = cursor.X, Y = cursor.Y };
                InstallWheelHook();
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add(Strings.OpenMixer, null, (s, e) => _form.ShowNearTray(GetTrayIconRect()));
            menu.Items.Add(Strings.SoundSettings, null, (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true }); } catch { }
            });
            menu.Items.Add(new ToolStripSeparator());

            var autoStartItem = new ToolStripMenuItem(Strings.AutoStart);
            autoStartItem.Checked = IsAutoStartEnabled();
            autoStartItem.Click += (s, e) =>
            {
                try { SetAutoStart(!autoStartItem.Checked); } catch { }
                autoStartItem.Checked = IsAutoStartEnabled();
            };
            menu.Items.Add(autoStartItem);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Strings.ExitItem, null, (s, e) =>
            {
                _icon.Visible = false;
                Application.Exit();
            });
            _icon.ContextMenuStrip = menu;

            _pollTimer = new System.Windows.Forms.Timer { Interval = 750 };
            _pollTimer.Tick += (s, e) =>
            {
                UpdateTrayIcon();
                RefreshMouseHookState();
            };
            _pollTimer.Start();
            UpdateTrayIcon();

            // Capture UI synchronization context so the wheel hook callback (called
            // on the hook thread) can marshal volume changes back to the UI thread.
            _uiCtx = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            // Listen for new audio sessions globally — when an app starts playing,
            // refresh the popup if it's open. Callback fires on a COM thread; the
            // form's BeginInvoke handles UI marshaling.
            AudioEngine.RegisterSessionListener(() =>
            {
                if (_form != null) _form.OnExternalSessionChange();
            });
        }

        private void InstallWheelHook()
        {
            if (_hookId != IntPtr.Zero) return;
            try
            {
                _hookProc = MouseHookCallback;
                _hookId = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _hookProc,
                    Win32.GetModuleHandle("user32"), 0);
            }
            catch { _hookId = IntPtr.Zero; }
        }

        private void UninstallWheelHook()
        {
            if (_hookId != IntPtr.Zero)
            {
                try { Win32.UnhookWindowsHookEx(_hookId); } catch { }
                _hookId = IntPtr.Zero;
            }
        }

        private void RefreshMouseHookState()
        {
            if (_popupVisible)
            {
                InstallWheelHook();
                return;
            }

            if (IsForegroundFullscreenAppActive())
            {
                UninstallWheelHook();
                return;
            }

            Point cursor = Cursor.Position;
            bool nearLastConfirmedIconPoint = _lastNotifyIconMouseMove != DateTime.MinValue
                && (DateTime.UtcNow - _lastNotifyIconMouseMove).TotalSeconds <= 60
                && Math.Abs(cursor.X - _lastNotifyIconMousePoint.X) <= 16
                && Math.Abs(cursor.Y - _lastNotifyIconMousePoint.Y) <= 16;

            if (nearLastConfirmedIconPoint) InstallWheelHook();
            else UninstallWheelHook();
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int msg = wParam.ToInt32();
                    if (msg == Win32.WM_MOUSEWHEEL)
                    {
                        var data = (Win32.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Win32.MSLLHOOKSTRUCT));
                        short delta = (short)((data.mouseData >> 16) & 0xFFFF);
                        if (IsCursorOverTrayIcon(data.pt))
                        {
                            // Marshal to UI thread to mutate volume; do NOT block hook thread.
                            int notches = delta / 120;
                            if (notches != 0 && _uiCtx != null)
                                _uiCtx.Post(_ => AdjustMasterVolume(notches), null);
                            return new IntPtr(1); // mark handled, suppress further wheel processing
                        }
                    }
                    else if (msg == Win32.WM_LBUTTONDOWN || msg == Win32.WM_RBUTTONDOWN || msg == Win32.WM_MBUTTONDOWN)
                    {
                        var data = (Win32.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Win32.MSLLHOOKSTRUCT));
                        if (_popupVisible && _uiCtx != null)
                        {
                            var pt = data.pt;
                            _uiCtx.Post(_ => _form.CloseIfPointOutside(pt), null);
                        }
                    }
                }
            }
            catch { }
            return Win32.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private void TogglePopupFromTray()
        {
            try
            {
                if (IsForegroundFullscreenAppActive()) return;

                var now = DateTime.UtcNow;
                if ((now - _lastTrayToggle).TotalMilliseconds < 80)
                    return;

                _lastTrayToggle = now;
                _form.ToggleNearTray(GetTrayIconRect());
            }
            catch { }
        }

        private bool IsCursorOverTrayIcon(Win32.POINT pt)
        {
            if ((DateTime.UtcNow - _lastNotifyIconMouseMove).TotalSeconds > 60)
                return false;
            if (Math.Abs(pt.X - _lastNotifyIconMousePoint.X) > 12
                || Math.Abs(pt.Y - _lastNotifyIconMousePoint.Y) > 12)
                return false;

            Win32.RECT rect;
            if (!TryGetTrayIconRect(out rect)) return false;
            return pt.X >= rect.Left && pt.X <= rect.Right
                && pt.Y >= rect.Top && pt.Y <= rect.Bottom;
        }

        private Win32.RECT? GetTrayIconRect()
        {
            Win32.RECT rect;
            return TryGetTrayIconRect(out rect) ? (Win32.RECT?)rect : null;
        }

        private bool TryGetTrayIconRect(out Win32.RECT rect)
        {
            rect = new Win32.RECT();
            try
            {
                if (IsForegroundFullscreenAppActive()) return false;

                var winFld = typeof(NotifyIcon).GetField("window",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var idFld = typeof(NotifyIcon).GetField("id",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (winFld == null || idFld == null) return false;
                var window = winFld.GetValue(_icon) as NativeWindow;
                if (window == null) return false;
                int id = (int)idFld.GetValue(_icon);

                var ident = new Win32.NOTIFYICONIDENTIFIER
                {
                    cbSize = Marshal.SizeOf(typeof(Win32.NOTIFYICONIDENTIFIER)),
                    hWnd = window.Handle,
                    uID = (uint)id,
                    guidItem = Guid.Empty,
                };
                int hr = Win32.Shell_NotifyIconGetRect(ref ident, out rect);
                if (hr != 0) return false;
                return true;
            }
            catch
            {
                rect = new Win32.RECT();
                return false;
            }
        }

        private bool IsForegroundFullscreenAppActive()
        {
            try
            {
                IntPtr hwnd = Win32.GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return false;
                if (_formHandle != IntPtr.Zero && hwnd == _formHandle) return false;

                Win32.RECT wr;
                if (!Win32.GetWindowRect(hwnd, out wr)) return false;

                IntPtr mon = Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST);
                if (mon == IntPtr.Zero) return false;

                var mi = new Win32.MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(Win32.MONITORINFO));
                if (!Win32.GetMonitorInfo(mon, ref mi)) return false;

                int tolerance = 2;
                bool coversMonitor =
                    wr.Left <= mi.rcMonitor.Left + tolerance &&
                    wr.Top <= mi.rcMonitor.Top + tolerance &&
                    wr.Right >= mi.rcMonitor.Right - tolerance &&
                    wr.Bottom >= mi.rcMonitor.Bottom - tolerance;

                return coversMonitor;
            }
            catch { return false; }
        }

        private void AdjustMasterVolume(int notches)
        {
            List<AudioMaster> masters = null;
            AudioMaster retained = null;
            try
            {
                // Always re-acquire — cached endpoints can go stale after output changes.
                // Adjust every default render role because some apps use the communications
                // endpoint while the tray tooltip is reading the multimedia endpoint.
                masters = AudioEngine.GetDefaultRoleMasters();
                if (masters == null || masters.Count == 0) return;

                for (int i = 0; i < masters.Count; i++)
                {
                    var master = masters[i];
                    if (master == null) continue;

                    bool applied = master.TryStepVolume(notches);
                    if (!applied)
                    {
                        float current;
                        if (!master.TryGetVolume(out current)) continue;
                        float step = 0.02f * notches; // 2% per wheel notch
                        float next = Math.Max(0f, Math.Min(1f, current + step));
                        if (!master.TrySetVolume(next)) continue;
                        if (next > 0.001f)
                        {
                            bool muted;
                            if (master.TryGetMute(out muted) && muted)
                                master.TrySetMute(false);
                        }
                    }
                }

                retained = masters[0]; // keep tray tooltip on the primary multimedia endpoint
                ReplaceTrayMaster(retained);
                UpdateTrayIcon();
            }
            catch { ReplaceTrayMaster(null); }
            finally
            {
                if (masters != null)
                {
                    foreach (var master in masters)
                    {
                        if (!ReferenceEquals(master, retained)) AudioEngine.ReleaseMaster(master);
                    }
                }
            }
        }

        private void ReplaceTrayMaster(AudioMaster next)
        {
            if (ReferenceEquals(_master, next)) return;
            AudioMaster previous = _master;
            _master = next;
            AudioEngine.ReleaseMaster(previous);
        }

        private int _accentPollCounter;
        private int _masterRefreshCounter;

        private void UpdateTrayIcon()
        {
            // Periodically refresh the cached endpoint so we don't keep talking to the
            // old default device after the user switches output. Every ~6 seconds.
            if (++_masterRefreshCounter >= 8)
            {
                _masterRefreshCounter = 0;
                ReplaceTrayMaster(null);
            }
            try
            {
                if (_master == null) ReplaceTrayMaster(AudioEngine.GetMaster());
                bool muted = _master.Mute;
                float v = _master.Volume;
                int bucket;
                if (muted) bucket = 0;
                else if (v <= 0.001f) bucket = 1;
                else if (v < 0.34f)  bucket = 2;
                else if (v < 0.67f)  bucket = 3;
                else                 bucket = 4;

                if (bucket != _lastBucket)
                {
                    _icon.Icon = _iconSet[bucket];
                    _lastBucket = bucket;
                }
                int pct = (int)Math.Round(v * 100);
                _icon.Text = muted
                    ? (Strings.VolumePrefix + Strings.MutedText)
                    : (Strings.VolumePrefix + pct + "%");
            }
            catch
            {
                ReplaceTrayMaster(null); // re-acquire on next tick
            }

            // Backup accent-color polling. SystemEvents.UserPreferenceChanged is the
            // primary path but doesn't always fire on Win11; check every ~6s anyway.
            if (++_accentPollCounter >= 8)
            {
                _accentPollCounter = 0;
                if (_form != null) _form.RefreshThemeIfChanged();
            }
        }

        // Keep the old shortcut path only to clean up entries from earlier builds.
        private static string AutoStartShortcutPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "VolumeMixer.lnk"); }
        }

        private const string AutoStartRunRegPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AutoStartApprovalRegPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string AutoStartRunValueName = "VolumeMixer";

        private static bool IsAutoStartEnabled()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(AutoStartRunRegPath))
                {
                    string command = k == null ? null : k.GetValue(AutoStartRunValueName) as string;
                    if (string.IsNullOrWhiteSpace(command)) return false;
                }

                // Task Manager / Settings stores the user's enabled state here.
                // A stale disabled value previously left the tray menu checked even
                // though Windows would no longer launch the app at sign-in.
                using (var k = Registry.CurrentUser.OpenSubKey(AutoStartApprovalRegPath))
                {
                    var value = k == null ? null : k.GetValue(AutoStartRunValueName) as byte[];
                    return value == null || value.Length == 0 || value[0] == 2;
                }
            }
            catch { return false; }
        }

        private static void SetAutoStart(bool enable)
        {
            // The Run value is the durable source of truth. Older builds only used
            // a Startup-folder shortcut, which could disappear while Windows kept
            // stale approval metadata for it.
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(AutoStartRunRegPath))
                {
                    if (k != null)
                    {
                        if (enable)
                        {
                            string exe = Application.ExecutablePath;
                            k.SetValue(AutoStartRunValueName, "\"" + exe + "\" --startup", RegistryValueKind.String);
                            using (var approval = Registry.CurrentUser.CreateSubKey(AutoStartApprovalRegPath))
                            {
                                if (approval != null) approval.SetValue(AutoStartRunValueName, new byte[] { 2, 0, 0, 0 }, RegistryValueKind.Binary);
                            }
                        }
                        else
                        {
                            k.DeleteValue(AutoStartRunValueName, false);
                            using (var approval = Registry.CurrentUser.OpenSubKey(AutoStartApprovalRegPath, true))
                            {
                                if (approval != null) approval.DeleteValue(AutoStartRunValueName, false);
                            }
                        }
                    }
                }
            }
            catch { }

            string lnk = AutoStartShortcutPath;
            if (!enable)
            {
                try { if (File.Exists(lnk)) File.Delete(lnk); } catch { }
                return;
            }

            // Remove the obsolete shortcut left by older builds. The Run entry above
            // is the only startup registration used now.
            try { if (File.Exists(lnk)) File.Delete(lnk); } catch { }
        }

        private static Icon[] LoadVolumeIcons()
        {
            // Try Windows' SndVolSSO.dll. Indices:
            //   On Win10/11 the first 5 icons in this DLL are the speaker volume states.
            //   Mapping verified by trial: 0=high, 1=zero, 2=mute, 3=low, 4=mid (varies per build)
            //   We try common arrangement; if it looks wrong, custom-rendered icons take over.
            string sso = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "SndVolSSO.dll");
            Icon[] icons = new Icon[5]; // [mute, zero, low, mid, high]

            if (File.Exists(sso))
            {
                // Verified SndVolSSO.dll layout (Win11 26200):
                //   index 0 = legacy/old speaker icon (DO NOT USE — looks broken)
                //   index 1 = muted speaker with × overlay
                //   index 2 = speaker low (1 wave + faded)
                //   index 3 = speaker low (1 wave variant)
                //   index 4 = speaker mid (2 waves)
                //   index 5 = speaker high (3 waves, full)
                icons[0] = Win32.LoadIconFromFile(sso, 1, true); // mute
                icons[1] = Win32.LoadIconFromFile(sso, 1, true); // zero (use mute X)
                icons[2] = Win32.LoadIconFromFile(sso, 3, true); // low
                icons[3] = Win32.LoadIconFromFile(sso, 4, true); // mid
                icons[4] = Win32.LoadIconFromFile(sso, 5, true); // high (3 waves = full)
            }

            // Fallback: render our own white speaker icons.
            for (int i = 0; i < 5; i++)
            {
                if (icons[i] == null) icons[i] = RenderSpeakerIcon(i);
            }
            return icons;
        }

        private static Icon RenderSpeakerIcon(int bucket)
        {
            // 0=mute, 1=zero, 2=low, 3=mid, 4=high
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    using (var br = new SolidBrush(Color.White))
                    {
                        Point[] speaker =
                        {
                            new Point(7, 13), new Point(12, 13),
                            new Point(17, 7),  new Point(17, 25),
                            new Point(12, 19), new Point(7, 19)
                        };
                        g.FillPolygon(br, speaker);
                    }

                    using (var pen = new Pen(Color.White, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        if (bucket >= 2) g.DrawArc(pen, 19, 12, 5, 8,  -45, 90);
                        if (bucket >= 3) g.DrawArc(pen, 22, 9,  6, 14, -45, 90);
                        if (bucket >= 4) g.DrawArc(pen, 25, 6,  6, 20, -45, 90);
                    }

                    if (bucket == 0)
                    {
                        using (var pen = new Pen(Color.FromArgb(255, 80, 80), 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        {
                            g.DrawLine(pen, 19, 9, 30, 23);
                        }
                    }
                }
                IntPtr hIcon = bmp.GetHicon();
                Icon ico = (Icon)Icon.FromHandle(hIcon).Clone();
                Win32.DestroyIcon(hIcon);
                return ico;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                AudioEngine.UnregisterSessionListener();
                UninstallWheelHook();
                ReplaceTrayMaster(null);
                if (_pollTimer != null) _pollTimer.Dispose();
                if (_icon != null) _icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // ============================================================
    // Entry point
    // ============================================================
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        // Held for process lifetime so the mutex stays acquired.
        private static System.Threading.Mutex _singleInstanceMutex;

        [STAThread]
        public static void Main()
        {
            // Single-instance guard. Auto-start + manual launch was producing two
            // tray icons + two pollers stepping on each other's SetDefaultDevice calls.
            // Per-user mutex (Local\) — multiple Windows users can each have their own.
            bool createdNew;
            _singleInstanceMutex = new System.Threading.Mutex(true, @"Local\VolumeMixer.SingleInstance.{8C7F3B9A-4D1A-4A82-9B3E-7E5C1A8D5F12}", out createdNew);
            if (!createdNew)
            {
                // Another instance is already running — exit silently. The user will
                // see the existing tray icon; double-launching is a no-op.
                return;
            }

            // DPI awareness — try Per-Monitor V2 (Win10 1607+), fall back to system DPI aware (Vista+).
            try
            {
                if (!SetProcessDpiAwarenessContext(new IntPtr(-4))) // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
                    SetProcessDPIAware();
            }
            catch
            {
                try { SetProcessDPIAware(); } catch { }
            }

            // Last-resort exception handlers — log to %TEMP%\VolumeMixer.log so a crash
            // leaves a trail without forcing a UI dialog (this is a tray app).
            Application.ThreadException += (s, e) => LogException(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogException(e.ExceptionObject as Exception);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Application.Run(new TrayApp()); }
            finally
            {
                try { _singleInstanceMutex.ReleaseMutex(); } catch { }
                _singleInstanceMutex.Dispose();
            }
        }

        private static void LogException(Exception ex)
        {
            if (ex == null) return;
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "VolumeMixer.log");
                File.AppendAllText(path,
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + ex + Environment.NewLine);
            }
            catch { }
        }
    }
}
