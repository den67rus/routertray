using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RouterTray.Tests;

public sealed class KeeneticClientTests
{
    [Fact]
    public async Task LoginAsync_UsesNdw4WhenRouterAdvertisesNewAndLegacyProtocols()
    {
        const string password = "test-password";
        const string serverNonce = "server-nonce@example%value";
        const string expectedClientProof =
            "nhprkl5zzuo5mEjFLpxDIBxeCpAUKMc3HZ6yv+IKPhqsjlmgX4z91S/WdGth8NAR7mng+3RfycCnvj1ybuR52A==";
        const string serverSignature =
            "JmkWM31tYAwmpUx7HFenRKz/HZJnSVOfMzPLVthPnP0tHdaPou+bpfjeinxgj/sPBEVQ+UIQTtCjt3m3QA/c7w==";
        const string expectedSignatureProof =
            "9L/R3fewWoaSnGtJDaTzmSZSospttuppVSB1FR/AsK7VZfK8U2yqsrz8Am2oKMREB/EarLF6E6rgeyuCcQxjLQ==";

        var handler = new StubHandler(async (request, requestIndex, ct) =>
        {
            switch (requestIndex)
            {
                case 0:
                    Assert.Equal(HttpMethod.Get, request.Method);
                    Assert.Equal("/auth", request.RequestUri!.AbsolutePath);
                    var challenge = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                    challenge.Headers.TryAddWithoutValidation(
                        "WWW-Authenticate",
                        "x-ndw2-interactive endpoint=\"/auth\", " +
                        "x-ndw4-interactive endpoint=\"/auth\"");
                    challenge.Headers.TryAddWithoutValidation("X-NDM-Realm", "Test Router");
                    challenge.Headers.TryAddWithoutValidation("X-NDM-Challenge", "legacy-challenge");
                    return challenge;

                case 1:
                    {
                        using var body = await ReadJsonAsync(request, ct);
                        Assert.Equal("codex", body.RootElement.GetProperty("login").GetString());
                        Assert.Equal(
                            "EBESExQVFhcYGRobHB0eHw==",
                            body.RootElement.GetProperty("nonce").GetString());
                        return CreateNdw4DataResponse(
                            """
                        {
                          "salt": "AAECAwQFBgcICQoLDA0ODw==",
                          "nonce": "server-nonce@example%value",
                          "iter": 2,
                          "memcost": 8
                        }
                        """);
                    }

                case 2:
                    {
                        using var body = await ReadJsonAsync(request, ct);
                        Assert.Equal(serverNonce, body.RootElement.GetProperty("nonce").GetString());
                        Assert.Equal(expectedClientProof, body.RootElement.GetProperty("proof").GetString());
                        return CreateNdw4DataResponse($$"""{"signature":"{{serverSignature}}"}""");
                    }

                case 3:
                    {
                        using var body = await ReadJsonAsync(request, ct);
                        Assert.Equal(serverNonce, body.RootElement.GetProperty("nonce").GetString());
                        Assert.Equal(
                            expectedSignatureProof,
                            body.RootElement.GetProperty("signature-proof").GetString());
                        return new HttpResponseMessage(HttpStatusCode.OK);
                    }

                default:
                    throw new InvalidOperationException("Unexpected request.");
            }
        });

        using var client = new KeeneticClient(
            new Uri("http://router.example/"),
            RouterAuthMode.Password,
            "codex",
            password,
            string.Empty,
            handler,
            () => Enumerable.Range(16, 16).Select(value => (byte)value).ToArray());

        await client.LoginAsync();

        Assert.True(client.IsAuthenticated);
        Assert.Equal(KeeneticAuthProtocol.Ndw4, client.AuthenticationProtocol);
        Assert.Equal(4, handler.RequestCount);
    }

