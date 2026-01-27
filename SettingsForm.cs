namespace RouterTray;

internal sealed class SettingsForm : Form
{
    private readonly TextBox _loginTextBox;
    private readonly TextBox _passwordTextBox;
    private readonly CheckBox _showPasswordCheckBox;
    private readonly CheckBox _autoStartCheckBox;
    private readonly CheckBox _notifyPolicyCheckBox;

    public SettingsForm(AppSettings settings)
    {
        Text = UiText.SettingsTitle;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(12, 12, 12, 8),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var loginLabel = new Label
        {
            Text = UiText.SettingsLogin,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 12, 6)
        };

        _loginTextBox = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Text = settings.Login,
            Margin = new Padding(0, 4, 0, 4)
        };

        var passwordLabel = new Label
        {
            Text = UiText.SettingsPassword,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 12, 6)
        };

        _passwordTextBox = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            UseSystemPasswordChar = true,
            Text = settings.Password,
            Margin = new Padding(0, 4, 0, 4)
        };

        _showPasswordCheckBox = new CheckBox
        {
            Text = UiText.SettingsShowPassword,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 0, 8)
        };
        _showPasswordCheckBox.CheckedChanged += (_, __) =>
            _passwordTextBox.UseSystemPasswordChar = !_showPasswordCheckBox.Checked;

        _autoStartCheckBox = new CheckBox
        {
            Text = UiText.SettingsAutoStart,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 0, 8),
            Checked = settings.AutoStart
        };

        _notifyPolicyCheckBox = new CheckBox
        {
            Text = UiText.SettingsShowPolicyNotifications,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 0, 8),
            Checked = settings.ShowPolicyNotifications
        };

        var buttonsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 6, 0, 0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        var saveButton = new Button
        {
            Text = UiText.SettingsSave,
            DialogResult = DialogResult.OK,
            AutoSize = true,
            MinimumSize = new Size(80, 28)
        };
        saveButton.Click += OnSaveClick;

        var cancelButton = new Button
        {
            Text = UiText.SettingsCancel,
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            MinimumSize = new Size(80, 28)
        };

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        buttonsPanel.Controls.Add(saveButton);
        buttonsPanel.Controls.Add(cancelButton);

        layout.Controls.Add(loginLabel, 0, 0);
        layout.Controls.Add(_loginTextBox, 1, 0);
        layout.Controls.Add(passwordLabel, 0, 1);
        layout.Controls.Add(_passwordTextBox, 1, 1);
        layout.Controls.Add(_showPasswordCheckBox, 1, 2);
        layout.Controls.Add(_autoStartCheckBox, 1, 3);
        layout.Controls.Add(_notifyPolicyCheckBox, 1, 4);
        layout.Controls.Add(buttonsPanel, 0, 5);
        layout.SetColumnSpan(buttonsPanel, 2);

        Controls.Add(layout);
    }

    public string Login => _loginTextBox.Text.Trim();
    public string Password => _passwordTextBox.Text;
    public bool AutoStart => _autoStartCheckBox.Checked;
    public bool ShowPolicyNotifications => _notifyPolicyCheckBox.Checked;

    private void OnSaveClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Login) || string.IsNullOrWhiteSpace(Password))
        {
            MessageBox.Show(
                this,
                UiText.SettingsValidationMessage,
                UiText.SettingsTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
        }
    }
}
