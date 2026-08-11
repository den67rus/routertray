using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RouterTray;

internal enum KeeneticAuthProtocol
{
    AccessToken,
    Ndw4,
    Ndw2
}

internal sealed class KeeneticClient : IDisposable
{
    private const string DefaultPolicy = "default";
    private const string AccessTokenHeaderName = "X-NDMA-TKN";
    private const string Ndw2Scheme = "x-ndw2-interactive";
    private const string Ndw4Scheme = "x-ndw4-interactive";
    private const string Ndw4DataHeaderName = "X-NDM-Data";
    private const int Ndw4SaltLength = 16;
    private const int Ndw4NonceLength = 16;
    private const int MaxNdw4Iterations = 100;
    private const int MaxNdw4MemoryCost = 262_144;
    private const int MaxNdw4HeaderLength = 16_384;
    private const int MaxErrorBodyLength = 2048;
    private readonly HttpClient _http;
    private readonly RouterAuthMode _authMode;
    private readonly string _login;
    private readonly string _password;
    private readonly string _accessToken;
    private readonly Func<byte[]> _nonceFactory;
    private readonly SemaphoreSlim _authLock = new(1, 1);
    private Uri _baseUri;
    private bool _isAuthenticated;

    internal bool IsAuthenticated => _isAuthenticated;
    internal KeeneticAuthProtocol? AuthenticationProtocol { get; private set; }

    public KeeneticClient(Uri baseUri, string login, string password)
        : this(baseUri, RouterAuthMode.Password, login, password, string.Empty)
    {
    }

    public KeeneticClient(
        Uri baseUri,
        RouterAuthMode authMode,
        string login,
        string password,
        string accessToken)
        : this(
            baseUri,
            authMode,
            login,
            password,
            accessToken,
            CreateHttpHandler(authMode),
            null)
    {
    }

    internal KeeneticClient(
        Uri baseUri,
        RouterAuthMode authMode,
        string login,
        string password,
        string accessToken,
        HttpMessageHandler handler,
        Func<byte[]>? nonceFactory = null)
    {
        _baseUri = RouterEndpoint.GetConfiguredUri(baseUri.OriginalString) ??
                   throw new InvalidOperationException("Router URL is required.");
        _authMode = authMode;
        _login = login?.Trim() ?? string.Empty;
        _password = password ?? string.Empty;
        _accessToken = accessToken?.Trim() ?? string.Empty;
        _nonceFactory = nonceFactory ?? (() => RandomNumberGenerator.GetBytes(Ndw4NonceLength));

        _http = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static HttpClientHandler CreateHttpHandler(RouterAuthMode authMode)
    {
        return new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            AutomaticDecompression = DecompressionMethods.All,
            CheckCertificateRevocationList = true,
            // A custom token header must never be forwarded to a redirect target.
            AllowAutoRedirect = authMode != RouterAuthMode.AccessToken
        };
    }

    public async Task LoginAsync(CancellationToken ct = default)
    {
        if (_authMode == RouterAuthMode.AccessToken)
        {
            if (string.IsNullOrWhiteSpace(_accessToken))
            {
                throw new KeeneticAuthException("Access token is required.");
            }

            AuthenticationProtocol = KeeneticAuthProtocol.AccessToken;
            _isAuthenticated = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(_login) || string.IsNullOrWhiteSpace(_password))
        {
            throw new KeeneticAuthException("Login and password are required.");
        }

        var challenge = await GetAuthenticationChallengeAsync(ct);
        if (challenge.AlreadyAuthenticated)
        {
            _isAuthenticated = true;
            return;
        }

        if (challenge.SupportsNdw4 && challenge.Ndw4Endpoint is not null)
        {
            await LoginWithNdw4Async(challenge.Ndw4Endpoint, ct);
            AuthenticationProtocol = KeeneticAuthProtocol.Ndw4;
            _isAuthenticated = true;
            return;
        }

        if (!challenge.SupportsNdw2 ||
            string.IsNullOrWhiteSpace(challenge.Realm) ||
            string.IsNullOrWhiteSpace(challenge.LegacyChallenge))
        {
            throw new KeeneticRequestException("Router does not advertise a supported authentication method.");
        }

        await LoginWithNdw2Async(challenge.Realm, challenge.LegacyChallenge, ct);
        AuthenticationProtocol = KeeneticAuthProtocol.Ndw2;
        _isAuthenticated = true;
    }