    [Fact]
    public async Task LoginAsync_UsesLegacyNdw2OnOlderFirmware()
    {
        const string realm = "Legacy Router";
        const string challengeValue = "legacy-challenge";
        const string password = "legacy-password";
        var expectedPassword = ComputeLegacyPassword("codex", realm, password, challengeValue);

        var handler = new StubHandler(async (request, requestIndex, ct) =>
        {
            if (requestIndex == 0)
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                var challenge = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                challenge.Headers.TryAddWithoutValidation(
                    "WWW-Authenticate",
                    "x-ndw2-interactive endpoint=\"/auth\"");
                challenge.Headers.TryAddWithoutValidation("X-NDM-Realm", realm);
                challenge.Headers.TryAddWithoutValidation("X-NDM-Challenge", challengeValue);
                return challenge;
            }

            Assert.Equal(1, requestIndex);
            Assert.Equal(HttpMethod.Post, request.Method);
            using var body = await ReadJsonAsync(request, ct);
            Assert.Equal("codex", body.RootElement.GetProperty("login").GetString());
            Assert.Equal(expectedPassword, body.RootElement.GetProperty("password").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = new KeeneticClient(
            new Uri("http://legacy-router.example/"),
            RouterAuthMode.Password,
            "codex",
            password,
            string.Empty,
            handler);

        await client.LoginAsync();

        Assert.True(client.IsAuthenticated);
        Assert.Equal(KeeneticAuthProtocol.Ndw2, client.AuthenticationProtocol);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task GetPoliciesAsync_SendsAccessTokenOnlyInHeaderAndSkipsInteractiveLogin()
    {
        const string accessToken = "token-value";
        var handler = new StubHandler((request, requestIndex, _) =>
        {
            Assert.Equal(0, requestIndex);
            Assert.Equal("/rci/show/rc/ip/policy", request.RequestUri!.AbsolutePath);
            Assert.DoesNotContain(accessToken, request.RequestUri.OriginalString, StringComparison.Ordinal);
            Assert.True(request.Headers.TryGetValues("X-NDMA-TKN", out var values));
            Assert.Equal(accessToken, Assert.Single(values));
            return Task.FromResult(CreateJsonResponse(
                HttpStatusCode.OK,
                """{"Policy0":{"description":"VPN"}}"""));
        });

        using var client = new KeeneticClient(
            new Uri("http://router.example/"),
            RouterAuthMode.AccessToken,
            string.Empty,
            string.Empty,
            accessToken,
            handler);

        var policies = await client.GetPoliciesAsync();

        Assert.Single(policies);
        Assert.Equal(KeeneticAuthProtocol.AccessToken, client.AuthenticationProtocol);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetPoliciesAsync_ReportsRejectedAccessTokenAsAuthenticationFailure()
    {
        var handler = new StubHandler((_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));
        using var client = new KeeneticClient(
            new Uri("http://router.example/"),
            RouterAuthMode.AccessToken,
            string.Empty,
            string.Empty,
            "invalid-token",
            handler);

        await Assert.ThrowsAsync<KeeneticAuthException>(() => client.GetPoliciesAsync());
    }

    [Fact]
    public async Task GetKnownHostAsync_FindsRegisteredDeviceInKnownHostConfiguration()
    {
        var handler = new StubHandler((request, requestIndex, _) =>
        {
            Assert.Equal(0, requestIndex);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/rci/known/host", request.RequestUri!.AbsolutePath);
            return Task.FromResult(CreateJsonResponse(
                HttpStatusCode.OK,
                """{"host":[{"name":"Work PC","mac":"02:11:22:33:44:55"}]}"""));
        });
        using var client = CreateTokenClient(handler);

        var host = await client.GetKnownHostAsync("02-11-22-33-44-55");

        Assert.NotNull(host);
        Assert.Equal("02:11:22:33:44:55", host.MacAddress);
        Assert.Equal("Work PC", host.Name);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetKnownHostAsync_FallsBackToHotspotAndRequiresRegistrationEvidence()
    {
        var handler = new StubHandler((request, requestIndex, _) =>
        {
            if (requestIndex == 0)
            {
                Assert.Equal("/rci/known/host", request.RequestUri!.AbsolutePath);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));
            }

            Assert.Equal(1, requestIndex);
            Assert.Equal("/rci/show/ip/hotspot", request.RequestUri!.AbsolutePath);
            return Task.FromResult(CreateJsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "host": [
                    {"mac":"00:AA:BB:CC:DD:01","hostname":"unregistered-pc"},
                    {"mac":"00:AA:BB:CC:DD:02","name":"Registered PC","registered":true}
                  ]
                }
                """));
        });
        using var client = CreateTokenClient(handler);

        var unregistered = await client.GetKnownHostAsync("00:AA:BB:CC:DD:01");

        Assert.Null(unregistered);
    }

    [Fact]
    public async Task RegisterKnownHostAsync_RegistersSavesAndVerifiesDevice()
    {
        var handler = new StubHandler(async (request, requestIndex, ct) =>
        {
            switch (requestIndex)
            {
                case 0:
                    Assert.Equal(HttpMethod.Post, request.Method);
                    Assert.Equal("/rci/known/host", request.RequestUri!.AbsolutePath);
                    using (var body = await ReadJsonAsync(request, ct))
                    {
                        Assert.Equal("RouterTray PC", body.RootElement.GetProperty("name").GetString());
                        Assert.Equal("02:11:22:33:44:55", body.RootElement.GetProperty("mac").GetString());
                    }

                    return CreateJsonResponse(HttpStatusCode.OK, """{"status":[{"status":"ok"}]}""");

                case 1:
                    Assert.Equal(HttpMethod.Post, request.Method);
                    Assert.Equal("/rci/system/configuration/save", request.RequestUri!.AbsolutePath);
                    return CreateJsonResponse(HttpStatusCode.OK, """{"status":[{"status":"ok"}]}""");

                case 2:
                    Assert.Equal(HttpMethod.Get, request.Method);
                    Assert.Equal("/rci/known/host", request.RequestUri!.AbsolutePath);
                    return CreateJsonResponse(
                        HttpStatusCode.OK,
                        """{"host":[{"name":"RouterTray PC","mac":"02:11:22:33:44:55"}]}""");

                default:
                    throw new InvalidOperationException("Unexpected request.");
            }
        });
        using var client = CreateTokenClient(handler);

        var host = await client.RegisterKnownHostAsync(
            "02-11-22-33-44-55",
            " RouterTray PC ");

        Assert.Equal("RouterTray PC", host.Name);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task EnsureSuccessOrThrow_RejectsRciErrorInsideHttpSuccess()
    {
        using var response = CreateJsonResponse(
            HttpStatusCode.OK,
            """
            {
              "status": [
                {
                  "status": "error",
                  "code": "6553609",
                  "message": "Command was rejected."
                }
              ]
            }
            """);

        var exception = await Assert.ThrowsAsync<KeeneticRequestException>(() =>
            KeeneticClient.EnsureSuccessOrThrow(response, "set policy", CancellationToken.None));

        Assert.Contains("Command was rejected.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("6553609", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureSuccessOrThrow_AcceptsSuccessfulRciStatus()
    {
        using var response = CreateJsonResponse(
            HttpStatusCode.OK,
            """{"status":[{"status":"ok"}]}""");

        await KeeneticClient.EnsureSuccessOrThrow(response, "set policy", CancellationToken.None);
    }

    [Fact]
    public void ExtractPolicies_ReadsRawPolicyMap()
    {
        var policies = ExtractPolicies(
            """
            {
              "Policy0": { "description": "VPN" },
              "Policy1": { "description": "Direct" }
            }
            """);

        Assert.Equal(2, policies.Count);
        Assert.Contains(policies, policy => policy.Id == "Policy0" && policy.Name == "VPN");
        Assert.Contains(policies, policy => policy.Id == "Policy1" && policy.Name == "Direct");
    }

    [Fact]
    public void ExtractPolicies_UnwrapsPolicyResponse()
    {
        var policies = ExtractPolicies(
            """{"policy":{"Policy0":{"description":"VPN"}}}""");

        var policy = Assert.Single(policies);
        Assert.Equal("Policy0", policy.Id);
        Assert.Equal("VPN", policy.Name);
    }

    [Fact]
    public void ExtractPolicies_RejectsErrorEnvelope()
    {
        using var document = JsonDocument.Parse(
            """{"status":"error","message":"Command was rejected."}""");

        var exception = Assert.Throws<KeeneticRequestException>(() =>
            KeeneticClient.ExtractPolicies(document.RootElement));

        Assert.Contains("Command was rejected.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractPolicies_RejectsScalarPolicyEntries()
    {
        using var document = JsonDocument.Parse(
            """{"policy":{"Policy0":"VPN"}}""");

        Assert.Throws<KeeneticRequestException>(() =>
            KeeneticClient.ExtractPolicies(document.RootElement));
    }

    private static IReadOnlyList<PolicyInfo> ExtractPolicies(string json)
    {
        using var document = JsonDocument.Parse(json);
        return KeeneticClient.ExtractPolicies(document.RootElement);
    }

    private static KeeneticClient CreateTokenClient(HttpMessageHandler handler)
    {
        return new KeeneticClient(
            new Uri("http://router.example/"),
            RouterAuthMode.AccessToken,
            string.Empty,
            string.Empty,
            "test-token",
            handler);
    }

    private static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage CreateNdw4DataResponse(string json)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        response.Headers.TryAddWithoutValidation(
            "X-NDM-Data",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));
        return response;
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpRequestMessage request,
        CancellationToken ct)
    {
        Assert.NotNull(request.Content);
        var json = await request.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json);
    }

    private static string ComputeLegacyPassword(
        string login,
        string realm,
        string password,
        string challenge)
    {
        var md5 = Convert.ToHexString(MD5.HashData(
            Encoding.UTF8.GetBytes($"{login}:{realm}:{password}"))).ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{challenge}{md5}"))).ToLowerInvariant();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<
            HttpRequestMessage,
            int,
            CancellationToken,
            Task<HttpResponseMessage>> _responseFactory;

        public StubHandler(Func<
            HttpRequestMessage,
            int,
            CancellationToken,
            Task<HttpResponseMessage>> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return _responseFactory(request, RequestCount++, cancellationToken);
        }
    }
}
