using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Net.Http;

namespace RouterTray;

internal sealed class FirstRunSetupForm : Form
{
    private const int StepCount = 5;
    private const int AuthenticationStep = 2;
    private const int DeviceStep = 3;
    private const int FinishStep = 4;
    private const int WorkingAreaMargin = 24;

    private static readonly Color AccentColor = Color.FromArgb(15, 108, 189);
    private static readonly Color AccentSurfaceColor = Color.FromArgb(235, 246, 255);
    private static readonly Color NeutralSurfaceColor = Color.FromArgb(247, 248, 250);
    private static readonly Color SuccessColor = Color.FromArgb(16, 124, 16);
    private static readonly Color SuccessSurfaceColor = Color.FromArgb(223, 246, 221);
    private static readonly Color WarningColor = Color.FromArgb(140, 84, 0);
    private static readonly Color WarningSurfaceColor = Color.FromArgb(255, 244, 206);
    private static readonly Color ErrorColor = Color.FromArgb(164, 38, 44);
    private static readonly Color ErrorSurfaceColor = Color.FromArgb(253, 231, 233);

    private readonly AppSettings _workingSettings;
    private readonly RouterProfile _profile;
    private readonly FileLogger _logger;
    private readonly bool _isAddingProfile;
    private readonly NetworkInterfaceService _interfaceService = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly Icon _formIcon;
    private readonly Font _titleFont;
    private readonly Font _sectionFont;
    private readonly Font _emphasisFont;

    private readonly SetupStepRail _stepRail;
    private readonly Panel[] _pages;
    private readonly Label _errorBanner;
    private readonly Label _progressLabel;
    private readonly Button _backButton;
    private readonly Button _nextButton;
    private readonly Button _cancelButton;

    private TextBox _profileNameTextBox = null!;
    private RadioButton _automaticAddressRadio = null!;
    private RadioButton _manualAddressRadio = null!;
    private TextBox _routerUrlTextBox = null!;
    private Label _detectedRouterLabel = null!;
    private CheckBox _bindNetworkCheckBox = null!;
    private Label _networkStatusLabel = null!;
    private RadioButton _passwordModeRadio = null!;
    private RadioButton _tokenModeRadio = null!;
    private TableLayoutPanel _passwordFields = null!;
    private TableLayoutPanel _tokenFields = null!;
    private TextBox _loginTextBox = null!;
    private TextBox _passwordTextBox = null!;
    private TextBox _accessTokenTextBox = null!;
    private Label _deviceAdapterValue = null!;
    private Label _deviceMacValue = null!;
    private BorderedPanel _temporaryMacPanel = null!;
    private CheckBox _temporaryMacAcknowledgeCheckBox = null!;
    private TextBox _deviceNameTextBox = null!;
    private BorderedPanel _deviceStatusPanel = null!;
    private Label _deviceStatusLabel = null!;
    private ProgressBar _deviceProgressBar = null!;
    private Button _registerDeviceButton = null!;
    private Button _recheckDeviceButton = null!;
    private Label _summaryRouterValue = null!;
    private Label _summaryAuthenticationValue = null!;
    private Label _summaryNetworkValue = null!;
    private Label _summaryDeviceValue = null!;
    private BorderedPanel _testStatusPanel = null!;
    private Label _testStatusLabel = null!;
    private ProgressBar _testProgressBar = null!;
    private Button _testConnectionButton = null!;
    private CheckBox _autoStartCheckBox = null!;
    private CheckBox _notifyPolicyCheckBox = null!;

    private InterfaceSnapshot? _snapshot;
    private Task? _networkLoadTask;
    private CancellationTokenSource? _deviceCts;
    private CancellationTokenSource? _testCts;
    private int _currentStep;
    private int? _checkedDeviceSignature;
    private int? _lastTestSignature;
    private bool _deviceRegistered;
    private bool _deviceOperationInProgress;
    private bool _temporaryMacDetected;
    private string _displayedDeviceMac = string.Empty;
    private string _registeredDeviceName = string.Empty;
    private readonly Dictionary<string, bool> _networkBindingChoices =
        new(StringComparer.OrdinalIgnoreCase);
    private PendingNetworkBindingMove? _pendingNetworkBindingMove;
    private bool _updatingNetworkBindingChoice;
    private bool _closeConfirmed;
    private bool _disposed;

    public FirstRunSetupForm(AppSettings settings, FileLogger logger)
        : this(settings, logger, isAddingProfile: false)
    {
    }

    private FirstRunSetupForm(
        AppSettings settings,
        FileLogger logger,
        bool isAddingProfile)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _isAddingProfile = isAddingProfile;
        if (_isAddingProfile)
        {
            var draft = CreateNewProfileDraft(settings);
            _workingSettings = draft.Settings;
            _profile = draft.Profile;
        }
        else
        {
            _workingSettings = settings.Clone();
            _profile = _workingSettings.SelectedProfile ?? _workingSettings.Profiles.First();
            if (string.Equals(_profile.Name, "Default", StringComparison.Ordinal))
            {
                _profile.Name = UiText.SetupDefaultProfileName;
            }
        }

        _logger = logger;

        Font = SystemFonts.MessageBoxFont;
        _formIcon = AppIconProvider.CreateIcon();
        Icon = _formIcon;
        _titleFont = new Font(Font.FontFamily, 18f, FontStyle.Bold, GraphicsUnit.Point);
        _sectionFont = new Font(Font.FontFamily, 12f, FontStyle.Bold, GraphicsUnit.Point);
        _emphasisFont = new Font(Font, FontStyle.Bold);

