using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using VolumeMixer;

internal static class RegressionTests
{
    private static int failures;
    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(IntPtr value);
    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Run("Production binary has no global mouse-hook capability", delegate {
            Check(typeof(Win32).GetMethod("SetWindowsHookEx", BindingFlags.Static | BindingFlags.Public) == null,
                "Low-level global mouse hook remains imported");
            Check(typeof(TrayApp).GetField("_hookId", BindingFlags.Instance | BindingFlags.NonPublic) == null,
                "Tray application still owns a global mouse hook");
        });
        Run("Native property clearing stays inside the managed buffer", delegate {
            int size = Marshal.SizeOf(typeof(PROPVARIANT));
            IntPtr buffer = Marshal.AllocHGlobal(size + 16);
            try {
                for (int i = 0; i < size + 16; i++) Marshal.WriteByte(buffer, i, i < size ? (byte)0 : (byte)0x7B);
                PropVariantClear(buffer);
                for (int i = size; i < size + 16; i++) Check(Marshal.ReadByte(buffer, i) == 0x7B, "Native PROPVARIANT overwrote memory after the declared structure");
            }
            finally { Marshal.FreeHGlobal(buffer); }
        });
        Run("Late preload cannot replace a newer audio refresh", delegate {
            using (var form = new MixerForm()) {
                var handle = form.Handle;
                form.PreloadAsync();
                Thread.Sleep(500); // Hold UI delivery while the worker finishes.
                form.Show();
                var current = new AudioMaster { Endpoint = new Endpoint() };
                Call(form, "ApplyAudioReload", current, new List<AudioSession>());
                Pump(600);
                var master = typeof(MixerForm).GetField("_master", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                Check(ReferenceEquals(master, current), "Old preload replaced newer state behind the rendered controls");
            }
        });
        Run("Preloading never shows or activates a window", delegate {
            using (var form = new MixerForm()) {
                var handle = form.Handle;
                int shows = 0;
                form.VisibleChanged += delegate { if (form.Visible) shows++; };
                form.PreloadAsync();
                var sw = Stopwatch.StartNew();
                while (!(bool)typeof(MixerForm).GetField("_preloaded", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form) && sw.ElapsedMilliseconds < 5000) Pump(20);
                Check(sw.ElapsedMilliseconds < 5000, "Preload did not finish");
                Check(shows == 0, "Startup briefly showed a focus-stealing window");
            }
        });
        Run("Returning focus cancels pending dismissal", delegate {
            using (var form = OpenForm()) {
                Call(form, "OnDeactivate", EventArgs.Empty);
                Call(form, "OnActivated", EventArgs.Empty);
                Check(!DismissalPending(form), "Popup still has a pending close after regaining focus");
            }
        });
        Run("Reopening settings renders the mixer", delegate {
            using (var form = OpenForm()) {
                var field = typeof(MixerForm).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic);
                field.SetValue(form, Enum.Parse(field.FieldType, "Settings"));
                Call(form, "Render");
                form.ShowNearTray();
                Check(Descendants(form).Any(c => c is Label && c.Text == Strings.VolumeMixer), "Settings controls remained in mixer view");
            }
        });
        Run("Clicking focused settings and back buttons keeps the popup open", delegate {
            using (var form = OpenForm()) {
                Call(form, "Render");
                for (int i = 0; i < 4; i++) {
                    var button = Descendants(form).OfType<IconHeaderButton>().Single();
                    button.Focus();
                    Call(button, "OnClick", EventArgs.Empty);
                    Check(form.Visible && !DismissalPending(form), "Navigation scheduled popup dismissal");
                    string expectedTitle = i % 2 == 0 ? Strings.SoundSettings : Strings.VolumeMixer;
                    Check(Descendants(form).Any(c => c is Label && c.Text == expectedTitle), "Navigation did not render the requested page");
                    Pump(350);
                    Check(form.Visible, "Popup closed during page navigation");
                }
            }
        });
        Run("Master appearing without app changes creates its row", delegate {
            using (var form = OpenForm()) {
                Call(form, "Render");
                Call(form, "ApplyAudioReload", new AudioMaster { Endpoint = new Endpoint() }, new List<AudioSession>());
                Check(Descendants(form).OfType<AppRow>().Count() == 1, "Master row remained absent");
            }
        });
        Run("Disconnected cached endpoint does not abort rendering", delegate {
            using (var form = OpenForm()) {
                Set(form, "_master", new AudioMaster { Endpoint = new Endpoint { Disconnected = true } });
                Call(form, "Render");
                Check(Descendants(form).Any(c => c is HeaderPanel), "Header was not rendered");
            }
        });
        Run("A recovered endpoint restores the omitted master row", delegate {
            using (var form = OpenForm()) {
                Set(form, "_master", new AudioMaster { Endpoint = new Endpoint { Disconnected = true } });
                Call(form, "Render");
                Call(form, "ApplyAudioReload", new AudioMaster { Endpoint = new Endpoint() }, new List<AudioSession>());
                Check(Descendants(form).OfType<AppRow>().Count() == 1, "Recovered master stayed hidden");
            }
        });
        Run("Rapid open-close reversals settle and can reopen", delegate {
            using (var form = OpenForm()) {
                for (int i = 0; i < 12; i++) form.ToggleNearTray();
                form.ShowNearTray();
                Pump(400);
                Check(form.Visible, "Rapid toggles hid an explicitly opened popup");
                Call(form, "BeginAnimatedClose");
                var closing = Stopwatch.StartNew();
                while (form.Visible && closing.ElapsedMilliseconds < 3000) Pump(20);
                Check(!form.Visible, "Close animation did not finish");
                form.ShowNearTray();
                Pump(400);
                Check(form.Visible, "Popup could not reopen after rapid toggles");
            }
        });
        Run("Visible mixer reconciles endpoint and expired sessions without notifications", delegate {
            using (var form = OpenForm()) {
                var stale = new AudioMaster { Endpoint = new Endpoint(), DeviceId = "disconnected-test-device" };
                Set(form, "_master", stale);
                Set(form, "_sessions", new List<AudioSession> { new AudioSession { ProcessId = uint.MaxValue } });
                Call(form, "Render");
                for (int i = 0; i < 8; i++) Call(form, "SyncFromSystem");
                Pump(1000);
                var sessions = (List<AudioSession>)typeof(MixerForm).GetField("_sessions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                Check(!sessions.Any(s => s.ProcessId == uint.MaxValue), "Expired session remained without a creation notification");
                Check(!ReferenceEquals(typeof(MixerForm).GetField("_master", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form), stale), "Mixer still targeted the previous output device");
            }
        });
        Run("Losing mouse capture stops slider drag", delegate {
            using (var form = OpenForm()) {
                var slider = new FlatSlider { Width = 200 };
                form.Controls.Add(slider);
                Call(slider, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 50, 10, 0));
                slider.Capture = false;
                int before = slider.Value;
                Call(slider, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 190, 10, 0));
                Check(slider.Value == before, "Slider kept changing volume without a held button");
            }
        });
        Run("Refresh completion during a drag preserves the captured row", delegate {
            using (var form = OpenForm()) {
                Set(form, "_master", new AudioMaster { Endpoint = new Endpoint() });
                Call(form, "Render");
                var row = Descendants(form).OfType<AppRow>().Single();
                var slider = Descendants(row).OfType<FlatSlider>().Single();
                Call(slider, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 90, 10, 0));
                Call(form, "ApplyAudioReload", new AudioMaster { Endpoint = new Endpoint() }, new List<AudioSession> { new AudioSession { ProcessId = uint.MaxValue } });
                Check(!row.IsDisposed && slider.Capture, "Full refresh disposed a slider while it was being dragged");
                Call(form, "ApplySessionReload", new List<AudioSession> { new AudioSession { ProcessId = uint.MaxValue } });
                Check(!row.IsDisposed && slider.Capture, "Session refresh disposed a slider while it was being dragged");
            }
        });
        Run("Live sync does not overwrite a dragged slider", delegate {
            using (var form = OpenForm()) {
                var row = new AppRow("Test", new IconTile("T", new Font("Segoe UI", 12), Color.Blue), Color.Blue, null, .5f, false, 0, false, null, null, null);
                form.Controls.Add(row);
                var slider = Descendants(row).OfType<FlatSlider>().Single();
                Call(slider, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 90, 10, 0));
                int before = slider.Value;
                row.UpdateExternalState(.01f, false);
                Check(slider.Value == before, "Polling moved the thumb during a drag");
            }
        });
        Console.WriteLine("Failures: " + failures);
        return failures == 0 ? 0 : 1;
    }

    private static MixerForm OpenForm()
    {
        var form = new MixerForm { Location = new Point(-10000, -10000) };
        Set(form, "_preloaded", true);
        form.Show();
        return form;
    }
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls) {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static void Run(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.GetBaseException().Message); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static bool DismissalPending(MixerForm form)
    {
        var timer = (System.Windows.Forms.Timer)typeof(MixerForm).GetField("_delayedCloseTimer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        return timer != null && timer.Enabled;
    }
    private static void Set(object target, string name, object value)
    {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
    private static void Call(object target, string name, params object[] args)
    {
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    }
    private static void Pump(int milliseconds)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(5); }
    }

    // Only the hardware COM boundary is replaced; real forms, timers, controls and rendering run.
    private sealed class Endpoint : CoreAudio.IAudioEndpointVolume
    {
        public bool Disconnected;
        public int NotImpl_RegisterControlChangeNotify() { return 0; }
        public int NotImpl_UnregisterControlChangeNotify() { return 0; }
        public int GetChannelCount(out int count) { count = 2; return 0; }
        public int SetMasterVolumeLevel(float level, ref Guid context) { return 0; }
        public int SetMasterVolumeLevelScalar(float level, ref Guid context) { return 0; }
        public int GetMasterVolumeLevel(out float level) { level = 0; return 0; }
        public int GetMasterVolumeLevelScalar(out float level) { level = .5f; return Disconnected ? unchecked((int)0x88890004) : 0; }
        public int NotImpl_SetChannelVolumeLevel() { return 0; }
        public int NotImpl_SetChannelVolumeLevelScalar() { return 0; }
        public int NotImpl_GetChannelVolumeLevel() { return 0; }
        public int NotImpl_GetChannelVolumeLevelScalar() { return 0; }
        public int SetMute(bool mute, ref Guid context) { return 0; }
        public int GetMute(out bool mute) { mute = false; return Disconnected ? unchecked((int)0x88890004) : 0; }
        public int GetVolumeStepInfo(out uint step, out uint count) { step = 50; count = 101; return 0; }
        public int VolumeStepUp(ref Guid context) { return 0; }
        public int VolumeStepDown(ref Guid context) { return 0; }
        public int QueryHardwareSupport(out uint mask) { mask = 0; return 0; }
        public int GetVolumeRange(out float min, out float max, out float increment) { min = -96; max = 0; increment = 1; return 0; }
    }
}
