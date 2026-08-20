using Microsoft.Win32;
using Windows.ApplicationModel;

namespace RouterTray;

internal enum AutoStartApplyResult
{
    Applied,
    DisabledByUser,
    DisabledByPolicy
}

internal sealed class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string PackagedStartupTaskId = "RouterTrayStartup";
    private readonly string _appName;
    private readonly string _executablePath;
    private readonly bool _isPackaged;

    public AutoStartService(string appName, string executablePath, bool isPackaged)
    {
        _appName = appName;
        _executablePath = executablePath;
        _isPackaged = isPackaged;
    }

    public async Task<AutoStartApplyResult> EnsureEnabledAsync(bool enabled)
    {
        if (_isPackaged)
        {
            return await EnsurePackagedStartupTaskAsync(enabled);
        }

        EnsureRegistryEntry(enabled);
        return AutoStartApplyResult.Applied;
    }

    private void EnsureRegistryEntry(bool enabled)
    {
        var expectedValue = CreateEntryValue(_executablePath);
        if (enabled)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true) ??
                            Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
            if (key is null)
            {
                throw new InvalidOperationException("Failed to open Run registry key.");
            }

            var currentValue = key.GetValue(_appName) as string;
            if (!string.Equals(currentValue, expectedValue, StringComparison.Ordinal))
            {
                key.SetValue(_appName, expectedValue);
            }

            return;
        }

        RemoveEntry(_appName, _executablePath);
    }

    private static async Task<AutoStartApplyResult> EnsurePackagedStartupTaskAsync(bool enabled)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
        {
            return AutoStartApplyResult.DisabledByPolicy;
        }

        var startupTask = await StartupTask.GetAsync(PackagedStartupTaskId);
        if (!enabled)
        {
            if (startupTask.State == StartupTaskState.EnabledByPolicy)
            {
                return AutoStartApplyResult.DisabledByPolicy;
            }

            if (startupTask.State == StartupTaskState.Enabled)
            {
                startupTask.Disable();
            }

            return AutoStartApplyResult.Applied;
        }

        var state = startupTask.State == StartupTaskState.Disabled
            ? await startupTask.RequestEnableAsync()
            : startupTask.State;
        return state switch
        {
            StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy =>
                AutoStartApplyResult.Applied,
            StartupTaskState.DisabledByUser => AutoStartApplyResult.DisabledByUser,
            _ => AutoStartApplyResult.DisabledByPolicy
        };
    }

    public static void RemoveEntry(string appName, string executablePath)
    {
        using var existingKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        if (existingKey is null)
        {
            return;
        }

        var existingValue = existingKey.GetValue(appName) as string;
        if (EntryTargetsExecutable(existingValue, executablePath))
        {
            existingKey.DeleteValue(appName, false);
        }
    }

    internal static bool EntryTargetsExecutable(string? entryValue, string executablePath)
    {
        return !string.IsNullOrWhiteSpace(entryValue) &&
               !string.IsNullOrWhiteSpace(executablePath) &&
               string.Equals(
                   entryValue,
                   CreateEntryValue(executablePath),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateEntryValue(string executablePath) => $"\"{executablePath}\"";
}
