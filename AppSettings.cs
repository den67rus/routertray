using System.Text.Json;

namespace RouterTray;

internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions LoadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions SaveOptions = new()
    {
        WriteIndented = true
    };

    public string RouterUrl { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool AutoStart { get; set; }
    public bool ShowPolicyNotifications { get; set; } = true;

    public static AppSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("appsettings.json not found.", path);
        }

        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, LoadOptions);
        if (settings is null)
        {
            throw new InvalidOperationException("Invalid appsettings.json.");
        }

        settings.Validate();
        return settings;
    }

    private void Validate()
    {
        if (!string.IsNullOrWhiteSpace(RouterUrl) &&
            !Uri.TryCreate(RouterUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("RouterUrl must be an absolute URI.");
        }
    }

    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, SaveOptions);
        File.WriteAllText(path, json);
    }
}
