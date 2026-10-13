using System.Drawing;

namespace RouterTray;

internal static class AppIconProvider
{
    private const string IconResourceName = "RouterTray.favicon.ico";
    private static readonly Lazy<Icon?> CachedIcon = new(LoadIcon);

    public static Icon CreateIcon()
    {
        return (Icon)(CachedIcon.Value ?? SystemIcons.Application).Clone();
    }

    private static Icon? LoadIcon()
    {
        try
        {
            using var stream = typeof(AppIconProvider).Assembly.GetManifestResourceStream(IconResourceName);
            if (stream is not null)
            {
                using var resourceIcon = new Icon(stream);
                return (Icon)resourceIcon.Clone();
            }
        }
        catch
        {
            // Fall back to the native executable icon below.
        }

        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            return null;
        }
    }
}
