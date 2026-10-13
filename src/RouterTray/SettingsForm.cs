namespace RouterTray;

internal sealed class SettingsForm : Form
{
    private const int CompactProfileEditorWidth = 640;
    private const int WorkingAreaMargin = 24;

    private AppSettings _workingSettings;
    private readonly RouterNetworkBinding? _currentNetwork;
    private readonly FileLogger _logger;
    private readonly Func<
        ApplicationUpdateChannel,
        CancellationToken,
        Task<ApplicationUpdateCheckResult>> _checkForUpdates;
    private readonly bool _updatesManagedByPackage;
    private readonly CancellationTokenSource _updateCheckCancellation = new();
    private readonly Icon _formIcon;

    private TabControl _settingsTabs = null!;
    private TabPage _profilesPage = null!;
    private Panel _profilesScrollHost = null!;
    private TableLayoutPanel _profilesContent = null!;
    private TableLayoutPanel _profileEditorLayout = null!;
    private GroupBox _connectionGroup = null!;
    private GroupBox _authenticationGroup = null!;
    private Label _routerUrlHintLabel = null!;
    private ToolStrip _profileTabsToolStrip = null!;
    private Button _removeProfileButton = null!;
    private TextBox _profileNameTextBox = null!;
    private TextBox _routerUrlTextBox = null!;
    private ComboBox _authModeComboBox = null!;
    private Label _loginLabel = null!;
    private TextBox _loginTextBox = null!;
    private Label _passwordLabel = null!;
    private TextBox _passwordTextBox = null!;
    private CheckBox _showPasswordCheckBox = null!;
    private Label _accessTokenLabel = null!;
    private TextBox _accessTokenTextBox = null!;
    private CheckBox _showAccessTokenCheckBox = null!;
    private ListBox _networkBindingsListBox = null!;
    private Button _removeNetworkButton = null!;
    private CheckBox _automaticProfileSelectionCheckBox = null!;
    private CheckBox _autoStartCheckBox = null!;
    private CheckBox _checkForUpdatesCheckBox = null!;
    private ComboBox _updateChannelComboBox = null!;
    private Button _checkForUpdatesButton = null!;
    private Label _updateStatusLabel = null!;
    private CheckBox _notifyPolicyCheckBox = null!;

    private RouterProfile? _editingProfile;
    private bool _loadingProfile;
    private bool _profileEditorsStacked;
    private bool _updatingProfilesLayout;
    private bool _updateCheckCancellationDisposed;

    public SettingsForm(
        AppSettings settings,
        RouterNetworkBinding? currentNetwork,
        Func<
            ApplicationUpdateChannel,
            CancellationToken,
            Task<ApplicationUpdateCheckResult>> checkForUpdates,
        FileLogger logger,
        bool updatesManagedByPackage = false)
    {
        ArgumentNullException.ThrowIfNull(checkForUpdates);
        ArgumentNullException.ThrowIfNull(logger);
        _workingSettings = settings.Clone();
        _currentNetwork = currentNetwork?.Clone();
        _checkForUpdates = checkForUpdates;
        _logger = logger;
        _updatesManagedByPackage = updatesManagedByPackage;

        _formIcon = AppIconProvider.CreateIcon();
        Icon = _formIcon;
        Text = UiText.SettingsTitle;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(
            Math.Max(760, Font.Height * 32),
            Math.Max(700, Font.Height * 25));
        MinimumSize = new Size(440, 360);
        BackColor = SystemColors.Window;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = SystemColors.Window
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _settingsTabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(12, 12, 12, 0)
        };
        _profilesPage = CreateProfilesPage();
        _settingsTabs.TabPages.Add(_profilesPage);
        _settingsTabs.TabPages.Add(CreateApplicationPage());
        if (!_updatesManagedByPackage)
        {
            _settingsTabs.TabPages.Add(CreateUpdatesPage());
        }

