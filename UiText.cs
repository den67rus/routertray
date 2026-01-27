using System.Globalization;
using System.Resources;

namespace RouterTray;

internal static class UiText
{
    private static readonly ResourceManager ResourceManager =
        new("RouterTray.Resources.Strings", typeof(UiText).Assembly);
    private const string PolicySetMessageFormatKey = "PolicySetMessageFormat";

    public static string AppName => Get(nameof(AppName));
    public static string AppConfigLoadFailedMessage => Get(nameof(AppConfigLoadFailedMessage));

    public static string MenuInterfaces => Get(nameof(MenuInterfaces));
    public static string MenuPolicies => Get(nameof(MenuPolicies));
    public static string MenuSettings => Get(nameof(MenuSettings));
    public static string MenuAbout => Get(nameof(MenuAbout));
    public static string MenuExit => Get(nameof(MenuExit));
    public static string Loading => Get(nameof(Loading));

    public static string InterfacesNone => Get(nameof(InterfacesNone));
    public static string InterfacesLoadFailedMenu => Get(nameof(InterfacesLoadFailedMenu));
    public static string InterfacesLoadFailedMessage => Get(nameof(InterfacesLoadFailedMessage));

    public static string PoliciesNone => Get(nameof(PoliciesNone));
    public static string PoliciesLoadFailedMenu => Get(nameof(PoliciesLoadFailedMenu));

    public static string PolicyDefaultDisplay => Get(nameof(PolicyDefaultDisplay));
    public static string PolicyTitle => Get(nameof(PolicyTitle));

    public static string SettingsTitle => Get(nameof(SettingsTitle));
    public static string SettingsLogin => Get(nameof(SettingsLogin));
    public static string SettingsPassword => Get(nameof(SettingsPassword));
    public static string SettingsShowPassword => Get(nameof(SettingsShowPassword));
    public static string SettingsAutoStart => Get(nameof(SettingsAutoStart));
    public static string SettingsShowPolicyNotifications => Get(nameof(SettingsShowPolicyNotifications));
    public static string SettingsSave => Get(nameof(SettingsSave));
    public static string SettingsCancel => Get(nameof(SettingsCancel));
    public static string SettingsValidationMessage => Get(nameof(SettingsValidationMessage));
    public static string SettingsSavedMessage => Get(nameof(SettingsSavedMessage));
    public static string SettingsSaveFailedMessage => Get(nameof(SettingsSaveFailedMessage));

    public static string AboutTitle => Get(nameof(AboutTitle));
    public static string AboutVersionFormat => Get(nameof(AboutVersionFormat));
    public static string AboutDescription => Get(nameof(AboutDescription));
    public static string AboutOk => Get(nameof(AboutOk));
    public static string AboutLicenseText => Get(nameof(AboutLicenseText));
    public static string AboutLicenseUrl => Get(nameof(AboutLicenseUrl));
    public static string AboutWebsiteText => Get(nameof(AboutWebsiteText));
    public static string AboutWebsiteUrl => Get(nameof(AboutWebsiteUrl));
    public static string AboutCopyright => Get(nameof(AboutCopyright));

    public static string AuthFailedMessage => Get(nameof(AuthFailedMessage));
    public static string RequestTimeoutMessage => Get(nameof(RequestTimeoutMessage));
    public static string RouterUnreachableMessage => Get(nameof(RouterUnreachableMessage));
    public static string RouterApiErrorMessage => Get(nameof(RouterApiErrorMessage));
    public static string UnexpectedErrorMessage => Get(nameof(UnexpectedErrorMessage));

    public static string AutoStartEnabledMessage => Get(nameof(AutoStartEnabledMessage));
    public static string AutoStartDisabledMessage => Get(nameof(AutoStartDisabledMessage));
    public static string AutoStartFailedMessage => Get(nameof(AutoStartFailedMessage));

    public static string PolicySetMessage(string policyName)
    {
        return string.Format(
            CultureInfo.CurrentUICulture,
            Get(PolicySetMessageFormatKey),
            policyName);
    }

    public static string AboutVersion(string version)
    {
        return string.Format(
            CultureInfo.CurrentUICulture,
            AboutVersionFormat,
            version);
    }

    private static string Get(string key)
    {
        return ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    }
}
