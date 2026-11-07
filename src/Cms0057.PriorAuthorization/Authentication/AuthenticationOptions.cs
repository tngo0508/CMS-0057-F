namespace Cms0057.PriorAuthorization.Authentication;

/// <summary>
/// The <c>Authentication</c> section of the configuration: whether the Prior Authorization API demands a token
/// at all, how long a token lives, and which systems may ask for one.
/// </summary>
public sealed class AuthenticationOptions
{
    public const string Section = "Authentication";

    /// <summary>
    /// Off, the service answers unauthenticated calls as Da Vinci's br-payer does in its sample setup. The Final
    /// Rule requires SMART Backend Services, so this is for local demonstration only.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How long an access token is good for. SMART recommends five minutes for Backend Services.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether the CDS Hooks endpoints (CRD) demand an access token like the FHIR ones. CDS Hooks has the EHR
    /// identify itself with a JWT it signs, not a token from the payer, and Da Vinci's br-provider run without its
    /// security server sends nothing at all; off, CRD answers an EHR that presents no credentials. The Prior
    /// Authorization API itself is unaffected.
    /// </summary>
    public bool RequireTokenForCdsHooks { get; set; } = true;

    public List<RegisteredClient> Clients { get; set; } = [];
}

/// <summary>
/// A provider system or EHR the plan has registered: the client id it authenticates as, the public key(s) its
/// client assertions are checked against, and the scopes it may be granted.
/// </summary>
public sealed class RegisteredClient
{
    public string ClientId { get; set; } = "";

    /// <summary>Who this is, for the plan's own records.</summary>
    public string? Name { get; set; }

    /// <summary>The system's JWK Set, given inline; the keys' members (kty, n, e, crv, x, y, kid, alg...) as JWK writes them.</summary>
    public JwkSet? Jwks { get; set; }

    /// <summary>Where the system publishes its JWK Set, for a system that rotates keys; fetched when it authenticates.</summary>
    public string? JwksUri { get; set; }

    /// <summary>The SMART system scopes this client may be granted, space-separated.</summary>
    public string Scopes { get; set; } = "";
}

/// <summary>A JWK Set as configuration holds it: each key is its JWK members by name.</summary>
public sealed class JwkSet
{
    public List<Dictionary<string, string>> Keys { get; set; } = [];
}
