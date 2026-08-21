namespace RouterTray.Tests;

public sealed class FirstRunSetupFormTests
{
    [Fact]
    public void CreateNewProfileDraft_PreservesSourceAndSelectsUniqueProfile()
    {
        var settings = new AppSettings();
        settings.Profiles[0].Name = "Home";
        settings.Profiles.Add(new RouterProfile
        {
            Name = UiText.SettingsNewProfileName(3)
        });
        settings.SelectedProfileId = settings.Profiles[0].Id;

        var (draft, newProfile) = FirstRunSetupForm.CreateNewProfileDraft(settings);

        Assert.Equal(2, settings.Profiles.Count);
        Assert.Equal(settings.Profiles[0].Id, settings.SelectedProfileId);
        Assert.Equal(3, draft.Profiles.Count);
        Assert.Same(newProfile, draft.Profiles[^1]);
        Assert.Equal(newProfile.Id, draft.SelectedProfileId);
        Assert.DoesNotContain(
            draft.Profiles.Take(draft.Profiles.Count - 1),
            profile => string.Equals(
                profile.Name,
                newProfile.Name,
                StringComparison.OrdinalIgnoreCase));
        Assert.True(Guid.TryParseExact(newProfile.Id, "D", out _));
    }

    [Fact]
    public void IsProfileNameDuplicate_IgnoresCurrentProfileAndMatchesTrimmedName()
    {
        var currentProfile = new RouterProfile { Name = "Home" };
        var otherProfile = new RouterProfile { Name = "Office" };
        var profiles = new[] { currentProfile, otherProfile };

        Assert.False(FirstRunSetupForm.IsProfileNameDuplicate(
            profiles,
            currentProfile,
            " home "));
        Assert.True(FirstRunSetupForm.IsProfileNameDuplicate(
            profiles,
            currentProfile,
            " office "));
        Assert.False(FirstRunSetupForm.IsProfileNameDuplicate(
            profiles,
            currentProfile,
            "Travel"));
    }

    [Fact]
    public void ResolveNetworkLookupUri_UsesManualRouterAndSkipsAutomaticAddress()
    {
        Assert.Null(FirstRunSetupForm.ResolveNetworkLookupUri(
            automaticAddress: true,
            "https://router.example"));

        var uri = FirstRunSetupForm.ResolveNetworkLookupUri(
            automaticAddress: false,
            "https://router.example");

        Assert.Equal("https://router.example/", uri?.AbsoluteUri);
    }

    [Theory]
    [InlineData(false, false, true, false, false, false)]
    [InlineData(true, false, true, true, true, true)]
    public void ResolveNetworkBindingChoice_PreservesRememberedChoice(
        bool rememberedChoice,
        bool isAlreadyBound,
        bool profileHasNoBindings,
        bool isAddingProfile,
        bool isBoundToAnotherProfile,
        bool expected)
    {
        var actual = FirstRunSetupForm.ResolveNetworkBindingChoice(
            rememberedChoice,
            isAlreadyBound,
            profileHasNoBindings,
            isAddingProfile,
            isBoundToAnotherProfile);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(false, true, false, false, true)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, false, true, true, true)]
    public void ResolveNetworkBindingChoice_ComputesDefaultForUnseenNetwork(
        bool isAlreadyBound,
        bool profileHasNoBindings,
        bool isAddingProfile,
        bool isBoundToAnotherProfile,
        bool expected)
    {
        var actual = FirstRunSetupForm.ResolveNetworkBindingChoice(
            rememberedChoice: null,
            isAlreadyBound,
            profileHasNoBindings,
            isAddingProfile,
            isBoundToAnotherProfile);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void PendingNetworkBindingMove_RestoresBindingAfterActiveNetworkChanges()
    {
        var sourceProfile = new RouterProfile { Name = "Home" };
        var newProfile = new RouterProfile { Name = "Travel" };
        var displacedBinding = new RouterNetworkBinding
        {
            NetworkId = Guid.NewGuid().ToString("D"),
            NetworkName = "Home Wi-Fi"
        };
        var settings = new AppSettings
        {
            Profiles = new List<RouterProfile> { sourceProfile, newProfile },
            SelectedProfileId = newProfile.Id
        };
        var pendingMove = new PendingNetworkBindingMove(
            sourceProfile.Id,
            displacedBinding);
        newProfile.Networks.Add(displacedBinding.Clone());

        var unchanged = pendingMove.TryRestoreAfterNetworkChange(
            settings,
            newProfile,
            displacedBinding.NetworkId);

        Assert.False(unchanged);
        Assert.Empty(sourceProfile.Networks);
        Assert.True(newProfile.IsBoundTo(displacedBinding.NetworkId));

        var restored = pendingMove.TryRestoreAfterNetworkChange(
            settings,
            newProfile,
            Guid.NewGuid().ToString("D"));

        Assert.True(restored);
        var restoredBinding = Assert.Single(sourceProfile.Networks);
        Assert.Equal(displacedBinding.NetworkId, restoredBinding.NetworkId);
        Assert.Equal(displacedBinding.NetworkName, restoredBinding.NetworkName);
        Assert.False(newProfile.IsBoundTo(displacedBinding.NetworkId));
    }
}
