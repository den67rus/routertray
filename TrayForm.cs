using System.Net.Http;

namespace RouterTray;

internal sealed class TrayForm : Form
{
    private const string DefaultPolicyId = "default";
    private readonly AppSettings _settings;
    private readonly FileLogger _logger;
    private readonly NetworkInterfaceService _interfaceService;
    private readonly KeeneticClient _client;
    private readonly AutoStartService _autoStartService;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _interfacesMenu;
    private readonly ToolStripMenuItem _policiesMenu;
    private readonly ToolStripMenuItem _settingsMenu;
    private readonly ToolStripMenuItem _aboutMenu;
    private readonly SemaphoreSlim _interfaceLoadLock = new(1, 1);
    private readonly SemaphoreSlim _policyLoadLock = new(1, 1);
    private readonly Icon _icon;
    private readonly bool _ownsIcon;
    private AboutForm? _aboutForm;
    private SettingsForm? _settingsForm;

    public TrayForm(AppSettings settings, FileLogger logger)
    {
        _settings = settings;
        _logger = logger;
        _interfaceService = new NetworkInterfaceService();
        _settings.RouterUrl = _interfaceService.GetRouterUrl(_settings.RouterUrl);
        _client = new KeeneticClient(settings, GetActiveDeviceMac);
        _autoStartService = new AutoStartService("RouterTray", Application.ExecutablePath);

        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;

        var menu = new ContextMenuStrip();

        _interfacesMenu = new ToolStripMenuItem(UiText.MenuInterfaces);
        _interfacesMenu.DropDownOpening += async (_, __) => await RefreshInterfacesAsync();
        _interfacesMenu.DropDownItems.Add(new ToolStripMenuItem(UiText.Loading) { Enabled = false });

        _policiesMenu = new ToolStripMenuItem(UiText.MenuPolicies);
        _policiesMenu.DropDownOpening += async (_, __) => await RefreshPoliciesAsync();
        _policiesMenu.DropDownItems.Add(new ToolStripMenuItem(UiText.Loading) { Enabled = false });

        _settingsMenu = new ToolStripMenuItem(UiText.MenuSettings);
        _settingsMenu.Click += (_, __) => ShowSettings();

        _aboutMenu = new ToolStripMenuItem(UiText.MenuAbout);
        _aboutMenu.Click += (_, __) => ShowAbout();

        var exit = new ToolStripMenuItem(UiText.MenuExit);
        exit.Click += (_, __) => Close();

        menu.Items.Add(_interfacesMenu);
        menu.Items.Add(_policiesMenu);
        menu.Items.Add(_settingsMenu);
        menu.Items.Add(_aboutMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);

        _icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (_icon is null)
        {
            _icon = SystemIcons.Application;
            _ownsIcon = false;
        }
        else
        {
            _ownsIcon = true;
        }

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Visible = true,
            Text = UiText.AppName,
            ContextMenuStrip = menu
        };

        FormClosing += OnFormClosing;

