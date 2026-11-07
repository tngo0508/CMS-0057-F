using System.Net;
using System.Text.Json;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// What a provider system (or another payer) reads from this service about the Groups it may see: attribution
/// lists as PDex Provider Group resources. Reads the raw JSON so the tests do not depend on the service's model.
/// </summary>
public sealed class GroupResponse
{
    private readonly JsonElement _root;

    public GroupResponse(HttpStatusCode status, string body)
    {
        Status = status;
        Body = body;
        using var document = JsonDocument.Parse(body);
        _root = document.RootElement.Clone();
        ResourceType = _root.GetProperty("resourceType").GetString()!;
    }

    public HttpStatusCode Status { get; }
    public string Body { get; }
    public string ResourceType { get; }

    public int Total => _root.GetProperty("total").GetInt32();

    public IReadOnlyList<AttributionList> Groups
    {
        get
        {
            if (ResourceType == "Group") return [new AttributionList(_root)];
            if (ResourceType != "Bundle" || !_root.TryGetProperty("entry", out var entries)) return [];
            return [.. entries.EnumerateArray().Select(e => e.GetProperty("resource")).Where(r => r.GetProperty("resourceType").GetString() == "Group").Select(r => new AttributionList(r))];
        }
    }

    public AttributionList Single => Groups.Single();

    public string? ErrorDiagnostics =>
        ResourceType == "OperationOutcome" ? _root.GetProperty("issue")[0].GetProperty("diagnostics").GetString() : null;
}

/// <summary>One attribution list as a provider reads it from a Group.</summary>
public sealed class AttributionList(JsonElement group)
{
    public string Id => group.GetProperty("id").GetString()!;
    public string? Profile => group.GetProperty("meta").GetProperty("profile")[0].GetString();
    public string? Type => group.GetProperty("type").GetString();
    public bool? Actual => group.GetProperty("actual").GetBoolean();

    /// <summary>The NPI the list is for.</summary>
    public string? Npi => group.TryGetProperty("identifier", out var ids)
        ? ids.EnumerateArray().FirstOrDefault(i => i.GetProperty("system").GetString() == "http://hl7.org/fhir/sid/us-npi").GetProperty("value").GetString()
        : null;

    /// <summary>The Members on the list, as the Patient references the list names them by.</summary>
    public IReadOnlyList<string?> Members => group.TryGetProperty("member", out var members)
        ? [.. members.EnumerateArray().Select(m => m.GetProperty("entity").GetProperty("reference").GetString())]
        : [];

    public string? Code => group.GetProperty("code").GetProperty("coding")[0].GetProperty("code").GetString();
}

/// <summary>The answer to a Bulk Data kick-off: 202 and where to poll, or an error.</summary>
public sealed record ExportKickoff(HttpStatusCode Status, string? ContentLocation, string Body);

/// <summary>The answer to a Bulk Data status request: the manifest once complete.</summary>
public sealed class ExportManifest(HttpStatusCode status, string body)
{
    public HttpStatusCode Status { get; } = status;
    public string Body { get; } = body;

    private JsonElement Root => JsonDocument.Parse(Body).RootElement;

    public string? TransactionTime => Root.GetProperty("transactionTime").GetString();
    public bool RequiresAccessToken => Root.GetProperty("requiresAccessToken").GetBoolean();

    /// <summary>The output files: resource type, URL and count.</summary>
    public IReadOnlyList<(string Type, string Url, int Count)> Output =>
        [.. Root.GetProperty("output").EnumerateArray().Select(o =>
            (o.GetProperty("type").GetString()!, o.GetProperty("url").GetString()!, o.TryGetProperty("count", out var c) ? c.GetInt32() : -1))];
}
