namespace RouterTray.Tests;

public sealed class PolicyMenuSnapshotTests
{
    [Fact]
    public void FromRouter_MarksTheCurrentPolicyById()
    {
        var snapshot = PolicyMenuSnapshot.FromRouter(
            new[]
            {
                new PolicyInfo("Policy0", "VPN"),
                new PolicyInfo("Policy1", "Direct")
            },
            " policy1 ");

        Assert.False(snapshot.IsDefaultSelected);
        Assert.False(snapshot.Policies[0].IsSelected);
        Assert.True(snapshot.Policies[1].IsSelected);
    }

    [Fact]
    public void FromRouter_MarksDefaultWithoutSelectingAListedPolicy()
    {
        var snapshot = PolicyMenuSnapshot.FromRouter(
            new[] { new PolicyInfo("Policy0", "VPN") },
            "DEFAULT");

        Assert.True(snapshot.IsDefaultSelected);
        Assert.False(snapshot.Policies[0].IsSelected);
    }

    [Fact]
    public void ContentEquals_ComparesValuesInsteadOfPolicyInstances()
    {
        var first = PolicyMenuSnapshot.FromRouter(
            new[] { new PolicyInfo("Policy0", "VPN") },
            "Policy0");
        var equivalent = PolicyMenuSnapshot.FromRouter(
            new[] { new PolicyInfo("Policy0", "VPN") },
            "VPN");
        var changed = PolicyMenuSnapshot.FromRouter(
            new[] { new PolicyInfo("Policy0", "VPN") },
            "default");

        Assert.True(first.ContentEquals(equivalent));
        Assert.False(first.ContentEquals(changed));
    }

    [Fact]
    public void WithPolicySelected_UpdatesOnlyTheSelection()
    {
        var snapshot = PolicyMenuSnapshot.FromRouter(
            new[]
            {
                new PolicyInfo("Policy0", "VPN"),
                new PolicyInfo("Policy1", "Direct")
            },
            "default");

        var updated = snapshot.WithPolicySelected("policy0");

        Assert.False(updated.IsDefaultSelected);
        Assert.True(updated.Policies[0].IsSelected);
        Assert.False(updated.Policies[1].IsSelected);
        Assert.True(snapshot.HasSameStructure(updated));
        Assert.False(snapshot.ContentEquals(updated));
    }

    [Fact]
    public void HasSameStructure_DetectsAVisiblePolicyNameChange()
    {
        var first = PolicyMenuSnapshot.FromRouter(
            new[] { new PolicyInfo("Policy0", "VPN") },
            "Policy0");
        var renamed = PolicyMenuSnapshot.FromRouter(
            new[] { new PolicyInfo("Policy0", "Work VPN") },
            "Policy0");

        Assert.False(first.HasSameStructure(renamed));
    }

    [Fact]
    public void NativeMenu_AcceptsStructuralAndSelectionOnlyUpdates()
    {
        using var menu = new NativePolicyMenu();
        menu.Update(PolicyMenuSnapshot.Loading);
        menu.Update(PolicyMenuSnapshot.Loading.WithDefaultSelected());

        var loaded = PolicyMenuSnapshot.FromRouter(
            new[]
            {
                new PolicyInfo("Policy0", "VPN"),
                new PolicyInfo("Policy1", "Direct")
            },
            "Policy0");
        menu.Update(loaded);
        menu.Update(loaded.WithPolicySelected("Policy1"));
    }

    [Fact]
    public void WithSelectionFrom_PreservesASelectionWhenTheProbeIsUnknown()
    {
        var cached = PolicyMenuSnapshot.FromRouter(
            new[] { new PolicyInfo("Policy0", "VPN") },
            "Policy0");
        var refreshedWithoutSelection = PolicyMenuSnapshot.FromRouter(
            new[] { new PolicyInfo("Policy0", "Renamed VPN") },
            currentPolicy: null);

        var merged = refreshedWithoutSelection.WithSelectionFrom(cached);

        Assert.False(merged.IsDefaultSelected);
        Assert.True(merged.Policies[0].IsSelected);
        Assert.Equal("Renamed VPN", merged.Policies[0].DisplayName);
    }

    [Fact]
    public void ProfileCache_RestoresTheLastLoadedSnapshotWhenProfileReturns()
    {
        var cache = new ProfilePolicyCache();
        var home = PolicyMenuSnapshot.FromRouter(
            new[] { new PolicyInfo("Policy0", "Home VPN") },
            "Policy0");
        var work = PolicyMenuSnapshot.FromRouter(
            new[] { new PolicyInfo("Policy1", "Work VPN") },
            "default");

        Assert.True(cache.Activate("home"));
        Assert.True(cache.Update(home));
        Assert.True(cache.Activate("work"));
        Assert.Same(PolicyMenuSnapshot.Loading, cache.Current);
        Assert.True(cache.Update(work));

        Assert.True(cache.Activate("HOME"));
        Assert.Same(home, cache.Current);
    }

    [Fact]
    public void ProfileCache_DoesNotReplaceLoadedValuesWithAnErrorState()
    {
        var cache = new ProfilePolicyCache();
        var loaded = PolicyMenuSnapshot.FromRouter(
            new[] { new PolicyInfo("Policy0", "VPN") },
            "Policy0");

        cache.Activate("home");
        cache.Update(loaded);
        cache.Update(PolicyMenuSnapshot.Failed);
        cache.Activate("work");
        cache.Activate("home");

        Assert.Same(loaded, cache.Current);
    }
}
