using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace FlowBoard.Services;

/// <summary>
/// Registers .flowboard files for the current user (no admin rights needed): they get the FlowBoard
/// project icon in Explorer and open in FlowBoard on double-click.
/// </summary>
public static class FileAssociation
{
    private const string ProgId = "FlowBoard.Project";

    public static void Register()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null || !OperatingSystem.IsWindows()) return;
            var icon = WriteDocumentIcon() ?? exe;
            var command = $"\"{exe}\" \"%1\"";

            // Only touch the registry (and refresh Explorer) when something changed, e.g. the app moved.
            using (var existing = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command"))
            using (var existingIcon = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\DefaultIcon"))
            {
                if (existing?.GetValue(null) as string == command && existingIcon?.GetValue(null) as string == $"\"{icon}\",0") return;
            }

            using (var ext = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{AppPaths.ProjectExtension}"))
            {
                ext.SetValue(null, ProgId);
                ext.SetValue("Content Type", "application/x-flowboard");
                using var owp = ext.CreateSubKey("OpenWithProgids");
                owp.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
            }

            using (var prog = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
            {
                prog.SetValue(null, "FlowBoard project");
                prog.SetValue("FriendlyTypeName", "FlowBoard project");
                using (var di = prog.CreateSubKey("DefaultIcon")) di.SetValue(null, $"\"{icon}\",0");
                using (var cmd = prog.CreateSubKey(@"shell\open\command")) cmd.SetValue(null, command);
            }

            SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"File association failed: {ex}");
        }
    }

    /// <summary>Copies the project-file icon out of the app so Explorer can show it.</summary>
    private static string? WriteDocumentIcon()
    {
        try
        {
            var target = Path.Combine(AppPaths.AppDir, "flowboard-project-v2.ico");
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/project.ico"));
            if (info == null) return null;
            using var src = info.Stream;
            using var ms = new MemoryStream();
            src.CopyTo(ms);
            var bytes = ms.ToArray();
            if (!File.Exists(target) || !File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes)) File.WriteAllBytes(target, bytes);
            return target;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
}