    private async Task LoginWithNdw2Async(string realm, string challenge, CancellationToken ct)
    {
        // Legacy challenge-response auth: MD5(login:realm:password), then SHA256(challenge + md5).
        var md5 = ComputeMd5Hex($"{_login}:{realm}:{_password}");
        var sha = ComputeSha256Hex($"{challenge}{md5}");

        var payload = new { login = _login, password = sha };

        using var content = CreateJsonContent(payload);
        using var response = await _http.PostAsync(GetRequestUri("auth"), content, ct);
        if (IsAuthenticationFailure(response.StatusCode))
        {
            throw new KeeneticAuthException("Invalid login or password.");
        }

        await EnsureSuccessOrThrow(response, "auth", ct);
    }

    private async Task LoginWithNdw4Async(Uri endpoint, CancellationToken ct)
    {
        var clientNonce = _nonceFactory() ??
                          throw new KeeneticRequestException("NDW4 nonce generator returned no value.");
        if (clientNonce.Length != Ndw4NonceLength)
        {
            CryptographicOperations.ZeroMemory(clientNonce);
            throw new KeeneticRequestException("NDW4 nonce generator returned an invalid value.");
        }

        string clientNonceBase64;
        try
        {
            clientNonceBase64 = Convert.ToBase64String(clientNonce);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clientNonce);
        }

        using var phase1Response = await PostAuthJsonAsync(
            endpoint,
            new { login = _login, nonce = clientNonceBase64 },
            ct);
        await EnsureNdw4StatusAsync(phase1Response, HttpStatusCode.Unauthorized, "NDW4 phase 1", ct);

        var phase1 = ParseNdw4Phase1(phase1Response);
        byte[] salt;
        try
        {
            salt = Convert.FromBase64String(phase1.Salt);
        }
        catch (FormatException ex)
        {
            throw new KeeneticRequestException("NDW4 phase 1 returned an invalid salt.", ex);
        }

        if (salt.Length != Ndw4SaltLength)
        {
            CryptographicOperations.ZeroMemory(salt);
            throw new KeeneticRequestException("NDW4 phase 1 returned a salt with an invalid length.");
        }

        var authenticationMessage =
            $"login1={_login},nonce1={clientNonceBase64};" +
            $"iter2={phase1.Iterations},memcost2={phase1.MemoryCost}," +
            $"nonce2={phase1.ServerNonce},salt2={phase1.Salt};" +
            $"login3={_login},nonce3={phase1.ServerNonce}";

