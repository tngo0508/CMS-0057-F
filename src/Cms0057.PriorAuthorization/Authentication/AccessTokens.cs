using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Cms0057.PriorAuthorization.Authentication;

/// <summary>
/// The token endpoint's work (SMART App Launch 1.0 Backend Services, client_credentials with a signed JWT as the
/// client's credential): check the client assertion against the registered key, then issue a short-lived access
/// token signed with the service's own key.
/// </summary>
public sealed class AccessTokens(
    ClientRegister clients,
    IOptions<AuthenticationOptions> options,
    TimeProvider clock)
{
    public const string ClientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    /// <summary>SMART has a client assertion expire no more than five minutes after it is presented.</summary>
    public static readonly TimeSpan LongestAssertionLife = TimeSpan.FromMinutes(5);

    /// <summary>The key access tokens are signed with; one per process, so tokens do not survive a restart.</summary>
    public static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "prior-authorization-" + Guid.NewGuid().ToString("N") };

    private readonly JsonWebTokenHandler _jwt = new() { SetDefaultTimesOnTokenCreation = false };

    /// <summary>Assertions already accepted (client id and jti), each kept until it would have expired anyway.</summary>
    private readonly ConcurrentDictionary<(string ClientId, string Jti), DateTimeOffset> _presented = new();

    public sealed record Request(string? GrantType, string? ClientAssertionType, string? ClientAssertion, string? Scope);

    public sealed record Issued(string AccessToken, int ExpiresIn, string Scope);

    /// <summary>Why a token request was refused, as RFC 6749 section 5.2 names the reasons.</summary>
    public sealed class Refused(string error, string description) : Exception(description)
    {
        public string Error { get; } = error;
    }

    /// <param name="tokenEndpoint">This endpoint's own URL, which the client assertion must name as its audience.</param>
    /// <exception cref="Refused">The request is not a valid token request from a registered client.</exception>
    public async Task<Issued> Issue(Request request, string tokenEndpoint)
    {
        if (request.GrantType != "client_credentials")
            throw new Refused("unsupported_grant_type", "grant_type must be client_credentials");
        if (request.ClientAssertionType != ClientAssertionType)
            throw new Refused("invalid_request", $"client_assertion_type must be {ClientAssertionType}");
        if (string.IsNullOrEmpty(request.ClientAssertion) || !_jwt.CanReadToken(request.ClientAssertion))
            throw new Refused("invalid_request", "client_assertion must be a signed JWT");

        var now = clock.GetUtcNow();
        var client = await Authenticate(request.ClientAssertion, tokenEndpoint, now);
        var scope = Grant(client, request.Scope);
        var lifetime = options.Value.AccessTokenLifetime;
        var token = _jwt.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = tokenEndpoint,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = client.ClientId,
                ["client_id"] = client.ClientId,
                ["scope"] = scope,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            },
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = (now + lifetime).UtcDateTime,
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha384),
        });
        return new Issued(token, (int)lifetime.TotalSeconds, scope);
    }

    /// <summary>
    /// Checks the client assertion: from a registered client, signed with one of its keys, addressed to this token
    /// endpoint, not expired and not good for more than five minutes, and never presented before.
    /// </summary>
    private async Task<RegisteredClient> Authenticate(string assertion, string tokenEndpoint, DateTimeOffset now)
    {
        var unverified = _jwt.ReadJsonWebToken(assertion);
        var client = clients.Find(unverified.Issuer)
                     ?? throw new Refused("invalid_client", "The client is not registered");

        var validation = await _jwt.ValidateTokenAsync(assertion, new TokenValidationParameters
        {
            ValidIssuer = client.ClientId,
            ValidAudience = tokenEndpoint,
            IssuerSigningKeys = await clients.KeysOf(client),
            ValidAlgorithms = SmartConfiguration.SigningAlgorithms,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            LifetimeValidator = (notBefore, expires, _, _) =>
                expires is { } exp
                && exp > now.UtcDateTime
                && exp <= (now + LongestAssertionLife).UtcDateTime
                && (notBefore is null || notBefore <= now.UtcDateTime),
        });
        if (!validation.IsValid)
            throw new Refused("invalid_client", "The client assertion is not valid: " + validation.Exception?.Message);

        var verified = (JsonWebToken)validation.SecurityToken;
        if (verified.Subject != client.ClientId)
            throw new Refused("invalid_client", "The client assertion's sub must equal its iss");
        if (string.IsNullOrEmpty(verified.Id))
            throw new Refused("invalid_client", "The client assertion must carry a jti");
        RefuseReplay(client.ClientId, verified.Id, verified.ValidTo, now);
        return client;
    }

    private void RefuseReplay(string clientId, string jti, DateTime expires, DateTimeOffset now)
    {
        foreach (var (key, expired) in _presented)
            if (expired <= now) _presented.TryRemove(key, out _);
        if (!_presented.TryAdd((clientId, jti), new DateTimeOffset(expires, TimeSpan.Zero)))
            throw new Refused("invalid_client", "The client assertion has already been presented");
    }

    /// <summary>
    /// What the client asked for, as far as it is registered for it (SMART lets the server grant fewer scopes than
    /// were asked for, and the response says which); everything it is registered for when it asks for nothing in
    /// particular. Refused only when none of what it asked for can be granted.
    /// </summary>
    private static string Grant(RegisteredClient client, string? requested)
    {
        var registered = Scopes.Parse(client.Scopes);
        var asked = Scopes.Parse(requested);
        if (asked.Count == 0) return string.Join(' ', registered);
        var granted = asked.SelectMany(scope => Scopes.Granted(registered, scope)).Distinct().ToList();
        if (granted.Count == 0)
            throw new Refused("invalid_scope", "The client is not registered for any of: " + string.Join(' ', asked));
        return string.Join(' ', granted);
    }
}
