using System.Net.Http.Headers;
using System.Text.Json;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// How a call to the service identifies itself: as a registered system, which obtains a token the way a real
/// provider system would and sends it as a bearer token; with a given token; or not at all.
/// </summary>
public abstract record Caller
{
    public static readonly Caller Anonymous = new Nobody();

    public static Caller As(RegisteredSystem system) => new Registered(system);

    public static Caller Bearing(string accessToken) => new Token(accessToken);

    private sealed record Nobody : Caller;

    private sealed record Registered(RegisteredSystem System) : Caller;

    private sealed record Token(string AccessToken) : Caller;

    /// <summary>
    /// Attaches the caller's bearer token to each request. A registered system's token is fetched from the token
    /// endpoint with a fresh client assertion, and fetched again once the test clock has run past its expiry.
    /// </summary>
    internal sealed class Handler(Caller caller, PriorAuthorizationApi api) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var token = caller switch
            {
                Token given => given.AccessToken,
                Registered registered => await api.AccessTokenFor(registered.System, assertion => FetchToken(request.RequestUri!, assertion, ct)),
                _ => null,
            };
            if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await base.SendAsync(request, ct);
        }

        private async Task<(string AccessToken, int ExpiresIn)> FetchToken(Uri calledUrl, string clientAssertion, CancellationToken ct)
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
                ["client_assertion"] = clientAssertion,
            });
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(calledUrl, "/auth/token")) { Content = form };
            using var response = await base.SendAsync(tokenRequest, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"The token endpoint answered {(int)response.StatusCode}: {body}");
            var json = JsonDocument.Parse(body).RootElement;
            return (json.GetProperty("access_token").GetString()!, json.GetProperty("expires_in").GetInt32());
        }
    }
}