        KeeneticNdw4Keys keys;
        try
        {
            keys = await Task.Run(
                () => KeeneticNdw4Keys.Derive(
                    _password,
                    salt,
                    phase1.Iterations,
                    phase1.MemoryCost),
                ct);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            throw new KeeneticRequestException("NDW4 key derivation failed.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
        }

        using (keys)
        {
            var clientProof = keys.CreateClientProof(authenticationMessage);
            string clientProofBase64;
            try
            {
                clientProofBase64 = Convert.ToBase64String(clientProof);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(clientProof);
            }

            using var phase2Response = await PostAuthJsonAsync(
                endpoint,
                new { login = _login, nonce = phase1.ServerNonce, proof = clientProofBase64 },
                ct);
            await EnsureNdw4StatusAsync(phase2Response, HttpStatusCode.Unauthorized, "NDW4 phase 2", ct);

            var serverSignatureBase64 = ParseNdw4ServerSignature(phase2Response);
            byte[] serverSignature;
            try
            {
                serverSignature = Convert.FromBase64String(serverSignatureBase64);
            }
            catch (FormatException ex)
            {
                throw new KeeneticRequestException("NDW4 phase 2 returned an invalid signature.", ex);
            }

            try
            {
                if (!keys.VerifyServerSignature(authenticationMessage, serverSignature))
                {
                    throw new KeeneticAuthException("Invalid login or password.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(serverSignature);
            }

            var finalAuthenticationMessage =
                $"{authenticationMessage};signature4={serverSignatureBase64}";
            var signatureProof = keys.CreateClientProof(finalAuthenticationMessage);
            string signatureProofBase64;
            try
            {
                signatureProofBase64 = Convert.ToBase64String(signatureProof);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(signatureProof);
            }

            using var phase3Response = await PostAuthJsonAsync(
                endpoint,
                new Dictionary<string, string>
                {
                    ["login"] = _login,
                    ["nonce"] = phase1.ServerNonce,
                    ["signature-proof"] = signatureProofBase64
                },
                ct);
            await EnsureNdw4StatusAsync(phase3Response, HttpStatusCode.OK, "NDW4 phase 3", ct);
        }
    }

    private async Task<HttpResponseMessage> PostAuthJsonAsync(
        Uri endpoint,
        object payload,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = CreateJsonContent(payload)
        };
        return await _http.SendAsync(request, ct);
    }

    private static async Task EnsureNdw4StatusAsync(
        HttpResponseMessage response,
        HttpStatusCode expected,
        string phase,
        CancellationToken ct)
    {
        if (response.StatusCode == expected)
        {
            return;
        }

        if (IsAuthenticationFailure(response.StatusCode))
        {
            throw new KeeneticAuthException("Invalid login or password.");
        }

        if (!response.IsSuccessStatusCode)
        {
            await EnsureSuccessOrThrow(response, phase, ct);
        }

        throw new KeeneticRequestException(
            $"{phase} returned unexpected status {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    private static Ndw4Phase1 ParseNdw4Phase1(HttpResponseMessage response)
    {
        using var document = ParseNdw4Data(response, "phase 1");
        if (!TryGetString(document.RootElement, "salt", out var salt) ||
            !TryGetString(document.RootElement, "nonce", out var serverNonce) ||
            !TryGetInt32(document.RootElement, "iter", out var iterations) ||
            !TryGetInt32(document.RootElement, "memcost", out var memoryCost))
        {
            throw new KeeneticRequestException("NDW4 phase 1 response is incomplete.");
        }

        if (iterations is < 1 or > MaxNdw4Iterations ||
            memoryCost is < 8 or > MaxNdw4MemoryCost ||
            serverNonce!.Length > 1024)
        {
            throw new KeeneticRequestException("NDW4 phase 1 parameters are outside safe limits.");
        }

        return new Ndw4Phase1(salt!, serverNonce, iterations, memoryCost);
    }

    private static string ParseNdw4ServerSignature(HttpResponseMessage response)
    {
        using var document = ParseNdw4Data(response, "phase 2");
        if (!TryGetString(document.RootElement, "signature", out var signature))
        {
            throw new KeeneticRequestException("NDW4 phase 2 response is incomplete.");
        }

        return signature!;
    }

    private static JsonDocument ParseNdw4Data(HttpResponseMessage response, string phase)
    {
        var encoded = GetHeaderValue(response, Ndw4DataHeaderName);
        if (string.IsNullOrWhiteSpace(encoded) || encoded.Length > MaxNdw4HeaderLength)
        {
            throw new KeeneticRequestException($"NDW4 {phase} data header is missing or invalid.");
        }

        try
        {
            var json = Convert.FromBase64String(encoded);
            try
            {
                return JsonDocument.Parse(Encoding.UTF8.GetString(json));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(json);
            }
        }
        catch (FormatException ex)
        {
            throw new KeeneticRequestException($"NDW4 {phase} data is not valid Base64.", ex);
        }
        catch (JsonException ex)
        {
            throw new KeeneticRequestException($"NDW4 {phase} data is not valid JSON.", ex);
        }
    }

    public async Task SetPolicyAsync(string policy, string deviceMac, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceMac);
        var payload = new { mac = deviceMac, permit = true, policy = policy };

        using var response = await SendJsonWithAuthAsync("rci/ip/hotspot/host", payload, ct);
        await EnsureSuccessOrThrow(response, "set policy", ct);
    }

    public async Task ClearPolicyAsync(string deviceMac, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceMac);
        var payload = new { mac = deviceMac, no = true };

        using var response = await SendJsonWithAuthAsync("rci/ip/hotspot/host/policy", payload, ct);
        await EnsureSuccessOrThrow(response, "clear policy", ct);
    }

    public async Task<string> GetCurrentPolicyAsync(string deviceMac, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceMac);
        using var response = await SendWithAuthAsync(
            () => SendRciRequestAsync(HttpMethod.Get, "rci/ip/hotspot/host", null, ct), ct);

        if (response.StatusCode == HttpStatusCode.MethodNotAllowed ||
            response.StatusCode == HttpStatusCode.NotFound)
        {
            return await GetCurrentPolicyByPostAsync(deviceMac, ct);
        }

        await EnsureSuccessOrThrow(response, "get policy", ct);
        return await ParsePolicyAsync(response, deviceMac, ct);
    }

