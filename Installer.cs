using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

// Per-user installer. LocalAppData avoids UAC while keeping files on C:.
internal static class Installer
{
    private const string ProductName = "VolumeMixer";
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedRunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VolumeMixer";

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            bool quiet = HasArgument(args, "/quiet");
            if (HasArgument(args, "/uninstall")) Uninstall();
            else Install(quiet);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, ProductName + " Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void Install(bool quiet)
    {
        string installDirectory = GetInstallDirectory();
        string appPath = Path.Combine(installDirectory, "VolumeMixer.exe");
        string setupPath = Path.Combine(installDirectory, "VolumeMixerSetup.exe");

        Directory.CreateDirectory(installDirectory);
        StopRunningApplication();
        WriteEmbeddedApplication(appPath);
        CopyInstallerTo(setupPath);
        EnableAutoStart(appPath);
        RegisterUninstaller(setupPath);

        Process.Start(new ProcessStartInfo(appPath) { UseShellExecute = true });
        if (!quiet)
        {
            MessageBox.Show("VolumeMixer was installed and will start automatically when you sign in.", ProductName + " Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private static void Uninstall()
    {
        if (MessageBox.Show("Remove VolumeMixer and its automatic startup entry?", ProductName + " Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        StopRunningApplication();
        using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
        {
            if (runKey != null) runKey.DeleteValue(ProductName, false);
        }
        using (RegistryKey approvalKey = Registry.CurrentUser.OpenSubKey(StartupApprovedRunKeyPath, true))
        {
            if (approvalKey != null) approvalKey.DeleteValue(ProductName, false);
        }
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false);

        string installDirectory = GetInstallDirectory();
        string selfPath = Assembly.GetExecutingAssembly().Location;
        string appPath = Path.Combine(installDirectory, "VolumeMixer.exe");
        if (File.Exists(appPath)) File.Delete(appPath);

        // The installer cannot remove its own executable until it exits.
        ScheduleSelfDelete(selfPath, installDirectory);
        MessageBox.Show("VolumeMixer was removed.", ProductName + " Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void WriteEmbeddedApplication(string destination)
    {
        using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream("VolumeMixer.Payload.exe"))
        {
            if (input == null) throw new InvalidOperationException("The embedded VolumeMixer application is missing.");
            string temporaryPath = destination + ".new";
            using (FileStream output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None)) input.CopyTo(output);
            if (File.Exists(destination)) File.Replace(temporaryPath, destination, null);
            else File.Move(temporaryPath, destination);
        }
    }

    private static void CopyInstallerTo(string destination)
    {
        string source = Assembly.GetExecutingAssembly().Location;
        if (!string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) File.Copy(source, destination, true);
    }

    private static void EnableAutoStart(string appPath)
    {
        using (RegistryKey runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath))
        {
            runKey.SetValue(ProductName, "\"" + appPath + "\" --startup", RegistryValueKind.String);
        }
        // This is the state Windows Startup Apps / Task Manager uses. It can
        // survive a removed Run value, so reset it explicitly on install.
        using (RegistryKey approvalKey = Registry.CurrentUser.CreateSubKey(StartupApprovedRunKeyPath))
        {
            approvalKey.SetValue(ProductName, new byte[] { 2, 0, 0, 0 }, RegistryValueKind.Binary);
        }
    }

    private static void RegisterUninstaller(string setupPath)
    {
        using (RegistryKey uninstallKey = Registry.CurrentUser.CreateSubKey(UninstallKeyPath))
        {
            uninstallKey.SetValue("DisplayName", ProductName);
            uninstallKey.SetValue("DisplayVersion", "1.0.0");
            uninstallKey.SetValue("Publisher", "Rdect");
            uninstallKey.SetValue("UninstallString", "\"" + setupPath + "\" /uninstall");
            uninstallKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
            uninstallKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
    }

    private static void StopRunningApplication()
    {
        foreach (Process process in Process.GetProcessesByName(ProductName))
        {
            try
            {
                process.CloseMainWindow();
                if (!process.WaitForExit(1500)) process.Kill();
                process.WaitForExit(1500);
            }
            catch { }
            finally { process.Dispose(); }
        }
    }

    private static void ScheduleSelfDelete(string setupPath, string installDirectory)
    {
        string command = "/c ping 127.0.0.1 -n 3 > nul & del /f /q \"" + setupPath + "\" & rmdir \"" + installDirectory + "\"";
        Process.Start(new ProcessStartInfo("cmd.exe", command) { CreateNoWindow = true, UseShellExecute = false });
    }

    private static bool HasArgument(string[] args, string expected)
    {
        foreach (string arg in args)
        {
            if (string.Equals(arg, expected, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string GetInstallDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductName);
    }
}
