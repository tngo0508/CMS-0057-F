using System.Net;
using System.Text.Json;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>What a provider system reads from the token endpoint's answer: a token, or an OAuth error.</summary>
public sealed class TokenResponse(HttpStatusCode status, string body)
{
    private readonly JsonElement _root = JsonDocument.Parse(body.Length == 0 ? "{}" : body).RootElement.Clone();

    public HttpStatusCode Status { get; } = status;
    public string Body { get; } = body;

    public string? AccessToken => String("access_token");
    public string? TokenType => String("token_type");
    public int? ExpiresIn => _root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : null;
    public string? Scope => String("scope");

    /// <summary>The OAuth 2.0 error code (invalid_client, invalid_request, invalid_scope...), if refused.</summary>
    public string? Error => String("error");

    private string? String(string name) => _root.TryGetProperty(name, out var value) ? value.GetString() : null;
}