        var footer = new Panel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(16, 10, 16, 10),
            BackColor = SystemColors.Control
        };

        var buttonsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            BackColor = SystemColors.Control
        };

        var saveButton = new Button
        {
            Text = UiText.SettingsSave,
            AutoSize = true,
            MinimumSize = new Size(96, 32),
            Margin = new Padding(8, 0, 0, 0)
        };
        saveButton.Click += OnSaveClick;

        var cancelButton = new Button
        {
            Text = UiText.SettingsCancel,
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            MinimumSize = new Size(96, 32),
            Margin = Padding.Empty
        };

        AcceptButton = saveButton;
        CancelButton = cancelButton;
        buttonsPanel.Controls.Add(saveButton);
        buttonsPanel.Controls.Add(cancelButton);
        footer.Controls.Add(buttonsPanel);

        mainLayout.Controls.Add(_settingsTabs, 0, 0);
        mainLayout.Controls.Add(footer, 0, 1);
        Controls.Add(mainLayout);

        var currentProfile = _workingSettings.FindProfileForNetwork(_currentNetwork?.NetworkId);
        PopulateProfileTabs(currentProfile?.Id ?? _workingSettings.SelectedProfileId);
        Shown += (_, _) =>
        {
            FitToWorkingArea();
            UpdateProfilesLayout();
            FocusSelectedProfileTab();
        };
    }

    public AppSettings ResultSettings => _workingSettings.Clone();

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (!_updateCheckCancellationDisposed)
        {
            _updateCheckCancellation.Cancel();
        }

        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_updateCheckCancellationDisposed)
        {
            _updateCheckCancellationDisposed = true;
            _updateCheckCancellation.Cancel();
            _updateCheckCancellation.Dispose();
            Icon = null;
            _formIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private TabPage CreateProfilesPage()
    {
        var page = new TabPage(UiText.SettingsProfilesTab)
        {
            Padding = Padding.Empty,
            BackColor = SystemColors.Window,
            UseVisualStyleBackColor = false
        };

        _profilesScrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(12),
            Margin = Padding.Empty,
            BackColor = SystemColors.Window
        };

        _profilesContent = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
            BackColor = SystemColors.Window
        };
        _profilesContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _profilesContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _profilesContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _profilesContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var profileHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 12),
            BackColor = SystemColors.Window
        };
        profileHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        profileHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        profileHeader.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _profileTabsToolStrip = new ToolStrip
        {
            Dock = DockStyle.Fill,
            GripStyle = ToolStripGripStyle.Hidden,
            LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow,
            Renderer = new ProfileTabRenderer(),
            ShowItemToolTips = true,
            TabStop = true,
            CanOverflow = true,
            BackColor = SystemColors.Window,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };

        _removeProfileButton = new Button
        {
            Text = UiText.SettingsProfileRemove,
            AutoSize = true,
            MinimumSize = new Size(104, 32),
            Anchor = AnchorStyles.Right,
            Margin = new Padding(12, 2, 0, 2)
        };
        _removeProfileButton.Click += OnRemoveProfileClick;

        var profileActions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty,
            BackColor = SystemColors.Window
        };

        var addProfileButton = new Button
        {
            Text = UiText.SettingsProfileAdd,
            AutoSize = true,
            MinimumSize = new Size(128, 32),
            Margin = new Padding(12, 2, 0, 2)
        };
        addProfileButton.Click += OnAddProfileClick;

        profileActions.Controls.Add(addProfileButton);
        profileActions.Controls.Add(_removeProfileButton);

        profileHeader.Controls.Add(_profileTabsToolStrip, 0, 0);
        profileHeader.Controls.Add(profileActions, 1, 0);

        _profileEditorLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = SystemColors.Window,
            Margin = new Padding(0, 0, 0, 12)
        };
        _profileEditorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        _profileEditorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        _profileEditorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _connectionGroup = CreateConnectionGroup();
        _connectionGroup.Dock = DockStyle.Fill;
        _connectionGroup.Margin = new Padding(0, 0, 6, 0);

        _authenticationGroup = CreateAuthenticationGroup();
        _authenticationGroup.Dock = DockStyle.Fill;
        _authenticationGroup.Margin = new Padding(6, 0, 0, 0);

        _profileEditorLayout.Controls.Add(_connectionGroup, 0, 0);
        _profileEditorLayout.Controls.Add(_authenticationGroup, 1, 0);

        var networkGroup = CreateNetworkBindingsGroup();
        networkGroup.Dock = DockStyle.Fill;

        _profilesContent.Controls.Add(profileHeader, 0, 0);
        _profilesContent.Controls.Add(_profileEditorLayout, 0, 1);
        _profilesContent.Controls.Add(networkGroup, 0, 2);

        _profilesScrollHost.Controls.Add(_profilesContent);
        _profilesScrollHost.ClientSizeChanged += (_, _) => UpdateProfilesLayout();
        _profilesScrollHost.Layout += (_, _) => UpdateProfilesLayout();
        page.Controls.Add(_profilesScrollHost);
        return page;
    }

    private GroupBox CreateConnectionGroup()
    {
        var group = CreateSectionGroup(UiText.SettingsConnectionSection);
        var fields = CreateStackedFieldLayout(5);

        _profileNameTextBox = CreateTextBox();
        _profileNameTextBox.Margin = new Padding(0, 0, 0, 6);
        _profileNameTextBox.TextChanged += OnProfileNameTextChanged;

        _routerUrlTextBox = CreateTextBox();
        _routerUrlTextBox.Margin = new Padding(0, 0, 0, 6);
        _routerUrlTextBox.PlaceholderText = AppSettings.RouterUrlExample;

        _routerUrlHintLabel = new Label
        {
            Text = UiText.SettingsRouterUrlHint,
            AutoSize = true,
            MaximumSize = new Size(320, 0),
            ForeColor = SystemColors.GrayText,
            Margin = Padding.Empty
        };
        fields.ClientSizeChanged += (_, _) => UpdateWrappingLabelWidth(_routerUrlHintLabel, fields);

        fields.Controls.Add(CreateCompactFieldLabel(UiText.SettingsProfileName), 0, 0);
        fields.Controls.Add(_profileNameTextBox, 0, 1);
        fields.Controls.Add(CreateCompactFieldLabel(UiText.SettingsRouterUrl), 0, 2);
        fields.Controls.Add(_routerUrlTextBox, 0, 3);
        fields.Controls.Add(_routerUrlHintLabel, 0, 4);
        group.Controls.Add(fields);
        return group;
    }

    private GroupBox CreateAuthenticationGroup()
    {
        var group = CreateSectionGroup(UiText.SettingsAuthenticationSection);
        var fields = CreateStackedFieldLayout(7);

        _authModeComboBox = new ComboBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 0, 0, 6)
        };
        _authModeComboBox.Items.Add(new AuthModeItem(
            RouterAuthMode.Password,
            UiText.SettingsAuthModePassword));
        _authModeComboBox.Items.Add(new AuthModeItem(
            RouterAuthMode.AccessToken,
            UiText.SettingsAuthModeAccessToken));
        _authModeComboBox.SelectedIndexChanged += (_, _) => UpdateAuthModeVisibility();

        _loginLabel = CreateCompactFieldLabel(UiText.SettingsLogin);
        _loginTextBox = CreateTextBox();
        _loginTextBox.Margin = new Padding(0, 0, 0, 6);
        _passwordLabel = CreateCompactFieldLabel(UiText.SettingsPassword);
        _passwordTextBox = CreateTextBox();
        _passwordTextBox.Margin = new Padding(0, 0, 0, 6);
        _passwordTextBox.UseSystemPasswordChar = true;

        _showPasswordCheckBox = new CheckBox
        {
            Text = UiText.SettingsShowPassword,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty
        };
        _showPasswordCheckBox.CheckedChanged += (_, _) =>
            _passwordTextBox.UseSystemPasswordChar = !_showPasswordCheckBox.Checked;

        _accessTokenLabel = CreateCompactFieldLabel(UiText.SettingsAccessToken);
        _accessTokenTextBox = CreateTextBox();
        _accessTokenTextBox.Margin = new Padding(0, 0, 0, 6);
        _accessTokenTextBox.UseSystemPasswordChar = true;

        _showAccessTokenCheckBox = new CheckBox
        {
            Text = UiText.SettingsShowAccessToken,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty
        };
        _showAccessTokenCheckBox.CheckedChanged += (_, _) =>
            _accessTokenTextBox.UseSystemPasswordChar = !_showAccessTokenCheckBox.Checked;

        fields.Controls.Add(CreateCompactFieldLabel(UiText.SettingsAuthMode), 0, 0);
        fields.Controls.Add(_authModeComboBox, 0, 1);
        fields.Controls.Add(_loginLabel, 0, 2);
        fields.Controls.Add(_loginTextBox, 0, 3);
        fields.Controls.Add(_passwordLabel, 0, 4);
        fields.Controls.Add(_passwordTextBox, 0, 5);
        fields.Controls.Add(_showPasswordCheckBox, 0, 6);
        fields.Controls.Add(_accessTokenLabel, 0, 2);
        fields.Controls.Add(_accessTokenTextBox, 0, 3);
        fields.Controls.Add(_showAccessTokenCheckBox, 0, 6);
        group.Controls.Add(fields);
        return group;
    }

    private GroupBox CreateNetworkBindingsGroup()
    {
        var group = CreateSectionGroup(UiText.SettingsProfileNetworks);
        group.MinimumSize = new Size(0, 170);
        group.Margin = Padding.Empty;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = SystemColors.Window,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var currentNetworkText = _currentNetwork is null
            ? UiText.SettingsCurrentNetworkUnavailable
            : UiText.SettingsCurrentNetwork(
                string.IsNullOrWhiteSpace(_currentNetwork.NetworkName)
                    ? _currentNetwork.NetworkId
                    : _currentNetwork.NetworkName);
        var currentNetworkLabel = new Label
        {
            Text = currentNetworkText,
            AutoSize = true,
            MaximumSize = new Size(600, 0),
            ForeColor = _currentNetwork is null ? SystemColors.GrayText : SystemColors.WindowText,
            Margin = new Padding(0, 0, 0, 8)
        };
        layout.ClientSizeChanged += (_, _) => UpdateWrappingLabelWidth(currentNetworkLabel, layout);

        _networkBindingsListBox = new ListBox
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(0, 96),
            IntegralHeight = false,
            BorderStyle = BorderStyle.FixedSingle,
            HorizontalScrollbar = true
        };
        _networkBindingsListBox.SelectedIndexChanged += (_, _) =>
            _removeNetworkButton.Enabled = _networkBindingsListBox.SelectedItem is RouterNetworkBinding;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        var bindButton = new Button
        {
            Text = UiText.SettingsBindCurrentNetwork,
            AutoSize = true,
            MinimumSize = new Size(0, 30),
            Enabled = _currentNetwork is not null,
            Margin = new Padding(0, 0, 8, 0)
        };
        bindButton.Click += OnBindCurrentNetworkClick;

        _removeNetworkButton = new Button
        {
            Text = UiText.SettingsRemoveNetwork,
            AutoSize = true,
            MinimumSize = new Size(0, 30),
            Enabled = false,
            Margin = Padding.Empty
        };
        _removeNetworkButton.Click += OnRemoveNetworkClick;
        buttons.Controls.Add(bindButton);
        buttons.Controls.Add(_removeNetworkButton);

        layout.Controls.Add(currentNetworkLabel, 0, 0);
        layout.Controls.Add(_networkBindingsListBox, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        group.Controls.Add(layout);
        return group;
    }

    private TabPage CreateApplicationPage()
    {
        var page = new TabPage(UiText.SettingsApplicationTab)
        {
            Padding = Padding.Empty,
            BackColor = SystemColors.Window,
            UseVisualStyleBackColor = false
        };

        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(16),
            BackColor = SystemColors.Window
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            BackColor = SystemColors.Window
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _automaticProfileSelectionCheckBox = new CheckBox
        {
            Text = UiText.SettingsAutomaticProfileSelection,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Checked = _workingSettings.AutomaticProfileSelection,
            Margin = new Padding(0, 0, 0, 12)
        };
        _autoStartCheckBox = new CheckBox
        {
            Text = UiText.SettingsAutoStart,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Checked = _workingSettings.AutoStart,
            Margin = new Padding(0, 0, 0, 12)
        };
        _notifyPolicyCheckBox = new CheckBox
        {
            Text = UiText.SettingsShowPolicyNotifications,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Checked = _workingSettings.ShowPolicyNotifications,
            Margin = new Padding(0, 0, 0, 12)
        };

        layout.Controls.Add(_automaticProfileSelectionCheckBox, 0, 0);
        layout.Controls.Add(_autoStartCheckBox, 0, 1);
        layout.Controls.Add(_notifyPolicyCheckBox, 0, 2);

        var updatingCheckBoxLayout = false;
        void UpdateCheckBoxLayout()
        {
            if (updatingCheckBoxLayout)
            {
                return;
            }

            var availableWidth = scrollHost.ClientSize.Width - scrollHost.Padding.Horizontal;
            if (scrollHost.VerticalScroll.Visible)
            {
                availableWidth -= SystemInformation.VerticalScrollBarWidth;
            }

            if (availableWidth <= 0)
            {
                return;
            }

            updatingCheckBoxLayout = true;
            try
            {
                foreach (var checkBox in new[]
                         {
                             _automaticProfileSelectionCheckBox,
                             _autoStartCheckBox,
                             _notifyPolicyCheckBox
                         })
                {
                    var textWidth = Math.Max(1, availableWidth - checkBox.Font.Height - 12);
                    var textSize = TextRenderer.MeasureText(
                        checkBox.Text,
                        checkBox.Font,
                        new Size(textWidth, int.MaxValue),
                        TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                    var height = Math.Max(checkBox.Font.Height + 8, textSize.Height + 8);
                    checkBox.MinimumSize = new Size(0, height);
                    checkBox.Height = height;
                }
            }
            finally
            {
                updatingCheckBoxLayout = false;
            }
        }

        scrollHost.Controls.Add(layout);
        scrollHost.ClientSizeChanged += (_, _) => UpdateCheckBoxLayout();
        scrollHost.Layout += (_, _) => UpdateCheckBoxLayout();
        page.Controls.Add(scrollHost);
        return page;
    }

    private TabPage CreateUpdatesPage()
    {
        var page = new TabPage(UiText.SettingsUpdatesTab)
        {
            Padding = Padding.Empty,
            BackColor = SystemColors.Window,
            UseVisualStyleBackColor = false
        };

        if (_updatesManagedByPackage)
        {
            var storeUpdateDescription = new Label
            {
                Text = UiText.SettingsStoreUpdatesDescription,
                Dock = DockStyle.Top,
                AutoSize = true,
                MaximumSize = new Size(Math.Max(1, ClientSize.Width - 64), 0),
                Margin = Padding.Empty
            };
            var storeUpdatePanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(16),
                BackColor = SystemColors.Window
            };
            storeUpdatePanel.Controls.Add(storeUpdateDescription);
            storeUpdatePanel.ClientSizeChanged += (_, _) =>
                storeUpdateDescription.MaximumSize = new Size(
                    Math.Max(1, storeUpdatePanel.ClientSize.Width - storeUpdatePanel.Padding.Horizontal),
                    0);
            page.Controls.Add(storeUpdatePanel);
            return page;
        }

        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(16),
            BackColor = SystemColors.Window
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 6,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            BackColor = SystemColors.Window
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _checkForUpdatesCheckBox = new CheckBox
        {
            Text = UiText.SettingsCheckForUpdates,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Checked = _workingSettings.CheckForUpdatesAutomatically,
            Margin = new Padding(0, 0, 0, 10)
        };
        var scheduleDescriptionLabel = new Label
        {
            Text = UiText.SettingsUpdateScheduleDescription,
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 18)
        };
        var channelLabel = CreateCompactFieldLabel(UiText.SettingsUpdateChannel);
        _updateChannelComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 4, 0, 14)
        };
        _updateChannelComboBox.Items.Add(new UpdateChannelItem(
            ApplicationUpdateChannel.Stable,
            UiText.SettingsUpdateChannelStable));
        _updateChannelComboBox.Items.Add(new UpdateChannelItem(
            ApplicationUpdateChannel.Preview,
            UiText.SettingsUpdateChannelPreview));
        _updateChannelComboBox.SelectedIndex =
            _workingSettings.UpdateChannel == ApplicationUpdateChannel.Preview ? 1 : 0;

        _checkForUpdatesButton = new Button
        {
            Text = UiText.SettingsCheckForUpdatesNow,
            AutoSize = true,
            MinimumSize = new Size(160, 32),
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 0, 12)
        };
        _checkForUpdatesButton.Click += OnCheckForUpdatesClick;
        _updateStatusLabel = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.ControlText,
            Margin = Padding.Empty,
            Visible = false
        };

        layout.Controls.Add(_checkForUpdatesCheckBox, 0, 0);
        layout.Controls.Add(scheduleDescriptionLabel, 0, 1);
        layout.Controls.Add(channelLabel, 0, 2);
        layout.Controls.Add(_updateChannelComboBox, 0, 3);
        layout.Controls.Add(_checkForUpdatesButton, 0, 4);
        layout.Controls.Add(_updateStatusLabel, 0, 5);

        var updatingLayout = false;
        void UpdateLayout()
        {
            if (updatingLayout)
            {
                return;
            }

            var availableWidth = scrollHost.ClientSize.Width - scrollHost.Padding.Horizontal;
            if (scrollHost.VerticalScroll.Visible)
            {
                availableWidth -= SystemInformation.VerticalScrollBarWidth;
            }

            if (availableWidth <= 0)
            {
                return;
            }

            updatingLayout = true;
            try
            {
                var checkBoxTextWidth = Math.Max(
                    1,
                    availableWidth - _checkForUpdatesCheckBox.Font.Height - 12);
                var checkBoxTextSize = TextRenderer.MeasureText(
                    _checkForUpdatesCheckBox.Text,
                    _checkForUpdatesCheckBox.Font,
                    new Size(checkBoxTextWidth, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                _checkForUpdatesCheckBox.MinimumSize = new Size(
                    0,
                    Math.Max(_checkForUpdatesCheckBox.Font.Height + 8, checkBoxTextSize.Height + 8));
                _checkForUpdatesCheckBox.Height = _checkForUpdatesCheckBox.MinimumSize.Height;
                scheduleDescriptionLabel.MaximumSize = new Size(availableWidth, 0);
                _updateStatusLabel.MaximumSize = new Size(availableWidth, 0);
            }
            finally
            {
                updatingLayout = false;
            }
        }

        scrollHost.Controls.Add(layout);
        scrollHost.ClientSizeChanged += (_, _) => UpdateLayout();
        scrollHost.Layout += (_, _) => UpdateLayout();
        page.Controls.Add(scrollHost);
        return page;
    }

    private void UpdateProfilesLayout()
    {
        if (_updatingProfilesLayout ||
            _profilesScrollHost is null ||
            _profilesContent is null ||
            _profileEditorLayout is null)
        {
            return;
        }

        _updatingProfilesLayout = true;
        try
        {
            var availableWidth = _profilesScrollHost.ClientSize.Width -
                                 _profilesScrollHost.Padding.Horizontal;
            if (_profilesScrollHost.VerticalScroll.Visible)
            {
                availableWidth -= SystemInformation.VerticalScrollBarWidth;
            }

            if (availableWidth <= 0)
            {
                return;
            }

            var wideEditorMinimum = Math.Max(CompactProfileEditorWidth, Font.Height * 27);
            var shouldStackEditors = availableWidth < wideEditorMinimum;
            if (shouldStackEditors != _profileEditorsStacked)
            {
                ConfigureProfileEditorLayout(shouldStackEditors);
            }

            var viewportHeight = Math.Max(
                0,
                _profilesScrollHost.ClientSize.Height - _profilesScrollHost.Padding.Vertical);
            if (_profilesContent.MinimumSize.Height != viewportHeight)
            {
                _profilesContent.MinimumSize = new Size(0, viewportHeight);
            }

            if (_routerUrlHintLabel.Parent is Control hintContainer)
            {
                UpdateWrappingLabelWidth(_routerUrlHintLabel, hintContainer);
            }
        }
        finally
        {
            _updatingProfilesLayout = false;
        }
    }

    private void ConfigureProfileEditorLayout(bool stacked)
    {
        _profileEditorLayout.SuspendLayout();
        try
        {
            _profileEditorLayout.Controls.Remove(_connectionGroup);
            _profileEditorLayout.Controls.Remove(_authenticationGroup);
            _profileEditorLayout.ColumnStyles.Clear();
            _profileEditorLayout.RowStyles.Clear();

            if (stacked)
            {
                _profileEditorLayout.ColumnCount = 1;
                _profileEditorLayout.RowCount = 2;
                _profileEditorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                _profileEditorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _profileEditorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _connectionGroup.Margin = new Padding(0, 0, 0, 6);
                _authenticationGroup.Margin = new Padding(0, 6, 0, 0);
                _profileEditorLayout.Controls.Add(_connectionGroup, 0, 0);
                _profileEditorLayout.Controls.Add(_authenticationGroup, 0, 1);
            }
            else
            {
                _profileEditorLayout.ColumnCount = 2;
                _profileEditorLayout.RowCount = 1;
                _profileEditorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
                _profileEditorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
                _profileEditorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _connectionGroup.Margin = new Padding(0, 0, 6, 0);
                _authenticationGroup.Margin = new Padding(6, 0, 0, 0);
                _profileEditorLayout.Controls.Add(_connectionGroup, 0, 0);
                _profileEditorLayout.Controls.Add(_authenticationGroup, 1, 0);
            }

            _profileEditorsStacked = stacked;
        }
        finally
        {
            _profileEditorLayout.ResumeLayout(true);
        }
    }

    private void FitToWorkingArea()
    {
        var screen = Screen.FromControl(Owner ?? this);
        var workingArea = screen.WorkingArea;
        var margin = ScaleLogicalPixels(WorkingAreaMargin);
        var maximumWidth = Math.Max(1, workingArea.Width - margin * 2);
        var maximumHeight = Math.Max(1, workingArea.Height - margin * 2);
        var fittedSize = new Size(
            Math.Min(Width, maximumWidth),
            Math.Min(Height, maximumHeight));

        if (Size != fittedSize)
        {
            Size = fittedSize;
        }

        Location = new Point(
            workingArea.Left + Math.Max(0, (workingArea.Width - Width) / 2),
            workingArea.Top + Math.Max(0, (workingArea.Height - Height) / 2));
    }

    private int ScaleLogicalPixels(int value)
    {
        return Math.Max(1, (int)Math.Ceiling(value * DeviceDpi / 96d));
    }

    private static void UpdateWrappingLabelWidth(Label label, Control container)
    {
        var availableWidth = container.ClientSize.Width - container.Padding.Horizontal;
        if (availableWidth > 0 && label.MaximumSize.Width != availableWidth)
        {
            label.MaximumSize = new Size(availableWidth, 0);
        }
    }

    private void PopulateProfileTabs(string? selectedProfileId)
    {
        var selected = _workingSettings.FindProfile(selectedProfileId) ??
                       _workingSettings.Profiles.First();

        _loadingProfile = true;
        try
        {
            while (_profileTabsToolStrip.Items.Count > 0)
            {
                var item = _profileTabsToolStrip.Items[0];
                _profileTabsToolStrip.Items.RemoveAt(0);
                item.Dispose();
            }

            foreach (var profile in _workingSettings.Profiles)
            {
                var profileTab = new ToolStripButton
                {
                    Text = GetProfileTabText(profile),
                    Tag = profile,
                    DisplayStyle = ToolStripItemDisplayStyle.Text,
                    AutoSize = true,
                    AutoToolTip = false,
                    ToolTipText = GetProfileDisplayName(profile),
                    AccessibleName = GetProfileDisplayName(profile),
                    Padding = new Padding(12, 6, 12, 6),
                    Margin = Padding.Empty,
                    Overflow = ToolStripItemOverflow.AsNeeded
                };
                profileTab.Click += OnProfileTabClick;
                _profileTabsToolStrip.Items.Add(profileTab);
            }

            var addProfileTab = new ToolStripButton
            {
                Text = "＋",
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                AutoToolTip = false,
                ToolTipText = UiText.SettingsProfileAdd,
                AccessibleName = UiText.SettingsProfileAdd,
                Padding = new Padding(10, 6, 10, 6),
                Margin = Padding.Empty,
                Overflow = ToolStripItemOverflow.Never
            };
            addProfileTab.Click += OnAddProfileClick;
            _profileTabsToolStrip.Items.Add(addProfileTab);
        }
        finally
        {
            _loadingProfile = false;
        }

        LoadProfileEditor(selected);
        UpdateProfileTabSelection();
        UpdateRemoveProfileButton();
    }

    private void OnProfileTabClick(object? sender, EventArgs e)
    {
        if (_loadingProfile || sender is not ToolStripButton { Tag: RouterProfile profile })
        {
            return;
        }

        if (ReferenceEquals(profile, _editingProfile))
        {
            UpdateProfileTabSelection();
            return;
        }

        CommitProfileEditor();
        LoadProfileEditor(profile);
        UpdateProfileTabSelection();
    }

    private void LoadProfileEditor(RouterProfile? profile)
    {
        _editingProfile = profile;
        _loadingProfile = true;
        try
        {
            _profileNameTextBox.Text = profile?.Name ?? string.Empty;
            _routerUrlTextBox.Text = profile?.RouterUrl ?? string.Empty;
            _loginTextBox.Text = profile?.Login ?? string.Empty;
            _passwordTextBox.Text = profile?.Password ?? string.Empty;
            _accessTokenTextBox.Text = profile?.AccessToken ?? string.Empty;
            _authModeComboBox.SelectedIndex = profile?.AuthMode == RouterAuthMode.AccessToken ? 1 : 0;
            RefreshNetworkBindings();
            UpdateAuthModeVisibility();
        }
        finally
        {
            _loadingProfile = false;
        }
    }

    private void CommitProfileEditor()
    {
        if (_editingProfile is null || _loadingProfile)
        {
            return;
        }

        _editingProfile.Name = _profileNameTextBox.Text;
        _editingProfile.RouterUrl = _routerUrlTextBox.Text;
        _editingProfile.AuthMode = SelectedAuthMode;
        _editingProfile.Login = _loginTextBox.Text;
        _editingProfile.Password = _passwordTextBox.Text;
        _editingProfile.AccessToken = _accessTokenTextBox.Text;
        UpdateProfileTab(_editingProfile);
    }

    private void OnProfileNameTextChanged(object? sender, EventArgs e)
    {
        if (_loadingProfile || _editingProfile is null)
        {
            return;
        }

        _editingProfile.Name = _profileNameTextBox.Text;
        UpdateProfileTab(_editingProfile);
    }

    private void OnAddProfileClick(object? sender, EventArgs e)
    {
        CommitProfileEditor();
        if (!TryValidateProfiles())
        {
            return;
        }

        CommitApplicationEditors();

        try
        {
            using var wizard = FirstRunSetupForm.CreateForNewProfile(_workingSettings, _logger);
            if (wizard.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            _workingSettings = wizard.ResultSettings;
            PopulateProfileTabs(_workingSettings.SelectedProfileId);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to open the router profile setup wizard.", ex);
            ShowValidationMessage(UiText.UnexpectedErrorMessage);
        }
    }

    private void OnRemoveProfileClick(object? sender, EventArgs e)
    {
        if (_editingProfile is null)
        {
            return;
        }

        if (_workingSettings.Profiles.Count <= 1)
        {
            ShowValidationMessage(UiText.SettingsCannotRemoveLastProfileMessage);
            return;
        }

        var answer = MessageBox.Show(
            this,
            UiText.SettingsRemoveProfileConfirmation(_editingProfile.Name),
            UiText.SettingsTitle,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        var removedIndex = _workingSettings.Profiles.IndexOf(_editingProfile);
        var removedId = _editingProfile.Id;
        _workingSettings.Profiles.Remove(_editingProfile);
        var next = _workingSettings.Profiles[Math.Min(removedIndex, _workingSettings.Profiles.Count - 1)];
        if (string.Equals(_workingSettings.SelectedProfileId, removedId, StringComparison.OrdinalIgnoreCase))
        {
            _workingSettings.SelectedProfileId = next.Id;
        }

        PopulateProfileTabs(next.Id);
    }

    private void OnBindCurrentNetworkClick(object? sender, EventArgs e)
    {
        if (_editingProfile is null || _currentNetwork is null)
        {
            return;
        }

        var otherProfile = _workingSettings.Profiles.FirstOrDefault(profile =>
            !ReferenceEquals(profile, _editingProfile) && profile.IsBoundTo(_currentNetwork.NetworkId));
        if (otherProfile is not null)
        {
            var answer = MessageBox.Show(
                this,
                UiText.SettingsMoveNetworkConfirmation(otherProfile.Name),
                UiText.SettingsTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                return;
            }

            otherProfile.Networks.RemoveAll(binding =>
                string.Equals(binding.NetworkId, _currentNetwork.NetworkId, StringComparison.OrdinalIgnoreCase));
        }

        var existing = _editingProfile.Networks.FirstOrDefault(binding =>
            string.Equals(binding.NetworkId, _currentNetwork.NetworkId, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            _editingProfile.Networks.Add(_currentNetwork.Clone());
        }
        else
        {
            existing.NetworkName = _currentNetwork.NetworkName;
        }

        RefreshNetworkBindings();
    }

    private void OnRemoveNetworkClick(object? sender, EventArgs e)
    {
        if (_editingProfile is null ||
            _networkBindingsListBox.SelectedItem is not RouterNetworkBinding binding)
        {
            return;
        }

        _editingProfile.Networks.Remove(binding);
        RefreshNetworkBindings();
    }

    private void RefreshNetworkBindings()
    {
        _networkBindingsListBox.Items.Clear();
        if (_editingProfile is not null)
        {
            foreach (var binding in _editingProfile.Networks
                         .OrderBy(item => item.NetworkName, StringComparer.CurrentCultureIgnoreCase))
            {
                _networkBindingsListBox.Items.Add(binding);
            }
        }

        _removeNetworkButton.Enabled = false;
    }

    private void UpdateProfileTabSelection()
    {
        foreach (ToolStripItem item in _profileTabsToolStrip.Items)
        {
            if (item is ToolStripButton { Tag: RouterProfile profile } profileTab)
            {
                profileTab.Checked = ReferenceEquals(profile, _editingProfile);
            }
        }

        _profileTabsToolStrip.Invalidate();
    }

    private void FocusSelectedProfileTab()
    {
        _profileTabsToolStrip.Focus();
        foreach (ToolStripItem item in _profileTabsToolStrip.Items)
        {
            if (item is ToolStripButton { Checked: true } profileTab)
            {
                profileTab.Select();
                break;
            }
        }
    }

    private void UpdateProfileTab(RouterProfile profile)
    {
        foreach (ToolStripItem item in _profileTabsToolStrip.Items)
        {
            if (item is not ToolStripButton { Tag: RouterProfile tabProfile } profileTab ||
                !ReferenceEquals(tabProfile, profile))
            {
                continue;
            }

            var displayName = GetProfileDisplayName(profile);
            profileTab.Text = GetProfileTabText(profile);
            profileTab.ToolTipText = displayName;
            profileTab.AccessibleName = displayName;
            break;
        }
    }

    private void UpdateRemoveProfileButton()
    {
        _removeProfileButton.Enabled = _editingProfile is not null && _workingSettings.Profiles.Count > 1;
    }

    private static string GetProfileDisplayName(RouterProfile profile)
    {
        var name = profile.Name.Trim();
        return string.IsNullOrEmpty(name) ? UiText.SettingsUnnamedProfile : name;
    }

    private static string GetProfileTabText(RouterProfile profile)
    {
        const int maximumLength = 28;
        var name = GetProfileDisplayName(profile);
        return name.Length <= maximumLength ? name : $"{name[..(maximumLength - 1)]}…";
    }

    private async void OnCheckForUpdatesClick(object? sender, EventArgs e)
    {
        if (_updateChannelComboBox.SelectedItem is not UpdateChannelItem channelItem)
        {
            return;
        }

        var updateScheduled = false;
        _checkForUpdatesButton.Enabled = false;
        _updateChannelComboBox.Enabled = false;
        _updateStatusLabel.Text = UiText.SettingsUpdateCheckInProgress;
        _updateStatusLabel.ForeColor = SystemColors.ControlText;
        _updateStatusLabel.Visible = true;

        try
        {
            var result = await _checkForUpdates(
                channelItem.Channel,
                _updateCheckCancellation.Token);
            if (_updateCheckCancellation.IsCancellationRequested || IsDisposed)
            {
                return;
            }

            updateScheduled = result == ApplicationUpdateCheckResult.UpdateScheduled;
            _updateStatusLabel.Text = result switch
            {
                ApplicationUpdateCheckResult.UpToDate => UiText.SettingsUpdateUpToDate,
                ApplicationUpdateCheckResult.UpdateScheduled => UiText.SettingsUpdateReady,
                ApplicationUpdateCheckResult.NotPackaged => UiText.SettingsUpdateCheckUnavailable,
                _ => UiText.SettingsUpdateCheckFailed
            };
            _updateStatusLabel.ForeColor = result == ApplicationUpdateCheckResult.Failed
                ? Color.Firebrick
                : SystemColors.ControlText;
        }
        catch (OperationCanceledException) when (_updateCheckCancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (!IsDisposed)
            {
                _updateStatusLabel.Text = UiText.SettingsUpdateCheckFailed;
                _updateStatusLabel.ForeColor = Color.Firebrick;
                _updateStatusLabel.Visible = true;
            }
        }
        finally
        {
            if (!IsDisposed && !_updateCheckCancellation.IsCancellationRequested)
            {
                _updateChannelComboBox.Enabled = true;
                _checkForUpdatesButton.Enabled = !updateScheduled;
            }
        }
    }

    private void OnSaveClick(object? sender, EventArgs e)
    {
        CommitProfileEditor();

        if (!TryValidateProfiles())
        {
            return;
        }

        CommitApplicationEditors();
        if (_workingSettings.FindProfile(_workingSettings.SelectedProfileId) is null)
        {
            _workingSettings.SelectedProfileId = _workingSettings.Profiles[0].Id;
        }

        try
        {
            _workingSettings.NormalizeAndValidate();
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        {
            ShowValidationMessage(ex.Message);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private bool TryValidateProfiles()
    {

        var emptyNameProfile = _workingSettings.Profiles.FirstOrDefault(profile =>
            string.IsNullOrWhiteSpace(profile.Name));
        if (emptyNameProfile is not null)
        {
            SelectProfile(emptyNameProfile);
            ShowValidationMessage(UiText.SettingsProfileNameValidationMessage);
            _profileNameTextBox.Focus();
            return false;
        }

        var duplicateName = _workingSettings.Profiles
            .GroupBy(profile => profile.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateName is not null)
        {
            SelectProfile(duplicateName.First());
            ShowValidationMessage(UiText.SettingsProfileDuplicateNameMessage);
            _profileNameTextBox.Focus();
            _profileNameTextBox.SelectAll();
            return false;
        }

        foreach (var profile in _workingSettings.Profiles)
        {
            if (profile.AuthMode == RouterAuthMode.Password &&
                (string.IsNullOrWhiteSpace(profile.Login) || string.IsNullOrWhiteSpace(profile.Password)))
            {
                SelectProfile(profile);
                ShowValidationMessage(UiText.SettingsProfileValidation(
                    profile.Name,
                    UiText.SettingsValidationMessage));
                var missingCredential = string.IsNullOrWhiteSpace(profile.Login)
                    ? _loginTextBox
                    : _passwordTextBox;
                missingCredential.Focus();
                return false;
            }

            if (profile.AuthMode == RouterAuthMode.AccessToken &&
                string.IsNullOrWhiteSpace(profile.AccessToken))
            {
                SelectProfile(profile);
                ShowValidationMessage(UiText.SettingsProfileValidation(
                    profile.Name,
                    UiText.SettingsAccessTokenValidationMessage));
                _accessTokenTextBox.Focus();
                return false;
            }

            try
            {
                _ = RouterEndpoint.NormalizeConfiguredUrl(profile.RouterUrl);
            }
            catch (InvalidOperationException)
            {
                SelectProfile(profile);
                ShowValidationMessage(UiText.SettingsProfileValidation(
                    profile.Name,
                    UiText.SettingsRouterUrlValidationMessage));
                _routerUrlTextBox.Focus();
                _routerUrlTextBox.SelectAll();
                return false;
            }
        }

        return true;
    }

    private void CommitApplicationEditors()
    {
        _workingSettings.AutomaticProfileSelection = _automaticProfileSelectionCheckBox.Checked;
        _workingSettings.AutoStart = _autoStartCheckBox.Checked;
        if (!_updatesManagedByPackage)
        {
            _workingSettings.CheckForUpdatesAutomatically = _checkForUpdatesCheckBox.Checked;
            _workingSettings.UpdateChannel = SelectedUpdateChannel;
        }
        _workingSettings.ShowPolicyNotifications = _notifyPolicyCheckBox.Checked;
    }

    private void SelectProfile(RouterProfile profile)
    {
        _settingsTabs.SelectedTab = _profilesPage;
        if (ReferenceEquals(_editingProfile, profile))
        {
            UpdateProfileTabSelection();
            return;
        }

        CommitProfileEditor();
        LoadProfileEditor(profile);
        UpdateProfileTabSelection();
    }

    private void ShowValidationMessage(string message)
    {
        MessageBox.Show(
            this,
            message,
            UiText.SettingsTitle,
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private RouterAuthMode SelectedAuthMode =>
        (_authModeComboBox.SelectedItem as AuthModeItem)?.Mode ?? RouterAuthMode.Password;

    private ApplicationUpdateChannel SelectedUpdateChannel =>
        (_updateChannelComboBox.SelectedItem as UpdateChannelItem)?.Channel ??
        ApplicationUpdateChannel.Stable;

    private void UpdateAuthModeVisibility()
    {
        if (_authModeComboBox is null)
        {
            return;
        }

        var usePassword = SelectedAuthMode == RouterAuthMode.Password;
        _loginLabel.Visible = usePassword;
        _loginTextBox.Visible = usePassword;
        _passwordLabel.Visible = usePassword;
        _passwordTextBox.Visible = usePassword;
        _showPasswordCheckBox.Visible = usePassword;

        _accessTokenLabel.Visible = !usePassword;
        _accessTokenTextBox.Visible = !usePassword;
        _showAccessTokenCheckBox.Visible = !usePassword;
    }

    private static Label CreateCompactFieldLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 0, 2)
        };
    }

    private static GroupBox CreateSectionGroup(string text)
    {
        return new GroupBox
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = SystemColors.Window,
            Padding = new Padding(12, 10, 12, 12),
            Margin = new Padding(0, 0, 0, 14)
        };
    }

    private static TableLayoutPanel CreateStackedFieldLayout(int rowCount)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = rowCount,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = SystemColors.Window,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var index = 0; index < rowCount; index++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        return layout;
    }

    private static TextBox CreateTextBox()
    {
        return new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 4, 0, 4)
        };
    }

    private sealed class ProfileTabRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(SystemColors.Window);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.DrawLine(
                SystemPens.ControlLight,
                0,
                e.ToolStrip.Height - 1,
                e.ToolStrip.Width,
                e.ToolStrip.Height - 1);
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item is not ToolStripButton button)
            {
                base.OnRenderButtonBackground(e);
                return;
            }

            var bounds = new Rectangle(Point.Empty, button.Size);
            if (button.Pressed)
            {
                e.Graphics.FillRectangle(SystemBrushes.ControlLight, bounds);
            }
            else if (button.Selected && !button.Checked)
            {
                e.Graphics.FillRectangle(SystemBrushes.Control, bounds);
            }

            if (button.Checked)
            {
                e.Graphics.FillRectangle(
                    SystemBrushes.Highlight,
                    new Rectangle(4, Math.Max(0, bounds.Height - 3), Math.Max(0, bounds.Width - 8), 3));
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item is ToolStripButton { Checked: true }
                ? SystemColors.Highlight
                : e.Item.Enabled
                    ? SystemColors.WindowText
                    : SystemColors.GrayText;
            base.OnRenderItemText(e);
        }
    }

    private sealed record AuthModeItem(RouterAuthMode Mode, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record UpdateChannelItem(
        ApplicationUpdateChannel Channel,
        string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
