using System.Runtime.InteropServices;

namespace RouterTray;

internal static class AppInstallation
{
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private const int AppModelErrorNoPackage = 15700;

    public static bool UsesPackageManagedUpdates { get; } = DetectPackageIdentity();

    private static bool DetectPackageIdentity()
    {
#if MICROSOFT_STORE
        // Keep Store behavior when a package layout is launched during development,
        // before it has been registered and received package identity.
        return true;
#else
        try
        {
            uint packageFullNameLength = 0;
            var result = GetCurrentPackageFullName(ref packageFullNameLength, IntPtr.Zero);
            return ProbeResultHasPackageIdentity(result);
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
#endif
    }

    internal static bool ProbeResultHasPackageIdentity(int result) => result switch
    {
        ErrorSuccess or ErrorInsufficientBuffer => true,
        AppModelErrorNoPackage => false,
        _ => false
    };

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(
        ref uint packageFullNameLength,
        IntPtr packageFullName);
}
