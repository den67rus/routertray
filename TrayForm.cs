using System.Net.Http;
using System.Net.NetworkInformation;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RouterTray;

internal sealed class TrayForm : Form
{
    private const string DefaultPolicyId = "default";
    private const int NetworkProfileRefreshAttempts = 6;
    private const int NetworkProfileStableSamples = 2;
    private const float InactiveIconGrayscaleWeight = 0.55f;
    private const float InactiveIconBrightness = 1f - InactiveIconGrayscaleWeight;
    private static readonly TimeSpan NetworkChangeDebounce = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan NetworkProfileRefreshDelay = TimeSpan.FromMilliseconds(500);

    private AppSettings _settings;
    private readonly string _settingsPath;
    private readonly FileLogger _logger;
    private readonly NetworkInterfaceService _interfaceService;
    private readonly AutoStartService _autoStartService;
    private readonly AppUpdateService _updateService;
    private readonly ContextMenuStrip _menu;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _profilesMenu;
    private readonly ToolStripMenuItem _interfacesMenu;
    private readonly ToolStripMenuItem _policiesMenu;
    private readonly NativePolicyMenu _nativePolicyMenu;
    private readonly SemaphoreSlim _interfaceLoadLock = new(1, 1);
    private readonly SemaphoreSlim _policyLoadLock = new(1, 1);
    private readonly SemaphoreSlim _routerOperationLock = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private CancellationTokenSource _connectionCts = new();
    private readonly Icon _icon;
    private readonly Icon _inactiveIcon;
    private readonly bool _ownsIcon;
    private readonly bool _usesPackageManagedUpdates;

    private KeeneticClient? _client;
    private Uri? _clientEndpoint;
    private string? _clientInterfaceId;
    private string? _clientGateway;
    private string? _clientProfileId;
    private string? _activeProfileId;
    private readonly ProfilePolicyCache _policyCache = new();
    private int _policyCacheGeneration;
    private int _policyRefreshRunningGeneration = -1;
    private bool _policyRefreshPending;
    private bool _policyRefreshNotifyOnFailure;
    private long _networkChangeVersion;
    private AboutForm? _aboutForm;
    private SettingsForm? _settingsForm;
    private Action? _scheduledUpdateApply;
    private bool _isShuttingDown;
    private bool _shutdownComplete;
    private bool _resourcesDisposed;

    public TrayForm(AppSettings settings, string settingsPath, FileLogger logger)
    {
        _settings = settings;
        _settingsPath = settingsPath;
        _logger = logger;
        _usesPackageManagedUpdates = AppInstallation.UsesPackageManagedUpdates;
        _interfaceService = new NetworkInterfaceService();
        _autoStartService = new AutoStartService(
            "RouterTray",
            Application.ExecutablePath,
            _usesPackageManagedUpdates);
        _updateService = new AppUpdateService(
            logger,
            ScheduleUpdateApply,
            settings.CheckForUpdatesAutomatically,
            settings.UpdateChannel,
            packageManaged: _usesPackageManagedUpdates);

        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;

        _menu = new ContextMenuStrip();

        _profilesMenu = new ToolStripMenuItem(UiText.MenuProfiles);
        _profilesMenu.DropDownOpening += OnProfilesDropDownOpening;
        _profilesMenu.DropDownItems.Add(new ToolStripMenuItem(UiText.Loading) { Enabled = false });

        _interfacesMenu = new ToolStripMenuItem(UiText.MenuInterfaces);
        _interfacesMenu.DropDownOpening += OnInterfacesDropDownOpening;
        _interfacesMenu.DropDownItems.Add(new ToolStripMenuItem(UiText.Loading) { Enabled = false });

        _policiesMenu = new ToolStripMenuItem(UiText.MenuPolicies);
        _policiesMenu.DropDownOpening += OnPoliciesDropDownOpening;
        _nativePolicyMenu = new NativePolicyMenu();
        _nativePolicyMenu.Update(_policyCache.Current);
        PopulatePoliciesMenu(_policyCache.Current);

        var settingsMenu = new ToolStripMenuItem(UiText.MenuSettings);
        settingsMenu.Click += OnSettingsMenuClick;

        var aboutMenu = new ToolStripMenuItem(UiText.MenuAbout);
        aboutMenu.Click += (_, _) => ShowAbout();

        var exitMenu = new ToolStripMenuItem(UiText.MenuExit);
        exitMenu.Click += (_, _) => Close();

        _menu.Items.Add(_profilesMenu);
        _menu.Items.Add(_interfacesMenu);
        _menu.Items.Add(_policiesMenu);
        _menu.Items.Add(settingsMenu);
        _menu.Items.Add(aboutMenu);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exitMenu);

        var extractedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (extractedIcon is null)
        {
            _icon = SystemIcons.Application;
            _ownsIcon = false;
        }
        else
        {
            _icon = extractedIcon;
            _ownsIcon = true;
        }

        _inactiveIcon = CreateInactiveIcon(_icon);
        if (!_settings.AutomaticProfileSelection)
        {
            _activeProfileId = _settings.SelectedProfile?.Id;
        }

        _policyCache.Activate(_activeProfileId);

        _notifyIcon = new NotifyIcon
        {
            Icon = _activeProfileId is null ? _inactiveIcon : _icon,
            Visible = true,
            Text = UiText.AppName,
            ContextMenuStrip = _menu
        };
        _notifyIcon.MouseClick += OnNotifyIconMouseClick;

        FormClosing += OnFormClosing;
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Hide();
        await ApplyAutoStartAsync(_settings.AutoStart, showNotification: false);
        _updateService.Start();

