using System.Globalization;
using Velopack;

namespace RouterTray;

internal static class Program
{
    private const string AppUserModelId = "RouterTray.App";
    private const string SingleInstanceName = @"Local\RouterTray.6FEC1E8E-0DA0-4E5B-9A4B-0A3F5CF6E6A1";

    [STAThread]
    private static void Main()
    {
        VelopackApp.Build()
            .SetAutoApplyOnStartup(true)
            .SetAppUserModelId(AppUserModelId)
            .OnBeforeUninstallFastCallback(_ =>
                AutoStartService.RemoveEntry("RouterTray", Application.ExecutablePath))
            .Run();

        using var singleInstance = SingleInstanceGuard.Acquire(SingleInstanceName);
        if (!singleInstance.IsPrimaryInstance)
        {
            return;
        }

        RunApplication();
    }

    private static void RunApplication()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;

        ApplicationConfiguration.Initialize();

        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RouterTray");
        var logPath = Path.Combine(appDataDirectory, "routertray.log");
        var settingsPath = Path.Combine(appDataDirectory, "appsettings.json");
        var packagedSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        using var logger = new FileLogger(logPath);

        Application.ThreadException += (_, e) => logger.Error("UI thread exception.", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                logger.Error("Unhandled exception.", ex);
            }
        };

        AppSettings settings;
        var recovered = false;
        try
        {
            var loadResult = SettingsStore.Load(settingsPath, packagedSettingsPath);
            settings = loadResult.Settings;
            recovered = loadResult.Recovered;

            var containedLegacyPassword = settings.ContainsLegacyPlaintextPassword;
            if (loadResult.NeedsSave)
            {
                settings.Save(settingsPath);
            }

            if (containedLegacyPassword &&
                loadResult.SourcePath is not null &&
                !PathsEqual(loadResult.SourcePath, settingsPath))
            {
                try
                {
                    settings.Save(loadResult.SourcePath, createBackup: false);
                }
                catch (Exception ex)
                {
                    logger.Error("Failed to remove a legacy plaintext password.", ex);
                    recovered = true;
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error("Failed to load or migrate configuration; using safe defaults.", ex);
            settings = new AppSettings();
            recovered = true;
        }

        if (recovered)
        {
            MessageBox.Show(
                UiText.AppConfigRecoveredMessage,
                UiText.AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        try
        {
            using var trayForm = new TrayForm(settings, settingsPath, logger);
            Application.Run(trayForm);
        }
        catch (Exception ex)
        {
            logger.Error("Failed to start application.", ex);
            MessageBox.Show(
                UiText.UnexpectedErrorMessage,
                UiText.AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }
}
