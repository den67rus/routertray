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
    public static string AppConfigRecoveredMessage => Get(nameof(AppConfigRecoveredMessage));

    public static string MenuInterfaces => Get(nameof(MenuInterfaces));
    public static string MenuProfiles => Get(nameof(MenuProfiles));
    public static string MenuPolicies => Get(nameof(MenuPolicies));
    public static string MenuSettings => Get(nameof(MenuSettings));
    public static string MenuAbout => Get(nameof(MenuAbout));
    public static string MenuExit => Get(nameof(MenuExit));
    public static string Loading => Get(nameof(Loading));

    public static string InterfacesNone => Get(nameof(InterfacesNone));
    public static string InterfacesAutomatic => Get(nameof(InterfacesAutomatic));
    public static string InterfacesLoadFailedMenu => Get(nameof(InterfacesLoadFailedMenu));
    public static string InterfacesLoadFailedMessage => Get(nameof(InterfacesLoadFailedMessage));

    public static string ProfilesAutomatic => Get(nameof(ProfilesAutomatic));
    public static string ProfilesNoneActive => Get(nameof(ProfilesNoneActive));

    public static string PoliciesNone => Get(nameof(PoliciesNone));
    public static string PoliciesLoadFailedMenu => Get(nameof(PoliciesLoadFailedMenu));

    public static string PolicyDefaultDisplay => Get(nameof(PolicyDefaultDisplay));
    public static string PolicyTitle => Get(nameof(PolicyTitle));

    public static string SettingsTitle => Get(nameof(SettingsTitle));
    public static string SettingsProfilesTab => Get(nameof(SettingsProfilesTab));
    public static string SettingsApplicationTab => Get(nameof(SettingsApplicationTab));
    public static string SettingsProfile => Get(nameof(SettingsProfile));
    public static string SettingsProfileName => Get(nameof(SettingsProfileName));
    public static string SettingsProfileAdd => Get(nameof(SettingsProfileAdd));
    public static string SettingsProfileRemove => Get(nameof(SettingsProfileRemove));
    public static string SettingsUnnamedProfile => Get(nameof(SettingsUnnamedProfile));
    public static string SettingsConnectionSection => Get(nameof(SettingsConnectionSection));
    public static string SettingsAuthenticationSection => Get(nameof(SettingsAuthenticationSection));
    public static string SettingsProfileNetworks => Get(nameof(SettingsProfileNetworks));
    public static string SettingsCurrentNetworkUnavailable => Get(nameof(SettingsCurrentNetworkUnavailable));
    public static string SettingsBindCurrentNetwork => Get(nameof(SettingsBindCurrentNetwork));
    public static string SettingsRemoveNetwork => Get(nameof(SettingsRemoveNetwork));
    public static string SettingsAutomaticProfileSelection => Get(nameof(SettingsAutomaticProfileSelection));
    public static string SettingsProfileNameValidationMessage => Get(nameof(SettingsProfileNameValidationMessage));
    public static string SettingsProfileDuplicateNameMessage => Get(nameof(SettingsProfileDuplicateNameMessage));
    public static string SettingsCannotRemoveLastProfileMessage => Get(nameof(SettingsCannotRemoveLastProfileMessage));
    public static string SettingsRouterUrl => Get(nameof(SettingsRouterUrl));
    public static string SettingsRouterUrlHint => Get(nameof(SettingsRouterUrlHint));
    public static string SettingsRouterUrlValidationMessage => Get(nameof(SettingsRouterUrlValidationMessage));
    public static string SettingsAuthMode => Get(nameof(SettingsAuthMode));
    public static string SettingsAuthModePassword => Get(nameof(SettingsAuthModePassword));
    public static string SettingsAuthModeAccessToken => Get(nameof(SettingsAuthModeAccessToken));
    public static string SettingsLogin => Get(nameof(SettingsLogin));
    public static string SettingsPassword => Get(nameof(SettingsPassword));
    public static string SettingsShowPassword => Get(nameof(SettingsShowPassword));
    public static string SettingsAccessToken => Get(nameof(SettingsAccessToken));
    public static string SettingsShowAccessToken => Get(nameof(SettingsShowAccessToken));
    public static string SettingsAutoStart => Get(nameof(SettingsAutoStart));
    public static string SettingsShowPolicyNotifications => Get(nameof(SettingsShowPolicyNotifications));
    public static string SettingsSave => Get(nameof(SettingsSave));
    public static string SettingsCancel => Get(nameof(SettingsCancel));
    public static string SettingsValidationMessage => Get(nameof(SettingsValidationMessage));
    public static string SettingsAccessTokenValidationMessage => Get(nameof(SettingsAccessTokenValidationMessage));
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
    public static string RouterEndpointUnavailableMessage => Get(nameof(RouterEndpointUnavailableMessage));
    public static string RouterProfileUnavailableMessage => Get(nameof(RouterProfileUnavailableMessage));
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

    public static string ProfilesActive(string profileName) =>
        Format("ProfilesActiveFormat", profileName);

    public static string ProfileChangedMessage(string profileName) =>
        Format("ProfileChangedMessageFormat", profileName);

    public static string SettingsCurrentNetwork(string networkName) =>
        Format("SettingsCurrentNetworkFormat", networkName);

    public static string SettingsNewProfileName(int number) =>
        Format("SettingsNewProfileNameFormat", number);

    public static string SettingsProfileValidation(string profileName, string message) =>
        Format("SettingsProfileValidationFormat", profileName, message);

    public static string SettingsRemoveProfileConfirmation(string profileName) =>
        Format("SettingsRemoveProfileConfirmationFormat", profileName);

    public static string SettingsMoveNetworkConfirmation(string profileName) =>
        Format("SettingsMoveNetworkConfirmationFormat", profileName);

    private static string Format(string key, params object[] arguments)
    {
        return string.Format(CultureInfo.CurrentUICulture, Get(key), arguments);
    }

    private static string Get(string key)
    {
        return ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    }
}