        try
        {
            var resolution = await ResolveStableProfileAsync(
                minimumAttempts: NetworkProfileStableSamples,
                isCurrent: null,
                ct: _lifetimeCts.Token);
            if (resolution is not null)
            {
                SetActiveProfile(resolution.Profile, showNotification: false);
                StartPolicyRefresh(showFailureNotification: false);
            }
        }
        catch (OperationCanceledException) when (_isShuttingDown)
        {
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to select the initial router profile.", ex);
        }
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_shutdownComplete)
        {
            return;
        }

        _isShuttingDown = true;
        _notifyIcon.Visible = false;
        _updateService.Dispose();
        _lifetimeCts.Cancel();
        _connectionCts.Cancel();

        if (e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
        {
            _shutdownComplete = true;
            return;
        }

        e.Cancel = true;
        await ShutdownRouterClientAsync();
        _shutdownComplete = true;
        Close();
    }

    private async Task ShutdownRouterClientAsync()
    {
        await _routerOperationLock.WaitAsync();
        try
        {
            DisposeRouterClient();
        }
        finally
        {
            _routerOperationLock.Release();
        }

        // Let menu refresh continuations leave their finally blocks before the
        // semaphores and controls are disposed by the second Close call.
        await _policyLoadLock.WaitAsync();
        _policyLoadLock.Release();
        await _interfaceLoadLock.WaitAsync();
        _interfaceLoadLock.Release();
    }

    private async void OnProfilesDropDownOpening(object? sender, EventArgs e)
    {
        SetProfilesLoading();
        try
        {
            var changed = await RefreshActiveProfileAsync(
                showNotification: false,
                _lifetimeCts.Token);
            if (changed)
            {
                await ResetRouterClientAsync();
                StartPolicyRefresh(showFailureNotification: false);
            }

            PopulateProfilesMenu();
        }
        catch (OperationCanceledException) when (_isShuttingDown)
        {
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to refresh router profiles.", ex);
            PopulateProfilesMenu();
        }
    }

    private void SetProfilesLoading()
    {
        _profilesMenu.DropDownItems.Clear();
        _profilesMenu.DropDownItems.Add(new ToolStripMenuItem(UiText.Loading) { Enabled = false });
    }

    private void PopulateProfilesMenu()
    {
        _profilesMenu.DropDownItems.Clear();

        var activeProfile = _settings.FindProfile(_activeProfileId);
        _profilesMenu.DropDownItems.Add(new ToolStripMenuItem(
            activeProfile is null ? UiText.ProfilesNoneActive : UiText.ProfilesActive(activeProfile.Name))
        {
            Enabled = false
        });
        _profilesMenu.DropDownItems.Add(new ToolStripSeparator());

        var automaticItem = new ToolStripMenuItem(UiText.ProfilesAutomatic)
        {
            Checked = _settings.AutomaticProfileSelection
        };
        automaticItem.Click += OnAutomaticProfileClick;
        _profilesMenu.DropDownItems.Add(automaticItem);
        _profilesMenu.DropDownItems.Add(new ToolStripSeparator());

        foreach (var profile in _settings.Profiles)
        {
            var item = new ToolStripMenuItem(profile.Name)
            {
                Checked = string.Equals(
                    profile.Id,
                    _activeProfileId,
                    StringComparison.OrdinalIgnoreCase),
                Tag = profile.Id
            };
            item.Click += OnProfileItemClick;
            _profilesMenu.DropDownItems.Add(item);
        }
    }

    private async void OnAutomaticProfileClick(object? sender, EventArgs e)
    {
        if (_settings.AutomaticProfileSelection)
        {
            return;
        }

        var candidate = _settings.Clone();
        candidate.AutomaticProfileSelection = true;
        await ApplyProfileSelectionAsync(candidate);
    }

    private async void OnProfileItemClick(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem { Tag: string profileId } ||
            _settings.FindProfile(profileId) is null)
        {
            return;
        }

        if (!_settings.AutomaticProfileSelection &&
            string.Equals(_settings.SelectedProfileId, profileId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var candidate = _settings.Clone();
        candidate.AutomaticProfileSelection = false;
        candidate.SelectedProfileId = profileId;
        await ApplyProfileSelectionAsync(candidate);
    }

    private async Task ApplyProfileSelectionAsync(AppSettings candidate)
    {
        try
        {
            candidate.Save(_settingsPath);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to save router profile selection.", ex);
            ShowBalloon(UiText.MenuProfiles, UiText.SettingsSaveFailedMessage, ToolTipIcon.Error);
            return;
        }

        _settings = candidate;
        MarkPolicyCacheStale();
        try
        {
            await ResetRouterClientAsync();
            await RefreshActiveProfileAsync(showNotification: true, _lifetimeCts.Token);
            StartPolicyRefresh(showFailureNotification: false);
            _logger.Info(_settings.AutomaticProfileSelection
                ? "Automatic router profile selection enabled."
                : $"Router profile selected manually: {_settings.SelectedProfile?.Name}.");
        }
        catch (OperationCanceledException) when (_isShuttingDown)
        {
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to apply router profile selection.", ex);
            HandleException(UiText.MenuProfiles, ex);
        }
    }

    private async Task<bool> RefreshActiveProfileAsync(bool showNotification, CancellationToken ct)
    {
        var snapshot = await _interfaceService.GetSnapshotAsync(ct: ct);
        var resolution = ResolveProfile(snapshot);
        return SetActiveProfile(resolution.Profile, showNotification);
    }

    private async Task<bool> RefreshActiveProfileAfterNetworkChangeAsync(
        long changeVersion,
        CancellationToken ct)
    {
        var minimumAttempts = _settings.AutomaticProfileSelection
            ? NetworkProfileRefreshAttempts
            : NetworkProfileStableSamples;
        var resolution = await ResolveStableProfileAsync(
            minimumAttempts,
            () => changeVersion == Interlocked.Read(ref _networkChangeVersion),
            ct);
        if (resolution is null)
        {
            return false;
        }

        SetActiveProfile(resolution.Profile, showNotification: false);
        if (resolution.Profile is null)
        {
            _logger.Info(
                "No stable matching router profile after network change; " +
                "passive notification was suppressed.");
        }

        return true;
    }

    private Task<RouterProfileSelection?> ResolveStableProfileAsync(
        int minimumAttempts,
        Func<bool>? isCurrent,
        CancellationToken ct)
    {
        return RouterProfileConvergence.ResolveAsync(
            token => _interfaceService.GetSnapshotAsync(ct: token),
            ResolveProfile,
            NetworkProfileRefreshAttempts,
            NetworkProfileStableSamples,
            minimumAttempts,
            NetworkProfileRefreshDelay,
            isCurrent,
            ct);
    }

    private RouterProfileSelection ResolveProfile(InterfaceSnapshot snapshot) =>
        RouterProfileSelector.Resolve(_settings, snapshot);

    private async Task<InterfaceSnapshot> GetProfileSnapshotAsync(
        RouterProfileSelection resolution,
        InterfaceSnapshot selectionSnapshot,
        CancellationToken ct)
    {
        var profile = resolution.Profile;
        if (profile is null)
        {
            return selectionSnapshot;
        }

        var configuredUri = RouterEndpoint.GetConfiguredUri(profile.RouterUrl);
        var preferredInterfaceId = !string.IsNullOrWhiteSpace(profile.PreferredInterfaceId)
            ? profile.PreferredInterfaceId
            : resolution.MatchedInterfaceId;
        if (string.IsNullOrWhiteSpace(preferredInterfaceId) && configuredUri is null)
        {
            return selectionSnapshot;
        }

        return await _interfaceService.GetSnapshotAsync(
            preferredInterfaceId,
            configuredUri,
            ct);
    }

    private bool SetActiveProfile(RouterProfile? profile, bool showNotification)
    {
        var profileId = profile?.Id;
        var changed = !string.Equals(
            _activeProfileId,
            profileId,
            StringComparison.OrdinalIgnoreCase);
        _activeProfileId = profileId;
        _notifyIcon.Icon = profile is null ? _inactiveIcon : _icon;

        var notifyText = profile is null
            ? UiText.AppName
            : $"{UiText.AppName} — {profile.Name}";
        _notifyIcon.Text = notifyText.Length <= 63 ? notifyText : notifyText[..63];

        if (!changed)
        {
            return false;
        }

        ActivatePolicyCache(profileId);

        if (profile is null)
        {
            _logger.Info("No router profile matches the current network.");
        }
        else
        {
            _logger.Info($"Active router profile changed to {profile.Name} ({profile.Id}).");
            if (showNotification)
            {
                ShowBalloon(
                    UiText.MenuProfiles,
                    UiText.ProfileChangedMessage(profile.Name),
                    ToolTipIcon.Info);
            }
        }

        return true;
    }

    private static Icon CreateInactiveIcon(Icon source)
    {
        using var sourceBitmap = source.ToBitmap();
        using var grayscaleBitmap = new Bitmap(sourceBitmap.Width, sourceBitmap.Height);
        using var graphics = Graphics.FromImage(grayscaleBitmap);
        using var imageAttributes = new ImageAttributes();
        var grayscaleMatrix = new ColorMatrix(
            new[]
            {
                new[]
                {
                    0.299f * InactiveIconGrayscaleWeight,
                    0.299f * InactiveIconGrayscaleWeight,
                    0.299f * InactiveIconGrayscaleWeight,
                    0f,
                    0f
                },
                new[]
                {
                    0.587f * InactiveIconGrayscaleWeight,
                    0.587f * InactiveIconGrayscaleWeight,
                    0.587f * InactiveIconGrayscaleWeight,
                    0f,
                    0f
                },
                new[]
                {
                    0.114f * InactiveIconGrayscaleWeight,
                    0.114f * InactiveIconGrayscaleWeight,
                    0.114f * InactiveIconGrayscaleWeight,
                    0f,
                    0f
                },
                new[] { 0f, 0f, 0f, 1f, 0f },
                new[]
                {
                    InactiveIconBrightness,
                    InactiveIconBrightness,
                    InactiveIconBrightness,
                    0f,
                    1f
                }
            });

        imageAttributes.SetColorMatrix(grayscaleMatrix);
        graphics.DrawImage(
            sourceBitmap,
            new Rectangle(0, 0, grayscaleBitmap.Width, grayscaleBitmap.Height),
            0,
            0,
            sourceBitmap.Width,
            sourceBitmap.Height,
            GraphicsUnit.Pixel,
            imageAttributes);

        var iconHandle = grayscaleBitmap.GetHicon();
        try
        {
            using var temporaryIcon = Icon.FromHandle(iconHandle);
            return (Icon)temporaryIcon.Clone();
        }
        finally
        {
            _ = DestroyIcon(iconHandle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);

    private async void OnInterfacesDropDownOpening(object? sender, EventArgs e)
    {
        await RefreshInterfacesAsync();
    }

    private async Task RefreshInterfacesAsync()
    {
        try
        {
            if (!await _interfaceLoadLock.WaitAsync(0, _lifetimeCts.Token))
            {
                return;
            }
        }
        catch (OperationCanceledException) when (_isShuttingDown)
        {
            return;
        }

        try
        {
            SetInterfacesLoading();
            var selectionSnapshot = await _interfaceService.GetSnapshotAsync(ct: _lifetimeCts.Token);
            var resolution = ResolveProfile(selectionSnapshot);
            var profileChanged = SetActiveProfile(resolution.Profile, showNotification: false);
            var snapshot = await GetProfileSnapshotAsync(
                resolution,
                selectionSnapshot,
                _lifetimeCts.Token);
            PopulateInterfacesMenu(snapshot, resolution.Profile);
            if (profileChanged)
            {
                StartPolicyRefresh(showFailureNotification: false);
            }
        }
        catch (OperationCanceledException) when (_isShuttingDown)
        {
        }
        catch (Exception ex)
        {
            SetInterfacesError();
            _logger.Error("Failed to load interfaces.", ex);
            ShowBalloon(UiText.MenuInterfaces, UiText.InterfacesLoadFailedMessage, ToolTipIcon.Error);
        }
        finally
        {
            _interfaceLoadLock.Release();
        }
    }

    private void SetInterfacesLoading()
    {
        _interfacesMenu.DropDownItems.Clear();
        _interfacesMenu.DropDownItems.Add(new ToolStripMenuItem(UiText.Loading) { Enabled = false });
    }

    private void SetInterfacesError()
    {
        _interfacesMenu.DropDownItems.Clear();
        _interfacesMenu.DropDownItems.Add(
            new ToolStripMenuItem(UiText.InterfacesLoadFailedMenu) { Enabled = false });
    }

    private void PopulateInterfacesMenu(InterfaceSnapshot snapshot, RouterProfile? profile)
    {
        _interfacesMenu.DropDownItems.Clear();

        var automaticItem = new ToolStripMenuItem(UiText.InterfacesAutomatic)
        {
            Checked = profile is not null && string.IsNullOrWhiteSpace(profile.PreferredInterfaceId),
            Tag = string.Empty
        };
        automaticItem.Click += OnInterfaceItemClick;
        _interfacesMenu.DropDownItems.Add(automaticItem);

        if (snapshot.Interfaces.Count == 0)
        {
            _interfacesMenu.DropDownItems.Add(
                new ToolStripMenuItem(UiText.InterfacesNone) { Enabled = false });
            return;
        }

        _interfacesMenu.DropDownItems.Add(new ToolStripSeparator());
        foreach (var netInterface in snapshot.Interfaces)
        {
            var label = $"{netInterface.Name} ({netInterface.MacAddress})";
            var item = new ToolStripMenuItem(label)
            {
                Checked = netInterface.IsPreferred ||
                          (profile is not null &&
                           string.IsNullOrWhiteSpace(profile.PreferredInterfaceId) &&
                           netInterface.IsActive),
                Enabled = netInterface.IsUp && profile is not null,
                Tag = netInterface.Id
            };
            item.Click += OnInterfaceItemClick;
            _interfacesMenu.DropDownItems.Add(item);
        }
    }

    private async void OnInterfaceItemClick(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem item || item.Tag is not string interfaceId)
        {
            return;
        }

        await SelectInterfaceAsync(interfaceId);
    }

    private async Task SelectInterfaceAsync(string interfaceId)
    {
        var selectionSnapshot = await _interfaceService.GetSnapshotAsync(ct: _lifetimeCts.Token);
        var resolution = ResolveProfile(selectionSnapshot);
        var profile = resolution.Profile;
        if (profile is null)
        {
            ShowBalloon(
                UiText.MenuProfiles,
                UiText.RouterProfileUnavailableMessage,
                ToolTipIcon.Warning);
            return;
        }

        if (string.Equals(
                profile.PreferredInterfaceId,
                interfaceId,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var candidate = _settings.Clone();
        var candidateProfile = candidate.FindProfile(profile.Id);
        if (candidateProfile is null)
        {
            return;
        }

        candidateProfile.PreferredInterfaceId = interfaceId;
        try
        {
            candidate.Save(_settingsPath);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to save preferred network interface.", ex);
            ShowBalloon(UiText.MenuInterfaces, UiText.SettingsSaveFailedMessage, ToolTipIcon.Error);
            return;
        }

        _settings = candidate;
        MarkPolicyCacheStale();
        try
        {
            await ResetRouterClientAsync();
            StartPolicyRefresh(showFailureNotification: false);
        }
        catch (OperationCanceledException) when (_isShuttingDown)
        {
            return;
        }

        _logger.Info(string.IsNullOrEmpty(interfaceId)
            ? "Automatic network interface selection enabled."
            : $"Preferred network interface changed to {interfaceId}.");
    }

    private void OnPoliciesDropDownOpening(object? sender, EventArgs e)
    {
        StartPolicyRefresh(showFailureNotification: true);
    }

    private async void OnNotifyIconMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left ||
            _activeProfileId is null ||
            _isShuttingDown ||
            _nativePolicyMenu.IsOpen)
        {
            return;
        }

        // Queue the router I/O first, but never await it on the click path.
        // TrackPopupMenuEx therefore opens from the cache while the request is
        // already running on a worker thread.
        StartPolicyRefresh(showFailureNotification: true);

        NativePolicyMenuSelection? selection;
        try
        {
            selection = _nativePolicyMenu.Show(Handle, Cursor.Position, _policyCache.Current);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to show the native policy menu.", ex);
            HandleException(UiText.MenuPolicies, ex);
            return;
        }

        if (selection is null)
        {
            return;
        }

        if (selection.IsDefault)
        {
            await ClearPolicyAsync();
        }
        else if (selection.PolicyId is not null)
        {
            await ApplyPolicyAsync(selection.PolicyId, selection.DisplayName);
        }
    }

    private void StartPolicyRefresh(bool showFailureNotification)
    {
        if (_isShuttingDown || _activeProfileId is null)
        {
            return;
        }

        _policyRefreshNotifyOnFailure |= showFailureNotification;

        if (_policyCache.Current.State == PolicyMenuLoadState.Failed)
        {
            ApplyPolicyCache(PolicyMenuSnapshot.Loading);
        }

        var generation = _policyCacheGeneration;
        if (!_policyLoadLock.Wait(0))
        {
            if (_policyRefreshRunningGeneration != generation)
            {
                _policyRefreshPending = true;
            }

            return;
        }

        _policyRefreshRunningGeneration = generation;
        var activeProfileId = _activeProfileId;
        var refreshTask = Task.Run(
            () => LoadPolicyRefreshResultAsync(_lifetimeCts.Token),
            CancellationToken.None);
        _ = ObservePolicyRefreshAsync(
            refreshTask,
            generation,
            activeProfileId);
    }

    private Task<PolicyRefreshResult> LoadPolicyRefreshResultAsync(CancellationToken ct)
    {
        return WithRouterClientAsync(async (client, mac, requestCt) =>
        {
            // Authenticate once before the recoverable current-policy probe.
            // Authentication and connectivity failures must abort the refresh
            // instead of causing another login through GetPoliciesAsync.
            await client.EnsureAuthenticatedAsync(requestCt).ConfigureAwait(false);

            string? currentPolicy = null;
            try
            {
                currentPolicy = await client
                    .GetCurrentPolicyAsync(mac, requestCt)
                    .ConfigureAwait(false);
            }
            catch (KeeneticRequestException ex) when (client.IsAuthenticated)
            {
                _logger.Error(
                    "Failed to read current policy; continuing with an unknown selection.",
                    ex);
            }

            var policies = await client.GetPoliciesAsync(requestCt).ConfigureAwait(false);
            return new PolicyRefreshResult(policies, currentPolicy);
        }, ct, updateActiveProfile: false);
    }

    private async Task ObservePolicyRefreshAsync(
        Task<PolicyRefreshResult> refreshTask,
        int generation,
        string activeProfileId)
    {
        try
        {
            var result = await refreshTask;
            if (IsCurrentPolicyRefresh(generation, activeProfileId))
            {
                var refreshedSnapshot = PolicyMenuSnapshot.FromRouter(
                    result.Policies,
                    result.CurrentPolicy);
                if (result.CurrentPolicy is null &&
                    _policyCache.Current.State == PolicyMenuLoadState.Loaded)
                {
                    // A failed current-policy probe must not erase a still-useful
                    // cached check mark when the policy list itself was refreshed.
                    refreshedSnapshot = refreshedSnapshot.WithSelectionFrom(_policyCache.Current);
                }

                ApplyPolicyCache(refreshedSnapshot);
            }
        }
        catch (OperationCanceledException) when (
            _isShuttingDown ||
            !IsCurrentPolicyRefresh(generation, activeProfileId))
        {
        }
        catch (Exception ex)
        {
            if (IsCurrentPolicyRefresh(generation, activeProfileId))
            {
                if (_policyCache.Current.State != PolicyMenuLoadState.Loaded)
                {
                    ApplyPolicyCache(PolicyMenuSnapshot.Failed);
                }

                if (_policyRefreshNotifyOnFailure)
                {
                    HandleException(UiText.MenuPolicies, ex);
                }
                else
                {
                    _logger.Error("Failed to refresh policies in the background.", ex);
                }
            }
        }
        finally
        {
            _policyRefreshRunningGeneration = -1;
            _policyLoadLock.Release();

            var restart = _policyRefreshPending &&
                          !_isShuttingDown &&
                          _activeProfileId is not null;
            var notifyOnFailure = _policyRefreshNotifyOnFailure;
            _policyRefreshPending = false;
            _policyRefreshNotifyOnFailure = false;

            if (restart)
            {
                StartPolicyRefresh(notifyOnFailure);
            }
        }
    }

    private bool IsCurrentPolicyRefresh(int generation, string activeProfileId)
    {
        return !_isShuttingDown &&
               generation == _policyCacheGeneration &&
               string.Equals(
                   activeProfileId,
                   _activeProfileId,
                   StringComparison.OrdinalIgnoreCase);
    }

    private void MarkPolicyCacheStale()
    {
        _policyCacheGeneration = unchecked(_policyCacheGeneration + 1);
    }

    private void ActivatePolicyCache(string? profileId)
    {
        var previousSnapshot = _policyCache.Current;
        if (!_policyCache.Activate(profileId))
        {
            return;
        }

        MarkPolicyCacheStale();
        var activeSnapshot = _policyCache.Current;
        if (!previousSnapshot.ContentEquals(activeSnapshot))
        {
            RenderPolicyCacheChange(previousSnapshot, activeSnapshot);
        }
    }

    private void ApplyPolicyCache(PolicyMenuSnapshot snapshot)
    {
        var previousSnapshot = _policyCache.Current;
        if (!_policyCache.Update(snapshot))
        {
            return;
        }

        RenderPolicyCacheChange(previousSnapshot, snapshot);
    }

    private void RenderPolicyCacheChange(
        PolicyMenuSnapshot previousSnapshot,
        PolicyMenuSnapshot snapshot)
    {
        var structureUnchanged = previousSnapshot.HasSameStructure(snapshot);
        if (structureUnchanged)
        {
            UpdatePoliciesMenuChecks(snapshot);
        }
        else
        {
            PopulatePoliciesMenu(snapshot);
        }

        _nativePolicyMenu.Update(snapshot);
    }

    private void UpdatePoliciesMenuChecks(PolicyMenuSnapshot snapshot)
    {
        if (_policiesMenu.DropDownItems.Count == 0 ||
            _policiesMenu.DropDownItems[0] is not ToolStripMenuItem defaultItem)
        {
            PopulatePoliciesMenu(snapshot);
            return;
        }

        defaultItem.Checked = snapshot.IsDefaultSelected;
        for (var index = 0; index < snapshot.Policies.Count; index++)
        {
            var menuIndex = index + 1;
            if (menuIndex >= _policiesMenu.DropDownItems.Count ||
                _policiesMenu.DropDownItems[menuIndex] is not ToolStripMenuItem policyItem)
            {
                PopulatePoliciesMenu(snapshot);
                return;
            }

            policyItem.Checked = snapshot.Policies[index].IsSelected;
        }
    }

    private void PopulatePoliciesMenu(PolicyMenuSnapshot snapshot)
    {
        _policiesMenu.DropDownItems.Clear();

        var defaultItem = new ToolStripMenuItem(UiText.PolicyDefaultDisplay)
        {
            Checked = snapshot.IsDefaultSelected
        };
        defaultItem.Click += OnDefaultPolicyClick;
        _policiesMenu.DropDownItems.Add(defaultItem);

        switch (snapshot.State)
        {
            case PolicyMenuLoadState.Loading:
                _policiesMenu.DropDownItems.Add(
                    new ToolStripMenuItem(UiText.Loading) { Enabled = false });
                break;
            case PolicyMenuLoadState.Failed:
                _policiesMenu.DropDownItems.Add(
                    new ToolStripMenuItem(UiText.PoliciesLoadFailedMenu) { Enabled = false });
                break;
            case PolicyMenuLoadState.Loaded when snapshot.Policies.Count == 0:
                _policiesMenu.DropDownItems.Add(
                    new ToolStripMenuItem(UiText.PoliciesNone) { Enabled = false });
                break;
            case PolicyMenuLoadState.Loaded:
                foreach (var policy in snapshot.Policies)
                {
                    var item = new ToolStripMenuItem(policy.DisplayName)
                    {
                        Checked = policy.IsSelected,
                        Tag = policy
                    };
                    item.Click += OnPolicyClick;
                    _policiesMenu.DropDownItems.Add(item);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(snapshot));
        }
    }

    private async void OnDefaultPolicyClick(object? sender, EventArgs e)
    {
        await ClearPolicyAsync();
    }

    private async void OnPolicyClick(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem { Tag: PolicyMenuEntry policy })
        {
            return;
        }

        await ApplyPolicyAsync(policy.Id, policy.DisplayName);
    }

    private async Task ClearPolicyAsync()
    {
        var succeeded = await ExecuteAsync(UiText.PolicyTitle, async ct =>
        {
            await WithRouterClientAsync(async (client, mac, requestCt) =>
            {
                await client.ClearPolicyAsync(mac, requestCt);
                return true;
            }, ct);
            return UiText.PolicySetMessage(UiText.PolicyDefaultDisplay);
        }, _settings.ShowPolicyNotifications);

        if (succeeded)
        {
            ApplyPolicyCache(_policyCache.Current.WithDefaultSelected());
        }
    }

    private async Task ApplyPolicyAsync(string policyId, string displayName)
    {
        var succeeded = await ExecuteAsync(UiText.PolicyTitle, async ct =>
        {
            await WithRouterClientAsync(async (client, mac, requestCt) =>
            {
                await client.SetPolicyAsync(policyId, mac, requestCt);
                return true;
            }, ct);
            return UiText.PolicySetMessage(displayName);
        }, _settings.ShowPolicyNotifications);

        if (succeeded)
        {
            ApplyPolicyCache(_policyCache.Current.WithPolicySelected(policyId));
        }
    }

    private async Task<T> WithRouterClientAsync<T>(
        Func<KeeneticClient, string, CancellationToken, Task<T>> action,
        CancellationToken ct,
        bool updateActiveProfile = true)
    {
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token, ct);
        await _routerOperationLock.WaitAsync(waitCts.Token);
        try
        {
            if (_connectionCts.IsCancellationRequested)
            {
                throw new RouterConnectionChangedException(
                    "Network profile selection is still being refreshed.");
            }

            using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetimeCts.Token,
                _connectionCts.Token,
                ct);
            var requestToken = requestCts.Token;
            requestToken.ThrowIfCancellationRequested();

            var selectionSnapshot = await _interfaceService.GetSnapshotAsync(ct: requestToken);
            var resolution = ResolveProfile(selectionSnapshot);
            var profile = resolution.Profile;
            if (updateActiveProfile)
            {
                SetActiveProfile(profile, showNotification: false);
            }

            if (profile is null)
            {
                DisposeRouterClient();
                throw new RouterProfileUnavailableException();
            }

            if (profile.AuthMode == RouterAuthMode.Password &&
                (string.IsNullOrWhiteSpace(profile.Login) ||
                 string.IsNullOrWhiteSpace(profile.Password)))
            {
                throw new KeeneticAuthException("Router login and password are required.");
            }

            if (profile.AuthMode == RouterAuthMode.AccessToken &&
                string.IsNullOrWhiteSpace(profile.AccessToken))
            {
                throw new KeeneticAuthException("Router access token is required.");
            }

            var snapshot = await GetProfileSnapshotAsync(
                resolution,
                selectionSnapshot,
                requestToken);
            var endpoint = _interfaceService.ResolveRouterUri(profile.RouterUrl, snapshot);
            if (endpoint is null ||
                string.IsNullOrWhiteSpace(snapshot.ActiveMac) ||
                string.IsNullOrWhiteSpace(snapshot.ActiveInterfaceId))
            {
                throw new RouterEndpointUnavailableException();
            }

            if (_client is null ||
                !string.Equals(_clientProfileId, profile.Id, StringComparison.OrdinalIgnoreCase) ||
                !RouterEndpoint.Equals(_clientEndpoint, endpoint) ||
                !string.Equals(
                    _clientInterfaceId,
                    snapshot.ActiveInterfaceId,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    _clientGateway,
                    snapshot.ActiveGateway,
                    StringComparison.OrdinalIgnoreCase))
            {
                DisposeRouterClient();
                _client = new KeeneticClient(
                    endpoint,
                    profile.AuthMode,
                    profile.Login,
                    profile.Password,
                    profile.AccessToken);
                _clientEndpoint = endpoint;
                _clientInterfaceId = snapshot.ActiveInterfaceId;
                _clientGateway = snapshot.ActiveGateway;
                _clientProfileId = profile.Id;
            }

            try
            {
                return await action(_client, snapshot.ActiveMac, requestToken);
            }
            catch (OperationCanceledException ex)
                when (_connectionCts.IsCancellationRequested &&
                      !_lifetimeCts.IsCancellationRequested &&
                      !ct.IsCancellationRequested)
            {
                throw new RouterConnectionChangedException(ex);
            }
        }
        finally
        {
            _routerOperationLock.Release();
        }
    }

    private async Task ResetRouterClientAsync()
    {
        await _connectionCts.CancelAsync();
        await _routerOperationLock.WaitAsync(_lifetimeCts.Token);
        try
        {
            DisposeRouterClient();
            ReplaceConnectionToken();
        }
        finally
        {
            _routerOperationLock.Release();
        }
    }

    private void ReplaceConnectionToken()
    {
        _connectionCts.Dispose();
        _connectionCts = new CancellationTokenSource();
    }

    private void DisposeRouterClient()
    {
        _client?.Dispose();
        _client = null;
        _clientEndpoint = null;
        _clientInterfaceId = null;
        _clientGateway = null;
        _clientProfileId = null;
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        if (_isShuttingDown || !IsHandleCreated || IsDisposed)
        {
            return;
        }

        var changeVersion = Interlocked.Increment(ref _networkChangeVersion);
        try
        {
            _connectionCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A concurrent reset replaced the token. Still queue this network
            // version so the new session is converged against the latest NLM state.
        }

        try
        {
            BeginInvoke(new Action<long>(ResetRouterClientAfterNetworkChange), changeVersion);
        }
        catch (InvalidOperationException)
        {
            // The form was disposed between the checks and BeginInvoke.
        }
    }

    private async void ResetRouterClientAfterNetworkChange(long changeVersion)
    {
        if (_isShuttingDown)
        {
            return;
        }

        try
        {
            MarkPolicyCacheStale();
            await Task.Delay(NetworkChangeDebounce, _lifetimeCts.Token);
            if (changeVersion != Interlocked.Read(ref _networkChangeVersion))
            {
                return;
            }

            if (!await RefreshActiveProfileAfterNetworkChangeAsync(
                    changeVersion,
                    _lifetimeCts.Token))
            {
                return;
            }

            await ResetRouterClientAsync();
            if (changeVersion != Interlocked.Read(ref _networkChangeVersion))
            {
                try
                {
                    _connectionCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }

                return;
            }

            StartPolicyRefresh(showFailureNotification: false);

            _logger.Info("Network configuration changed; router session and profile were refreshed.");
        }
        catch (OperationCanceledException) when (_isShuttingDown)
        {
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to reset router client after a network change.", ex);
        }
    }

    private async Task<bool> ExecuteAsync(
        string title,
        Func<CancellationToken, Task<string>> action,
        bool showSuccess = true)
    {
        try
        {
            var message = await action(_lifetimeCts.Token);
            if (_isShuttingDown)
            {
                return false;
            }

            _logger.Info($"{title}: {message}");
            if (showSuccess)
            {
                ShowBalloon(title, message, ToolTipIcon.Info);
            }

            return true;
        }
        catch (OperationCanceledException) when (_isShuttingDown)
        {
            return false;
        }
        catch (Exception ex)
        {
            HandleException(title, ex);
            return false;
        }
    }

    private void HandleException(string title, Exception ex)
    {
        if (_isShuttingDown)
        {
            return;
        }

        switch (ex)
        {
            case KeeneticAuthException authEx:
                _logger.Error("Authentication failed.", authEx);
                ShowBalloon(title, UiText.AuthFailedMessage, ToolTipIcon.Error);
                break;
            case RouterProfileUnavailableException profileEx:
                _logger.Error("No router profile matches the current network.", profileEx);
                ShowBalloon(title, UiText.RouterProfileUnavailableMessage, ToolTipIcon.Warning);
                break;
            case RouterEndpointUnavailableException endpointEx:
                _logger.Error("Router endpoint or active interface unavailable.", endpointEx);
                ShowBalloon(title, UiText.RouterEndpointUnavailableMessage, ToolTipIcon.Error);
                break;
            case RouterConnectionChangedException changedEx:
                _logger.Error("Network changed during router request.", changedEx);
                ShowBalloon(title, UiText.RouterUnreachableMessage, ToolTipIcon.Warning);
                break;
            case TaskCanceledException canceledEx:
                _logger.Error("Request timed out.", canceledEx);
                ShowBalloon(title, UiText.RequestTimeoutMessage, ToolTipIcon.Warning);
                break;
            case HttpRequestException httpEx:
                _logger.Error("Router unreachable.", httpEx);
                ShowBalloon(title, UiText.RouterUnreachableMessage, ToolTipIcon.Error);
                break;
            case KeeneticRequestException requestEx:
                _logger.Error("Router API error.", requestEx);
                ShowBalloon(title, UiText.RouterApiErrorMessage, ToolTipIcon.Error);
                break;
            default:
                _logger.Error("Unexpected error.", ex);
                ShowBalloon(title, UiText.UnexpectedErrorMessage, ToolTipIcon.Error);
                break;
        }
    }

    internal static bool IsDefaultPolicy(string? policy)
    {
        return !string.IsNullOrWhiteSpace(policy) &&
               string.Equals(policy.Trim(), DefaultPolicyId, StringComparison.OrdinalIgnoreCase);
    }

    private async void OnSettingsMenuClick(object? sender, EventArgs e)
    {
        await ShowSettingsAsync();
    }

    private async Task ShowSettingsAsync()
    {
        if (_settingsForm is not null)
        {
            if (_settingsForm.WindowState == FormWindowState.Minimized)
            {
                _settingsForm.WindowState = FormWindowState.Normal;
            }

            _settingsForm.Activate();
            return;
        }

        RouterNetworkBinding? currentNetwork = null;
        try
        {
            var snapshot = await _interfaceService.GetSnapshotAsync(ct: _lifetimeCts.Token);
            if (!string.IsNullOrWhiteSpace(snapshot.ActiveNetworkId))
            {
                currentNetwork = new RouterNetworkBinding
                {
                    NetworkId = snapshot.ActiveNetworkId,
                    NetworkName = snapshot.ActiveNetworkName ?? string.Empty
                };
            }
        }
        catch (OperationCanceledException) when (_isShuttingDown)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to identify the current Windows network for Settings.", ex);
        }

        using var form = new SettingsForm(
            _settings,
            currentNetwork,
            _updateService.CheckNowAsync,
            _usesPackageManagedUpdates)
        {
            StartPosition = FormStartPosition.CenterScreen
        };
        _settingsForm = form;

        try
        {
            var result = form.ShowDialog(this);
            if (result != DialogResult.OK)
            {
                return;
            }

            var candidate = form.ResultSettings;

            try
            {
                candidate.Save(_settingsPath);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to save settings.", ex);
                ShowBalloon(UiText.SettingsTitle, UiText.SettingsSaveFailedMessage, ToolTipIcon.Error);
                return;
            }

            var previousAutoStart = _settings.AutoStart;
            _settings = candidate;
            _updateService.SetConfiguration(
                _settings.CheckForUpdatesAutomatically,
                _settings.UpdateChannel);

            MarkPolicyCacheStale();
            try
            {
                await ResetRouterClientAsync();
                await RefreshActiveProfileAsync(showNotification: false, _lifetimeCts.Token);
                StartPolicyRefresh(showFailureNotification: false);
            }
            catch (OperationCanceledException) when (_isShuttingDown)
            {
                return;
            }

            _logger.Info("Settings saved and router session reset.");
            ShowBalloon(UiText.SettingsTitle, UiText.SettingsSavedMessage, ToolTipIcon.Info);

            if (previousAutoStart != _settings.AutoStart)
            {
                await ApplyAutoStartAsync(_settings.AutoStart, showNotification: true);
            }
        }
        finally
        {
            _settingsForm = null;
            TryApplyScheduledUpdate();
        }
    }

    private async Task ApplyAutoStartAsync(bool enabled, bool showNotification)
    {
        try
        {
            var result = await _autoStartService.EnsureEnabledAsync(enabled);
            if (result != AutoStartApplyResult.Applied)
            {
                _logger.Info($"Auto start request was rejected by Windows ({result}).");
                if (showNotification)
                {
                    ShowBalloon(
                        UiText.SettingsTitle,
                        UiText.AutoStartFailedMessage,
                        ToolTipIcon.Error);
                }

                return;
            }

            _logger.Info($"Auto start {(enabled ? "enabled" : "disabled")}.");

            if (showNotification)
            {
                var message = enabled ? UiText.AutoStartEnabledMessage : UiText.AutoStartDisabledMessage;
                ShowBalloon(UiText.SettingsTitle, message, ToolTipIcon.Info);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to update auto start.", ex);
            if (showNotification)
            {
                ShowBalloon(UiText.SettingsTitle, UiText.AutoStartFailedMessage, ToolTipIcon.Error);
            }
        }
    }

    private void ShowAbout()
    {
        if (_aboutForm is not null)
        {
            if (_aboutForm.WindowState == FormWindowState.Minimized)
            {
                _aboutForm.WindowState = FormWindowState.Normal;
            }

            _aboutForm.Activate();
            return;
        }

        _aboutForm = new AboutForm
        {
            StartPosition = FormStartPosition.CenterScreen
        };
        _aboutForm.FormClosed += (_, _) =>
        {
            _aboutForm = null;
            TryApplyScheduledUpdate();
        };
        _aboutForm.Show(this);
    }

    private void ShowBalloon(string title, string message, ToolTipIcon icon)
    {
        if (_isShuttingDown || _resourcesDisposed)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(3000);
    }

    private void ScheduleUpdateApply(Action applyUpdate)
    {
        if (_isShuttingDown || _resourcesDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                if (_isShuttingDown || _resourcesDisposed)
                {
                    return;
                }

                _scheduledUpdateApply = applyUpdate;
                TryApplyScheduledUpdate();
            }));
        }
        catch (InvalidOperationException) when (_isShuttingDown || IsDisposed)
        {
        }
    }

    private void TryApplyScheduledUpdate()
    {
        if (_scheduledUpdateApply is null ||
            _settingsForm is not null ||
            _aboutForm is not null ||
            _isShuttingDown ||
            _resourcesDisposed)
        {
            return;
        }

        var applyUpdate = _scheduledUpdateApply;
        _scheduledUpdateApply = null;
        try
        {
            applyUpdate();
            Close();
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to start the downloaded application update.", ex);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesDisposed)
        {
            _resourcesDisposed = true;
            _updateService.Dispose();
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            _notifyIcon.Visible = false;
            _notifyIcon.MouseClick -= OnNotifyIconMouseClick;
            _notifyIcon.Dispose();
            _menu.Dispose();
            _nativePolicyMenu.Dispose();
            DisposeRouterClient();
            _connectionCts.Dispose();
            _lifetimeCts.Dispose();
            _interfaceLoadLock.Dispose();
            _policyLoadLock.Dispose();
            _routerOperationLock.Dispose();
            _inactiveIcon.Dispose();
            if (_ownsIcon)
            {
                _icon.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private sealed record PolicyRefreshResult(
        IReadOnlyList<PolicyInfo> Policies,
        string? CurrentPolicy);

}

internal sealed class RouterProfileUnavailableException : Exception
{
    public RouterProfileUnavailableException()
        : base("No router profile matches the current network.")
    {
    }
}

internal sealed class RouterEndpointUnavailableException : Exception
{
    public RouterEndpointUnavailableException()
        : base("Router address or active network interface is unavailable.")
    {
    }

    public RouterEndpointUnavailableException(string message)
        : base(message)
    {
    }

    public RouterEndpointUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed class RouterConnectionChangedException : Exception
{
    public RouterConnectionChangedException()
    {
    }

    public RouterConnectionChangedException(string message)
        : base(message)
    {
    }

    public RouterConnectionChangedException(Exception innerException)
        : base("Network configuration changed during the router request.", innerException)
    {
    }

    public RouterConnectionChangedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
