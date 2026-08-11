namespace RouterTray.Tests;

public sealed class RouterProfileSelectorTests
{
    [Fact]
    public void Resolve_AutomaticModePrefersProfileBoundToActiveNetwork()
    {
        var homeNetworkId = Guid.NewGuid().ToString("D");
        var workNetworkId = Guid.NewGuid().ToString("D");
        var home = CreateProfile("Home", homeNetworkId);
        var work = CreateProfile("Work", workNetworkId);
        var settings = CreateSettings(home, work);
        var snapshot = CreateSnapshot(
            CreateInterface("home-interface", homeNetworkId, isActive: true),
            CreateInterface("work-interface", workNetworkId, isActive: false));

        var result = RouterProfileSelector.Resolve(settings, snapshot);

        Assert.Same(home, result.Profile);
        Assert.Equal("home-interface", result.MatchedInterfaceId);
    }

    [Fact]
    public void Resolve_AutomaticModeUsesOnlyMatchingConnectedProfile()
    {
        var home = CreateProfile("Home", Guid.NewGuid().ToString("D"));
        var workNetworkId = Guid.NewGuid().ToString("D");
        var work = CreateProfile("Work", workNetworkId);
        var settings = CreateSettings(home, work);
        var snapshot = CreateSnapshot(
            CreateInterface("unknown-interface", Guid.NewGuid().ToString("D"), isActive: true),
            CreateInterface("work-interface", workNetworkId, isActive: false));

        var result = RouterProfileSelector.Resolve(settings, snapshot);

        Assert.Same(work, result.Profile);
        Assert.Equal("work-interface", result.MatchedInterfaceId);
    }

    [Fact]
    public void Resolve_AutomaticModeRejectsAmbiguousNonActiveMatches()
    {
        var homeNetworkId = Guid.NewGuid().ToString("D");
        var workNetworkId = Guid.NewGuid().ToString("D");
        var settings = CreateSettings(
            CreateProfile("Home", homeNetworkId),
            CreateProfile("Work", workNetworkId));
        var snapshot = CreateSnapshot(
            CreateInterface("unknown-interface", Guid.NewGuid().ToString("D"), isActive: true),
            CreateInterface("home-interface", homeNetworkId, isActive: false),
            CreateInterface("work-interface", workNetworkId, isActive: false));

        var result = RouterProfileSelector.Resolve(settings, snapshot);

        Assert.Null(result.Profile);
        Assert.Null(result.MatchedInterfaceId);
    }

    [Fact]
    public void Resolve_ManualModeUsesSelectedProfileRegardlessOfNetwork()
    {
        var home = CreateProfile("Home", Guid.NewGuid().ToString("D"));
        var work = CreateProfile("Work", Guid.NewGuid().ToString("D"));
        var settings = CreateSettings(home, work);
        settings.AutomaticProfileSelection = false;
        settings.SelectedProfileId = work.Id;
        var snapshot = CreateSnapshot(
            CreateInterface("home-interface", home.Networks[0].NetworkId, isActive: true));

        var result = RouterProfileSelector.Resolve(settings, snapshot);

        Assert.Same(work, result.Profile);
        Assert.Null(result.MatchedInterfaceId);
    }

    [Fact]
    public void Resolve_UnboundSingleProfilePreservesLegacyBehaviour()
    {
        var profile = CreateProfile("Default");
        var settings = CreateSettings(profile);
        var snapshot = CreateSnapshot(
            CreateInterface("active-interface", Guid.NewGuid().ToString("D"), isActive: true));

        var result = RouterProfileSelector.Resolve(settings, snapshot);

        Assert.Same(profile, result.Profile);
        Assert.Equal("active-interface", result.MatchedInterfaceId);
    }

    [Fact]
    public void Resolve_BoundSingleProfileDoesNotMatchUnknownNetwork()
    {
        var profile = CreateProfile("Home", Guid.NewGuid().ToString("D"));
        var settings = CreateSettings(profile);
        var snapshot = CreateSnapshot(
            CreateInterface("active-interface", Guid.NewGuid().ToString("D"), isActive: true));

        var result = RouterProfileSelector.Resolve(settings, snapshot);

        Assert.Null(result.Profile);
    }