    private async Task<string> GetCurrentPolicyByPostAsync(string deviceMac, CancellationToken ct)
    {
        var payload = new { mac = deviceMac };
        using var response = await SendJsonWithAuthAsync("rci/ip/hotspot/host", payload, ct);

        await EnsureSuccessOrThrow(response, "get policy", ct);
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

        try
        {
            using var doc = JsonDocument.Parse(json);
            var policy = TryGetPolicyFromHostList(doc.RootElement, deviceMac) ??
                         FindPolicyByMac(doc.RootElement, deviceMac);

            return NormalizePolicy(policy);
        }
        catch (JsonException ex)
        {
            throw new KeeneticRequestException("Invalid policy response JSON.", ex);
        }
    }

    public async Task<IReadOnlyList<PolicyInfo>> GetPoliciesAsync(CancellationToken ct = default)
    {
        using var response = await SendWithAuthAsync(
            () => SendRciRequestAsync(HttpMethod.Get, "rci/show/rc/ip/policy", null, ct), ct);

        if (response.StatusCode == HttpStatusCode.MethodNotAllowed ||
            response.StatusCode == HttpStatusCode.NotFound)
        {
            return await GetPoliciesFromLegacyEndpointAsync(ct);
        }

        await EnsureSuccessOrThrow(response, "get policies", ct);
        return await ParsePoliciesAsync(response, ct);
    }

    private async Task<IReadOnlyList<PolicyInfo>> GetPoliciesFromLegacyEndpointAsync(CancellationToken ct)
    {
        using var response = await SendWithAuthAsync(
            () => SendRciRequestAsync(HttpMethod.Get, "rci/ip/policy", null, ct), ct);

        if (response.StatusCode == HttpStatusCode.MethodNotAllowed ||
            response.StatusCode == HttpStatusCode.NotFound)
        {
            using var postResponse = await SendJsonWithAuthAsync("rci/ip/policy", new { }, ct);
            await EnsureSuccessOrThrow(postResponse, "get policies", ct);
            return await ParsePoliciesAsync(postResponse, ct);
        }

        await EnsureSuccessOrThrow(response, "get policies", ct);
        return await ParsePoliciesAsync(response, ct);
    }

    private async Task<AuthenticationChallenge> GetAuthenticationChallengeAsync(CancellationToken ct)
    {
        var authUri = GetRequestUri("auth");
        using var response = await _http.GetAsync(authUri, ct);
        UpdateBaseUriAfterRedirect(authUri, response, "auth");

        if (response.StatusCode == HttpStatusCode.OK)
        {
            return AuthenticationChallenge.Authenticated;
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new KeeneticAuthException("Router rejected authentication.");
        }

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            await EnsureSuccessOrThrow(response, "auth challenge", ct);
        }

        var authenticateHeader = GetJoinedHeaderValue(response, "WWW-Authenticate");
        var supportsNdw4 = HeaderContainsScheme(authenticateHeader, Ndw4Scheme);
        var realm = GetHeaderValue(response, "X-NDM-Realm");
        var legacyChallenge = GetHeaderValue(response, "X-NDM-Challenge");
        var supportsNdw2 = HeaderContainsScheme(authenticateHeader, Ndw2Scheme) ||
                           (!string.IsNullOrWhiteSpace(realm) &&
                            !string.IsNullOrWhiteSpace(legacyChallenge));

        if (supportsNdw2 &&
            (string.IsNullOrWhiteSpace(realm) || string.IsNullOrWhiteSpace(legacyChallenge)))
        {
            (realm, legacyChallenge) = await ParseLegacyChallengeBodyAsync(response, ct);
        }

        Uri? ndw4Endpoint = null;
        if (supportsNdw4)
        {
            var endpoint = ParseNdw4Endpoint(authenticateHeader) ?? "/auth";
            ndw4Endpoint = ResolveAuthenticationEndpoint(endpoint);
        }

