using System.Net;
using System.Text;
using System.Text.Json;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// Stands in for the web server where a registered system publishes its JWK Set, at the service's outbound HTTP
/// boundary: answers any GET with the system's public keys.
/// </summary>
public sealed class JwksServer(RegisteredSystem system) : HttpMessageHandler
{
    public List<Uri> Fetched { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Fetched.Add(request.RequestUri!);
        var jwks = JsonSerializer.Serialize(new { keys = new[] { system.PublicJwk } });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jwks, Encoding.UTF8, "application/json"),
        });
    }
}
