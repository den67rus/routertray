namespace RouterTray;

internal static class RouterProfileSelector
{
    public static RouterProfileSelection Resolve(AppSettings settings, InterfaceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!settings.AutomaticProfileSelection)
        {
            return new RouterProfileSelection(settings.SelectedProfile, null);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.ActiveNetworkId))
        {
            var activeMatch = settings.FindProfileForNetwork(snapshot.ActiveNetworkId);
            if (activeMatch is not null)
            {
                return new RouterProfileSelection(activeMatch, snapshot.ActiveInterfaceId);
            }
        }

        var matches = snapshot.Interfaces
            .Where(netInterface => netInterface.IsUp && !string.IsNullOrWhiteSpace(netInterface.NetworkId))
            .Select(netInterface => new
            {
                Profile = settings.FindProfileForNetwork(netInterface.NetworkId),
                InterfaceId = netInterface.Id
            })
            .Where(match => match.Profile is not null)
            .GroupBy(match => match.Profile!.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        if (matches.Length == 1)
        {
            return new RouterProfileSelection(matches[0].Profile, matches[0].InterfaceId);
        }

        if (matches.Length == 0 &&
            settings.Profiles.Count == 1 &&
            settings.Profiles[0].Networks.Count == 0)
        {
            // Preserve the behaviour of migrated single-profile installations
            // until the user explicitly binds that profile to a network.
            return new RouterProfileSelection(settings.Profiles[0], snapshot.ActiveInterfaceId);
        }

        return new RouterProfileSelection(null, null);
    }
}

internal sealed record RouterProfileSelection(
    RouterProfile? Profile,
    string? MatchedInterfaceId);

internal static class RouterProfileConvergence
{
    public static async Task<RouterProfileSelection?> ResolveAsync(
        Func<CancellationToken, Task<InterfaceSnapshot>> snapshotProvider,
        Func<InterfaceSnapshot, RouterProfileSelection> resolver,
        int maximumAttempts,
        int requiredStableSamples,
        int minimumAttempts,
        TimeSpan retryDelay,
        Func<bool>? isCurrent,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(requiredStableSamples, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(requiredStableSamples, maximumAttempts);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumAttempts, maximumAttempts);
        if (retryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryDelay));
        }

        RouterProfileSelection? previous = null;
        var stableSamples = 0;

        for (var attempt = 0; attempt < maximumAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (isCurrent?.Invoke() == false)
            {
                return null;
            }

            var snapshot = await snapshotProvider(ct);
            if (isCurrent?.Invoke() == false)
            {
                return null;
            }

            var current = resolver(snapshot);
            stableSamples = IsSameCandidate(previous, current)
                ? stableSamples + 1
                : 1;
            previous = current;

            var attemptsCompleted = attempt + 1;
            if (current.Profile is not null &&
                attemptsCompleted >= minimumAttempts &&
                stableSamples >= requiredStableSamples)
            {
                return current;
            }

            if (attempt < maximumAttempts - 1)
            {
                await Task.Delay(retryDelay, ct);
            }
        }

        return previous?.Profile is not null && stableSamples >= requiredStableSamples
            ? previous
            : new RouterProfileSelection(null, null);
    }

    private static bool IsSameCandidate(
        RouterProfileSelection? left,
        RouterProfileSelection right)
    {
        return left is not null &&
               string.Equals(
                   left.Profile?.Id,
                   right.Profile?.Id,
                   StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   left.MatchedInterfaceId,
                   right.MatchedInterfaceId,
                   StringComparison.OrdinalIgnoreCase);
    }
}