        ApplyAutoStart(_settings.AutoStart, showNotification: false);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Hide();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        if (_ownsIcon)
        {
            _icon.Dispose();
        }
        _client.Dispose();
        _interfaceLoadLock.Dispose();
        _policyLoadLock.Dispose();
    }

    private async Task RefreshInterfacesAsync()
    {
        if (!await _interfaceLoadLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            SetInterfacesLoading();
            var snapshot = _interfaceService.GetSnapshot();
            PopulateInterfacesMenu(snapshot);
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
        _interfacesMenu.DropDownItems.Add(new ToolStripMenuItem(UiText.InterfacesLoadFailedMenu) { Enabled = false });
    }

    private void PopulateInterfacesMenu(InterfaceSnapshot snapshot)
    {
        _interfacesMenu.DropDownItems.Clear();

        if (snapshot.Interfaces.Count == 0)
        {
            _interfacesMenu.DropDownItems.Add(new ToolStripMenuItem(UiText.InterfacesNone) { Enabled = false });
            return;
        }

        foreach (var netInterface in snapshot.Interfaces)
        {
            var label = $"{netInterface.Name} ({netInterface.MacAddress})";
            var item = new ToolStripMenuItem(label)
            {
                Checked = netInterface.IsActive,
                Enabled = netInterface.IsUp
            };
            _interfacesMenu.DropDownItems.Add(item);
        }
    }

    private async Task ClearPolicyAsync()
    {
        await ExecuteAsync(UiText.PolicyTitle, async ct =>
        {
            await _client.ClearPolicyAsync(ct);
            return UiText.PolicySetMessage(UiText.PolicyDefaultDisplay);
        }, _settings.ShowPolicyNotifications);
    }

    private async Task RefreshPoliciesAsync()
    {
        if (!await _policyLoadLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            SetPoliciesLoading();
            var currentPolicy = await TryGetCurrentPolicyAsync();
            var policies = await _client.GetPoliciesAsync(CancellationToken.None);
            PopulatePoliciesMenu(policies, currentPolicy);
        }
        catch (Exception ex)
        {
            SetPoliciesError();
            HandleException(UiText.MenuPolicies, ex);
        }
        finally
        {
            _policyLoadLock.Release();
        }
    }

    private void SetPoliciesLoading()
    {
        _policiesMenu.DropDownItems.Clear();
        _policiesMenu.DropDownItems.Add(new ToolStripMenuItem(UiText.Loading) { Enabled = false });
    }

    private void SetPoliciesError()
    {
        _policiesMenu.DropDownItems.Clear();
        _policiesMenu.DropDownItems.Add(new ToolStripMenuItem(UiText.PoliciesLoadFailedMenu) { Enabled = false });
    }

    private void PopulatePoliciesMenu(IReadOnlyList<PolicyInfo> policies, string? currentPolicy)
    {
        _policiesMenu.DropDownItems.Clear();

        var isDefault = IsDefaultPolicy(currentPolicy);
        var defaultItem = new ToolStripMenuItem(UiText.PolicyDefaultDisplay)
        {
            Checked = isDefault
        };
        defaultItem.Click += async (_, __) => await ClearPolicyAsync();
        _policiesMenu.DropDownItems.Add(defaultItem);

        if (policies.Count == 0)
        {
            _policiesMenu.DropDownItems.Add(new ToolStripMenuItem(UiText.PoliciesNone) { Enabled = false });
            return;
        }

        _policiesMenu.DropDownItems.Add(new ToolStripSeparator());
        foreach (var policy in policies)
        {
            var displayName = string.IsNullOrWhiteSpace(policy.Name) ? policy.Id : policy.Name;
            var isCurrent = IsCurrentPolicy(currentPolicy, policy);
            var item = new ToolStripMenuItem(displayName)
            {
                Checked = isCurrent
            };
            item.Click += async (_, __) => await ApplyPolicyAsync(policy.Id, displayName);
            _policiesMenu.DropDownItems.Add(item);
        }
    }

    private async Task ApplyPolicyAsync(string policyId, string displayName)
    {
        await ExecuteAsync(UiText.PolicyTitle, async ct =>
        {
            await _client.SetPolicyAsync(policyId, ct);
            return UiText.PolicySetMessage(displayName);
        }, _settings.ShowPolicyNotifications);
    }

    private async Task ExecuteAsync(string title, Func<CancellationToken, Task<string>> action, bool showSuccess = true)
    {
        try
        {
            var message = await action(CancellationToken.None);
            _logger.Info($"{title}: {message}");
            if (showSuccess)
            {
                ShowBalloon(title, message, ToolTipIcon.Info);
            }
        }
        catch (Exception ex)
        {
            HandleException(title, ex);
        }
    }

    private void HandleException(string title, Exception ex)
    {
        if (ex is KeeneticAuthException authEx)
        {
            _logger.Error("Authentication failed.", authEx);
            ShowBalloon(title, UiText.AuthFailedMessage, ToolTipIcon.Error);
            return;
        }

        if (ex is TaskCanceledException canceledEx)
        {
            _logger.Error("Request timed out.", canceledEx);
            ShowBalloon(title, UiText.RequestTimeoutMessage, ToolTipIcon.Warning);
            return;
        }

        if (ex is HttpRequestException httpEx)
        {
            _logger.Error("Router unreachable.", httpEx);
            ShowBalloon(title, UiText.RouterUnreachableMessage, ToolTipIcon.Error);
            return;
        }

        if (ex is KeeneticRequestException requestEx)
        {
            _logger.Error("Router API error.", requestEx);
            ShowBalloon(title, UiText.RouterApiErrorMessage, ToolTipIcon.Error);
            return;
        }

        _logger.Error("Unexpected error.", ex);
        ShowBalloon(title, UiText.UnexpectedErrorMessage, ToolTipIcon.Error);
    }

    private async Task<string?> TryGetCurrentPolicyAsync()
    {
        try
        {
            return await _client.GetCurrentPolicyAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to read current policy.", ex);
            return null;
        }
    }

    private static bool IsDefaultPolicy(string? policy)
    {
        if (string.IsNullOrWhiteSpace(policy))
        {
            return true;
        }

        return string.Equals(policy.Trim(), DefaultPolicyId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCurrentPolicy(string? currentPolicy, PolicyInfo policy)
    {
        if (string.IsNullOrWhiteSpace(currentPolicy))
        {
            return false;
        }

        var normalized = currentPolicy.Trim();
        return string.Equals(normalized, policy.Id, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, policy.Name, StringComparison.OrdinalIgnoreCase);
    }

    private string? GetActiveDeviceMac()
    {
        var snapshot = _interfaceService.GetSnapshot();
        return snapshot.ActiveMac;
    }

    private void ShowSettings()
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

        using var form = new SettingsForm(_settings)
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

            _settings.Login = form.Login;
            _settings.Password = form.Password;
            var previousAutoStart = _settings.AutoStart;
            _settings.AutoStart = form.AutoStart;
            _settings.ShowPolicyNotifications = form.ShowPolicyNotifications;

            try
            {
                var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                _settings.Save(settingsPath);
                _logger.Info("Settings saved.");
                ShowBalloon(UiText.SettingsTitle, UiText.SettingsSavedMessage, ToolTipIcon.Info);

                if (previousAutoStart != _settings.AutoStart)
                {
                    ApplyAutoStart(_settings.AutoStart, showNotification: true);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to save settings.", ex);
                ShowBalloon(UiText.SettingsTitle, UiText.SettingsSaveFailedMessage, ToolTipIcon.Error);
            }
        }
        finally
        {
            _settingsForm = null;
        }
    }

    private void ApplyAutoStart(bool enabled, bool showNotification)
    {
        try
        {
            _autoStartService.EnsureEnabled(enabled);
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
        _aboutForm.FormClosed += (_, __) => _aboutForm = null;
        _aboutForm.Show(this);
    }

    private void ShowBalloon(string title, string message, ToolTipIcon icon)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(3000);
    }
}
