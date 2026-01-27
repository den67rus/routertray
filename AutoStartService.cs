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
        var expectedValue = $"\"{_executablePath}\"";
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

        using var existingKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        if (existingKey is null)
        {
            return;
        }

        var existingValue = existingKey.GetValue(_appName) as string;
        if (!string.IsNullOrWhiteSpace(existingValue))
        {
            existingKey.DeleteValue(_appName, false);
        }
    }
}
