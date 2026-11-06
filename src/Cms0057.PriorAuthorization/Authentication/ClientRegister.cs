using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Cms0057.PriorAuthorization.Authentication;

/// <summary>The systems the plan has registered, and the public keys each one's client assertions are checked against.</summary>
public sealed class ClientRegister(IOptions<AuthenticationOptions> options, IHttpClientFactory httpClients)
{
    public const string HttpClientName = "ClientKeys";

    public RegisteredClient? Find(string clientId) =>
        options.Value.Clients.FirstOrDefault(c => c.ClientId == clientId);

    /// <summary>The client's current public keys: the ones registered inline, plus the ones at its JWKS URL.</summary>
    public async Task<IReadOnlyList<SecurityKey>> KeysOf(RegisteredClient client)
    {
        var keys = new List<SecurityKey>();
        if (client.Jwks is not null)
            keys.AddRange(client.Jwks.Keys.Select(members => new JsonWebKey(JsonSerializer.Serialize(members))));
        if (client.JwksUri is not null)
        {
            var json = await httpClients.CreateClient(HttpClientName).GetStringAsync(client.JwksUri);
            keys.AddRange(new JsonWebKeySet(json).Keys);
        }
        return keys;
    }
}
