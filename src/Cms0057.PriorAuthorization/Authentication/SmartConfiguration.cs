namespace Cms0057.PriorAuthorization.Authentication;

/// <summary>
/// The SMART App Launch discovery document (<c>/.well-known/smart-configuration</c>): where a provider system
/// obtains a token and how it must authenticate. This service offers Backend Services only, so there is no
/// authorization endpoint and the only client authentication method is a signed JWT (private_key_jwt).
/// </summary>
public static class SmartConfiguration
{
    public const string TokenPath = "/auth/token";

    /// <summary>SMART App Launch 1.0 system scopes; a scope may use <c>*</c> for the resource or the permission.</summary>
    public static readonly string[] ScopesSupported =
        ["system/Claim.read", "system/Claim.write", "system/Subscription.write"];

    public static readonly string[] SigningAlgorithms = ["RS384", "ES384"];

    public static object For(string serverRoot) => new
    {
        token_endpoint = serverRoot + TokenPath,
        token_endpoint_auth_methods_supported = new[] { "private_key_jwt" },
        token_endpoint_auth_signing_alg_values_supported = SigningAlgorithms,
        scopes_supported = ScopesSupported,
        grant_types_supported = new[] { "client_credentials" },
        capabilities = new[] { "client-confidential-asymmetric" },
    };
}
