using System.Globalization;
using System.Text.Json;

namespace RouterTray.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void NewSettings_HasOneAutomaticProfileUsingGatewayByDefault()
    {
        var settings = new AppSettings();

        var profile = Assert.Single(settings.Profiles);
        Assert.Empty(profile.RouterUrl);
        Assert.True(settings.AutomaticProfileSelection);
    }

    [Fact]
    public void SaveAndLoad_EncryptsEveryProfilePassword()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "appsettings.json");
        var settings = CreateSettings("correct horse battery staple");

        settings.Save(path);

        var json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("Password", out _));
        var storedProfile = document.RootElement.GetProperty("Profiles")[0];
        Assert.False(storedProfile.TryGetProperty("Password", out _));
        Assert.True(storedProfile.TryGetProperty("ProtectedPassword", out var protectedPassword));
        Assert.NotEqual(settings.Profiles[0].Password, protectedPassword.GetString());
        Assert.DoesNotContain(settings.Profiles[0].Password, json, StringComparison.Ordinal);

        var loaded = AppSettings.Load(path);
        var loadedProfile = Assert.Single(loaded.Profiles);
        Assert.Equal(settings.Profiles[0].RouterUrl, loadedProfile.RouterUrl);
        Assert.Equal(settings.Profiles[0].Login, loadedProfile.Login);
        Assert.Equal(settings.Profiles[0].Password, loadedProfile.Password);
        Assert.False(loaded.ContainsLegacyPlaintextPassword);
        Assert.False(loaded.RequiresMigrationSave);
    }

    [Fact]
    public void SaveAndLoad_EncryptsProfileAccessTokenAndPersistsAuthMode()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "appsettings.json");
        var settings = CreateSettings("unused-password");
        var profile = settings.Profiles[0];
        profile.AuthMode = RouterAuthMode.AccessToken;
        profile.AccessToken = "access-token-secret";

        settings.Save(path);

        var json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json);
        var storedProfile = document.RootElement.GetProperty("Profiles")[0];
        Assert.Equal("AccessToken", storedProfile.GetProperty("AuthMode").GetString());
        Assert.True(storedProfile.TryGetProperty(
            "ProtectedAccessToken",
            out var protectedAccessToken));
        Assert.NotEqual(profile.AccessToken, protectedAccessToken.GetString());
        Assert.DoesNotContain(profile.AccessToken, json, StringComparison.Ordinal);

        var loadedProfile = Assert.Single(AppSettings.Load(path).Profiles);
        Assert.Equal(RouterAuthMode.AccessToken, loadedProfile.AuthMode);
        Assert.Equal(profile.AccessToken, loadedProfile.AccessToken);
    }

    [Fact]
    public void SaveAndLoad_PersistsMultipleProfilesNetworkBindingsAndManualSelection()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "appsettings.json");
        var settings = CreateSettings("home-secret");
        var homeNetworkId = Guid.NewGuid().ToString("D");
        var workNetworkId = Guid.NewGuid().ToString("D");
        settings.Profiles[0].Networks.Add(new RouterNetworkBinding
        {
            NetworkId = homeNetworkId,
            NetworkName = "Home Wi-Fi"
        });
        var workProfile = new RouterProfile
        {
            Name = "Work",
            Login = "work-admin",
            Password = "work-secret",
            Networks = new List<RouterNetworkBinding>
            {
                new()
                {
                    NetworkId = workNetworkId,
                    NetworkName = "Office LAN"
                }
            }
        };
        settings.Profiles.Add(workProfile);
        settings.AutomaticProfileSelection = false;
        settings.SelectedProfileId = workProfile.Id;

        settings.Save(path);
        var loaded = AppSettings.Load(path);

        Assert.Equal(2, loaded.Profiles.Count);
        Assert.False(loaded.AutomaticProfileSelection);
        Assert.Equal(workProfile.Id, loaded.SelectedProfileId);
        Assert.Equal("home-secret", loaded.Profiles[0].Password);
        Assert.Equal(homeNetworkId, Assert.Single(loaded.Profiles[0].Networks).NetworkId);
        var loadedWork = Assert.Single(loaded.Profiles, profile => profile.Name == "Work");
        Assert.Equal("work-secret", loadedWork.Password);
        Assert.Equal(workNetworkId, Assert.Single(loadedWork.Networks).NetworkId);
    }

    [Fact]
    public void SaveAndLoad_ProfileNameUniquenessDoesNotDependOnCurrentCulture()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "appsettings.json");
        var originalCulture = CultureInfo.CurrentCulture;
        var settings = new AppSettings
        {
            Profiles = new List<RouterProfile>
            {
                new() { Name = "I" },
                new() { Name = "ı" }
            }
        };

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            settings.Save(path);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var loaded = AppSettings.Load(path);

            Assert.Equal(new[] { "I", "ı" }, loaded.Profiles.Select(profile => profile.Name));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void LoadAndSave_MigratesLegacySingleProfileAndPlaintextPassword()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "appsettings.json");
        File.WriteAllText(
            path,
            """
            {
              "RouterUrl": "http://192.168.1.1",
              "Login": "admin",
              "Password": "legacy-secret",
              "PreferredInterfaceId": "legacy-interface",
              "AutoStart": false,
              "ShowPolicyNotifications": true
            }
            """);

        var loaded = AppSettings.Load(path);
        var profile = Assert.Single(loaded.Profiles);
        Assert.True(loaded.ContainsLegacyPlaintextPassword);
        Assert.True(loaded.RequiresMigrationSave);
        Assert.Equal("legacy-secret", profile.Password);
        Assert.Equal("legacy-interface", profile.PreferredInterfaceId);
        Assert.Equal(RouterAuthMode.Password, profile.AuthMode);

        loaded.Save(path);

        var migratedJson = File.ReadAllText(path);
        Assert.DoesNotContain("legacy-secret", migratedJson, StringComparison.Ordinal);
        Assert.Contains("\"Profiles\"", migratedJson, StringComparison.Ordinal);
        using (var migratedDocument = JsonDocument.Parse(migratedJson))
        {
            Assert.False(migratedDocument.RootElement.TryGetProperty("Password", out _));
            Assert.False(migratedDocument.RootElement.GetProperty("Profiles")[0]
                .TryGetProperty("Password", out _));
        }
        Assert.False(File.Exists(path + ".bak"));
        Assert.Equal("legacy-secret", AppSettings.Load(path).Profiles[0].Password);
    }

    [Fact]
    public void SettingsStore_FlagsProtectedSingleProfileFormatForMigration()
    {
        using var temp = new TemporaryDirectory();
        var primaryPath = Path.Combine(temp.Path, "appsettings.json");
        var protectedPassword = SecretProtector.Protect("protected-legacy-secret");
        File.WriteAllText(
            primaryPath,
            JsonSerializer.Serialize(new
            {
                RouterUrl = "http://192.168.1.1/",
                Login = "admin",
                ProtectedPassword = protectedPassword
            }));

        var result = SettingsStore.Load(primaryPath, Path.Combine(temp.Path, "missing.json"));

        Assert.True(result.NeedsSave);
        Assert.False(result.Recovered);
        Assert.Equal("protected-legacy-secret", result.Settings.Profiles[0].Password);
    }

    [Fact]
    public void Save_CreatesUsableBackupBeforeReplacingExistingProfiles()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "appsettings.json");
        var settings = CreateSettings("first-secret");
        settings.Save(path);

        settings.Profiles[0].Password = "second-secret";
        settings.Save(path);

        Assert.Equal("second-secret", AppSettings.Load(path).Profiles[0].Password);
        Assert.Equal("first-secret", AppSettings.Load(path + ".bak").Profiles[0].Password);
    }

    [Fact]
    public void SettingsStore_RecoversFromCorruptPrimaryFile()
    {
        using var temp = new TemporaryDirectory();
        var primaryPath = Path.Combine(temp.Path, "user", "appsettings.json");
        var fallbackPath = Path.Combine(temp.Path, "packaged.json");
        Directory.CreateDirectory(Path.GetDirectoryName(primaryPath)!);
        File.WriteAllText(primaryPath, "{ not valid json");
        CreateSettings("fallback-secret").Save(fallbackPath);

        var result = SettingsStore.Load(primaryPath, fallbackPath);

        Assert.True(result.Recovered);
        Assert.True(result.NeedsSave);
        Assert.Equal("fallback-secret", result.Settings.Profiles[0].Password);
        Assert.Single(Directory.GetFiles(
            Path.GetDirectoryName(primaryPath)!,
            "appsettings.json.corrupt-*"));
    }

    [Fact]
    public void SettingsStore_RecoversWhenOnlyAtomicBackupRemains()
    {
        using var temp = new TemporaryDirectory();
        var primaryPath = Path.Combine(temp.Path, "user", "appsettings.json");
        var fallbackPath = Path.Combine(temp.Path, "packaged.json");
        CreateSettings("backup-secret").Save(primaryPath + ".bak");
        CreateSettings("fallback-secret").Save(fallbackPath);

        var result = SettingsStore.Load(primaryPath, fallbackPath);

        Assert.True(result.Recovered);
        Assert.True(result.NeedsSave);
        Assert.Equal("backup-secret", result.Settings.Profiles[0].Password);
    }

    [Fact]
    public void Clone_DeepCopiesProfilesAndNetworkBindings()
    {
        var settings = CreateSettings("secret");
        var networkId = Guid.NewGuid().ToString("D");
        settings.Profiles[0].Networks.Add(new RouterNetworkBinding
        {
            NetworkId = networkId,
            NetworkName = "Home"
        });

        var clone = settings.Clone();
        clone.Profiles[0].Name = "Changed";
        clone.Profiles[0].Networks[0].NetworkName = "Changed network";

        Assert.Equal("Home", settings.Profiles[0].Name);
        Assert.Equal("Home", settings.Profiles[0].Networks[0].NetworkName);
    }

    [Fact]
    public void FindProfileForNetwork_SupportsMultipleMeshNetworkIds()
    {
        var settings = CreateSettings("secret");
        var firstNetworkId = Guid.NewGuid().ToString("D");
        var secondNetworkId = Guid.NewGuid().ToString("D");
        settings.Profiles[0].Networks.Add(new RouterNetworkBinding { NetworkId = firstNetworkId });
        settings.Profiles[0].Networks.Add(new RouterNetworkBinding { NetworkId = secondNetworkId });
        settings.NormalizeAndValidate();

        Assert.Same(settings.Profiles[0], settings.FindProfileForNetwork(firstNetworkId));
        Assert.Same(settings.Profiles[0], settings.FindProfileForNetwork(secondNetworkId.ToUpperInvariant()));
        Assert.Null(settings.FindProfileForNetwork(Guid.NewGuid().ToString("D")));
    }

    [Fact]
    public void NormalizeAndValidate_RejectsNetworkBoundToTwoProfiles()
    {
        var settings = CreateSettings("secret");
        var networkId = Guid.NewGuid().ToString("D");
        settings.Profiles[0].Networks.Add(new RouterNetworkBinding { NetworkId = networkId });
        settings.Profiles.Add(new RouterProfile
        {
            Name = "Work",
            Login = "admin",
            Password = "work-secret",
            Networks = new List<RouterNetworkBinding>
            {
                new() { NetworkId = networkId }
            }
        });

        Assert.Throws<InvalidDataException>(settings.NormalizeAndValidate);
    }

    [Fact]
    public void UnknownPolicy_IsNotReportedAsDefault()
    {
        Assert.False(TrayForm.IsDefaultPolicy(null));
        Assert.False(TrayForm.IsDefaultPolicy(string.Empty));
        Assert.True(TrayForm.IsDefaultPolicy(" DEFAULT "));
    }

    private static AppSettings CreateSettings(string password)
    {
        var profile = new RouterProfile
        {
            Name = "Home",
            RouterUrl = "https://router.example:8443/api",
            Login = "admin",
            Password = password,
            PreferredInterfaceId = "interface-id"
        };

        return new AppSettings
        {
            Profiles = new List<RouterProfile> { profile },
            SelectedProfileId = profile.Id,
            AutoStart = true,
            ShowPolicyNotifications = false
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "RouterTray.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