        Text = _isAddingProfile ? UiText.SetupAddProfileTitle : UiText.SetupTitle;
        StartPosition = _isAddingProfile
            ? FormStartPosition.CenterParent
            : FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = !_isAddingProfile;
        // The 96-DPI baseline is applied after the runtime-built control tree exists.
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(940, 700);
        MinimumSize = new Size(780, 580);
        BackColor = SystemColors.Window;

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = SystemColors.Window
        };
        // A fixed-width rail becomes unusably narrow when Windows enlarges only the
        // fonts (for example at 225% DPI). Keep both sides proportional instead.
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _stepRail = new SetupStepRail(
            new[]
            {
                UiText.SetupStepWelcome,
                UiText.SetupStepRouter,
                UiText.SetupStepAuthentication,
                UiText.SetupStepDevice,
                UiText.SetupStepFinish
            },
            _isAddingProfile
                ? UiText.SetupAddProfileSidebarCaption
                : UiText.SetupSidebarCaption,
            Font)
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };

        var rightLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = SystemColors.Window
        };
        rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _errorBanner = new WrappingLabel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(24, 10, 24, 10),
            BackColor = ErrorSurfaceColor,
            ForeColor = ErrorColor,
            Font = _emphasisFont,
            Visible = false,
            UseMnemonic = false
        };

        var pageHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = SystemColors.Window
        };

        _pages =
        [
            CreateWelcomePage(),
            CreateRouterPage(),
            CreateAuthenticationPage(),
            CreateDevicePage(),
            CreateFinishPage()
        ];
        foreach (var page in _pages)
        {
            pageHost.Controls.Add(page);
        }

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(24, 12, 24, 12),
            Margin = Padding.Empty,
            BackColor = SystemColors.Control
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _progressLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = SystemColors.GrayText,
            Margin = Padding.Empty
        };

        var footerButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty
        };

        _nextButton = CreatePrimaryButton(UiText.SetupNext);
        _nextButton.Click += OnNextClick;

        _backButton = CreateButton(UiText.SetupBack);
        _backButton.Click += OnBackClick;

        _cancelButton = CreateButton(
            _isAddingProfile ? UiText.SettingsCancel : UiText.SetupCancel);
        _cancelButton.Margin = new Padding(0, 0, 16, 0);
        _cancelButton.Click += OnCancelClick;

        footerButtons.Controls.Add(_nextButton);
        footerButtons.Controls.Add(_backButton);
        footerButtons.Controls.Add(_cancelButton);
        footer.Controls.Add(_progressLabel, 0, 0);
        footer.Controls.Add(footerButtons, 1, 0);

        rightLayout.Controls.Add(_errorBanner, 0, 0);
        rightLayout.Controls.Add(pageHost, 0, 1);
        rightLayout.Controls.Add(footer, 0, 2);

        shell.Controls.Add(_stepRail, 0, 0);
        shell.Controls.Add(rightLayout, 1, 0);
        Controls.Add(shell);

        AcceptButton = _nextButton;
        CancelButton = _cancelButton;

        UpdateAuthenticationMode();
        ShowStep(0);

        // Unlike a designer-generated form, this form has no generated call that
        // scales the finished control tree. Scale it once now; WinForms updates
        // AutoScaleDimensions afterwards and handles later monitor changes normally.
        AutoScaleDimensions = new SizeF(96f, 96f);
        PerformAutoScale();

        Shown += async (_, _) =>
        {
            FitToWorkingArea();
            await EnsureNetworkLoadedAsync();
        };
    }

    public AppSettings ResultSettings => _workingSettings.Clone();

    public static FirstRunSetupForm CreateForNewProfile(
        AppSettings settings,
        FileLogger logger)
    {
        return new FirstRunSetupForm(settings, logger, isAddingProfile: true);
    }

    internal static (AppSettings Settings, RouterProfile Profile) CreateNewProfileDraft(
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var draft = settings.Clone();
        var number = draft.Profiles.Count + 1;
        string name;
        do
        {
            name = UiText.SettingsNewProfileName(number++);
        }
        while (draft.Profiles.Any(profile =>
                   string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)));

        var profile = new RouterProfile { Name = name };
        draft.Profiles.Add(profile);
        draft.SelectedProfileId = profile.Id;
        return (draft, profile);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        _stepRail.Invalidate();
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(FitToWorkingArea);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_closeConfirmed && DialogResult != DialogResult.OK && e.CloseReason == CloseReason.UserClosing)
        {
            var result = MessageBox.Show(
                this,
                _isAddingProfile
                    ? UiText.SetupAddProfileCancelConfirmation
                    : UiText.SetupCancelConfirmation,
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            _closeConfirmed = true;
            DialogResult = DialogResult.Cancel;
        }

        if (!e.Cancel)
        {
            CancelDeviceOperation();
            CancelConnectionTest();
            _lifetimeCts.Cancel();
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            CancelDeviceOperation();
            CancelConnectionTest();
            _lifetimeCts.Cancel();
            _lifetimeCts.Dispose();
            _titleFont.Dispose();
            _sectionFont.Dispose();
            _emphasisFont.Dispose();
            Icon = null;
            _formIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private Panel CreateWelcomePage()
    {
        var (page, content) = CreatePage(
            _isAddingProfile
                ? UiText.SetupAddProfileWelcomeTitle
                : UiText.SetupWelcomeTitle,
            _isAddingProfile
                ? UiText.SetupAddProfileWelcomeSubtitle
                : UiText.SetupWelcomeSubtitle);

        AddPageRow(content, CreateSectionHeading(UiText.SetupWelcomeBeforeTitle), 14);
        AddPageRow(
            content,
            CreateChecklistItem(
                "1",
                UiText.SetupWelcomeRouterTitle,
                UiText.SetupWelcomeRouterDescription),
            8);
        AddPageRow(
            content,
            CreateChecklistItem(
                "2",
                UiText.SetupWelcomePoliciesTitle,
                UiText.SetupWelcomePoliciesDescription),
            8);
        AddPageRow(
            content,
            CreateChecklistItem(
                "3",
                UiText.SetupWelcomeCredentialsTitle,
                UiText.SetupWelcomeCredentialsDescription),
            12);
        AddPageRow(content, CreateCallout(UiText.SetupWelcomeSecurityNote), 0);

        return page;
    }

    private Panel CreateRouterPage()
    {
        var (page, content) = CreatePage(UiText.SetupRouterTitle, UiText.SetupRouterSubtitle);

        var profileCard = CreateCard();
        var profileLayout = CreateSingleColumnLayout();
        profileCard.Controls.Add(profileLayout);

        _profileNameTextBox = CreateTextBox();
        _profileNameTextBox.Text = _profile.Name;
        _profileNameTextBox.AccessibleName = UiText.SettingsProfileName;
        AddPageRow(profileLayout, CreateField(UiText.SettingsProfileName, _profileNameTextBox), 0);
        AddPageRow(content, profileCard, 12);

        var addressCard = CreateCard();
        var addressLayout = CreateSingleColumnLayout();
        addressCard.Controls.Add(addressLayout);

        _automaticAddressRadio = new RadioButton
        {
            Text = UiText.SetupAutomaticAddress,
            AutoSize = true,
            Font = _emphasisFont,
            Margin = Padding.Empty,
            Checked = string.IsNullOrWhiteSpace(_profile.RouterUrl)
        };
        _automaticAddressRadio.CheckedChanged += (_, _) => UpdateAddressMode();
        AddPageRow(addressLayout, _automaticAddressRadio, 4);
        AddPageRow(addressLayout, CreateIndentedHint(UiText.SetupAutomaticAddressDescription), 4);

        _detectedRouterLabel = CreateIndentedHint(UiText.SetupDetectingNetwork);
        _detectedRouterLabel.ForeColor = AccentColor;
        AddPageRow(addressLayout, _detectedRouterLabel, 12);
        AddPageRow(addressLayout, CreateSeparator(), 12);

        _manualAddressRadio = new RadioButton
        {
            Text = UiText.SetupManualAddress,
            AutoSize = true,
            Font = _emphasisFont,
            Margin = Padding.Empty,
            Checked = !string.IsNullOrWhiteSpace(_profile.RouterUrl)
        };
        _manualAddressRadio.CheckedChanged += (_, _) => UpdateAddressMode();
        AddPageRow(addressLayout, _manualAddressRadio, 4);
        AddPageRow(addressLayout, CreateIndentedHint(UiText.SetupManualAddressDescription), 6);

        _routerUrlTextBox = CreateTextBox();
        _routerUrlTextBox.Text = _profile.RouterUrl;
        _routerUrlTextBox.PlaceholderText = AppSettings.RouterUrlExample;
        _routerUrlTextBox.AccessibleName = UiText.SettingsRouterUrl;
        _routerUrlTextBox.Margin = new Padding(24, 0, 0, 0);
        AddPageRow(addressLayout, _routerUrlTextBox, 0);
        AddPageRow(content, addressCard, 12);

        var networkCard = CreateCard();
        var networkLayout = CreateSingleColumnLayout();
        networkCard.Controls.Add(networkLayout);

        _bindNetworkCheckBox = new CheckBox
        {
            Text = UiText.SetupBindCurrentNetwork,
            AutoSize = true,
            Font = _emphasisFont,
            Enabled = false,
            Margin = Padding.Empty
        };
        _bindNetworkCheckBox.CheckedChanged += (_, _) => RememberNetworkBindingChoice();
        AddPageRow(networkLayout, _bindNetworkCheckBox, 4);
        _networkStatusLabel = CreateHint(UiText.SetupDetectingNetwork);
        AddPageRow(networkLayout, _networkStatusLabel, 0);
        AddPageRow(content, networkCard, 0);

        UpdateAddressMode();
        return page;
    }

    private Panel CreateAuthenticationPage()
    {
        var (page, content) = CreatePage(
            UiText.SetupAuthenticationTitle,
            UiText.SetupAuthenticationSubtitle);

        AddPageRow(content, CreateCallout(UiText.SetupCredentialsInstruction), 12);

        var authCard = CreateCard();
        var authLayout = CreateSingleColumnLayout();
        authCard.Controls.Add(authLayout);

        _passwordModeRadio = new RadioButton
        {
            Text = UiText.SettingsAuthModePassword,
            AutoSize = true,
            Font = _emphasisFont,
            Checked = _profile.AuthMode == RouterAuthMode.Password,
            Margin = Padding.Empty
        };
        _passwordModeRadio.CheckedChanged += (_, _) => UpdateAuthenticationMode();
        AddPageRow(authLayout, _passwordModeRadio, 4);
        AddPageRow(authLayout, CreateIndentedHint(UiText.SetupPasswordMethodDescription), 8);

        _loginTextBox = CreateTextBox();
        _loginTextBox.Text = _profile.Login;
        _loginTextBox.AccessibleName = UiText.SettingsLogin;
        _passwordTextBox = CreateTextBox();
        _passwordTextBox.Text = _profile.Password;
        _passwordTextBox.UseSystemPasswordChar = true;
        _passwordTextBox.AccessibleName = UiText.SettingsPassword;

        var showPassword = new CheckBox
        {
            Text = UiText.SettingsShowPassword,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 0)
        };
        showPassword.CheckedChanged += (_, _) =>
            _passwordTextBox.UseSystemPasswordChar = !showPassword.Checked;

        _passwordFields = CreateSingleColumnLayout();
        _passwordFields.Padding = new Padding(24, 0, 0, 0);
        AddPageRow(_passwordFields, CreateField(UiText.SettingsLogin, _loginTextBox), 8);
        AddPageRow(_passwordFields, CreateField(UiText.SettingsPassword, _passwordTextBox), 0);
        AddPageRow(_passwordFields, showPassword, 12);
        AddPageRow(authLayout, _passwordFields, 0);
        AddPageRow(authLayout, CreateSeparator(), 12);

        _tokenModeRadio = new RadioButton
        {
            Text = UiText.SettingsAuthModeAccessToken,
            AutoSize = true,
            Font = _emphasisFont,
            Checked = _profile.AuthMode == RouterAuthMode.AccessToken,
            Margin = Padding.Empty
        };
        _tokenModeRadio.CheckedChanged += (_, _) => UpdateAuthenticationMode();
        AddPageRow(authLayout, _tokenModeRadio, 4);
        AddPageRow(authLayout, CreateIndentedHint(UiText.SetupTokenMethodDescription), 8);

        _accessTokenTextBox = CreateTextBox();
        _accessTokenTextBox.Text = _profile.AccessToken;
        _accessTokenTextBox.UseSystemPasswordChar = true;
        _accessTokenTextBox.AccessibleName = UiText.SettingsAccessToken;
        var showToken = new CheckBox
        {
            Text = UiText.SettingsShowAccessToken,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 0)
        };
        showToken.CheckedChanged += (_, _) =>
            _accessTokenTextBox.UseSystemPasswordChar = !showToken.Checked;

        _tokenFields = CreateSingleColumnLayout();
        _tokenFields.Padding = new Padding(24, 0, 0, 0);
        AddPageRow(_tokenFields, CreateField(UiText.SettingsAccessToken, _accessTokenTextBox), 0);
        AddPageRow(_tokenFields, showToken, 12);
        AddPageRow(authLayout, _tokenFields, 0);
        AddPageRow(content, authCard, 10);

        var openRouterLink = new LinkLabel
        {
            Text = UiText.SetupOpenRouter,
            AutoSize = true,
            Margin = Padding.Empty,
            LinkColor = AccentColor,
            ActiveLinkColor = AccentColor
        };
        openRouterLink.LinkClicked += OnOpenRouterLinkClicked;
        AddPageRow(content, openRouterLink, 0);

        return page;
    }

    private Panel CreateDevicePage()
    {
        var (page, content) = CreatePage(UiText.SetupDeviceTitle, UiText.SetupDeviceSubtitle);

        var connectionCard = CreateCard();
        var connectionLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 3,
            Margin = Padding.Empty,
            BackColor = SystemColors.Window
        };
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
        connectionLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        connectionLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        connectionLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var connectionHeading = CreateSectionHeading(UiText.SetupDeviceCurrentTitle);
        connectionLayout.Controls.Add(connectionHeading, 0, 0);
        connectionLayout.SetColumnSpan(connectionHeading, 2);

        _deviceAdapterValue = CreateDeviceValue();
        _deviceMacValue = CreateDeviceValue();
        AddDeviceDetailRow(
            connectionLayout,
            1,
            UiText.SetupDeviceAdapter,
            _deviceAdapterValue);
        AddDeviceDetailRow(
            connectionLayout,
            2,
            UiText.SetupDeviceMacAddress,
            _deviceMacValue);
        connectionCard.Controls.Add(connectionLayout);
        AddPageRow(content, connectionCard, 12);

        _temporaryMacPanel = new BorderedPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14),
            Margin = Padding.Empty,
            BackColor = WarningSurfaceColor,
            BorderColor = WarningColor,
            Visible = false
        };
        var warningLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 4,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        warningLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        warningLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        warningLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        warningLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        warningLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var warningMark = new Label
        {
            Text = "⚠",
            AutoSize = true,
            Font = _emphasisFont,
            ForeColor = WarningColor,
            Margin = Padding.Empty,
            AccessibleName = string.Empty
        };
        var warningText = CreateHint(UiText.SetupTemporaryMacWarning);
        warningText.WidthOffset = 30;
        warningText.ForeColor = WarningColor;
        _temporaryMacAcknowledgeCheckBox = new CheckBox
        {
            Text = UiText.SetupTemporaryMacAcknowledge,
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 6),
            ForeColor = WarningColor
        };
        _temporaryMacAcknowledgeCheckBox.CheckedChanged += (_, _) =>
        {
            HideError();
            UpdateNavigationState();
        };
        var openWifiSettingsLink = new LinkLabel
        {
            Text = UiText.SetupOpenWifiSettings,
            AutoSize = true,
            Margin = Padding.Empty,
            LinkColor = AccentColor,
            ActiveLinkColor = AccentColor
        };
        openWifiSettingsLink.LinkClicked += OnOpenWifiSettingsLinkClicked;
        warningLayout.Controls.Add(warningMark, 0, 0);
        warningLayout.SetRowSpan(warningMark, 3);
        warningLayout.Controls.Add(warningText, 1, 0);
        warningLayout.Controls.Add(_temporaryMacAcknowledgeCheckBox, 1, 1);
        warningLayout.Controls.Add(openWifiSettingsLink, 1, 2);
        _temporaryMacPanel.Controls.Add(warningLayout);
        AddPageRow(content, _temporaryMacPanel, 12);

        var registrationCard = CreateCard();
        var registrationLayout = CreateSingleColumnLayout();
        registrationCard.Controls.Add(registrationLayout);
        AddPageRow(
            registrationLayout,
            CreateSectionHeading(UiText.SetupDeviceRegistrationTitle),
            4);
        AddPageRow(
            registrationLayout,
            CreateHint(UiText.SetupDeviceRegistrationDescription),
            10);

        _deviceNameTextBox = CreateTextBox();
        _deviceNameTextBox.Text = GetDefaultDeviceName();
        _deviceNameTextBox.AccessibleName = UiText.SetupDeviceName;
        _deviceNameTextBox.Enabled = false;
        AddPageRow(
            registrationLayout,
            CreateField(UiText.SetupDeviceName, _deviceNameTextBox),
            10);

        _deviceStatusPanel = new BorderedPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
            Margin = Padding.Empty,
            BackColor = NeutralSurfaceColor,
            BorderColor = SystemColors.ControlLight
        };
        var deviceStatusLayout = CreateSingleColumnLayout();
        deviceStatusLayout.BackColor = Color.Transparent;
        _deviceStatusLabel = CreateHint(UiText.SetupDeviceNotChecked);
        _deviceStatusLabel.ForeColor = SystemColors.WindowText;
        _deviceProgressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 4,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 24,
            Margin = new Padding(0, 8, 0, 0),
            Visible = false
        };
        AddPageRow(deviceStatusLayout, _deviceStatusLabel, 0);
        AddPageRow(deviceStatusLayout, _deviceProgressBar, 0);
        _deviceStatusPanel.Controls.Add(deviceStatusLayout);
        AddPageRow(registrationLayout, _deviceStatusPanel, 10);

        var deviceButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty
        };
        _registerDeviceButton = CreatePrimaryButton(UiText.SetupRegisterDevice);
        _registerDeviceButton.Margin = Padding.Empty;
        _registerDeviceButton.Enabled = false;
        _registerDeviceButton.Click += async (_, _) => await RegisterDeviceAsync();
        _recheckDeviceButton = CreateButton(UiText.SetupRecheckDevice);
        _recheckDeviceButton.Click += async (_, _) =>
            await CheckDeviceRegistrationAsync(refreshNetwork: true);
        deviceButtons.Controls.Add(_registerDeviceButton);
        deviceButtons.Controls.Add(_recheckDeviceButton);
        AddPageRow(registrationLayout, deviceButtons, 0);
        AddPageRow(content, registrationCard, 0);

        return page;
    }

    private Panel CreateFinishPage()
    {
        var (page, content) = CreatePage(UiText.SetupFinishTitle, UiText.SetupFinishSubtitle);

        var summaryCard = CreateCard();
        var summaryLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 4,
            Margin = Padding.Empty,
            BackColor = SystemColors.Window
        };
        summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var index = 0; index < 4; index++)
        {
            summaryLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        _summaryRouterValue = CreateSummaryValue();
        _summaryAuthenticationValue = CreateSummaryValue();
        _summaryNetworkValue = CreateSummaryValue();
        _summaryDeviceValue = CreateSummaryValue();
        AddSummaryRow(summaryLayout, 0, UiText.SetupSummaryRouter, _summaryRouterValue);
        AddSummaryRow(summaryLayout, 1, UiText.SetupSummaryAuthentication, _summaryAuthenticationValue);
        AddSummaryRow(summaryLayout, 2, UiText.SetupSummaryNetwork, _summaryNetworkValue);
        AddSummaryRow(summaryLayout, 3, UiText.SetupSummaryDevice, _summaryDeviceValue);
        summaryCard.Controls.Add(summaryLayout);
        AddPageRow(content, summaryCard, 12);

        var testCard = CreateCard();
        var testLayout = CreateSingleColumnLayout();
        testCard.Controls.Add(testLayout);
        AddPageRow(testLayout, CreateSectionHeading(UiText.SetupConnectionCheckTitle), 4);
        AddPageRow(testLayout, CreateHint(UiText.SetupConnectionCheckDescription), 10);

        _testStatusPanel = new BorderedPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
            Margin = Padding.Empty,
            BackColor = NeutralSurfaceColor,
            BorderColor = SystemColors.ControlLight
        };
        var statusLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _testStatusLabel = CreateHint(UiText.SetupConnectionNotChecked);
        _testStatusLabel.ForeColor = SystemColors.WindowText;
        _testProgressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 4,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 24,
            Margin = new Padding(0, 8, 0, 0),
            Visible = false
        };
        statusLayout.Controls.Add(_testStatusLabel, 0, 0);
        statusLayout.Controls.Add(_testProgressBar, 0, 1);
        _testStatusPanel.Controls.Add(statusLayout);
        AddPageRow(testLayout, _testStatusPanel, 10);

        _testConnectionButton = CreateButton(UiText.SetupTestConnection);
        _testConnectionButton.Anchor = AnchorStyles.Left;
        _testConnectionButton.Click += async (_, _) => await TestConnectionAsync();
        AddPageRow(testLayout, _testConnectionButton, 0);
        AddPageRow(content, testCard, 12);

        if (!_isAddingProfile)
        {
            var preferencesCard = CreateCard();
            var preferencesLayout = CreateSingleColumnLayout();
            preferencesCard.Controls.Add(preferencesLayout);
            _autoStartCheckBox = new CheckBox
            {
                Text = UiText.SettingsAutoStart,
                Checked = _workingSettings.AutoStart,
                AutoSize = true,
                Margin = Padding.Empty
            };
            _notifyPolicyCheckBox = new CheckBox
            {
                Text = UiText.SettingsShowPolicyNotifications,
                Checked = _workingSettings.ShowPolicyNotifications,
                AutoSize = true,
                Margin = Padding.Empty
            };
            AddPageRow(preferencesLayout, _autoStartCheckBox, 8);
            AddPageRow(preferencesLayout, _notifyPolicyCheckBox, 0);
            AddPageRow(content, preferencesCard, 12);
            AddPageRow(content, CreateCallout(UiText.SetupTrayHint), 0);
        }

        return page;
    }

    private async void OnNextClick(object? sender, EventArgs e)
    {
        if (_currentStep == FinishStep)
        {
            CancelDeviceOperation();
            CancelConnectionTest();
            ApplyInputsToSettings();
            _closeConfirmed = true;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        _nextButton.Enabled = false;
        try
        {
            if (!await ValidateStepAsync(_currentStep))
            {
                return;
            }

            if (_currentStep == AuthenticationStep)
            {
                ApplyInputsToSettings();
            }

            ShowStep(_currentStep + 1);
            if (_currentStep == DeviceStep)
            {
                await CheckDeviceRegistrationAsync(refreshNetwork: true);
            }
            else if (_currentStep == FinishStep)
            {
                UpdateReview();
                if (_lastTestSignature != GetConnectionSignature())
                {
                    _ = TestConnectionAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to advance the router setup wizard.", ex);
            ShowError(UiText.UnexpectedErrorMessage);
        }
        finally
        {
            if (!_disposed && !_nextButton.IsDisposed)
            {
                UpdateNavigationState();
            }
        }
    }

    private void OnBackClick(object? sender, EventArgs e)
    {
        if (_currentStep <= 0)
        {
            return;
        }

        CancelDeviceOperation();
        CancelConnectionTest();
        ShowStep(_currentStep - 1);
    }

    private void OnCancelClick(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            this,
            _isAddingProfile
                ? UiText.SetupAddProfileCancelConfirmation
                : UiText.SetupCancelConfirmation,
            Text,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (result != DialogResult.Yes)
        {
            return;
        }

        _closeConfirmed = true;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private async void OnOpenRouterLinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        try
        {
            await EnsureNetworkLoadedAsync();
            var endpoint = ResolveEndpointFromInputs();
            if (endpoint is null)
            {
                ShowError(UiText.SetupConnectionNoEndpoint);
                return;
            }

            Process.Start(new ProcessStartInfo(endpoint.GetLeftPart(UriPartial.Authority))
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Error("Failed to open the router web interface.", ex);
            ShowError(UiText.SetupConnectionUnreachable);
        }
    }

    private void OnOpenWifiSettingsLinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:network-wifi")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or
                                   System.ComponentModel.Win32Exception)
        {
            _logger.Error("Failed to open Windows Wi-Fi settings.", ex);
            ShowError(UiText.UnexpectedErrorMessage);
        }
    }

    private async Task<bool> ValidateStepAsync(int step)
    {
        HideError();

        if (step == 1)
        {
            if (string.IsNullOrWhiteSpace(_profileNameTextBox.Text))
            {
                ShowError(UiText.SettingsProfileNameValidationMessage);
                _profileNameTextBox.Focus();
                return false;
            }

            if (IsProfileNameDuplicate(
                    _workingSettings.Profiles,
                    _profile,
                    _profileNameTextBox.Text))
            {
                ShowError(UiText.SettingsProfileDuplicateNameMessage);
                _profileNameTextBox.Focus();
                _profileNameTextBox.SelectAll();
                return false;
            }

            Uri? configuredRouterUri = null;
            if (_manualAddressRadio.Checked)
            {
                try
                {
                    _routerUrlTextBox.Text = RouterEndpoint.NormalizeConfiguredUrl(
                        _routerUrlTextBox.Text);
                    if (string.IsNullOrWhiteSpace(_routerUrlTextBox.Text))
                    {
                        throw new InvalidOperationException();
                    }

                    configuredRouterUri = RouterEndpoint.GetConfiguredUri(
                        _routerUrlTextBox.Text);
                }
                catch (InvalidOperationException)
                {
                    ShowError(UiText.SettingsRouterUrlValidationMessage);
                    _routerUrlTextBox.Focus();
                    return false;
                }
            }

            await ReloadNetworkAsync(configuredRouterUri);
            ResolveNetworkBindingConflict();
        }

        if (step == AuthenticationStep)
        {
            if (_passwordModeRadio.Checked &&
                (string.IsNullOrWhiteSpace(_loginTextBox.Text) ||
                 string.IsNullOrWhiteSpace(_passwordTextBox.Text)))
            {
                ShowError(UiText.SettingsValidationMessage);
                (string.IsNullOrWhiteSpace(_loginTextBox.Text)
                    ? _loginTextBox
                    : _passwordTextBox).Focus();
                return false;
            }

            if (_tokenModeRadio.Checked && string.IsNullOrWhiteSpace(_accessTokenTextBox.Text))
            {
                ShowError(UiText.SettingsAccessTokenValidationMessage);
                _accessTokenTextBox.Focus();
                return false;
            }
        }

        if (step == DeviceStep)
        {
            if (!_deviceRegistered || _checkedDeviceSignature != GetDeviceSignature())
            {
                ShowError(UiText.SetupDeviceRegistrationRequired);
                return false;
            }

            if (_temporaryMacDetected && !_temporaryMacAcknowledgeCheckBox.Checked)
            {
                ShowError(UiText.SetupTemporaryMacConfirmationRequired);
                _temporaryMacAcknowledgeCheckBox.Focus();
                return false;
            }
        }

        return true;
    }

    private void ApplyInputsToSettings()
    {
        string? completedNetworkMoveId = null;
        _profile.Name = _profileNameTextBox.Text.Trim();
        _profile.RouterUrl = _automaticAddressRadio.Checked
            ? string.Empty
            : RouterEndpoint.NormalizeConfiguredUrl(_routerUrlTextBox.Text);

        if (_passwordModeRadio.Checked)
        {
            _profile.AuthMode = RouterAuthMode.Password;
            _profile.Login = _loginTextBox.Text.Trim();
            _profile.Password = _passwordTextBox.Text;
            _profile.AccessToken = string.Empty;
        }
        else
        {
            _profile.AuthMode = RouterAuthMode.AccessToken;
            _profile.Login = string.Empty;
            _profile.Password = string.Empty;
            _profile.AccessToken = _accessTokenTextBox.Text.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_snapshot?.ActiveNetworkId))
        {
            var activeNetworkId = _snapshot.ActiveNetworkId;
            _profile.Networks.RemoveAll(binding =>
                string.Equals(
                    binding.NetworkId,
                    activeNetworkId,
                    StringComparison.OrdinalIgnoreCase));
            if (_bindNetworkCheckBox.Checked)
            {
                _profile.Networks.Add(new RouterNetworkBinding
                {
                    NetworkId = activeNetworkId,
                    NetworkName = _snapshot.ActiveNetworkName?.Trim() ?? string.Empty
                });
                completedNetworkMoveId = activeNetworkId;
            }
            else
            {
                RestoreDisplacedNetworkBinding(activeNetworkId);
            }
        }

        if (!_isAddingProfile && _bindNetworkCheckBox.Enabled)
        {
            _workingSettings.AutomaticProfileSelection = _bindNetworkCheckBox.Checked;
        }
        _workingSettings.SelectedProfileId = _profile.Id;
        if (!_isAddingProfile)
        {
            _workingSettings.AutoStart = _autoStartCheckBox.Checked;
            _workingSettings.ShowPolicyNotifications = _notifyPolicyCheckBox.Checked;
        }
        _workingSettings.NormalizeAndValidate();

        if (_pendingNetworkBindingMove?.IsForNetwork(completedNetworkMoveId) == true)
        {
            _pendingNetworkBindingMove = null;
        }
    }

    private void ResolveNetworkBindingConflict()
    {
        if (!_isAddingProfile ||
            !_bindNetworkCheckBox.Checked ||
            string.IsNullOrWhiteSpace(_snapshot?.ActiveNetworkId))
        {
            return;
        }

        var otherProfile = _workingSettings.Profiles.FirstOrDefault(profile =>
            !ReferenceEquals(profile, _profile) &&
            profile.IsBoundTo(_snapshot.ActiveNetworkId));
        if (otherProfile is null)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            UiText.SettingsMoveNetworkConfirmation(otherProfile.Name),
            Text,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (answer == DialogResult.Yes)
        {
            var binding = otherProfile.Networks.First(binding =>
                string.Equals(
                    binding.NetworkId,
                    _snapshot.ActiveNetworkId,
                    StringComparison.OrdinalIgnoreCase));
            _pendingNetworkBindingMove = new PendingNetworkBindingMove(
                otherProfile.Id,
                binding);
            otherProfile.Networks.RemoveAll(binding =>
                string.Equals(
                    binding.NetworkId,
                    _snapshot.ActiveNetworkId,
                    StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            _bindNetworkCheckBox.Checked = false;
        }
    }

    private void RestoreDisplacedNetworkBinding(string networkId)
    {
        if (_pendingNetworkBindingMove is not { } pendingMove ||
            !pendingMove.IsForNetwork(networkId))
        {
            return;
        }

        pendingMove.Restore(_workingSettings);
        _pendingNetworkBindingMove = null;
    }

    private void ShowStep(int step)
    {
        _currentStep = Math.Clamp(step, 0, StepCount - 1);
        for (var index = 0; index < _pages.Length; index++)
        {
            _pages[index].Visible = index == _currentStep;
            if (index == _currentStep)
            {
                _pages[index].BringToFront();
            }
        }

        _stepRail.CurrentStep = _currentStep;
        _progressLabel.Text = UiText.SetupProgress(_currentStep + 1, StepCount);
        UpdateNavigationState();
        HideError();
    }

    private void UpdateNavigationState()
    {
        if (_disposed || _nextButton.IsDisposed)
        {
            return;
        }

        _backButton.Enabled = _currentStep > 0 && !_deviceOperationInProgress;
        _nextButton.Text = _currentStep == FinishStep
            ? _isAddingProfile
                ? UiText.SetupAddProfileFinish
                : UiText.SetupFinish
            : UiText.SetupNext;
        var nextEnabled = !_deviceOperationInProgress &&
                          (_currentStep != DeviceStep ||
                           (_deviceRegistered &&
                            _checkedDeviceSignature == GetDeviceSignature() &&
                            (!_temporaryMacDetected ||
                             _temporaryMacAcknowledgeCheckBox.Checked)));
        SetPrimaryButtonState(_nextButton, nextEnabled);
    }

    private async Task EnsureNetworkLoadedAsync()
    {
        _networkLoadTask ??= LoadNetworkAsync(GetNetworkLookupUriFromInputs());
        await _networkLoadTask;
    }

    private async Task ReloadNetworkAsync(Uri? configuredRouterUri)
    {
        if (_networkLoadTask is not null)
        {
            await _networkLoadTask;
        }

        _networkLoadTask = LoadNetworkAsync(configuredRouterUri);
        await _networkLoadTask;
    }

    private async Task LoadNetworkAsync(Uri? configuredRouterUri)
    {
        try
        {
            _snapshot = await _interfaceService.GetSnapshotAsync(
                configuredRouterUri: configuredRouterUri,
                ct: _lifetimeCts.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _snapshot = null;
            _logger.Error("Failed to identify the network during first-run setup.", ex);
        }

        if (_disposed || IsDisposed)
        {
            return;
        }

        UpdateDetectedNetwork();
    }

    private void UpdateDetectedNetwork()
    {
        RestorePendingNetworkBindingIfNetworkChanged(_snapshot?.ActiveNetworkId);

        if (!string.IsNullOrWhiteSpace(_snapshot?.ActiveGateway))
        {
            var endpoint = RouterEndpoint.CreateGatewayUri(_snapshot.ActiveGateway);
            _detectedRouterLabel.Text = UiText.SetupRouterDetected(endpoint.AbsoluteUri);
            _detectedRouterLabel.ForeColor = SuccessColor;
        }
        else
        {
            _detectedRouterLabel.Text = UiText.SetupRouterNotDetected;
            _detectedRouterLabel.ForeColor = WarningColor;
        }

        if (!string.IsNullOrWhiteSpace(_snapshot?.ActiveNetworkId))
        {
            var networkName = string.IsNullOrWhiteSpace(_snapshot.ActiveNetworkName)
                ? _snapshot.ActiveNetworkId
                : _snapshot.ActiveNetworkName;
            _networkStatusLabel.Text = UiText.SetupCurrentNetwork(networkName!);
            _networkStatusLabel.ForeColor = SystemColors.WindowText;
            _bindNetworkCheckBox.Enabled = true;
            var boundToAnotherProfile = _workingSettings.Profiles.Any(profile =>
                !ReferenceEquals(profile, _profile) &&
                profile.IsBoundTo(_snapshot.ActiveNetworkId));
            var hasRememberedChoice = _networkBindingChoices.TryGetValue(
                _snapshot.ActiveNetworkId,
                out var rememberedChoice);
            var shouldBind = ResolveNetworkBindingChoice(
                hasRememberedChoice ? rememberedChoice : null,
                _profile.IsBoundTo(_snapshot.ActiveNetworkId),
                _profile.Networks.Count == 0,
                _isAddingProfile,
                boundToAnotherProfile);
            SetNetworkBindingChoice(_snapshot.ActiveNetworkId, shouldBind);
        }
        else
        {
            _networkStatusLabel.Text = UiText.SetupNetworkUnavailable;
            _networkStatusLabel.ForeColor = WarningColor;
            SetNetworkBindingCheckState(false);
            _bindNetworkCheckBox.Enabled = false;
        }

        UpdateDeviceIdentity();
    }

    private void RestorePendingNetworkBindingIfNetworkChanged(string? activeNetworkId)
    {
        if (_pendingNetworkBindingMove is not { } pendingMove ||
            !pendingMove.TryRestoreAfterNetworkChange(
                _workingSettings,
                _profile,
                activeNetworkId))
        {
            return;
        }

        _pendingNetworkBindingMove = null;
    }

    private void UpdateDeviceIdentity()
    {
        if (_deviceAdapterValue is null || _deviceMacValue is null)
        {
            return;
        }

        var activeInterface = _snapshot?.Interfaces.FirstOrDefault(info =>
            string.Equals(
                info.Id,
                _snapshot.ActiveInterfaceId,
                StringComparison.OrdinalIgnoreCase));
        _deviceAdapterValue.Text = activeInterface is null
            ? UiText.SetupDeviceUnavailable
            : string.IsNullOrWhiteSpace(activeInterface.Description) ||
              string.Equals(
                  activeInterface.Name,
                  activeInterface.Description,
                  StringComparison.OrdinalIgnoreCase)
                ? activeInterface.Name
                : $"{activeInterface.Name} — {activeInterface.Description}";

        var hasMac = MacAddressInspector.TryNormalize(_snapshot?.ActiveMac, out var normalizedMac);
        _deviceMacValue.Text = hasMac ? normalizedMac : UiText.SetupDeviceUnavailable;
        if (!string.Equals(_displayedDeviceMac, normalizedMac, StringComparison.OrdinalIgnoreCase))
        {
            _displayedDeviceMac = normalizedMac;
            _temporaryMacAcknowledgeCheckBox.Checked = false;
            _deviceRegistered = false;
            _registeredDeviceName = string.Empty;
            _checkedDeviceSignature = null;
        }

        _temporaryMacDetected = hasMac && MacAddressInspector.IsLocallyAdministered(normalizedMac);
        _temporaryMacPanel.Visible = _temporaryMacDetected;
        UpdateDeviceControls();
    }

    private async Task CheckDeviceRegistrationAsync(bool refreshNetwork)
    {
        CancelDeviceOperation();
        using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _deviceCts = operationCts;
        _deviceOperationInProgress = true;
        _deviceRegistered = false;
        _registeredDeviceName = string.Empty;
        _checkedDeviceSignature = null;
        SetDeviceStatus(UiText.SetupCheckingDevice, TestStatus.Progress);
        UpdateDeviceControls();

        try
        {
            if (refreshNetwork)
            {
                await ReloadNetworkAsync(GetNetworkLookupUriFromInputs());
            }
            else
            {
                await EnsureNetworkLoadedAsync();
            }

            operationCts.Token.ThrowIfCancellationRequested();
            UpdateDeviceIdentity();
            ApplyInputsToSettings();

            if (!MacAddressInspector.TryNormalize(_snapshot?.ActiveMac, out var normalizedMac))
            {
                SetDeviceStatus(UiText.SetupDeviceNoMac, TestStatus.Error);
                return;
            }

            var endpoint = ResolveEndpointFromSettings();
            if (endpoint is null)
            {
                SetDeviceStatus(UiText.SetupConnectionNoEndpoint, TestStatus.Error);
                return;
            }

            var signature = GetDeviceSignature();
            using var client = CreateRouterClient(endpoint);
            var knownHost = await client.GetKnownHostAsync(normalizedMac, operationCts.Token);
            operationCts.Token.ThrowIfCancellationRequested();

            _checkedDeviceSignature = signature;
            _deviceRegistered = knownHost is not null;
            _registeredDeviceName = knownHost?.Name?.Trim() ?? string.Empty;
            if (_deviceRegistered)
            {
                SetDeviceStatus(
                    string.IsNullOrWhiteSpace(_registeredDeviceName)
                        ? UiText.SetupDeviceRegisteredNoName
                        : UiText.SetupDeviceRegistered(_registeredDeviceName),
                    TestStatus.Success);
                _logger.Info("First-run device check found a registered device.");
            }
            else
            {
                SetDeviceStatus(UiText.SetupDeviceNotRegistered, TestStatus.Warning);
                _logger.Info("First-run device check found an unregistered device.");
            }
        }
        catch (OperationCanceledException) when (operationCts.IsCancellationRequested)
        {
        }
        catch (KeeneticAuthException ex)
        {
            ReportDeviceFailure(UiText.SetupConnectionAuthFailed, ex);
        }
        catch (OperationCanceledException ex)
        {
            ReportDeviceFailure(UiText.SetupConnectionTimeout, ex);
        }
        catch (HttpRequestException ex)
        {
            ReportDeviceFailure(UiText.SetupConnectionUnreachable, ex);
        }
        catch (KeeneticRequestException ex)
        {
            ReportDeviceFailure(UiText.SetupDeviceRegistrationFailed, ex);
        }
        catch (Exception ex)
        {
            ReportDeviceFailure(UiText.UnexpectedErrorMessage, ex);
        }
        finally
        {
            if (ReferenceEquals(_deviceCts, operationCts))
            {
                _deviceCts = null;
                _deviceOperationInProgress = false;
                UpdateDeviceControls();
            }
        }
    }

    private async Task RegisterDeviceAsync()
    {
        HideError();
        var deviceName = _deviceNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            ShowError(UiText.SetupDeviceNameRequired);
            _deviceNameTextBox.Focus();
            return;
        }

        if (_checkedDeviceSignature != GetDeviceSignature() ||
            !MacAddressInspector.TryNormalize(_snapshot?.ActiveMac, out var normalizedMac))
        {
            await CheckDeviceRegistrationAsync(refreshNetwork: true);
            return;
        }

        var endpoint = ResolveEndpointFromSettings();
        if (endpoint is null)
        {
            SetDeviceStatus(UiText.SetupConnectionNoEndpoint, TestStatus.Error);
            return;
        }

        CancelDeviceOperation();
        using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _deviceCts = operationCts;
        _deviceOperationInProgress = true;
        SetDeviceStatus(UiText.SetupRegisteringDevice, TestStatus.Progress);
        UpdateDeviceControls();

        try
        {
            using var client = CreateRouterClient(endpoint);
            var knownHost = await client.RegisterKnownHostAsync(
                normalizedMac,
                deviceName,
                operationCts.Token);
            operationCts.Token.ThrowIfCancellationRequested();

            _deviceRegistered = true;
            _registeredDeviceName = string.IsNullOrWhiteSpace(knownHost.Name)
                ? deviceName
                : knownHost.Name.Trim();
            _checkedDeviceSignature = GetDeviceSignature();
            _deviceNameTextBox.Text = _registeredDeviceName;
            SetDeviceStatus(
                UiText.SetupDeviceRegistered(_registeredDeviceName),
                TestStatus.Success);
            _logger.Info("Registered the first-run device on the router.");
        }
        catch (OperationCanceledException) when (operationCts.IsCancellationRequested)
        {
        }
        catch (KeeneticAuthException ex)
        {
            ReportDeviceFailure(UiText.SetupConnectionAuthFailed, ex);
        }
        catch (OperationCanceledException ex)
        {
            ReportDeviceFailure(UiText.SetupConnectionTimeout, ex);
        }
        catch (HttpRequestException ex)
        {
            ReportDeviceFailure(UiText.SetupConnectionUnreachable, ex);
        }
        catch (KeeneticRequestException ex)
        {
            ReportDeviceFailure(UiText.SetupDeviceRegistrationFailed, ex);
        }
        catch (Exception ex)
        {
            ReportDeviceFailure(UiText.UnexpectedErrorMessage, ex);
        }
        finally
        {
            if (ReferenceEquals(_deviceCts, operationCts))
            {
                _deviceCts = null;
                _deviceOperationInProgress = false;
                UpdateDeviceControls();
            }
        }
    }

    private KeeneticClient CreateRouterClient(Uri endpoint)
    {
        return new KeeneticClient(
            endpoint,
            _profile.AuthMode,
            _profile.Login,
            _profile.Password,
            _profile.AccessToken);
    }

    private void ReportDeviceFailure(string message, Exception exception)
    {
        _deviceRegistered = false;
        _registeredDeviceName = string.Empty;
        _checkedDeviceSignature = null;
        _logger.Error("First-run device registration check failed.", exception);
        SetDeviceStatus(message, TestStatus.Error);
    }

    private void UpdateDeviceControls()
    {
        if (_registerDeviceButton is null || _registerDeviceButton.IsDisposed)
        {
            return;
        }

        var canRegister = !_deviceOperationInProgress &&
                          !_deviceRegistered &&
                          _checkedDeviceSignature == GetDeviceSignature() &&
                          MacAddressInspector.TryNormalize(_snapshot?.ActiveMac, out _);
        _deviceNameTextBox.Enabled = canRegister;
        SetPrimaryButtonState(_registerDeviceButton, canRegister);
        _recheckDeviceButton.Enabled = !_deviceOperationInProgress;
        UpdateNavigationState();
    }

    private void SetDeviceStatus(string text, TestStatus status)
    {
        if (_disposed || _deviceStatusPanel.IsDisposed)
        {
            return;
        }

        _deviceStatusLabel.Text = text;
        _deviceProgressBar.Visible = status == TestStatus.Progress;
        (_deviceStatusPanel.BackColor, _deviceStatusPanel.BorderColor, _deviceStatusLabel.ForeColor) =
            GetStatusColors(status);
        _deviceStatusPanel.Invalidate();
    }

    private void CancelDeviceOperation()
    {
        try
        {
            _deviceCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void UpdateAddressMode()
    {
        if (_routerUrlTextBox is null)
        {
            return;
        }

        _routerUrlTextBox.Enabled = _manualAddressRadio.Checked;
        _detectedRouterLabel.Enabled = _automaticAddressRadio.Checked;
    }

    private void UpdateAuthenticationMode()
    {
        if (_passwordFields is null || _tokenFields is null)
        {
            return;
        }

        SetControlTreeEnabled(_passwordFields, _passwordModeRadio.Checked);
        SetControlTreeEnabled(_tokenFields, _tokenModeRadio.Checked);
    }

    private void UpdateReview()
    {
        var endpoint = ResolveEndpointFromSettings();
        _summaryRouterValue.Text = string.IsNullOrWhiteSpace(_profile.RouterUrl)
            ? endpoint is null
                ? UiText.SetupAutomaticAddressUnavailable
                : UiText.SetupAutomaticAddressSummary(endpoint.AbsoluteUri)
            : _profile.RouterUrl;
        _summaryAuthenticationValue.Text = _profile.AuthMode == RouterAuthMode.Password
            ? $"{UiText.SettingsAuthModePassword} — {_profile.Login}"
            : UiText.SettingsAuthModeAccessToken;
        _summaryNetworkValue.Text =
            !string.IsNullOrWhiteSpace(_snapshot?.ActiveNetworkId) &&
            _profile.IsBoundTo(_snapshot.ActiveNetworkId)
                ? UiText.SetupCurrentNetwork(
                    string.IsNullOrWhiteSpace(_snapshot.ActiveNetworkName)
                        ? _snapshot.ActiveNetworkId
                        : _snapshot.ActiveNetworkName!)
                : UiText.SetupNetworkNotBound;
        _summaryDeviceValue.Text = string.IsNullOrWhiteSpace(_registeredDeviceName)
            ? _deviceMacValue.Text
            : $"{_registeredDeviceName} — {_deviceMacValue.Text}";

        var signature = GetConnectionSignature();
        if (_lastTestSignature != signature)
        {
            SetTestStatus(UiText.SetupConnectionNotChecked, TestStatus.Neutral);
        }
    }

    private async Task TestConnectionAsync()
    {
        CancelConnectionTest();

        try
        {
            ApplyInputsToSettings();
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException)
        {
            _logger.Error("First-run connection test has invalid settings.", ex);
            SetTestStatus(UiText.SettingsRouterUrlValidationMessage, TestStatus.Error);
            return;
        }

        UpdateReview();
        var signature = GetConnectionSignature();
        var endpoint = ResolveEndpointFromSettings();
        if (endpoint is null)
        {
            _lastTestSignature = signature;
            SetTestStatus(UiText.SetupConnectionNoEndpoint, TestStatus.Error);
            return;
        }

        if (!MacAddressInspector.TryNormalize(_snapshot?.ActiveMac, out var normalizedMac))
        {
            _lastTestSignature = signature;
            SetTestStatus(UiText.SetupDeviceNoMac, TestStatus.Error);
            return;
        }

        using var testCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _testCts = testCts;
        _testConnectionButton.Enabled = false;
        SetTestStatus(UiText.SetupTestingConnection, TestStatus.Progress);

        try
        {
            using var client = CreateRouterClient(endpoint);
            var knownHost = await client.GetKnownHostAsync(normalizedMac, testCts.Token);
            if (knownHost is null)
            {
                _lastTestSignature = signature;
                SetTestStatus(UiText.SetupConnectionDeviceNotRegistered, TestStatus.Error);
                return;
            }

            var policies = await client.GetPoliciesAsync(testCts.Token);
            if (testCts.IsCancellationRequested)
            {
                return;
            }

            _lastTestSignature = signature;
            SetTestStatus(
                policies.Count == 0
                    ? UiText.SetupConnectionSuccessNoPolicies
                    : UiText.SetupConnectionSuccess(policies.Count),
                policies.Count == 0 ? TestStatus.Warning : TestStatus.Success);
            _logger.Info("First-run router connection test succeeded.");
        }
        catch (OperationCanceledException) when (testCts.IsCancellationRequested)
        {
        }
        catch (KeeneticAuthException ex)
        {
            ReportTestFailure(signature, UiText.SetupConnectionAuthFailed, ex);
        }
        catch (OperationCanceledException ex)
        {
            ReportTestFailure(signature, UiText.SetupConnectionTimeout, ex);
        }
        catch (HttpRequestException ex)
        {
            ReportTestFailure(signature, UiText.SetupConnectionUnreachable, ex);
        }
        catch (KeeneticRequestException ex)
        {
            ReportTestFailure(signature, UiText.SetupConnectionApiFailed, ex);
        }
        catch (Exception ex)
        {
            ReportTestFailure(signature, UiText.UnexpectedErrorMessage, ex);
        }
        finally
        {
            if (ReferenceEquals(_testCts, testCts))
            {
                _testCts = null;
                if (!_disposed && !_testConnectionButton.IsDisposed)
                {
                    _testConnectionButton.Enabled = true;
                }
            }
        }
    }

    private void ReportTestFailure(int signature, string message, Exception exception)
    {
        _lastTestSignature = signature;
        _logger.Error("First-run router connection test failed.", exception);
        SetTestStatus(message, TestStatus.Error);
    }

    private void CancelConnectionTest()
    {
        try
        {
            _testCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private Uri? ResolveEndpointFromInputs()
    {
        var configuredUrl = _automaticAddressRadio.Checked
            ? string.Empty
            : _routerUrlTextBox.Text;
        return RouterEndpoint.Resolve(configuredUrl, _snapshot?.ActiveGateway);
    }

    private Uri? GetNetworkLookupUriFromInputs()
    {
        return ResolveNetworkLookupUri(
            _automaticAddressRadio.Checked,
            _routerUrlTextBox.Text);
    }

    internal static Uri? ResolveNetworkLookupUri(
        bool automaticAddress,
        string? configuredRouterUrl)
    {
        return automaticAddress
            ? null
            : RouterEndpoint.GetConfiguredUri(configuredRouterUrl);
    }

    internal static bool IsProfileNameDuplicate(
        IEnumerable<RouterProfile> profiles,
        RouterProfile currentProfile,
        string candidateName)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(currentProfile);

        if (string.IsNullOrWhiteSpace(candidateName))
        {
            return false;
        }

        var normalizedName = candidateName.Trim();
        return profiles.Any(profile =>
            !ReferenceEquals(profile, currentProfile) &&
            string.Equals(
                profile.Name.Trim(),
                normalizedName,
                StringComparison.OrdinalIgnoreCase));
    }

    internal static bool ResolveNetworkBindingChoice(
        bool? rememberedChoice,
        bool isAlreadyBound,
        bool profileHasNoBindings,
        bool isAddingProfile,
        bool isBoundToAnotherProfile)
    {
        return rememberedChoice ??
               (isAlreadyBound ||
                (profileHasNoBindings &&
                 (!isAddingProfile || !isBoundToAnotherProfile)));
    }

    private void RememberNetworkBindingChoice()
    {
        if (_updatingNetworkBindingChoice ||
            string.IsNullOrWhiteSpace(_snapshot?.ActiveNetworkId))
        {
            return;
        }

        _networkBindingChoices[_snapshot.ActiveNetworkId] = _bindNetworkCheckBox.Checked;
    }

    private void SetNetworkBindingChoice(string networkId, bool isBound)
    {
        SetNetworkBindingCheckState(isBound);
        _networkBindingChoices[networkId] = isBound;
    }

    private void SetNetworkBindingCheckState(bool isBound)
    {
        _updatingNetworkBindingChoice = true;
        try
        {
            _bindNetworkCheckBox.Checked = isBound;
        }
        finally
        {
            _updatingNetworkBindingChoice = false;
        }
    }

    private Uri? ResolveEndpointFromSettings()
    {
        return RouterEndpoint.Resolve(_profile.RouterUrl, _snapshot?.ActiveGateway);
    }

    private int GetConnectionSignature()
    {
        var hash = new HashCode();
        hash.Add(ResolveEndpointFromSettings()?.AbsoluteUri, StringComparer.OrdinalIgnoreCase);
        hash.Add(_profile.AuthMode);
        hash.Add(_profile.Login, StringComparer.Ordinal);
        hash.Add(_profile.Password, StringComparer.Ordinal);
        hash.Add(_profile.AccessToken, StringComparer.Ordinal);
        hash.Add(_snapshot?.ActiveMac, StringComparer.OrdinalIgnoreCase);
        return hash.ToHashCode();
    }

    private int GetDeviceSignature()
    {
        var hash = new HashCode();
        hash.Add(GetConnectionSignature());
        hash.Add(_snapshot?.ActiveInterfaceId, StringComparer.OrdinalIgnoreCase);
        return hash.ToHashCode();
    }

    private void SetTestStatus(string text, TestStatus status)
    {
        if (_disposed || _testStatusPanel.IsDisposed)
        {
            return;
        }

        _testStatusLabel.Text = text;
        _testProgressBar.Visible = status == TestStatus.Progress;
        (_testStatusPanel.BackColor, _testStatusPanel.BorderColor, _testStatusLabel.ForeColor) =
            GetStatusColors(status);
        _testStatusPanel.Invalidate();
    }

    private static (Color BackColor, Color BorderColor, Color ForeColor) GetStatusColors(
        TestStatus status)
    {
        return status switch
        {
            TestStatus.Success => (SuccessSurfaceColor, SuccessColor, SuccessColor),
            TestStatus.Warning => (WarningSurfaceColor, WarningColor, WarningColor),
            TestStatus.Error => (ErrorSurfaceColor, ErrorColor, ErrorColor),
            TestStatus.Progress => (AccentSurfaceColor, AccentColor, AccentColor),
            _ => (NeutralSurfaceColor, SystemColors.ControlLight, SystemColors.WindowText)
        };
    }

    private void ShowError(string message)
    {
        _errorBanner.Text = message;
        _errorBanner.Visible = true;
        _errorBanner.BringToFront();
    }

    private void HideError()
    {
        _errorBanner.Visible = false;
        _errorBanner.Text = string.Empty;
    }

    private (Panel Page, TableLayoutPanel Content) CreatePage(string title, string subtitle)
    {
        var page = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = SystemColors.Window,
            Margin = Padding.Empty,
            Visible = false
        };
        var content = CreateSingleColumnLayout();
        content.Padding = new Padding(32, 26, 32, 28);
        page.Controls.Add(content);

        var titleLabel = new WrappingLabel
        {
            Text = title,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = _titleFont,
            ForeColor = SystemColors.WindowText,
            Margin = Padding.Empty,
            UseMnemonic = false
        };
        AddPageRow(content, titleLabel, 8);
        AddPageRow(content, CreateHint(subtitle), 22);

        return (page, content);
    }

    private Control CreateChecklistItem(string number, string title, string description)
    {
        var card = CreateCard();
        card.Padding = new Padding(14);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            BackColor = SystemColors.Window
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var badge = new NumberBadge
        {
            Text = number,
            Size = new Size(30, 30),
            Margin = new Padding(0, 2, 12, 0),
            AccessibleName = number
        };
        var titleLabel = new WrappingLabel
        {
            Text = title,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = _emphasisFont,
            Margin = Padding.Empty,
            UseMnemonic = false
        };
        var descriptionLabel = CreateHint(description);
        descriptionLabel.WidthOffset = 42;
        descriptionLabel.Margin = new Padding(0, 4, 0, 0);

        layout.Controls.Add(badge, 0, 0);
        layout.SetRowSpan(badge, 2);
        layout.Controls.Add(titleLabel, 1, 0);
        layout.Controls.Add(descriptionLabel, 1, 1);
        card.Controls.Add(layout);
        return card;
    }

    private Control CreateCallout(string text)
    {
        var panel = new BorderedPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14),
            Margin = Padding.Empty,
            BackColor = AccentSurfaceColor,
            BorderColor = Color.FromArgb(153, 209, 255)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var mark = new Label
        {
            Text = "✓",
            AutoSize = true,
            Font = _emphasisFont,
            ForeColor = AccentColor,
            Margin = Padding.Empty,
            AccessibleName = string.Empty
        };
        var label = CreateHint(text);
        label.WidthOffset = 26;
        label.ForeColor = SystemColors.WindowText;
        layout.Controls.Add(mark, 0, 0);
        layout.Controls.Add(label, 1, 0);
        panel.Controls.Add(layout);
        return panel;
    }

    private BorderedPanel CreateCard()
    {
        return new BorderedPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(16),
            Margin = Padding.Empty,
            BackColor = SystemColors.Window,
            BorderColor = SystemColors.ControlLight
        };
    }

    private TableLayoutPanel CreateSingleColumnLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 0,
            Margin = Padding.Empty,
            BackColor = SystemColors.Window
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return layout;
    }

    private Control CreateField(string labelText, Control editor)
    {
        var layout = CreateSingleColumnLayout();
        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 4),
            UseMnemonic = false
        };
        AddPageRow(layout, label, 0);
        AddPageRow(layout, editor, 0);
        return layout;
    }

    private Label CreateSectionHeading(string text)
    {
        return new WrappingLabel
        {
            Text = text,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = _sectionFont,
            Margin = Padding.Empty,
            UseMnemonic = false
        };
    }

    private static WrappingLabel CreateHint(string text)
    {
        return new WrappingLabel
        {
            Text = text,
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            Margin = Padding.Empty,
            UseMnemonic = false
        };
    }

    private static WrappingLabel CreateIndentedHint(string text)
    {
        var label = CreateHint(text);
        label.Margin = new Padding(24, 0, 0, 0);
        return label;
    }

    private static TextBox CreateTextBox()
    {
        return new TextBox
        {
            Dock = DockStyle.Top,
            Margin = Padding.Empty
        };
    }

    private Label CreateSummaryValue()
    {
        return new WrappingLabel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = _emphasisFont,
            Margin = new Padding(0, 0, 0, 10),
            UseMnemonic = false,
            WidthOffset = 150
        };
    }

    private Label CreateDeviceValue()
    {
        return new WrappingLabel
        {
            Text = UiText.SetupDeviceUnavailable,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = _emphasisFont,
            Margin = new Padding(12, 8, 0, 0),
            UseMnemonic = false
        };
    }

    private static void AddDeviceDetailRow(
        TableLayoutPanel layout,
        int row,
        string labelText,
        Label valueLabel)
    {
        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 8, 12, 0),
            UseMnemonic = false
        };
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(valueLabel, 1, row);
    }

    private static string GetDefaultDeviceName()
    {
        var machineName = Environment.MachineName.Trim();
        return string.IsNullOrWhiteSpace(machineName) ? "RouterTray-PC" : machineName;
    }

    private static void AddSummaryRow(
        TableLayoutPanel layout,
        int row,
        string labelText,
        Label valueLabel)
    {
        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 12, 10),
            UseMnemonic = false
        };
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(valueLabel, 1, row);
    }

    private static Control CreateSeparator()
    {
        return new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = SystemColors.ControlLight,
            Margin = Padding.Empty
        };
    }

    private static void AddPageRow(TableLayoutPanel layout, Control control, int bottomMargin)
    {
        control.Margin = new Padding(
            control.Margin.Left,
            control.Margin.Top,
            control.Margin.Right,
            bottomMargin);
        var row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(control, 0, row);
    }

    private static Button CreateButton(string text)
    {
        return new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(96, 34),
            Margin = new Padding(8, 0, 0, 0),
            UseVisualStyleBackColor = true
        };
    }

    private static Button CreatePrimaryButton(string text)
    {
        var button = CreateButton(text);
        button.MinimumSize = new Size(112, 34);
        if (!SystemInformation.HighContrast)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = AccentColor;
            button.ForeColor = Color.White;
            button.UseVisualStyleBackColor = false;
        }

        return button;
    }

    private static void SetPrimaryButtonState(Button button, bool enabled)
    {
        button.Enabled = enabled;
        if (SystemInformation.HighContrast)
        {
            return;
        }

        button.BackColor = enabled ? AccentColor : Color.FromArgb(225, 225, 225);
        button.ForeColor = enabled ? Color.White : SystemColors.GrayText;
    }

    private static void SetControlTreeEnabled(Control control, bool enabled)
    {
        control.Enabled = enabled;
    }

    private void FitToWorkingArea()
    {
        var workingArea = Screen.FromControl(this).WorkingArea;
        var margin = Math.Max(1, (int)Math.Ceiling(WorkingAreaMargin * DeviceDpi / 96d));
        var maximumWidth = Math.Max(1, workingArea.Width - margin * 2);
        var maximumHeight = Math.Max(1, workingArea.Height - margin * 2);

        // At an unusually large scale on a small monitor the DPI-scaled minimum can
        // itself exceed the working area. Lower it only as far as the current screen
        // requires so the title bar and footer always remain reachable.
        var fittedMinimumSize = new Size(
            Math.Min(MinimumSize.Width, maximumWidth),
            Math.Min(MinimumSize.Height, maximumHeight));
        MinimumSize = Size.Empty;
        Size = new Size(Math.Min(Width, maximumWidth), Math.Min(Height, maximumHeight));
        MinimumSize = fittedMinimumSize;
        Location = new Point(
            workingArea.Left + Math.Max(0, (workingArea.Width - Width) / 2),
            workingArea.Top + Math.Max(0, (workingArea.Height - Height) / 2));
    }

    private enum TestStatus
    {
        Neutral,
        Progress,
        Success,
        Warning,
        Error
    }

    private sealed class BorderedPanel : Panel
    {
        public Color BorderColor { get; set; } = SystemColors.ControlLight;

        public BorderedPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            ControlPaint.DrawBorder(e.Graphics, ClientRectangle, BorderColor, ButtonBorderStyle.Solid);
        }
    }

    private sealed class WrappingLabel : Label
    {
        private Control? _observedParent;
        private int _widthOffset;

        public int WidthOffset
        {
            get => _widthOffset;
            set
            {
                _widthOffset = Math.Max(0, value);
                UpdateMaximumWidth();
            }
        }

        protected override void OnParentChanged(EventArgs e)
        {
            if (_observedParent is not null)
            {
                _observedParent.ClientSizeChanged -= OnParentClientSizeChanged;
            }

            base.OnParentChanged(e);
            _observedParent = Parent;
            if (_observedParent is not null)
            {
                _observedParent.ClientSizeChanged += OnParentClientSizeChanged;
            }

            UpdateMaximumWidth();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            BeginInvoke(UpdateMaximumWidth);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _observedParent is not null)
            {
                _observedParent.ClientSizeChanged -= OnParentClientSizeChanged;
                _observedParent = null;
            }

            base.Dispose(disposing);
        }

        private void OnParentClientSizeChanged(object? sender, EventArgs e)
        {
            UpdateMaximumWidth();
        }

        private void UpdateMaximumWidth()
        {
            if (Parent is null)
            {
                return;
            }

            var availableWidth = Parent.ClientSize.Width -
                                 Parent.Padding.Horizontal -
                                 Margin.Horizontal -
                                 WidthOffset;
            if (availableWidth > 0 && MaximumSize.Width != availableWidth)
            {
                MaximumSize = new Size(availableWidth, 0);
            }
        }
    }

    private sealed class NumberBadge : Control
    {
        public NumberBadge()
        {
            DoubleBuffered = true;
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(1, 1, Math.Max(0, Width - 3), Math.Max(0, Height - 3));
            using var brush = new SolidBrush(AccentSurfaceColor);
            using var pen = new Pen(AccentColor);
            e.Graphics.FillEllipse(brush, bounds);
            e.Graphics.DrawEllipse(pen, bounds);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                bounds,
                AccentColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding);
        }
    }

    private sealed class SetupStepRail : Control
    {
        private readonly string[] _steps;
        private readonly string _caption;
        private readonly Font _brandFont;
        private readonly Font _captionFont;
        private readonly Font _stepFont;
        private int _currentStep;

        public SetupStepRail(string[] steps, string caption, Font baseFont)
        {
            _steps = steps;
            _caption = caption;
            _brandFont = new Font(baseFont.FontFamily, 16f, FontStyle.Bold, GraphicsUnit.Point);
            _captionFont = new Font(baseFont.FontFamily, 9f, FontStyle.Regular, GraphicsUnit.Point);
            _stepFont = new Font(baseFont, FontStyle.Bold);
            DoubleBuffered = true;
            TabStop = false;
            BackColor = SystemInformation.HighContrast ? SystemColors.Highlight : AccentColor;
            ForeColor = SystemInformation.HighContrast ? SystemColors.HighlightText : Color.White;
            AccessibleName = _caption;
        }

        public int CurrentStep
        {
            get => _currentStep;
            set
            {
                if (_currentStep == value)
                {
                    return;
                }

                _currentStep = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);

            // Point-sized fonts grow with monitor DPI, while raw painting coordinates
            // do not. Derive every position from measured text and the current font so
            // the rail remains readable at 100%, 150%, 225%, and after a monitor move.
            var outerPadding = Math.Max(20, Font.Height);
            var smallGap = Math.Max(8, Font.Height / 2);
            var sectionGap = Math.Max(22, Font.Height);
            var contentWidth = Math.Max(1, Width - outerPadding * 2);
            var currentY = outerPadding;

            var logoSize = Math.Max(42, _brandFont.Height + 4);
            var logoBounds = new Rectangle(outerPadding, currentY, logoSize, logoSize);
            using (var logoBrush = new SolidBrush(Color.FromArgb(42, ForeColor)))
            using (var logoPen = new Pen(Color.FromArgb(180, ForeColor)))
            {
                e.Graphics.FillEllipse(logoBrush, logoBounds);
                e.Graphics.DrawEllipse(logoPen, logoBounds);
            }

            TextRenderer.DrawText(
                e.Graphics,
                "R",
                _brandFont,
                logoBounds,
                ForeColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding);

            currentY = logoBounds.Bottom + smallGap;
            var brandHeight = TextRenderer.MeasureText(
                e.Graphics,
                UiText.AppName,
                _brandFont,
                new Size(contentWidth, int.MaxValue),
                TextFormatFlags.Left | TextFormatFlags.NoPadding).Height;
            TextRenderer.DrawText(
                e.Graphics,
                UiText.AppName,
                _brandFont,
                new Rectangle(outerPadding, currentY, contentWidth, brandHeight),
                ForeColor,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);

            currentY += brandHeight + smallGap;
            var captionHeight = TextRenderer.MeasureText(
                e.Graphics,
                _caption,
                _captionFont,
                new Size(contentWidth, int.MaxValue),
                TextFormatFlags.Left |
                TextFormatFlags.WordBreak |
                TextFormatFlags.NoPadding).Height;
            TextRenderer.DrawText(
                e.Graphics,
                _caption,
                _captionFont,
                new Rectangle(outerPadding, currentY, contentWidth, captionHeight),
                Color.FromArgb(210, ForeColor),
                TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

            var firstY = currentY + captionHeight + sectionGap;
            var badgeSize = Math.Max(28, _stepFont.Height + 8);
            var preferredRowHeight = Math.Max(badgeSize + smallGap, Font.Height * 4);
            var availableHeight = Math.Max(1, Height - firstY - outerPadding);
            var rowHeight = Math.Max(
                badgeSize,
                Math.Min(preferredRowHeight, availableHeight / Math.Max(1, _steps.Length)));

            for (var index = 0; index < _steps.Length; index++)
            {
                var rowTop = firstY + index * rowHeight;
                var badgeTop = rowTop + Math.Max(0, (rowHeight - badgeSize) / 2);
                var badgeBounds = new Rectangle(outerPadding, badgeTop, badgeSize, badgeSize);
                if (index < _steps.Length - 1)
                {
                    using var connectorPen = new Pen(Color.FromArgb(100, ForeColor), 2f);
                    e.Graphics.DrawLine(
                        connectorPen,
                        badgeBounds.Left + badgeSize / 2,
                        badgeBounds.Bottom + 4,
                        badgeBounds.Left + badgeSize / 2,
                        badgeBounds.Top + rowHeight - 4);
                }

                var isReached = index <= _currentStep;
                using var badgeBrush = new SolidBrush(
                    isReached ? ForeColor : Color.FromArgb(32, ForeColor));
                using var badgePen = new Pen(Color.FromArgb(isReached ? 255 : 150, ForeColor), 1.5f);
                e.Graphics.FillEllipse(badgeBrush, badgeBounds);
                e.Graphics.DrawEllipse(badgePen, badgeBounds);

                var badgeText = index < _currentStep ? "✓" : (index + 1).ToString();
                TextRenderer.DrawText(
                    e.Graphics,
                    badgeText,
                    _stepFont,
                    badgeBounds,
                    isReached ? BackColor : ForeColor,
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding);

                var textColor = index <= _currentStep
                    ? ForeColor
                    : Color.FromArgb(190, ForeColor);
                var textLeft = badgeBounds.Right + smallGap;
                TextRenderer.DrawText(
                    e.Graphics,
                    _steps[index],
                    index == _currentStep ? _stepFont : _captionFont,
                    new Rectangle(
                        textLeft,
                        rowTop,
                        Math.Max(0, Width - textLeft - outerPadding),
                        rowHeight),
                    textColor,
                    TextFormatFlags.Left |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.WordBreak |
                    TextFormatFlags.NoPadding);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _brandFont.Dispose();
                _captionFont.Dispose();
                _stepFont.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

internal sealed class PendingNetworkBindingMove
{
    private readonly string _sourceProfileId;
    private readonly RouterNetworkBinding _binding;

    public PendingNetworkBindingMove(
        string sourceProfileId,
        RouterNetworkBinding binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceProfileId);
        ArgumentNullException.ThrowIfNull(binding);

        _sourceProfileId = sourceProfileId;
        _binding = binding.Clone();
    }

    public bool IsForNetwork(string? networkId)
    {
        return Guid.TryParse(_binding.NetworkId, out var bindingId) &&
               Guid.TryParse(networkId, out var candidateId)
            ? bindingId == candidateId
            : string.Equals(
                _binding.NetworkId,
                networkId,
                StringComparison.OrdinalIgnoreCase);
    }

    public void Restore(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var sourceProfile = settings.FindProfile(_sourceProfileId);
        if (sourceProfile is not null && !sourceProfile.IsBoundTo(_binding.NetworkId))
        {
            sourceProfile.Networks.Add(_binding.Clone());
        }
    }

    public bool TryRestoreAfterNetworkChange(
        AppSettings settings,
        RouterProfile targetProfile,
        string? activeNetworkId)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(targetProfile);

        if (IsForNetwork(activeNetworkId))
        {
            return false;
        }

        targetProfile.Networks.RemoveAll(binding =>
            IsForNetwork(binding.NetworkId));
        Restore(settings);
        return true;
    }
}
