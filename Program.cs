using System.Globalization;

namespace RouterTray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;

        ApplicationConfiguration.Initialize();

        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RouterTray",
            "routertray.log");

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
        try
        {
            var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            settings = AppSettings.Load(settingsPath);
        }
        catch (Exception ex)
        {
            logger.Error("Failed to load configuration.", ex);
            MessageBox.Show(
                UiText.AppConfigLoadFailedMessage,
                UiText.AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        Application.Run(new TrayForm(settings, logger));
    }
}
