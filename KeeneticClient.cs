using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RouterTray;

internal sealed class KeeneticClient : IDisposable
{
    private const string DefaultPolicy = "default";
    private readonly HttpClient _http;
    private readonly AppSettings _settings;
    private readonly Func<string?> _deviceMacProvider;
    private readonly SemaphoreSlim _authLock = new(1, 1);
    private bool _isAuthenticated;

    public KeeneticClient(AppSettings settings, Func<string?> deviceMacProvider)
    {
        _settings = settings;
        _deviceMacProvider = deviceMacProvider;

        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = BuildBaseUri(settings.RouterUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task LoginAsync(CancellationToken ct = default)
    {
        var (realm, challenge) = await GetAuthChallengeAsync(ct);

        // Challenge-response auth: MD5(login:realm:password), then SHA256(challenge + md5).
        var md5 = ComputeMd5Hex($"{_settings.Login}:{realm}:{_settings.Password}");
        var sha = ComputeSha256Hex($"{challenge}{md5}");

        var payload = new { login = _settings.Login, password = sha };

        using var response = await _http.PostAsync("auth", CreateJsonContent(payload), ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new KeeneticAuthException("Invalid login or password.");
        }

        await EnsureSuccessOrThrow(response, "auth");
        _isAuthenticated = true;
    }

    public async Task SetPolicyAsync(string policy, CancellationToken ct = default)
    {
        var deviceMac = GetDeviceMac();
        var payload = new { mac = deviceMac, permit = true, policy = policy };

        using var response = await SendJsonWithAuthAsync("rci/ip/hotspot/host", payload, ct);
        await EnsureSuccessOrThrow(response, "set policy");
    }

    public async Task ClearPolicyAsync(CancellationToken ct = default)
    {
        var deviceMac = GetDeviceMac();
        var payload = new { mac = deviceMac, no = true };

        using var response = await SendJsonWithAuthAsync("rci/ip/hotspot/host/policy", payload, ct);
        await EnsureSuccessOrThrow(response, "clear policy");
    }

    public async Task<string> GetCurrentPolicyAsync(CancellationToken ct = default)
    {
        var deviceMac = GetDeviceMac();
        using var response = await SendWithAuthAsync(
            () => _http.GetAsync("rci/ip/hotspot/host", ct), ct);

        if (response.StatusCode == HttpStatusCode.MethodNotAllowed ||
            response.StatusCode == HttpStatusCode.NotFound)
        {
            return await GetCurrentPolicyByPostAsync(deviceMac, ct);
        }

        await EnsureSuccessOrThrow(response, "get policy");
        return await ParsePolicyAsync(response, deviceMac, ct);
    }

    private async Task<string> GetCurrentPolicyByPostAsync(string deviceMac, CancellationToken ct)
    {
        var payload = new { mac = deviceMac };
        using var response = await SendJsonWithAuthAsync("rci/ip/hotspot/host", payload, ct);

        await EnsureSuccessOrThrow(response, "get policy");
        return await ParsePolicyAsync(response, deviceMac, ct);
    }

    private async Task<string> ParsePolicyAsync(
        HttpResponseMessage response,
        string deviceMac,
        CancellationToken ct)
    {
        var json = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json))
        {
            return DefaultPolicy;
        }

        using var doc = JsonDocument.Parse(json);
        var policy = TryGetPolicyFromHostList(doc.RootElement, deviceMac) ??
                     FindPolicyByMac(doc.RootElement, deviceMac);

        return NormalizePolicy(policy);
    }

    public async Task<IReadOnlyList<PolicyInfo>> GetPoliciesAsync(CancellationToken ct = default)
    {
        using var response = await SendWithAuthAsync(
            () => _http.GetAsync("rci/ip/policy", ct), ct);

        if (response.StatusCode == HttpStatusCode.MethodNotAllowed ||
            response.StatusCode == HttpStatusCode.NotFound)
        {
            using var postResponse = await SendJsonWithAuthAsync("rci/ip/policy", new { }, ct);
            await EnsureSuccessOrThrow(postResponse, "get policies");
            return await ParsePoliciesAsync(postResponse, ct);
        }

        await EnsureSuccessOrThrow(response, "get policies");
        return await ParsePoliciesAsync(response, ct);
    }

