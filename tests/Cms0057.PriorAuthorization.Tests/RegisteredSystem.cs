using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// A provider system (or EHR) as it holds its own SMART Backend Services credentials: a client id the plan knows
/// it by and a private key whose public half the plan has on file. Test keys are generated fresh for each
/// instance; nothing here is a real credential.
/// </summary>
public sealed class RegisteredSystem
{
    private readonly RsaSecurityKey _key;

    public RegisteredSystem(string clientId, string scopes)
    {
        ClientId = clientId;
        Scopes = scopes;
        _key = new RsaSecurityKey(RSA.Create(2048)) { KeyId = $"{clientId}-key" };
    }

    public string ClientId { get; }

    /// <summary>The scopes the plan registered this system for, space-separated.</summary>
    public string Scopes { get; }

    /// <summary>The public key as the plan's registration holds it, as JWK members.</summary>
    public IReadOnlyDictionary<string, string> PublicJwk
    {
        get
        {
            var parameters = _key.Rsa.ExportParameters(includePrivateParameters: false);
            return new Dictionary<string, string>
            {
                ["kty"] = "RSA",
                ["kid"] = _key.KeyId,
                ["alg"] = "RS384",
                ["use"] = "sig",
                ["n"] = Base64UrlEncoder.Encode(parameters.Modulus),
                ["e"] = Base64UrlEncoder.Encode(parameters.Exponent),
            };
        }
    }

    /// <summary>
    /// The JWT the system signs to authenticate at the token endpoint: iss and sub are its client id, aud is the
    /// token endpoint, exp is at most five minutes ahead, jti is unique. Each parameter can be bent for a test.
    /// </summary>
    public string ClientAssertion(
        DateTimeOffset now,
        string audience = "http://localhost/auth/token",
        TimeSpan? lifetime = null,
        string? jti = null,
        string? subject = null,
        RsaSecurityKey? signedWith = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = ClientId,
            Audience = audience,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = subject ?? ClientId,
                [JwtRegisteredClaimNames.Jti] = jti ?? Guid.NewGuid().ToString("N"),
            },
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = (now + (lifetime ?? TimeSpan.FromMinutes(4))).UtcDateTime,
            SigningCredentials = new SigningCredentials(signedWith ?? _key, SecurityAlgorithms.RsaSha384),
        };
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }

    /// <summary>A key the plan has never seen.</summary>
    public static RsaSecurityKey UnknownKey() => new(RSA.Create(2048)) { KeyId = "unknown-key" };
}
