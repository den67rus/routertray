using Microsoft.Win32;

namespace RouterTray;

internal sealed class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _appName;
    private readonly string _executablePath;

    public AutoStartService(string appName, string executablePath)
    {
        _appName = appName;
        _executablePath = executablePath;
    }

    public void EnsureEnabled(bool enabled)
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
