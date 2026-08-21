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
    public static string SettingsUpdatesTab => Get(nameof(SettingsUpdatesTab));
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
    public static string SettingsCheckForUpdates => Get(nameof(SettingsCheckForUpdates));
    public static string SettingsCheckForUpdatesNow => Get(nameof(SettingsCheckForUpdatesNow));
    public static string SettingsUpdateChannel => Get(nameof(SettingsUpdateChannel));
    public static string SettingsUpdateChannelStable => Get(nameof(SettingsUpdateChannelStable));
    public static string SettingsUpdateChannelPreview => Get(nameof(SettingsUpdateChannelPreview));
    public static string SettingsUpdateScheduleDescription => Get(nameof(SettingsUpdateScheduleDescription));
    public static string SettingsStoreUpdatesDescription => Get(nameof(SettingsStoreUpdatesDescription));
    public static string SettingsUpdateCheckInProgress => Get(nameof(SettingsUpdateCheckInProgress));
    public static string SettingsUpdateUpToDate => Get(nameof(SettingsUpdateUpToDate));
    public static string SettingsUpdateReady => Get(nameof(SettingsUpdateReady));
    public static string SettingsUpdateCheckUnavailable => Get(nameof(SettingsUpdateCheckUnavailable));
    public static string SettingsUpdateCheckFailed => Get(nameof(SettingsUpdateCheckFailed));
    public static string SettingsShowPolicyNotifications => Get(nameof(SettingsShowPolicyNotifications));
    public static string SettingsSave => Get(nameof(SettingsSave));
    public static string SettingsCancel => Get(nameof(SettingsCancel));
    public static string SettingsValidationMessage => Get(nameof(SettingsValidationMessage));
    public static string SettingsAccessTokenValidationMessage => Get(nameof(SettingsAccessTokenValidationMessage));
    public static string SettingsSavedMessage => Get(nameof(SettingsSavedMessage));
    public static string SettingsSaveFailedMessage => Get(nameof(SettingsSaveFailedMessage));

    public static string SetupTitle => Get(nameof(SetupTitle));
    public static string SetupAddProfileTitle => Get(nameof(SetupAddProfileTitle));
    public static string SetupSidebarCaption => Get(nameof(SetupSidebarCaption));
    public static string SetupAddProfileSidebarCaption => Get(nameof(SetupAddProfileSidebarCaption));
    public static string SetupStepWelcome => Get(nameof(SetupStepWelcome));
    public static string SetupStepRouter => Get(nameof(SetupStepRouter));
    public static string SetupStepAuthentication => Get(nameof(SetupStepAuthentication));
    public static string SetupStepDevice => Get(nameof(SetupStepDevice));
    public static string SetupStepFinish => Get(nameof(SetupStepFinish));
    public static string SetupWelcomeTitle => Get(nameof(SetupWelcomeTitle));
    public static string SetupWelcomeSubtitle => Get(nameof(SetupWelcomeSubtitle));
    public static string SetupAddProfileWelcomeTitle => Get(nameof(SetupAddProfileWelcomeTitle));
    public static string SetupAddProfileWelcomeSubtitle => Get(nameof(SetupAddProfileWelcomeSubtitle));
    public static string SetupWelcomeBeforeTitle => Get(nameof(SetupWelcomeBeforeTitle));
    public static string SetupWelcomeRouterTitle => Get(nameof(SetupWelcomeRouterTitle));
    public static string SetupWelcomeRouterDescription => Get(nameof(SetupWelcomeRouterDescription));
    public static string SetupWelcomePoliciesTitle => Get(nameof(SetupWelcomePoliciesTitle));
    public static string SetupWelcomePoliciesDescription => Get(nameof(SetupWelcomePoliciesDescription));
    public static string SetupWelcomeCredentialsTitle => Get(nameof(SetupWelcomeCredentialsTitle));
    public static string SetupWelcomeCredentialsDescription => Get(nameof(SetupWelcomeCredentialsDescription));
    public static string SetupWelcomeSecurityNote => Get(nameof(SetupWelcomeSecurityNote));
    public static string SetupRouterTitle => Get(nameof(SetupRouterTitle));
    public static string SetupRouterSubtitle => Get(nameof(SetupRouterSubtitle));
    public static string SetupDefaultProfileName => Get(nameof(SetupDefaultProfileName));
    public static string SetupAutomaticAddress => Get(nameof(SetupAutomaticAddress));
    public static string SetupAutomaticAddressDescription => Get(nameof(SetupAutomaticAddressDescription));
    public static string SetupDetectingNetwork => Get(nameof(SetupDetectingNetwork));
    public static string SetupRouterNotDetected => Get(nameof(SetupRouterNotDetected));
    public static string SetupManualAddress => Get(nameof(SetupManualAddress));
    public static string SetupManualAddressDescription => Get(nameof(SetupManualAddressDescription));
    public static string SetupBindCurrentNetwork => Get(nameof(SetupBindCurrentNetwork));
    public static string SetupNetworkUnavailable => Get(nameof(SetupNetworkUnavailable));
    public static string SetupAuthenticationTitle => Get(nameof(SetupAuthenticationTitle));
    public static string SetupAuthenticationSubtitle => Get(nameof(SetupAuthenticationSubtitle));
    public static string SetupCredentialsInstruction => Get(nameof(SetupCredentialsInstruction));
    public static string SetupPasswordMethodDescription => Get(nameof(SetupPasswordMethodDescription));
    public static string SetupTokenMethodDescription => Get(nameof(SetupTokenMethodDescription));
    public static string SetupOpenRouter => Get(nameof(SetupOpenRouter));
    public static string SetupDeviceTitle => Get(nameof(SetupDeviceTitle));
    public static string SetupDeviceSubtitle => Get(nameof(SetupDeviceSubtitle));
    public static string SetupDeviceCurrentTitle => Get(nameof(SetupDeviceCurrentTitle));
    public static string SetupDeviceAdapter => Get(nameof(SetupDeviceAdapter));
    public static string SetupDeviceMacAddress => Get(nameof(SetupDeviceMacAddress));
    public static string SetupDeviceUnavailable => Get(nameof(SetupDeviceUnavailable));
    public static string SetupTemporaryMacWarning => Get(nameof(SetupTemporaryMacWarning));
    public static string SetupTemporaryMacAcknowledge => Get(nameof(SetupTemporaryMacAcknowledge));
    public static string SetupOpenWifiSettings => Get(nameof(SetupOpenWifiSettings));
    public static string SetupDeviceRegistrationTitle => Get(nameof(SetupDeviceRegistrationTitle));
    public static string SetupDeviceRegistrationDescription => Get(nameof(SetupDeviceRegistrationDescription));
    public static string SetupDeviceName => Get(nameof(SetupDeviceName));
    public static string SetupDeviceNotChecked => Get(nameof(SetupDeviceNotChecked));
    public static string SetupCheckingDevice => Get(nameof(SetupCheckingDevice));
    public static string SetupDeviceRegisteredNoName => Get(nameof(SetupDeviceRegisteredNoName));
    public static string SetupDeviceNotRegistered => Get(nameof(SetupDeviceNotRegistered));
    public static string SetupRegisterDevice => Get(nameof(SetupRegisterDevice));
    public static string SetupRegisteringDevice => Get(nameof(SetupRegisteringDevice));
    public static string SetupRecheckDevice => Get(nameof(SetupRecheckDevice));
    public static string SetupDeviceRegistrationRequired => Get(nameof(SetupDeviceRegistrationRequired));
    public static string SetupTemporaryMacConfirmationRequired => Get(nameof(SetupTemporaryMacConfirmationRequired));
    public static string SetupDeviceNameRequired => Get(nameof(SetupDeviceNameRequired));
    public static string SetupDeviceNoMac => Get(nameof(SetupDeviceNoMac));
    public static string SetupDeviceRegistrationFailed => Get(nameof(SetupDeviceRegistrationFailed));
    public static string SetupFinishTitle => Get(nameof(SetupFinishTitle));
    public static string SetupFinishSubtitle => Get(nameof(SetupFinishSubtitle));
    public static string SetupSummaryRouter => Get(nameof(SetupSummaryRouter));
    public static string SetupSummaryAuthentication => Get(nameof(SetupSummaryAuthentication));
    public static string SetupSummaryNetwork => Get(nameof(SetupSummaryNetwork));
    public static string SetupSummaryDevice => Get(nameof(SetupSummaryDevice));
    public static string SetupAutomaticAddressUnavailable => Get(nameof(SetupAutomaticAddressUnavailable));
    public static string SetupNetworkNotBound => Get(nameof(SetupNetworkNotBound));
    public static string SetupConnectionCheckTitle => Get(nameof(SetupConnectionCheckTitle));
    public static string SetupConnectionCheckDescription => Get(nameof(SetupConnectionCheckDescription));
    public static string SetupConnectionNotChecked => Get(nameof(SetupConnectionNotChecked));
    public static string SetupTestConnection => Get(nameof(SetupTestConnection));
    public static string SetupTestingConnection => Get(nameof(SetupTestingConnection));
    public static string SetupConnectionSuccessNoPolicies => Get(nameof(SetupConnectionSuccessNoPolicies));
    public static string SetupConnectionNoEndpoint => Get(nameof(SetupConnectionNoEndpoint));
    public static string SetupConnectionAuthFailed => Get(nameof(SetupConnectionAuthFailed));
    public static string SetupConnectionUnreachable => Get(nameof(SetupConnectionUnreachable));
    public static string SetupConnectionTimeout => Get(nameof(SetupConnectionTimeout));
    public static string SetupConnectionApiFailed => Get(nameof(SetupConnectionApiFailed));
    public static string SetupConnectionDeviceNotRegistered => Get(nameof(SetupConnectionDeviceNotRegistered));
    public static string SetupTrayHint => Get(nameof(SetupTrayHint));
    public static string SetupBack => Get(nameof(SetupBack));
    public static string SetupNext => Get(nameof(SetupNext));
    public static string SetupFinish => Get(nameof(SetupFinish));
    public static string SetupAddProfileFinish => Get(nameof(SetupAddProfileFinish));
    public static string SetupCancel => Get(nameof(SetupCancel));
    public static string SetupCancelConfirmation => Get(nameof(SetupCancelConfirmation));
    public static string SetupAddProfileCancelConfirmation =>
        Get(nameof(SetupAddProfileCancelConfirmation));

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

    public static string SetupRouterDetected(string address) =>
        Format("SetupRouterDetectedFormat", address);

    public static string SetupCurrentNetwork(string networkName) =>
        Format("SetupCurrentNetworkFormat", networkName);

    public static string SetupProgress(int currentStep, int stepCount) =>
        Format("SetupProgressFormat", currentStep, stepCount);

    public static string SetupAutomaticAddressSummary(string address) =>
        Format("SetupAutomaticAddressSummaryFormat", address);

    public static string SetupConnectionSuccess(int policyCount) =>
        Format("SetupConnectionSuccessFormat", policyCount);

    public static string SetupDeviceRegistered(string deviceName) =>
        Format("SetupDeviceRegisteredFormat", deviceName);

    private static string Format(string key, params object[] arguments)
    {
        return string.Format(CultureInfo.CurrentUICulture, Get(key), arguments);
    }

    private static string Get(string key)
    {
        return ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    }
}
