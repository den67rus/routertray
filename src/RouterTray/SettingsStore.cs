using System.Globalization;

namespace RouterTray;

internal static class SettingsStore
{
    public static SettingsLoadResult Load(string settingsPath, string fallbackPath)
    {
        var settingsFileExists = File.Exists(settingsPath);
        var backupPath = settingsPath + ".bak";
        var backupExists = File.Exists(backupPath);
        var isFirstRun = !settingsFileExists && !backupExists;

        if (settingsFileExists)
        {
            try
            {
                var settings = AppSettings.Load(settingsPath);
                return new SettingsLoadResult(
                    settings,
                    settingsPath,
                    settings.ContainsLegacyPlaintextPassword || settings.RequiresMigrationSave,
                    Recovered: false,
                    IsFirstRun: false);
            }
            catch (Exception primaryException) when (IsRecoverable(primaryException))
            {
                PreserveCorruptFile(settingsPath);

                if (File.Exists(backupPath))
                {
                    try
                    {
                        return new SettingsLoadResult(
                            AppSettings.Load(backupPath),
                            backupPath,
                            NeedsSave: true,
                            Recovered: true,
                            IsFirstRun: false);
                    }
                    catch (Exception backupException) when (IsRecoverable(backupException))
                    {
                        // Continue with the packaged defaults below.
                    }
                }

                return LoadFallback(fallbackPath, recovered: true, isFirstRun: false);
            }
        }

        if (backupExists)
        {
            try
            {
                return new SettingsLoadResult(
                    AppSettings.Load(backupPath),
                    backupPath,
                    NeedsSave: true,
                    Recovered: true,
                    IsFirstRun: false);
            }
            catch (Exception backupException) when (IsRecoverable(backupException))
            {
                // Continue with the packaged defaults below.
            }
        }

        return LoadFallback(
            fallbackPath,
            recovered: backupExists,
            isFirstRun: isFirstRun);
    }

    private static SettingsLoadResult LoadFallback(
        string fallbackPath,
        bool recovered,
        bool isFirstRun)
    {
        if (File.Exists(fallbackPath))
        {
            try
            {
                return new SettingsLoadResult(
                    AppSettings.Load(fallbackPath),
                    fallbackPath,
                    NeedsSave: true,
                    Recovered: recovered,
                    IsFirstRun: isFirstRun);
            }
            catch (Exception fallbackException) when (IsRecoverable(fallbackException))
            {
                // A damaged packaged template must not prevent access to the settings UI.
            }
        }

        return new SettingsLoadResult(
            new AppSettings(),
            null,
            NeedsSave: true,
            Recovered: true,
            IsFirstRun: isFirstRun);
    }

    private static void PreserveCorruptFile(string path)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var destination = path + $".corrupt-{timestamp}";
        var suffix = 0;
        while (File.Exists(destination))
        {
            suffix++;
            destination = path + $".corrupt-{timestamp}-{suffix}";
        }

        File.Move(path, destination);
    }

    private static bool IsRecoverable(Exception exception)
    {
        return exception is System.Text.Json.JsonException or
               InvalidDataException or InvalidOperationException;
    }
}

internal sealed record SettingsLoadResult(
    AppSettings Settings,
    string? SourcePath,
    bool NeedsSave,
    bool Recovered,
    bool IsFirstRun);
