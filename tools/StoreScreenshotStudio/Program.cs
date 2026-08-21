using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using RouterTray;

namespace RouterTray.StoreScreenshotStudio;

internal static class Program
{
    private const int OutputWidth = 1920;
    private const int OutputHeight = 1080;

    [STAThread]
    private static void Main(string[] args)
    {
        ApplyCulture("en-US");

        global::ApplicationConfiguration.Initialize();

        var repositoryRoot = FindRepositoryRoot();
        var backgroundPath = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.Combine(
                repositoryRoot,
                "docs",
                "store-assets",
                "source",
                "routertray-store-backdrop.png");
        var outputRootDirectory = args.Length > 1
            ? Path.GetFullPath(args[1])
            : Path.Combine(
                repositoryRoot,
                "docs",
                "store-assets",
                "screenshots");

        if (!File.Exists(backgroundPath))
        {
            throw new FileNotFoundException("Store screenshot backdrop was not found.", backgroundPath);
        }

        Directory.CreateDirectory(outputRootDirectory);

        var trayIconPath = Path.Combine(
            repositoryRoot,
            "docs",
            "images",
            "routertray-icon.png");
        var systemTrayPath = Path.Combine(
            repositoryRoot,
            "docs",
            "store-assets",
            "source",
            "real-system-tray-en.png");
        if (!File.Exists(systemTrayPath))
        {
            throw new FileNotFoundException("The real Windows system tray capture was not found.", systemTrayPath);
        }

        using var backdrop = new BackdropForm(backgroundPath, trayIconPath, systemTrayPath);
        backdrop.Show();
        backdrop.Activate();
        Application.DoEvents();
        Thread.Sleep(250);

        var requestedLocale = args.Length > 2 ? args[2] : null;
        var localizations = StoreScreenshotLocalization.All
            .Where(localization =>
                requestedLocale is null ||
                string.Equals(
                    localization.StoreLocale,
                    requestedLocale,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (localizations.Length == 0)
        {
            throw new ArgumentException(
                $"Unsupported Store screenshot locale: {requestedLocale}",
                nameof(args));
        }

        foreach (var localization in localizations)
        {
            ApplyCulture(localization.CultureName);
            var outputDirectory = Path.Combine(
                outputRootDirectory,
                localization.StoreLocale);
            Directory.CreateDirectory(outputDirectory);

            Console.WriteLine($"[{localization.StoreLocale}]");
            var studio = new ScreenshotStudio(
                backdrop,
                outputDirectory,
                localization);
            studio.CapturePolicySwitcher();
            studio.CaptureRouterProfilesMenu();
            studio.CaptureProfileSettings();
            studio.CaptureAccessTokenSettings();
            studio.CaptureApplicationSettings();
            studio.WriteCaptions();
        }

        backdrop.Close();
    }

    private static void ApplyCulture(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RouterTray.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the RouterTray repository root.");
    }

    private sealed class ScreenshotStudio
    {
        private readonly BackdropForm _backdrop;
        private readonly string _outputDirectory;
        private readonly StoreScreenshotLocalization _localization;
        private readonly FileLogger _logger;
        private readonly AppSettings _settings;
        private readonly RouterNetworkBinding _homeNetwork;
        private readonly RouterNetworkBinding _labNetwork;

        public ScreenshotStudio(
            BackdropForm backdrop,
            string outputDirectory,
            StoreScreenshotLocalization localization)
        {
            _backdrop = backdrop;
            _outputDirectory = outputDirectory;
            _localization = localization;
            _logger = new FileLogger(Path.Combine(outputDirectory, "screenshot-studio.log"));
            (_settings, _homeNetwork, _labNetwork) = CreateSampleSettings(localization);
        }

        public void CapturePolicySwitcher()
        {
            using var menu = new NativePolicyMenu();
            var snapshot = PolicyMenuSnapshot.FromRouter(
                new[]
                {
                    new PolicyInfo("direct", _localization.DirectPolicy),
                    new PolicyInfo("work-vpn", _localization.WorkVpnPolicy),
                    new PolicyInfo("privacy-vpn", _localization.PrivacyVpnPolicy),
                    new PolicyInfo("family", _localization.FamilyPolicy)
                },
                "privacy-vpn");
            var anchor = new Point(
                _backdrop.CaptureBounds.Left + (_backdrop.CaptureBounds.Width / 2) + 160,
                _backdrop.CaptureBounds.Top + (_backdrop.CaptureBounds.Height / 2) + 160);
            Bitmap? capture = null;
            Exception? captureFailure = null;
            using var captureFinished = new ManualResetEventSlim();

            var captureThread = new Thread(() =>
            {
                IntPtr menuWindow = IntPtr.Zero;
                try
                {
                    Thread.Sleep(600);
                    menuWindow = WindowFromPoint(new NativePoint
                    {
                        X = anchor.X - 8,
                        Y = anchor.Y - 8
                    });
                    if (menuWindow == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("Windows could not locate the native policy menu.");
                    }

                    capture = CaptureWindow(menuWindow, GetWindowBounds(menuWindow));
                }
                catch (Exception ex)
                {
                    captureFailure = ex;
                }
                finally
                {
                    if (menuWindow != IntPtr.Zero)
                    {
                        _ = PostMessage(menuWindow, WmCancelMode, UIntPtr.Zero, IntPtr.Zero);
                    }

                    _ = PostMessage(_backdrop.Handle, WmCancelMode, UIntPtr.Zero, IntPtr.Zero);
                    captureFinished.Set();
                }
            })
            {
                IsBackground = true,
                Name = "RouterTrayNativeMenuCapture"
            };

            _backdrop.Activate();
            _backdrop.BringToFront();
            captureThread.Start();
            _ = menu.Show(_backdrop.Handle, anchor, snapshot);

            if (!captureFinished.Wait(TimeSpan.FromSeconds(8)))
            {
                throw new TimeoutException("Timed out while capturing the native policy menu.");
            }

            if (captureFailure is not null)
            {
                throw new InvalidOperationException("Failed to capture the native policy menu.", captureFailure);
            }

            using (capture ?? throw new InvalidOperationException("The native policy menu capture is empty."))
            {
                SaveSingleWindowComposition(
                    "01-policy-switcher.png",
                    capture,
                    new Size(500, 540),
                    maximumScale: 1.25f,
                    anchorToTray: true,
                    eyebrow: string.Empty,
                    headline: _localization.PolicyHeadline,
                    supportingText: _localization.PolicyDescription);
            }

            RestoreBackdrop();
        }

        public void CaptureRouterProfilesMenu()
        {
            using var menu = BuildTrayMenu(out var profilesItem);
            profilesItem.DropDownDirection = ToolStripDropDownDirection.Left;
            var location = new Point(
                _backdrop.CaptureBounds.Left + (_backdrop.CaptureBounds.Width / 2) + 180,
                _backdrop.CaptureBounds.Top + 360);

            _backdrop.Activate();
            menu.Show(location);
            Application.DoEvents();
            profilesItem.ShowDropDown();
            Application.DoEvents();
            Thread.Sleep(450);

            var menuBounds = GetWindowBounds(menu.Handle);
            var submenuBounds = GetWindowBounds(profilesItem.DropDown.Handle);
            using var menuCapture = CaptureWindow(menu.Handle, menuBounds);
            using var submenuCapture = CaptureWindow(profilesItem.DropDown.Handle, submenuBounds);
            using var combined = CombineWindowCaptures(
                (menuCapture, menuBounds),
                (submenuCapture, submenuBounds));
            SaveSingleWindowComposition(
                "02-router-profiles.png",
                combined,
                new Size(566, 560),
                maximumScale: 1.2f,
                anchorToTray: true,
                eyebrow: string.Empty,
                headline: _localization.ProfilesHeadline,
                supportingText: _localization.ProfilesDescription);

            menu.Close();
            RestoreBackdrop();
        }

        public void WriteCaptions()
        {
            var path = Path.Combine(_outputDirectory, "captions.txt");
            File.WriteAllText(path, _localization.BuildCaptionsFile());
            Console.WriteLine(path);
        }

        public void CaptureProfileSettings()
        {
            using var form = CreateSettingsForm(_homeNetwork);
            ShowAndCaptureSettingsForm(
                form,
                new Size(1800, 980),
                "03-profile-settings.png");
        }

        public void CaptureAccessTokenSettings()
        {
            using var form = CreateSettingsForm(_labNetwork);
            ShowAndCaptureSettingsForm(
                form,
                new Size(1800, 980),
                "04-access-token.png");
        }

        public void CaptureApplicationSettings()
        {
            using var form = CreateSettingsForm(_homeNetwork);
            form.Show(_backdrop);
            form.Size = new Size(1040, 520);
            CenterInCaptureBounds(form);
            form.TopMost = true;
            form.BringToFront();
            Application.DoEvents();

            var tabsField = typeof(SettingsForm).GetField(
                "_settingsTabs",
                BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new MissingFieldException(typeof(SettingsForm).FullName, "_settingsTabs");
            var tabs = (TabControl)(tabsField.GetValue(form) ??
                                    throw new InvalidOperationException("Settings tabs were not initialized."));
            tabs.SelectedIndex = 1;
            Application.DoEvents();
            Thread.Sleep(450);

            using (var capture = CaptureWindow(form.Handle))
            {
                SaveSingleWindowComposition(
                    "05-application-settings.png",
                    capture,
                    new Size(1120, 570),
                    maximumScale: 1f,
                    verticalOffset: -20);
            }
            form.Close();
            RestoreBackdrop();
        }

        private SettingsForm CreateSettingsForm(RouterNetworkBinding currentNetwork)
        {
            return new SettingsForm(
                _settings,
                currentNetwork,
                (_, _) => Task.FromResult(ApplicationUpdateCheckResult.ManagedByPackage),
                _logger,
                updatesManagedByPackage: true);
        }

        private void ShowAndCaptureSettingsForm(Form form, Size size, string fileName)
        {
            form.Show(_backdrop);
            form.Size = size;
            CenterInCaptureBounds(form);
            form.TopMost = true;
            form.BringToFront();
            Application.DoEvents();
            Thread.Sleep(550);

            using (var capture = CaptureWindow(form.Handle))
            {
                SaveSingleWindowComposition(
                    fileName,
                    capture,
                    new Size(1500, 820),
                    maximumScale: 1f);
            }
            form.Close();
            RestoreBackdrop();
        }

        private void CenterInCaptureBounds(Form form)
        {
            form.Location = new Point(
                _backdrop.CaptureBounds.Left + Math.Max(0, (_backdrop.CaptureBounds.Width - form.Width) / 2),
                _backdrop.CaptureBounds.Top + Math.Max(0, (_backdrop.CaptureBounds.Height - form.Height) / 2));
        }

        private void SaveSingleWindowComposition(
            string fileName,
            Bitmap source,
            Size maximumSize,
            float maximumScale,
            int verticalOffset = 0,
            bool anchorToTray = false,
            string? eyebrow = null,
            string? headline = null,
            string? supportingText = null)
        {
            var widthScale = maximumSize.Width / (float)source.Width;
            var heightScale = maximumSize.Height / (float)source.Height;
            var scale = Math.Min(maximumScale, Math.Min(widthScale, heightScale));
            var destinationSize = new Size(
                Math.Max(1, (int)Math.Round(source.Width * scale)),
                Math.Max(1, (int)Math.Round(source.Height * scale)));
            var destination = anchorToTray
                ? new Rectangle(
                    BackdropForm.TrayPopupRight - destinationSize.Width,
                    BackdropForm.TrayTaskbarTop - destinationSize.Height - 4,
                    destinationSize.Width,
                    destinationSize.Height)
                : new Rectangle(
                    (OutputWidth - destinationSize.Width) / 2,
                    Math.Max(44, ((OutputHeight - destinationSize.Height) / 2) + verticalOffset),
                    destinationSize.Width,
                    destinationSize.Height);

            using var output = _backdrop.CreateOutputCanvas(includeTaskbar: anchorToTray);
            using (var graphics = Graphics.FromImage(output))
            {
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = scale < 1f
                    ? InterpolationMode.HighQualityBicubic
                    : InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                if (anchorToTray &&
                    eyebrow is not null &&
                    headline is not null &&
                    supportingText is not null)
                {
                    _backdrop.DrawMarketingCopy(
                        graphics,
                        eyebrow,
                        headline,
                        supportingText);
                }

                DrawSoftShadow(graphics, destination);
                graphics.DrawImage(
                    source,
                    destination,
                    new Rectangle(Point.Empty, source.Size),
                    GraphicsUnit.Pixel);
            }

            var path = Path.Combine(_outputDirectory, fileName);
            output.Save(path, ImageFormat.Png);
            Console.WriteLine(path);
        }

        private static Bitmap CombineWindowCaptures(
            (Bitmap Image, Rectangle Bounds) first,
            (Bitmap Image, Rectangle Bounds) second)
        {
            var union = Rectangle.Union(first.Bounds, second.Bounds);
            var combined = new Bitmap(
                union.Width,
                union.Height,
                PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(combined);
            graphics.Clear(Color.Transparent);
            graphics.DrawImageUnscaled(
                first.Image,
                first.Bounds.Left - union.Left,
                first.Bounds.Top - union.Top);
            graphics.DrawImageUnscaled(
                second.Image,
                second.Bounds.Left - union.Left,
                second.Bounds.Top - union.Top);
            return combined;
        }

        private static Bitmap CaptureWindow(IntPtr windowHandle)
        {
            var windowBounds = GetWindowBounds(windowHandle);
            using var fullWindow = CaptureWindow(windowHandle, windowBounds);
            if (!TryGetVisibleFrameBounds(windowHandle, windowBounds, out var visibleFrame))
            {
                return new Bitmap(fullWindow);
            }

            var crop = new Rectangle(
                visibleFrame.Left - windowBounds.Left,
                visibleFrame.Top - windowBounds.Top,
                visibleFrame.Width,
                visibleFrame.Height);
            if (crop.Left < 0 ||
                crop.Top < 0 ||
                crop.Right > fullWindow.Width ||
                crop.Bottom > fullWindow.Height ||
                crop.Width <= 0 ||
                crop.Height <= 0)
            {
                return new Bitmap(fullWindow);
            }

            return fullWindow.Clone(crop, PixelFormat.Format32bppArgb);
        }

        private static Bitmap CaptureWindow(IntPtr windowHandle, Rectangle bounds)
        {
            if (windowHandle == IntPtr.Zero)
            {
                throw new ArgumentException("A valid window handle is required.", nameof(windowHandle));
            }

            var bitmap = new Bitmap(
                Math.Max(1, bounds.Width),
                Math.Max(1, bounds.Height),
                PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            var deviceContext = graphics.GetHdc();
            try
            {
                if (!PrintWindow(windowHandle, deviceContext, PrintWindowRenderFullContent))
                {
                    throw new InvalidOperationException("Windows could not render a screenshot of the app window.");
                }
            }
            finally
            {
                graphics.ReleaseHdc(deviceContext);
            }

            return bitmap;
        }

        private static Rectangle GetWindowBounds(IntPtr windowHandle)
        {
            if (!GetWindowRect(windowHandle, out var rectangle))
            {
                throw new InvalidOperationException("Windows could not determine the app window bounds.");
            }

            return Rectangle.FromLTRB(
                rectangle.Left,
                rectangle.Top,
                rectangle.Right,
                rectangle.Bottom);
        }

        private static bool TryGetVisibleFrameBounds(
            IntPtr windowHandle,
            Rectangle windowBounds,
            out Rectangle visibleFrame)
        {
            visibleFrame = windowBounds;
            var result = DwmGetWindowAttribute(
                windowHandle,
                DwmWindowAttributeExtendedFrameBounds,
                out var rectangle,
                Marshal.SizeOf<NativeRectangle>());
            if (result != 0)
            {
                return false;
            }

            var candidate = Rectangle.FromLTRB(
                rectangle.Left,
                rectangle.Top,
                rectangle.Right,
                rectangle.Bottom);
            var marginsAreReasonable =
                candidate.Left >= windowBounds.Left &&
                candidate.Top >= windowBounds.Top &&
                candidate.Right <= windowBounds.Right &&
                candidate.Bottom <= windowBounds.Bottom &&
                candidate.Left - windowBounds.Left <= 64 &&
                candidate.Top - windowBounds.Top <= 64 &&
                windowBounds.Right - candidate.Right <= 64 &&
                windowBounds.Bottom - candidate.Bottom <= 64;
            if (!marginsAreReasonable || candidate.Width <= 0 || candidate.Height <= 0)
            {
                return false;
            }

            visibleFrame = candidate;
            return true;
        }

        private static void DrawSoftShadow(Graphics graphics, Rectangle bounds)
        {
            for (var spread = 28; spread >= 4; spread -= 4)
            {
                var alpha = Math.Max(2, 18 - (spread / 2));
                using var brush = new SolidBrush(Color.FromArgb(alpha, 0, 8, 24));
                graphics.FillRectangle(
                    brush,
                    bounds.Left - spread / 2,
                    bounds.Top + 10 - spread / 3,
                    bounds.Width + spread,
                    bounds.Height + spread);
            }
        }

        private void RestoreBackdrop()
        {
            _backdrop.Activate();
            _backdrop.BringToFront();
            Application.DoEvents();
            Thread.Sleep(250);
        }

        private ContextMenuStrip BuildTrayMenu(out ToolStripMenuItem profilesItem)
        {
            var menu = new ContextMenuStrip
            {
                ShowImageMargin = true,
                Font = SystemFonts.MenuFont
            };

            profilesItem = new ToolStripMenuItem(UiText.MenuProfiles);
            profilesItem.DropDownItems.Add(new ToolStripMenuItem(
                UiText.ProfilesActive(_localization.HomeProfile))
            {
                Enabled = false
            });
            profilesItem.DropDownItems.Add(new ToolStripSeparator());
            profilesItem.DropDownItems.Add(new ToolStripMenuItem(UiText.ProfilesAutomatic)
            {
                Checked = true
            });
            profilesItem.DropDownItems.Add(new ToolStripMenuItem(_localization.HomeProfile)
            {
                Checked = true
            });
            profilesItem.DropDownItems.Add(new ToolStripMenuItem(_localization.OfficeProfile));
            profilesItem.DropDownItems.Add(new ToolStripMenuItem(_localization.LabProfile));

            var interfacesItem = new ToolStripMenuItem(UiText.MenuInterfaces);
            interfacesItem.DropDownItems.Add(new ToolStripMenuItem(UiText.InterfacesAutomatic)
            {
                Checked = true
            });
            interfacesItem.DropDownItems.Add(new ToolStripMenuItem("Wi-Fi · Intel(R) Wi-Fi 6 AX201"));
            interfacesItem.DropDownItems.Add(new ToolStripMenuItem("Ethernet · Realtek PCIe GbE"));

            var policiesItem = new ToolStripMenuItem(UiText.MenuPolicies);
            policiesItem.DropDownItems.Add(new ToolStripMenuItem(UiText.PolicyDefaultDisplay));
            policiesItem.DropDownItems.Add(new ToolStripMenuItem(_localization.DirectPolicy));
            policiesItem.DropDownItems.Add(new ToolStripMenuItem(_localization.WorkVpnPolicy));
            policiesItem.DropDownItems.Add(new ToolStripMenuItem(_localization.PrivacyVpnPolicy)
            {
                Checked = true
            });
            policiesItem.DropDownItems.Add(new ToolStripMenuItem(_localization.FamilyPolicy));

            menu.Items.Add(profilesItem);
            menu.Items.Add(interfacesItem);
            menu.Items.Add(policiesItem);
            menu.Items.Add(new ToolStripMenuItem(UiText.MenuSettings));
            menu.Items.Add(new ToolStripMenuItem(UiText.MenuAbout));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem(UiText.MenuExit));
            return menu;
        }

        private static (AppSettings Settings, RouterNetworkBinding HomeNetwork, RouterNetworkBinding LabNetwork)
            CreateSampleSettings(StoreScreenshotLocalization localization)
        {
            var homeNetwork = new RouterNetworkBinding
            {
                NetworkId = "11111111-1111-1111-1111-111111111111",
                NetworkName = localization.HomeNetwork
            };
            var officeNetwork = new RouterNetworkBinding
            {
                NetworkId = "22222222-2222-2222-2222-222222222222",
                NetworkName = localization.OfficeNetwork
            };
            var labNetwork = new RouterNetworkBinding
            {
                NetworkId = "33333333-3333-3333-3333-333333333333",
                NetworkName = localization.LabNetwork
            };

            var home = new RouterProfile
            {
                Id = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                Name = localization.HomeProfile,
                Networks = new List<RouterNetworkBinding> { homeNetwork.Clone() },
                RouterUrl = "http://192.168.1.1/",
                AuthMode = RouterAuthMode.Password,
                Login = "routertray",
                Password = "correct-horse-battery"
            };
            var office = new RouterProfile
            {
                Id = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                Name = localization.OfficeProfile,
                Networks = new List<RouterNetworkBinding> { officeNetwork.Clone() },
                RouterUrl = "https://192.168.50.1/",
                AuthMode = RouterAuthMode.Password,
                Login = "network-user",
                Password = "office-router-password"
            };
            var lab = new RouterProfile
            {
                Id = "cccccccccccccccccccccccccccccccc",
                Name = localization.LabProfile,
                Networks = new List<RouterNetworkBinding> { labNetwork.Clone() },
                RouterUrl = "http://10.0.0.1/",
                AuthMode = RouterAuthMode.AccessToken,
                AccessToken = "rt_demo_5_2_access_token"
            };

            var settings = new AppSettings
            {
                Profiles = new List<RouterProfile> { home, office, lab },
                AutomaticProfileSelection = true,
                SelectedProfileId = home.Id,
                AutoStart = true,
                CheckForUpdatesAutomatically = true,
                UpdateChannel = ApplicationUpdateChannel.Stable,
                ShowPolicyNotifications = true
            };
            settings.NormalizeAndValidate();
            return (settings, homeNetwork, labNetwork);
        }
    }

    private sealed class BackdropForm : Form
    {
        private readonly Image _background;
        private readonly Image _trayIcon;
        private readonly Image _systemTray;

        private const int TraySceneLeft = 860;
        private const int TraySceneTop = 190;
        private const int TraySceneWidth = 960;
        private const int TraySceneBottom = 910;
        public const int TrayTaskbarTop = 830;
        public const int TrayPopupRight = 1454;

        public BackdropForm(string backgroundPath, string trayIconPath, string systemTrayPath)
        {
            _background = Image.FromFile(backgroundPath);
            _trayIcon = Image.FromFile(trayIconPath);
            _systemTray = Image.FromFile(systemTrayPath);
            var screenBounds = Screen.PrimaryScreen?.Bounds ??
                               throw new InvalidOperationException("No primary screen is available.");
            var captureHeight = Math.Min(screenBounds.Height, screenBounds.Width * 9 / 16);
            CaptureBounds = new Rectangle(
                screenBounds.Left,
                screenBounds.Top + ((screenBounds.Height - captureHeight) / 2),
                screenBounds.Width,
                captureHeight);

            Bounds = screenBounds;
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(5, 17, 35);
            DoubleBuffered = true;
        }

        public Rectangle CaptureBounds { get; }

        public Bitmap CreateOutputCanvas(bool includeTaskbar)
        {
            var output = new Bitmap(OutputWidth, OutputHeight, PixelFormat.Format24bppRgb);
            output.SetResolution(96, 96);
            using var graphics = Graphics.FromImage(output);
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(
                _background,
                new Rectangle(0, 0, OutputWidth, OutputHeight),
                new Rectangle(Point.Empty, _background.Size),
                GraphicsUnit.Pixel);
            if (includeTaskbar)
            {
                DrawFocusedTrayScene(graphics);
            }

            return output;
        }

        public void DrawMarketingCopy(
            Graphics graphics,
            string eyebrow,
            string headline,
            string supportingText)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.DrawImage(
                _trayIcon,
                new Rectangle(120, 132, 52, 52),
                new Rectangle(Point.Empty, _trayIcon.Size),
                GraphicsUnit.Pixel);

            using var brandFont = new Font("Segoe UI", 26f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var eyebrowFont = new Font("Segoe UI", 19f, FontStyle.Bold, GraphicsUnit.Pixel);

            const float preferredHeadlineSize = 72f;
            using var preferredHeadlineFont = new Font(
                "Segoe UI",
                preferredHeadlineSize,
                FontStyle.Bold,
                GraphicsUnit.Pixel);
            var widestHeadlineLine = headline
                .Split('\n')
                .Max(line => TextRenderer.MeasureText(
                    line,
                    preferredHeadlineFont,
                    Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width);
            var headlineFontSize = widestHeadlineLine > 610
                ? Math.Max(48f, preferredHeadlineSize * 610f / widestHeadlineLine)
                : preferredHeadlineSize;
            using var headlineFont = new Font(
                "Segoe UI",
                headlineFontSize,
                FontStyle.Bold,
                GraphicsUnit.Pixel);

            TextRenderer.DrawText(
                graphics,
                "RouterTray",
                brandFont,
                new Rectangle(190, 139, 240, 38),
                Color.FromArgb(244, 248, 252),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            var hasEyebrow = !string.IsNullOrWhiteSpace(eyebrow);
            if (hasEyebrow)
            {
                var eyebrowSize = TextRenderer.MeasureText(
                    eyebrow,
                    eyebrowFont,
                    Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                var eyebrowBounds = new Rectangle(120, 280, eyebrowSize.Width + 34, 38);
                using (var pillBrush = new SolidBrush(Color.FromArgb(78, 0, 174, 239)))
                using (var pillPen = new Pen(Color.FromArgb(150, 63, 198, 255), 1f))
                using (var pillPath = CreateRoundedRectanglePath(eyebrowBounds, 19))
                {
                    graphics.FillPath(pillBrush, pillPath);
                    graphics.DrawPath(pillPen, pillPath);
                }

                TextRenderer.DrawText(
                    graphics,
                    eyebrow,
                    eyebrowFont,
                    eyebrowBounds,
                    Color.FromArgb(180, 230, 255),
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix);
            }

            var headlineLineCount = headline.Count(character => character == '\n') + 1;
            var headlineLineHeight = TextRenderer.MeasureText(
                "Ag",
                headlineFont,
                Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Height;
            var headlineBounds = hasEyebrow
                ? new Rectangle(116, 346, 630, 220)
                : new Rectangle(
                    116,
                    272,
                    630,
                    Math.Min(286, (headlineLineCount * headlineLineHeight) + 10));
            var supportingBounds = hasEyebrow
                ? new Rectangle(120, 590, 610, 136)
                : new Rectangle(120, headlineBounds.Bottom + 32, 610, 220);

            const float preferredSupportingSize = 29f;
            using var preferredSupportingFont = new Font(
                "Segoe UI",
                preferredSupportingSize,
                FontStyle.Regular,
                GraphicsUnit.Pixel);
            var preferredSupportingHeight = TextRenderer.MeasureText(
                supportingText,
                preferredSupportingFont,
                supportingBounds.Size,
                TextFormatFlags.WordBreak |
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix).Height;
            var supportingFontSize = preferredSupportingHeight > supportingBounds.Height
                ? Math.Max(
                    22f,
                    preferredSupportingSize * supportingBounds.Height / preferredSupportingHeight)
                : preferredSupportingSize;
            using var supportingFont = new Font(
                "Segoe UI",
                supportingFontSize,
                FontStyle.Regular,
                GraphicsUnit.Pixel);

            TextRenderer.DrawText(
                graphics,
                headline,
                headlineFont,
                headlineBounds,
                Color.White,
                TextFormatFlags.Left |
                TextFormatFlags.Top |
                TextFormatFlags.NoPrefix |
                TextFormatFlags.NoPadding);
            TextRenderer.DrawText(
                graphics,
                supportingText,
                supportingFont,
                supportingBounds,
                Color.FromArgb(205, 219, 235),
                TextFormatFlags.Left |
                TextFormatFlags.Top |
                TextFormatFlags.WordBreak |
                TextFormatFlags.NoPrefix);
        }

        private void DrawFocusedTrayScene(Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var sceneBounds = Rectangle.FromLTRB(
                TraySceneLeft,
                TraySceneTop,
                TraySceneLeft + TraySceneWidth,
                TraySceneBottom);

            for (var spread = 34; spread >= 6; spread -= 4)
            {
                var alpha = Math.Max(2, 20 - spread / 2);
                using var shadowBrush = new SolidBrush(Color.FromArgb(alpha, 0, 7, 20));
                using var shadowPath = CreateRoundedRectanglePath(
                    Rectangle.Inflate(sceneBounds, spread / 2, spread / 2),
                    30 + spread / 2);
                graphics.FillPath(shadowBrush, shadowPath);
            }

            using var scenePath = CreateRoundedRectanglePath(sceneBounds, 26);
            var graphicsState = graphics.Save();
            graphics.SetClip(scenePath);
            graphics.DrawImage(
                _background,
                sceneBounds,
                GetCoverSourceRectangle(_background, sceneBounds),
                GraphicsUnit.Pixel);
            using (var tintBrush = new LinearGradientBrush(
                       sceneBounds,
                       Color.FromArgb(22, 1, 12, 31),
                       Color.FromArgb(60, 0, 22, 55),
                       LinearGradientMode.Vertical))
            {
                graphics.FillRectangle(tintBrush, sceneBounds);
            }

            var taskbar = Rectangle.FromLTRB(
                TraySceneLeft,
                TrayTaskbarTop,
                TraySceneLeft + TraySceneWidth,
                TraySceneBottom);
            graphics.DrawImage(
                _systemTray,
                taskbar,
                new Rectangle(Point.Empty, _systemTray.Size),
                GraphicsUnit.Pixel);

            graphics.Restore(graphicsState);
            using var borderPen = new Pen(Color.FromArgb(108, 113, 176, 224), 1.25f);
            graphics.DrawPath(borderPen, scenePath);
        }

        private static Rectangle GetCoverSourceRectangle(Image image, Rectangle destination)
        {
            var destinationAspect = destination.Width / (float)destination.Height;
            var imageAspect = image.Width / (float)image.Height;
            if (imageAspect > destinationAspect)
            {
                var sourceWidth = Math.Max(1, (int)Math.Round(image.Height * destinationAspect));
                return new Rectangle((image.Width - sourceWidth) / 2, 0, sourceWidth, image.Height);
            }

            var sourceHeight = Math.Max(1, (int)Math.Round(image.Width / destinationAspect));
            return new Rectangle(0, (image.Height - sourceHeight) / 2, image.Width, sourceHeight);
        }

        private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
        {
            var diameter = Math.Max(2, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.DrawImage(
                _background,
                new Rectangle(
                    CaptureBounds.Left - Left,
                    CaptureBounds.Top - Top,
                    CaptureBounds.Width,
                    CaptureBounds.Height));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _background.Dispose();
                _trayIcon.Dispose();
                _systemTray.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private const uint PrintWindowRenderFullContent = 0x00000002;
    private const uint WmCancelMode = 0x001F;
    private const int DwmWindowAttributeExtendedFrameBounds = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(
        IntPtr windowHandle,
        IntPtr deviceContext,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        IntPtr windowHandle,
        out NativeRectangle rectangle);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        out NativeRectangle attributeValue,
        int attributeSize);

}
