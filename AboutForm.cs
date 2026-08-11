using System.Diagnostics;
using System.Reflection;

namespace RouterTray;

internal sealed class AboutForm : Form
{
    private const int MaxTextWidth = 460;
    private readonly Image? _logoImage;

    public AboutForm()
    {
        Text = UiText.AboutTitle;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = SystemColors.Window;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var content = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(16, 16, 16, 12),
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = SystemColors.Window
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var left = new TableLayoutPanel
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
        left.Controls.Add(nameLabel, 0, 0);
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var versionLabel = new Label
        {
            Text = UiText.AboutVersion(TrimBuildMetadata(GetVersion())),
            AutoSize = true,
            MaximumSize = new Size(MaxTextWidth, 0),
            Margin = new Padding(4, 0, 0, 10),
            Font = new Font(Font.FontFamily, Font.Size + 1f, FontStyle.Regular)
        };
        left.Controls.Add(versionLabel, 0, 1);
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var rowIndex = 2;
        rowIndex = AddOptionalLabel(left, rowIndex, UiText.AboutDescription);

        _logoImage = GetAppIconBitmap();
        var logo = new PictureBox
        {
            Image = _logoImage,
            Size = new Size(110, 110),
            SizeMode = PictureBoxSizeMode.CenterImage,
            Margin = new Padding(24, 4, 0, 0)
        };

        content.Controls.Add(left, 0, 0);
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

        var footerLeft = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = SystemColors.Control
        };
        footerLeft.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var footerRow = 0;
        footerRow = AddOptionalLabel(footerLeft, footerRow, ResolveCopyright());

        var licenseLink = CreateLink(UiText.AboutLicenseText, UiText.AboutLicenseUrl);
        footerRow = AddOptionalControl(footerLeft, footerRow, licenseLink);

        var websiteLink = CreateLink(UiText.AboutWebsiteText, UiText.AboutWebsiteUrl);
        footerRow = AddOptionalControl(footerLeft, footerRow, websiteLink);

        var buttonsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = SystemColors.Control,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Margin = new Padding(0, 60, 0, 0)
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

        footerLayout.Controls.Add(footerLeft, 0, 0);
        footerLayout.Controls.Add(buttonsPanel, 1, 0);
        footerPanel.Controls.Add(footerLayout);

        root.Controls.Add(content, 0, 0);
        root.Controls.Add(footerPanel, 0, 1);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Controls.Add(root);
    }

    private static int AddOptionalLabel(TableLayoutPanel panel, int rowIndex, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return rowIndex;
        }

        var label = CreateLabel(text, 8);
        panel.Controls.Add(label, 0, rowIndex);
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return rowIndex + 1;
    }

    private static int AddOptionalControl(TableLayoutPanel panel, int rowIndex, Control? control)
    {
        if (control is null)
        {
            return rowIndex;
        }

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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
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
            using var extractedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            return (extractedIcon ?? SystemIcons.Application).ToBitmap();
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