    [Fact]
    public async Task Convergence_RetriesMissingProfileDuringStartup()
    {
        var homeNetworkId = Guid.NewGuid().ToString("D");
        var home = CreateProfile("Home", homeNetworkId);
        var work = CreateProfile("Work", Guid.NewGuid().ToString("D"));
        var settings = CreateSettings(home, work);
        var unknownNetworkId = Guid.NewGuid().ToString("D");
        var snapshots = new Queue<InterfaceSnapshot>(new[]
        {
            CreateSnapshot(CreateInterface("wifi", unknownNetworkId, isActive: true)),
            CreateSnapshot(CreateInterface("wifi", unknownNetworkId, isActive: true)),
            CreateSnapshot(CreateInterface("wifi", homeNetworkId, isActive: true)),
            CreateSnapshot(CreateInterface("wifi", homeNetworkId, isActive: true))
        });
        var requestCount = 0;

        var result = await RouterProfileConvergence.ResolveAsync(
            _ =>
            {
                requestCount++;
                return Task.FromResult(snapshots.Dequeue());
            },
            snapshot => RouterProfileSelector.Resolve(settings, snapshot),
            maximumAttempts: 6,
            requiredStableSamples: 2,
            minimumAttempts: 2,
            retryDelay: TimeSpan.Zero,
            isCurrent: null,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Same(home, result.Profile);
        Assert.Equal(4, requestCount);
    }

    [Fact]
    public async Task Convergence_ObservesFullNetworkChangeWindowBeforeSelectingProfile()
    {
        var oldNetworkId = Guid.NewGuid().ToString("D");
        var newNetworkId = Guid.NewGuid().ToString("D");
        var oldProfile = CreateProfile("Old", oldNetworkId);
        var newProfile = CreateProfile("New", newNetworkId);
        var settings = CreateSettings(oldProfile, newProfile);
        var snapshots = new Queue<InterfaceSnapshot>(new[]
        {
            CreateSnapshot(CreateInterface("wifi", oldNetworkId, isActive: true)),
            CreateSnapshot(CreateInterface("wifi", oldNetworkId, isActive: true)),
            CreateSnapshot(CreateInterface("wifi", oldNetworkId, isActive: true)),
            CreateSnapshot(CreateInterface("wifi", newNetworkId, isActive: true)),
            CreateSnapshot(CreateInterface("wifi", newNetworkId, isActive: true)),
            CreateSnapshot(CreateInterface("wifi", newNetworkId, isActive: true))
        });
        var requestCount = 0;

        var result = await RouterProfileConvergence.ResolveAsync(
            _ =>
            {
                requestCount++;
                return Task.FromResult(snapshots.Dequeue());
            },
            snapshot => RouterProfileSelector.Resolve(settings, snapshot),
            maximumAttempts: 6,
            requiredStableSamples: 2,
            minimumAttempts: 6,
            retryDelay: TimeSpan.Zero,
            isCurrent: null,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Same(newProfile, result.Profile);
        Assert.Equal(6, requestCount);
    }

    [Fact]
    public async Task Convergence_RejectsAnUnstableFinalProfile()
    {
        var oldNetworkId = Guid.NewGuid().ToString("D");
        var newNetworkId = Guid.NewGuid().ToString("D");
        var settings = CreateSettings(
            CreateProfile("Old", oldNetworkId),
            CreateProfile("New", newNetworkId));
        var snapshots = new Queue<InterfaceSnapshot>(new[]
        {
            CreateSnapshot(CreateInterface("wifi", oldNetworkId, isActive: true)),
            CreateSnapshot(CreateInterface("wifi", oldNetworkId, isActive: true)),
            CreateSnapshot(CreateInterface("wifi", newNetworkId, isActive: true))
        });

        var result = await RouterProfileConvergence.ResolveAsync(
            _ => Task.FromResult(snapshots.Dequeue()),
            snapshot => RouterProfileSelector.Resolve(settings, snapshot),
            maximumAttempts: 3,
            requiredStableSamples: 2,
            minimumAttempts: 3,
            retryDelay: TimeSpan.Zero,
            isCurrent: null,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result.Profile);
        Assert.Null(result.MatchedInterfaceId);
    }

    private static AppSettings CreateSettings(params RouterProfile[] profiles)
    {
        return new AppSettings
        {
            Profiles = profiles.ToList(),
            SelectedProfileId = profiles[0].Id,
            AutomaticProfileSelection = true
        };
    }

    private static RouterProfile CreateProfile(string name, params string[] networkIds)
    {
        return new RouterProfile
        {
            Name = name,
            Login = "admin",
            Password = "secret",
            Networks = networkIds.Select(networkId => new RouterNetworkBinding
            {
                NetworkId = networkId,
                NetworkName = name
            }).ToList()
        };
    }

    private static NetworkInterfaceInfo CreateInterface(
        string id,
        string networkId,
        bool isActive)
    {
        return new NetworkInterfaceInfo(
            id,
            id,
            id,
            "00:11:22:33:44:55",
            "192.168.1.1",
            networkId,
            id,
            IsUp: true,
            IsActive: isActive,
            IsPreferred: false);
    }

    private static InterfaceSnapshot CreateSnapshot(params NetworkInterfaceInfo[] interfaces)
    {
        var active = interfaces.Single(netInterface => netInterface.IsActive);
        return new InterfaceSnapshot(
            interfaces,
            active.Id,
            active.MacAddress,
            active.Gateway,
            active.NetworkId,
            active.NetworkName);
    }
}