        return new AuthenticationChallenge(
            false,
            supportsNdw4,
            supportsNdw2,
            ndw4Endpoint,
            realm,
            legacyChallenge);
    }

    private static async Task<(string? Realm, string? Challenge)> ParseLegacyChallengeBodyAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        var json = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json))
        {
            return (null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            TryGetString(doc.RootElement, "realm", out var realm);
            TryGetString(doc.RootElement, "challenge", out var challenge);
            return (realm, challenge);
        }
        catch (JsonException ex)
        {
            throw new KeeneticRequestException("Invalid auth challenge JSON.", ex);
        }
    }

    private static bool HeaderContainsScheme(string header, string scheme)
    {
        return header.IndexOf(scheme, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string? ParseNdw4Endpoint(string authenticateHeader)
    {
        var match = Regex.Match(
            authenticateHeader,
            @"(?:^|,)\s*x-ndw4-interactive\b[^,]*\bendpoint\s*=\s*""(?<endpoint>[^""]+)""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["endpoint"].Value : null;
    }

    private Uri ResolveAuthenticationEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(_baseUri, endpoint, out var resolved) ||
            (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps) ||
            !string.Equals(resolved.Scheme, _baseUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(resolved.Authority, _baseUri.Authority, StringComparison.OrdinalIgnoreCase) ||
            !resolved.AbsolutePath.EndsWith("/auth", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(resolved.Query) ||
            !string.IsNullOrEmpty(resolved.Fragment))
        {
            throw new KeeneticRequestException("Router advertised an unsafe NDW4 authentication endpoint.");
        }

        return resolved;
    }

    internal async Task EnsureAuthenticatedAsync(CancellationToken ct)
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
        if (_authMode == RouterAuthMode.AccessToken &&
            IsAuthenticationFailure(response.StatusCode))
        {
            response.Dispose();
            _isAuthenticated = false;
            throw new KeeneticAuthException("Invalid or unauthorized access token.");
        }

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        _isAuthenticated = false;

        await EnsureAuthenticatedAsync(ct);
        response = await send();
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            _isAuthenticated = false;
            throw new KeeneticAuthException("Router session could not be authenticated.");
        }

        return response;
    }

    private Task<HttpResponseMessage> SendJsonWithAuthAsync(string path, object payload, CancellationToken ct)
    {
        return SendWithAuthAsync(
            () => SendRciRequestAsync(HttpMethod.Post, path, payload, ct),
            ct);
    }

    private async Task<HttpResponseMessage> SendRciRequestAsync(
        HttpMethod method,
        string path,
        object? payload,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, GetRequestUri(path));
        if (payload is not null)
        {
            request.Content = CreateJsonContent(payload);
        }

        if (_authMode == RouterAuthMode.AccessToken)
        {
            request.Headers.Add(AccessTokenHeaderName, _accessToken);
        }

        return await _http.SendAsync(request, ct);
    }

    private static StringContent CreateJsonContent(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static bool IsAuthenticationFailure(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
    }

    private sealed record Ndw4Phase1(
        string Salt,
        string ServerNonce,
        int Iterations,
        int MemoryCost);

    private sealed record AuthenticationChallenge(
        bool AlreadyAuthenticated,
        bool SupportsNdw4,
        bool SupportsNdw2,
        Uri? Ndw4Endpoint,
        string? Realm,
        string? LegacyChallenge)
    {
        public static AuthenticationChallenge Authenticated { get; } =
            new(true, false, false, null, null, null);
    }

    internal static async Task EnsureSuccessOrThrow(
        HttpResponseMessage response,
        string operation,
        CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new KeeneticRequestException(
                $"{operation} failed: {(int)response.StatusCode} {response.ReasonPhrase}. " +
                TruncateErrorBody(body));
        }

        if (TryGetRciError(body, out var errorMessage))
        {
            throw new KeeneticRequestException($"{operation} failed: {errorMessage}");
        }
    }

    private static string TruncateErrorBody(string body)
    {
        return body.Length > MaxErrorBodyLength
            ? body[..MaxErrorBodyLength] + "…"
            : body;
    }

    private Uri GetRequestUri(string relativePath)
    {
        return new Uri(_baseUri, relativePath);
    }

    private void UpdateBaseUriAfterRedirect(
        Uri requestedUri,
        HttpResponseMessage response,
        string relativePath)
    {
        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null ||
            string.Equals(
                requestedUri.OriginalString,
                finalUri.OriginalString,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (finalUri.Scheme != Uri.UriSchemeHttp && finalUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new KeeneticRequestException("Router redirected authentication to an unsupported URL.");
        }

        if (_baseUri.Scheme == Uri.UriSchemeHttps && finalUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new KeeneticRequestException("Router attempted to downgrade HTTPS authentication to HTTP.");
        }

        var suffix = relativePath.TrimStart('/');
        if (!finalUri.AbsolutePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            throw new KeeneticRequestException("Router redirected authentication to an unexpected path.");
        }

        var basePath = finalUri.AbsolutePath[..^suffix.Length];
        if (!basePath.EndsWith('/'))
        {
            basePath += "/";
        }

        _baseUri = new Uri(finalUri.GetLeftPart(UriPartial.Authority) + basePath, UriKind.Absolute);
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

        try
        {
            using var doc = JsonDocument.Parse(json);
            return ExtractPolicies(doc.RootElement);
        }
        catch (JsonException ex)
        {
            throw new KeeneticRequestException("Invalid policies response JSON.", ex);
        }
    }

    internal static IReadOnlyList<PolicyInfo> ExtractPolicies(JsonElement root)
    {
        if (TryFindRciError(root, out var errorMessage))
        {
            throw new KeeneticRequestException($"get policies failed: {errorMessage}");
        }

        var policyMap = root;
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("policy", out var wrappedPolicyMap))
        {
            policyMap = wrappedPolicyMap;
        }

        if (policyMap.ValueKind == JsonValueKind.Array && policyMap.GetArrayLength() == 0)
        {
            return Array.Empty<PolicyInfo>();
        }

        if (policyMap.ValueKind != JsonValueKind.Object)
        {
            throw new KeeneticRequestException("Invalid policies response shape.");
        }

        var policies = new List<PolicyInfo>();
        foreach (var property in policyMap.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name) ||
                property.Value.ValueKind != JsonValueKind.Object)
            {
                throw new KeeneticRequestException("Invalid policy entry in response.");
            }

            var name = ExtractPolicyName(property.Value) ?? property.Name;
            policies.Add(new PolicyInfo(property.Name, name));
        }

        return NormalizePolicies(policies);
    }

    private static bool TryGetRciError(string json, out string? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return TryFindRciError(document.RootElement, out message);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryFindRciError(JsonElement element, out string? message)
    {
        message = null;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryGetString(element, "status", out var status) &&
                    string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
                {
                    var detail = GetStringProperty(element, "message") ??
                                 GetStringProperty(element, "ident") ??
                                 "Router reported an RCI error.";
                    var code = GetStringProperty(element, "code");
                    message = string.IsNullOrWhiteSpace(code)
                        ? detail
                        : $"{detail} (code {code})";
                    return true;
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (TryFindRciError(property.Value, out message))
                    {
                        return true;
                    }
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (TryFindRciError(item, out message))
                    {
                        return true;
                    }
                }
                break;
        }

        return false;
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

        if (prop.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = prop.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetInt32(JsonElement element, string name, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out value);
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
#pragma warning disable CA5351 // MD5 is mandated by Keenetic's challenge-response protocol.
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
#pragma warning restore CA5351
#pragma warning disable CA1308 // Keenetic's authentication protocol requires lowercase hexadecimal.
        return Convert.ToHexString(hash).ToLowerInvariant();
#pragma warning restore CA1308
    }

    private static string ComputeSha256Hex(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
#pragma warning disable CA1308 // Keenetic's authentication protocol requires lowercase hexadecimal.
        return Convert.ToHexString(hash).ToLowerInvariant();
#pragma warning restore CA1308
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

    private static string GetJoinedHeaderValue(HttpResponseMessage response, string name)
    {
        var values = new List<string>();
        if (response.Headers.TryGetValues(name, out var headerValues))
        {
            values.AddRange(headerValues.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        if (response.Content.Headers.TryGetValues(name, out var contentValues))
        {
            values.AddRange(contentValues.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        return string.Join(",", values);
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
    public KeeneticAuthException()
    {
    }

    public KeeneticAuthException(string message) : base(message)
    {
    }

    public KeeneticAuthException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

internal sealed class KeeneticRequestException : Exception
{
    public KeeneticRequestException()
    {
    }

    public KeeneticRequestException(string message) : base(message)
    {
    }

    public KeeneticRequestException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