    private async Task<(string Realm, string Challenge)> GetAuthChallengeAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync("auth", ct);
        if (response.StatusCode != HttpStatusCode.OK &&
            response.StatusCode != HttpStatusCode.Unauthorized)
        {
            await EnsureSuccessOrThrow(response, "auth challenge");
        }

        var realm = GetHeaderValue(response, "X-NDM-Realm");
        var challenge = GetHeaderValue(response, "X-NDM-Challenge");
        if (!string.IsNullOrWhiteSpace(realm) && !string.IsNullOrWhiteSpace(challenge))
        {
            return (realm, challenge);
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new KeeneticRequestException("Auth challenge headers missing.");
        }

        try
        {
            using var doc = JsonDocument.Parse(json);

            if (!TryGetString(doc.RootElement, "realm", out realm) ||
                !TryGetString(doc.RootElement, "challenge", out challenge))
            {
                throw new KeeneticRequestException("Invalid auth challenge response.");
            }

            return (realm!, challenge!);
        }
        catch (JsonException ex)
        {
            throw new KeeneticRequestException($"Invalid auth challenge JSON: {ex.Message}");
        }
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken ct)
    {
        if (_isAuthenticated)
        {
            return;
        }

        await _authLock.WaitAsync(ct);
        try
        {
            if (_isAuthenticated)
            {
                return;
            }

            await LoginAsync(ct);
        }
        finally
        {
            _authLock.Release();
        }
    }

    private async Task<HttpResponseMessage> SendWithAuthAsync(
        Func<Task<HttpResponseMessage>> send,
        CancellationToken ct)
    {
        await EnsureAuthenticatedAsync(ct);

        var response = await send();
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        _isAuthenticated = false;

        await EnsureAuthenticatedAsync(ct);
        return await send();
    }

    private Task<HttpResponseMessage> SendJsonWithAuthAsync(string path, object payload, CancellationToken ct)
    {
        return SendWithAuthAsync(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = CreateJsonContent(payload)
            };
            return await _http.SendAsync(request, ct);
        }, ct);
    }

    private static StringContent CreateJsonContent(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private string GetDeviceMac()
    {
        var mac = _deviceMacProvider?.Invoke();
        if (!string.IsNullOrWhiteSpace(mac))
        {
            return mac;
        }

        throw new InvalidOperationException("Active device MAC not found.");
    }

    private static async Task EnsureSuccessOrThrow(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new KeeneticRequestException(
            $"{operation} failed: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
    }

    private static Uri BuildBaseUri(string routerUrl)
    {
        if (!Uri.TryCreate(routerUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("RouterUrl must be an absolute URI.");
        }

        var builder = new UriBuilder(uri);
        if (!builder.Path.EndsWith("/", StringComparison.Ordinal))
        {
            builder.Path += "/";
        }

        return builder.Uri;
    }

    private static string? FindPolicyByMac(JsonElement element, string mac)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryGetString(element, "mac", out var macValue) && MacEquals(macValue, mac))
                {
                    return NormalizePolicy(TryGetPolicyValue(element));
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (MacEquals(property.Name, mac))
                    {
                        return NormalizePolicy(TryGetPolicyValue(property.Value));
                    }

                    var found = FindPolicyByMac(property.Value, mac);
                    if (!string.IsNullOrWhiteSpace(found))
                    {
                        return found;
                    }
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var found = FindPolicyByMac(item, mac);
                    if (!string.IsNullOrWhiteSpace(found))
                    {
                        return found;
                    }
                }
                break;
        }

        return null;
    }

    private static string? TryGetPolicyFromHostList(JsonElement element, string mac)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("host", out var hostElement))
        {
            return TryGetPolicyFromHostList(hostElement, mac);
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!TryGetString(item, "mac", out var macValue) || !MacEquals(macValue, mac))
            {
                continue;
            }

            return NormalizePolicy(TryGetPolicyValue(item));
        }

        return null;
    }

    private static string? TryGetPolicyValue(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("policy", out var policyProperty))
            {
                if (policyProperty.ValueKind == JsonValueKind.String)
                {
                    var value = policyProperty.GetString();
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }

                if (policyProperty.ValueKind == JsonValueKind.Object)
                {
                    var value = GetStringProperty(policyProperty, "id") ??
                                GetStringProperty(policyProperty, "policy") ??
                                GetStringProperty(policyProperty, "name") ??
                                GetStringProperty(policyProperty, "description");
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }
            }

            var policyId = GetStringProperty(element, "policy-id") ??
                           GetStringProperty(element, "policyId");
            if (!string.IsNullOrWhiteSpace(policyId))
            {
                return policyId;
            }
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    private static async Task<IReadOnlyList<PolicyInfo>> ParsePoliciesAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        var json = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<PolicyInfo>();
        }

        using var doc = JsonDocument.Parse(json);
        return ExtractPolicies(doc.RootElement);
    }

    private static IReadOnlyList<PolicyInfo> ExtractPolicies(JsonElement root)
    {
        var policies = new List<PolicyInfo>();

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                var id = property.Name;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var name = ExtractPolicyName(property.Value) ?? id;
                policies.Add(new PolicyInfo(id, name));
            }

            if (policies.Count > 0)
            {
                return NormalizePolicies(policies);
            }
        }

        CollectPolicies(root, policies, root.ValueKind == JsonValueKind.Array);
        return NormalizePolicies(policies);
    }

    private static void CollectPolicies(JsonElement element, List<PolicyInfo> policies, bool allowStringValues)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var id = GetStringProperty(element, "id") ??
                         GetStringProperty(element, "policy") ??
                         GetStringProperty(element, "name") ??
                         GetStringProperty(element, "description");
                if (!string.IsNullOrWhiteSpace(id))
                {
                    var name = GetStringProperty(element, "description") ??
                               GetStringProperty(element, "name") ??
                               id;
                    policies.Add(new PolicyInfo(id, name));
                }

                foreach (var property in element.EnumerateObject())
                {
                    CollectPolicies(property.Value, policies, false);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectPolicies(item, policies, true);
                }
                break;
            case JsonValueKind.String when allowStringValues:
                var directValue = element.GetString();
                if (!string.IsNullOrWhiteSpace(directValue))
                {
                    policies.Add(new PolicyInfo(directValue, directValue));
                }
                break;
        }
    }

    private static string? ExtractPolicyName(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            return GetStringProperty(element, "description") ??
                   GetStringProperty(element, "name") ??
                   GetStringProperty(element, "id") ??
                   GetStringProperty(element, "policy");
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    private static IReadOnlyList<PolicyInfo> NormalizePolicies(IEnumerable<PolicyInfo> policies)
    {
        return policies
            .Where(static policy => !string.IsNullOrWhiteSpace(policy.Id))
            .GroupBy(static policy => policy.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group =>
                group.OrderByDescending(static policy =>
                        !string.IsNullOrWhiteSpace(policy.Name) &&
                        !string.Equals(policy.Name, policy.Id, StringComparison.OrdinalIgnoreCase))
                    .ThenBy(static policy => policy.Name, StringComparer.OrdinalIgnoreCase)
                    .First())
            .OrderBy(static policy => policy.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? GetStringProperty(JsonElement element, string name)
    {
        return TryGetString(element, name, out var value) ? value : null;
    }

    private static bool TryGetString(JsonElement element, string name, out string? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!element.TryGetProperty(name, out var prop))
        {
            return false;
        }

        value = prop.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool MacEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return NormalizeMac(left) == NormalizeMac(right);
    }

    private static string NormalizeMac(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (Uri.IsHexDigit(ch))
            {
                sb.Append(char.ToUpperInvariant(ch));
            }
        }

        return sb.ToString();
    }

    private static string NormalizePolicy(string? policy)
    {
        return string.IsNullOrWhiteSpace(policy) ? DefaultPolicy : policy;
    }

    private static string ComputeMd5Hex(string input)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeSha256Hex(string input)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void Dispose()
    {
        _http.Dispose();
        _authLock.Dispose();
    }

    private static string? GetHeaderValue(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        if (response.Content.Headers.TryGetValues(name, out var contentValues))
        {
            foreach (var value in contentValues)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }
}

internal sealed class PolicyInfo
{
    public PolicyInfo(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }
    public string Name { get; }
}

internal sealed class KeeneticAuthException : Exception
{
    public KeeneticAuthException(string message) : base(message)
    {
    }
}

internal sealed class KeeneticRequestException : Exception
{
    public KeeneticRequestException(string message) : base(message)
    {
    }
}
