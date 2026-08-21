using System.Diagnostics;
using System.Reflection;

namespace RouterTray;

internal sealed class AboutForm : Form
{
    private const int MaxTextWidth = 460;
    private const int WorkingAreaMargin = 24;

    private readonly Icon _formIcon;
    private readonly Image? _logoImage;
    private readonly List<Control> _bodyWrappingControls = [];
    private readonly List<Control> _footerWrappingControls = [];
    private readonly TableLayoutPanel _bodyTextLayout;
    private readonly TableLayoutPanel _footerTextLayout;
    private readonly Panel _scrollHost;
    private readonly TableLayoutPanel _rootLayout;
    private bool _updatingResponsiveLayout;
    private bool _disposed;

    public AboutForm()
    {
        _formIcon = AppIconProvider.CreateIcon();
        Icon = _formIcon;
        Text = UiText.AboutTitle;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(
            Math.Max(600, Font.Height * 24),
            Math.Max(360, Font.Height * 13));
        MinimumSize = new Size(380, 280);
        BackColor = SystemColors.Window;

        _scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = SystemColors.Window
        };

        _rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            BackColor = SystemColors.Window
        };
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var content = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(16, 16, 16, 12),
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            BackColor = SystemColors.Window
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _bodyTextLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill
        };

        var nameLabel = new Label
        {
            Text = UiText.AppName,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
            Font = new Font(Font.FontFamily, Font.Size + 10f, FontStyle.Bold)
        };
        _bodyTextLayout.Controls.Add(nameLabel, 0, 0);
        _bodyTextLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var versionLabel = new Label
        {
            Text = UiText.AboutVersion(TrimBuildMetadata(GetVersion())),
            AutoSize = true,
            MaximumSize = new Size(MaxTextWidth, 0),
            Margin = new Padding(4, 0, 0, 10),
            Font = new Font(Font.FontFamily, Font.Size + 1f, FontStyle.Regular)
        };
        _bodyWrappingControls.Add(versionLabel);
        _bodyTextLayout.Controls.Add(versionLabel, 0, 1);
        _bodyTextLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var rowIndex = 2;
        rowIndex = AddOptionalLabel(
            _bodyTextLayout,
            rowIndex,
            UiText.AboutDescription,
            _bodyWrappingControls);

        _logoImage = GetAppIconBitmap();
        var logo = new PictureBox
        {
            Image = _logoImage,
            Size = new Size(110, 110),
            SizeMode = PictureBoxSizeMode.CenterImage,
            Margin = new Padding(24, 4, 0, 0)
        };

        content.Controls.Add(_bodyTextLayout, 0, 0);
        content.Controls.Add(logo, 1, 0);

        var footerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = SystemColors.Control,
            Padding = new Padding(16, 10, 16, 10)
        };

        var footerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = SystemColors.Control
        };
        footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _footerTextLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = SystemColors.Control,
            Dock = DockStyle.Fill
        };
        _footerTextLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var footerRow = 0;
        footerRow = AddOptionalLabel(
            _footerTextLayout,
            footerRow,
            ResolveCopyright(),
            _footerWrappingControls);

        var licenseLink = CreateLink(UiText.AboutLicenseText, UiText.AboutLicenseUrl);
        footerRow = AddOptionalControl(
            _footerTextLayout,
            footerRow,
            licenseLink,
            _footerWrappingControls);

        var websiteLink = CreateLink(UiText.AboutWebsiteText, UiText.AboutWebsiteUrl);
        footerRow = AddOptionalControl(
            _footerTextLayout,
            footerRow,
            websiteLink,
            _footerWrappingControls);

        var buttonsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = SystemColors.Control,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Margin = new Padding(24, 0, 0, 0)
        };

        var okButton = new Button
        {
            Text = UiText.AboutOk,
            DialogResult = DialogResult.OK,
            AutoSize = true,
            MinimumSize = new Size(80, 28)
        };
        okButton.Click += (_, __) => Close();

        AcceptButton = okButton;
        CancelButton = okButton;

        buttonsPanel.Controls.Add(okButton);

        footerLayout.Controls.Add(_footerTextLayout, 0, 0);
        footerLayout.Controls.Add(buttonsPanel, 1, 0);
        footerPanel.Controls.Add(footerLayout);

        _rootLayout.Controls.Add(content, 0, 0);
        _rootLayout.Controls.Add(footerPanel, 0, 1);

        _scrollHost.Controls.Add(_rootLayout);
        Controls.Add(_scrollHost);
        _scrollHost.ClientSizeChanged += (_, _) => UpdateResponsiveLayout();
        _bodyTextLayout.ClientSizeChanged += (_, _) => UpdateResponsiveLayout();
        _footerTextLayout.ClientSizeChanged += (_, _) => UpdateResponsiveLayout();
        Shown += (_, _) =>
        {
            FitToWorkingArea();
            UpdateResponsiveLayout();
        };
    }

    private static int AddOptionalLabel(
        TableLayoutPanel panel,
        int rowIndex,
        string text,
        ICollection<Control>? wrappingControls = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return rowIndex;
        }

        var label = CreateLabel(text, 8);
        wrappingControls?.Add(label);
        panel.Controls.Add(label, 0, rowIndex);
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return rowIndex + 1;
    }

    private static int AddOptionalControl(
        TableLayoutPanel panel,
        int rowIndex,
        Control? control,
        ICollection<Control>? wrappingControls = null)
    {
        if (control is null)
        {
            return rowIndex;
        }

        wrappingControls?.Add(control);
        panel.Controls.Add(control, 0, rowIndex);
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return rowIndex + 1;
    }

    private static Label CreateLabel(string text, int bottomMargin)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(MaxTextWidth, 0),
            Margin = new Padding(4, 0, 0, bottomMargin)
        };
    }

    private static LinkLabel? CreateLink(string text, string url)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var link = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(MaxTextWidth, 0),
            Margin = new Padding(0, 0, 0, 8),
            LinkBehavior = LinkBehavior.HoverUnderline
        };

        link.Links.Add(0, text.Length, url);
        link.LinkClicked += (_, e) =>
        {
            var target = e.Link?.LinkData as string ?? url;
            OpenLink(target);
        };

        return link;
    }

    private void UpdateResponsiveLayout()
    {
        if (_updatingResponsiveLayout)
        {
            return;
        }

        _updatingResponsiveLayout = true;
        try
        {
            var viewportHeight = Math.Max(0, _scrollHost.ClientSize.Height);
            if (_rootLayout.MinimumSize.Height != viewportHeight)
            {
                _rootLayout.MinimumSize = new Size(0, viewportHeight);
            }

            UpdateWrappingControls(_bodyWrappingControls, _bodyTextLayout.ClientSize.Width);
            UpdateWrappingControls(_footerWrappingControls, _footerTextLayout.ClientSize.Width);
        }
        finally
        {
            _updatingResponsiveLayout = false;
        }
    }

    private void UpdateWrappingControls(IEnumerable<Control> controls, int containerWidth)
    {
        if (containerWidth <= 0)
        {
            return;
        }

        var maximumTextWidth = ScaleLogicalPixels(MaxTextWidth);
        foreach (var control in controls)
        {
            var availableWidth = Math.Max(1, containerWidth - control.Margin.Horizontal);
            var width = Math.Min(maximumTextWidth, availableWidth);
            if (control.MaximumSize.Width != width)
            {
                control.MaximumSize = new Size(width, 0);
            }
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

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            Icon = null;
            _formIcon.Dispose();
            _logoImage?.Dispose();
        }

        base.Dispose(disposing);
    }

    private static void OpenLink(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Ignore failures for invalid URLs or missing handlers.
        }
    }

    private static string GetVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            return info;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private static string TrimBuildMetadata(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "0.0.0";
        }

        var plusIndex = version.IndexOf('+');
        return plusIndex > 0 ? version.Substring(0, plusIndex) : version;
    }

    private static Bitmap? GetAppIconBitmap()
    {
        try
        {
            using var appIcon = AppIconProvider.CreateIcon();
            return appIcon.ToBitmap();
        }
        catch
        {
            return SystemIcons.Application.ToBitmap();
        }
    }

    private static string ResolveCopyright()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var attr = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>();
        if (!string.IsNullOrWhiteSpace(attr?.Copyright))
        {
            return attr!.Copyright;
        }

        return UiText.AboutCopyright;
    }
}
