using Microsoft.Win32;

namespace FlowBoard.Services;

/// <summary>Registers FlowBoard to start with Windows (per user, no admin rights needed).</summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FlowBoard";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (exe != null) key.SetValue(ValueName, $"\"{exe}\" --minimized");
            }
            else if (key.GetValue(ValueName) != null)
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch
        {
            // Registry access can be blocked by policy; the setting simply has no effect then.
        }
    }
}
