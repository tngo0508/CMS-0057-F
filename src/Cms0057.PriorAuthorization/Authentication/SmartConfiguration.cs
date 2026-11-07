namespace Cms0057.PriorAuthorization.Authentication;

/// <summary>
/// The SMART App Launch discovery document (<c>/.well-known/smart-configuration</c>): where a provider system
/// obtains a token and how it must authenticate. This service offers Backend Services only, so there is no
/// authorization endpoint and the only client authentication method is a signed JWT (private_key_jwt).
/// </summary>
public static class SmartConfiguration
{
    public const string TokenPath = "/auth/token";

    /// <summary>
    /// The system scopes the operations need, in the SMART 1 spelling; a client may ask in the SMART 2 spelling
    /// (<c>system/Claim.crus</c>) and is answered in it (see <see cref="Scopes"/>).
    /// </summary>
    public static readonly string[] ScopesSupported =
        ["system/Claim.read", "system/Claim.write", "system/ClaimResponse.read", "system/Subscription.read", "system/Subscription.write", "system/Questionnaire.read", "system/ExplanationOfBenefit.read", "system/Group.read"];

    public static readonly string[] SigningAlgorithms = ["RS384", "ES384"];

    public static object For(string serverRoot) => new
    {
        token_endpoint = serverRoot + TokenPath,
        token_endpoint_auth_methods_supported = new[] { "private_key_jwt" },
        token_endpoint_auth_signing_alg_values_supported = SigningAlgorithms,
        scopes_supported = ScopesSupported,
        grant_types_supported = new[] { "client_credentials" },
        capabilities = new[] { "client-confidential-asymmetric", "permission-v1", "permission-v2" },
    };
}
