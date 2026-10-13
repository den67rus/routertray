namespace RouterTray;

internal enum PolicyMenuLoadState
{
    Loading,
    Loaded,
    Failed
}

internal sealed class PolicyMenuSnapshot
{
    private readonly PolicyMenuEntry[] _policies;

    private PolicyMenuSnapshot(
        PolicyMenuLoadState state,
        bool isDefaultSelected,
        IEnumerable<PolicyMenuEntry> policies)
    {
        State = state;
        IsDefaultSelected = isDefaultSelected;
        _policies = policies.ToArray();
    }

    public static PolicyMenuSnapshot Loading { get; } = new(
        PolicyMenuLoadState.Loading,
        isDefaultSelected: false,
        Array.Empty<PolicyMenuEntry>());

    public static PolicyMenuSnapshot Failed { get; } = new(
        PolicyMenuLoadState.Failed,
        isDefaultSelected: false,
        Array.Empty<PolicyMenuEntry>());

    public PolicyMenuLoadState State { get; }

    public bool IsDefaultSelected { get; }

    public IReadOnlyList<PolicyMenuEntry> Policies => _policies;

    public static PolicyMenuSnapshot FromRouter(
        IEnumerable<PolicyInfo> policies,
        string? currentPolicy)
    {
        ArgumentNullException.ThrowIfNull(policies);

        var entries = policies.Select(policy =>
        {
            var displayName = string.IsNullOrWhiteSpace(policy.Name)
                ? policy.Id
                : policy.Name;
            return new PolicyMenuEntry(
                policy.Id,
                displayName,
                IsCurrentPolicy(currentPolicy, policy.Id, policy.Name));
        });

        return new PolicyMenuSnapshot(
            PolicyMenuLoadState.Loaded,
            TrayForm.IsDefaultPolicy(currentPolicy),
            entries);
    }

    public PolicyMenuSnapshot WithDefaultSelected()
    {
        return new PolicyMenuSnapshot(
            State,
            isDefaultSelected: true,
            _policies.Select(static policy => policy with { IsSelected = false }));
    }

    public PolicyMenuSnapshot WithPolicySelected(string policyId)
    {
        return new PolicyMenuSnapshot(
            State,
            isDefaultSelected: false,
            _policies.Select(policy => policy with
            {
                IsSelected = string.Equals(
                    policy.Id,
                    policyId,
                    StringComparison.OrdinalIgnoreCase)
            }));
    }

    public PolicyMenuSnapshot WithSelectionFrom(PolicyMenuSnapshot previous)
    {
        ArgumentNullException.ThrowIfNull(previous);

        if (previous.IsDefaultSelected)
        {
            return WithDefaultSelected();
        }

        var selectedPolicy = previous._policies.FirstOrDefault(
            static policy => policy.IsSelected);
        return selectedPolicy is null
            ? this
            : WithPolicySelected(selectedPolicy.Id);
    }

    public bool ContentEquals(PolicyMenuSnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (!HasSameStructure(other) ||
            IsDefaultSelected != other.IsDefaultSelected)
        {
            return false;
        }

        for (var index = 0; index < _policies.Length; index++)
        {
            if (_policies[index].IsSelected != other._policies[index].IsSelected)
            {
                return false;
            }
        }

        return true;
    }

    public bool HasSameStructure(PolicyMenuSnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (State != other.State || _policies.Length != other._policies.Length)
        {
            return false;
        }

        for (var index = 0; index < _policies.Length; index++)
        {
            var left = _policies[index];
            var right = other._policies[index];
            if (!string.Equals(left.Id, right.Id, StringComparison.Ordinal) ||
                !string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsCurrentPolicy(
        string? currentPolicy,
        string policyId,
        string policyName)
    {
        if (string.IsNullOrWhiteSpace(currentPolicy))
        {
            return false;
        }

        var normalized = currentPolicy.Trim();
        return string.Equals(normalized, policyId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, policyName, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record PolicyMenuEntry(
    string Id,
    string DisplayName,
    bool IsSelected);

internal sealed class ProfilePolicyCache
{
    private readonly Dictionary<string, PolicyMenuSnapshot> _snapshots = new(
        StringComparer.OrdinalIgnoreCase);

    public string? ActiveProfileId { get; private set; }

    public PolicyMenuSnapshot Current { get; private set; } = PolicyMenuSnapshot.Loading;

    public bool Activate(string? profileId)
    {
        if (string.Equals(
                ActiveProfileId,
                profileId,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ActiveProfileId = profileId;
        Current = profileId is not null && _snapshots.TryGetValue(profileId, out var cached)
            ? cached
            : PolicyMenuSnapshot.Loading;
        return true;
    }

    public bool Update(PolicyMenuSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (Current.ContentEquals(snapshot))
        {
            return false;
        }

        Current = snapshot;
        if (ActiveProfileId is not null && snapshot.State == PolicyMenuLoadState.Loaded)
        {
            _snapshots[ActiveProfileId] = snapshot;
        }

        return true;
    }
}
