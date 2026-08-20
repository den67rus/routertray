using System.Text.Json;
using System.Text.Json.Serialization;

namespace RouterTray;

internal enum RouterAuthMode
{
    Password,
    AccessToken
}

internal enum ApplicationUpdateChannel
{
    Stable,
    Preview
}

internal sealed class RouterNetworkBinding
{
    public string NetworkId { get; set; } = string.Empty;
    public string NetworkName { get; set; } = string.Empty;

    public RouterNetworkBinding Clone()
    {
        return new RouterNetworkBinding
        {
            NetworkId = NetworkId,
            NetworkName = NetworkName
        };
    }

    internal void NormalizeAndValidate()
    {
        if (!Guid.TryParse(NetworkId, out var networkId))
        {
            throw new InvalidDataException("Router profile contains an invalid Windows network ID.");
        }

        NetworkId = networkId.ToString("D");
        NetworkName = NetworkName.Trim();
    }

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(NetworkName)
            ? NetworkId
            : $"{NetworkName} ({NetworkId})";
    }
}

internal sealed class RouterProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Default";
    public List<RouterNetworkBinding> Networks { get; set; } = new();
    public string RouterUrl { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public RouterAuthMode AuthMode { get; set; } = RouterAuthMode.Password;
    public string AccessToken { get; set; } = string.Empty;
    public string PreferredInterfaceId { get; set; } = string.Empty;

    public RouterProfile Clone()
    {
        return new RouterProfile
        {
            Id = Id,
            Name = Name,
            Networks = Networks.Select(binding => binding.Clone()).ToList(),
            RouterUrl = RouterUrl,
            Login = Login,
            Password = Password,
            AuthMode = AuthMode,
            AccessToken = AccessToken,
            PreferredInterfaceId = PreferredInterfaceId
        };
    }

    public bool IsBoundTo(string? networkId)
    {
        if (!Guid.TryParse(networkId, out var candidate))
        {
            return false;
        }

        return Networks.Any(binding =>
            Guid.TryParse(binding.NetworkId, out var boundId) && boundId == candidate);
    }

    internal void NormalizeAndValidate()
    {
        if (!Guid.TryParse(Id, out var profileId))
        {
            throw new InvalidDataException("Router profile contains an invalid profile ID.");
        }

        Id = profileId.ToString("N");
        Name = Name.Trim();
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidDataException("Router profile name is required.");
        }

        if (!Enum.IsDefined(AuthMode))
        {
            throw new InvalidDataException("Unsupported router authentication mode.");
        }

        Networks ??= new List<RouterNetworkBinding>();
        if (Networks.Any(binding => binding is null))
        {
            throw new InvalidDataException("Router profile contains an invalid network binding.");
        }

        foreach (var binding in Networks)
        {
            binding.NormalizeAndValidate();
        }

        Networks = Networks
            .GroupBy(binding => binding.NetworkId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(binding => binding.NetworkName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(binding => binding.NetworkId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        RouterUrl = RouterEndpoint.NormalizeConfiguredUrl(RouterUrl);
        Login = Login.Trim();
        AccessToken = AccessToken.Trim();
        PreferredInterfaceId = PreferredInterfaceId.Trim();
    }

    public override string ToString() => Name;
}

internal sealed class AppSettings
{
    public const string RouterUrlExample = "http://192.168.1.1/";

    private static readonly JsonSerializerOptions LoadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static readonly JsonSerializerOptions SaveOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public List<RouterProfile> Profiles { get; set; } = new() { new RouterProfile() };
    public bool AutomaticProfileSelection { get; set; } = true;
    public string SelectedProfileId { get; set; } = string.Empty;
    public bool AutoStart { get; set; }
    public bool CheckForUpdatesAutomatically { get; set; } = true;
    public ApplicationUpdateChannel UpdateChannel { get; set; } = ApplicationUpdateChannel.Stable;
    public bool ShowPolicyNotifications { get; set; } = true;

    internal bool ContainsLegacyPlaintextPassword { get; private set; }
    internal bool RequiresMigrationSave { get; private set; }

    public RouterProfile? SelectedProfile => FindProfile(SelectedProfileId) ?? Profiles.FirstOrDefault();

    public static AppSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Settings file not found.", path);
        }

        var json = File.ReadAllText(path);
        var stored = JsonSerializer.Deserialize<StoredSettings>(json, LoadOptions) ??
                     throw new InvalidDataException("Settings file is empty or invalid.");

        var hasLegacyPassword = stored.Password is not null;
        var isLegacyFormat = stored.Profiles is null;
        if (!isLegacyFormat && stored.Profiles!.Any(profile => profile is null))
        {
            throw new InvalidDataException("Settings contain an invalid router profile.");
        }

        var profiles = isLegacyFormat
            ? new List<RouterProfile> { LoadLegacyProfile(stored) }
            : stored.Profiles!.Select(profile => LoadProfile(profile!)).ToList();

        if (profiles.Count == 0)
        {
            profiles.Add(new RouterProfile());
        }

        var settings = new AppSettings
        {
            Profiles = profiles,
            AutomaticProfileSelection = stored.AutomaticProfileSelection,
            SelectedProfileId = stored.SelectedProfileId ?? string.Empty,
            AutoStart = stored.AutoStart,
            CheckForUpdatesAutomatically = stored.CheckForUpdatesAutomatically,
            UpdateChannel = stored.UpdateChannel,
            ShowPolicyNotifications = stored.ShowPolicyNotifications,
            ContainsLegacyPlaintextPassword = hasLegacyPassword,
            RequiresMigrationSave = isLegacyFormat
        };

        settings.NormalizeAndValidate();
        return settings;
    }

    public AppSettings Clone()
    {
        return new AppSettings
        {
            Profiles = Profiles.Select(profile => profile.Clone()).ToList(),
            AutomaticProfileSelection = AutomaticProfileSelection,
            SelectedProfileId = SelectedProfileId,
            AutoStart = AutoStart,
            CheckForUpdatesAutomatically = CheckForUpdatesAutomatically,
            UpdateChannel = UpdateChannel,
            ShowPolicyNotifications = ShowPolicyNotifications,
            ContainsLegacyPlaintextPassword = ContainsLegacyPlaintextPassword,
            RequiresMigrationSave = RequiresMigrationSave
        };
    }

    public RouterProfile? FindProfile(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return null;
        }

        return Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase));
    }

    public RouterProfile? FindProfileForNetwork(string? networkId)
    {
        return Profiles.FirstOrDefault(profile => profile.IsBoundTo(networkId));
    }

    public void Save(string path, bool createBackup = true)
    {
        NormalizeAndValidate();

        var stored = new StoredSettings
        {
            Profiles = Profiles.Select(StoreProfile).ToList(),
            AutomaticProfileSelection = AutomaticProfileSelection,
            SelectedProfileId = SelectedProfileId,
            AutoStart = AutoStart,
            CheckForUpdatesAutomatically = CheckForUpdatesAutomatically,
            UpdateChannel = UpdateChannel,
            ShowPolicyNotifications = ShowPolicyNotifications
        };

        var json = JsonSerializer.Serialize(stored, SaveOptions);
        AtomicFile.WriteAllText(path, json, createBackup && !ContainsLegacyPlaintextPassword);
        ContainsLegacyPlaintextPassword = false;
        RequiresMigrationSave = false;
    }

    internal void NormalizeAndValidate()
    {
        if (!Enum.IsDefined(UpdateChannel))
        {
            throw new InvalidDataException("Unsupported application update channel.");
        }

        Profiles ??= new List<RouterProfile>();
        if (Profiles.Any(profile => profile is null))
        {
            throw new InvalidDataException("Settings contain an invalid router profile.");
        }

        if (Profiles.Count == 0)
        {
            Profiles.Add(new RouterProfile());
        }

        foreach (var profile in Profiles)
        {
            profile.NormalizeAndValidate();
        }

        var duplicateProfileId = Profiles
            .GroupBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateProfileId is not null)
        {
            throw new InvalidDataException("Router profile IDs must be unique.");
        }

        var duplicateName = Profiles
            .GroupBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateName is not null)
        {
            throw new InvalidDataException("Router profile names must be unique.");
        }

        var duplicateNetwork = Profiles
            .SelectMany(profile => profile.Networks.Select(binding => new { profile.Id, binding.NetworkId }))
            .GroupBy(entry => entry.NetworkId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Select(entry => entry.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
        if (duplicateNetwork is not null)
        {
            throw new InvalidDataException("A Windows network can be bound to only one router profile.");
        }

        SelectedProfileId = SelectedProfileId.Trim();
        if (FindProfile(SelectedProfileId) is null)
        {
            SelectedProfileId = Profiles[0].Id;
        }
    }

    private static RouterProfile LoadLegacyProfile(StoredSettings stored)
    {
        var password = !string.IsNullOrWhiteSpace(stored.ProtectedPassword)
            ? SecretProtector.Unprotect(stored.ProtectedPassword)
            : stored.Password ?? string.Empty;
        var accessToken = !string.IsNullOrWhiteSpace(stored.ProtectedAccessToken)
            ? SecretProtector.UnprotectAccessToken(stored.ProtectedAccessToken)
            : string.Empty;

        return new RouterProfile
        {
            Name = "Default",
            RouterUrl = stored.RouterUrl ?? string.Empty,
            Login = stored.Login ?? string.Empty,
            Password = password,
            AuthMode = stored.AuthMode,
            AccessToken = accessToken,
            PreferredInterfaceId = stored.PreferredInterfaceId ?? string.Empty
        };
    }

    private static RouterProfile LoadProfile(StoredProfile stored)
    {
        if (stored.Networks?.Any(binding => binding is null) == true)
        {
            throw new InvalidDataException("Router profile contains an invalid network binding.");
        }

        var password = !string.IsNullOrWhiteSpace(stored.ProtectedPassword)
            ? SecretProtector.Unprotect(stored.ProtectedPassword)
            : string.Empty;
        var accessToken = !string.IsNullOrWhiteSpace(stored.ProtectedAccessToken)
            ? SecretProtector.UnprotectAccessToken(stored.ProtectedAccessToken)
            : string.Empty;

        return new RouterProfile
        {
            Id = stored.Id ?? string.Empty,
            Name = stored.Name ?? string.Empty,
            Networks = stored.Networks?.Select(binding => new RouterNetworkBinding
            {
                NetworkId = binding!.NetworkId ?? string.Empty,
                NetworkName = binding.NetworkName ?? string.Empty
            }).ToList() ?? new List<RouterNetworkBinding>(),
            RouterUrl = stored.RouterUrl ?? string.Empty,
            Login = stored.Login ?? string.Empty,
            Password = password,
            AuthMode = stored.AuthMode,
            AccessToken = accessToken,
            PreferredInterfaceId = stored.PreferredInterfaceId ?? string.Empty
        };
    }

    private static StoredProfile StoreProfile(RouterProfile profile)
    {
        return new StoredProfile
        {
            Id = profile.Id,
            Name = profile.Name,
            Networks = profile.Networks.Select(binding => new StoredNetworkBinding
            {
                NetworkId = binding.NetworkId,
                NetworkName = binding.NetworkName
            }).ToList(),
            RouterUrl = profile.RouterUrl,
            Login = profile.Login,
            ProtectedPassword = string.IsNullOrEmpty(profile.Password)
                ? string.Empty
                : SecretProtector.Protect(profile.Password),
            AuthMode = profile.AuthMode,
            ProtectedAccessToken = string.IsNullOrEmpty(profile.AccessToken)
                ? string.Empty
                : SecretProtector.ProtectAccessToken(profile.AccessToken),
            PreferredInterfaceId = profile.PreferredInterfaceId
        };
    }

    private sealed class StoredSettings
    {
        public List<StoredProfile>? Profiles { get; set; }
        public bool AutomaticProfileSelection { get; set; } = true;
        public string? SelectedProfileId { get; set; }
        public bool AutoStart { get; set; }
        public bool CheckForUpdatesAutomatically { get; set; } = true;
        [JsonConverter(typeof(JsonStringEnumConverter<ApplicationUpdateChannel>))]
        public ApplicationUpdateChannel UpdateChannel { get; set; } = ApplicationUpdateChannel.Stable;
        public bool ShowPolicyNotifications { get; set; } = true;

        // Read-only migration fields used by the single-profile formats.
        public string? RouterUrl { get; set; }
        public string? Login { get; set; }
        public string? Password { get; set; }
        public string? ProtectedPassword { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter<RouterAuthMode>))]
        public RouterAuthMode AuthMode { get; set; } = RouterAuthMode.Password;
        public string? ProtectedAccessToken { get; set; }
        public string? PreferredInterfaceId { get; set; }
    }

    private sealed class StoredProfile
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public List<StoredNetworkBinding>? Networks { get; set; }
        public string? RouterUrl { get; set; }
        public string? Login { get; set; }
        public string? ProtectedPassword { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter<RouterAuthMode>))]
        public RouterAuthMode AuthMode { get; set; } = RouterAuthMode.Password;
        public string? ProtectedAccessToken { get; set; }
        public string? PreferredInterfaceId { get; set; }
    }

    private sealed class StoredNetworkBinding
    {
        public string? NetworkId { get; set; }
        public string? NetworkName { get; set; }
    }
}
