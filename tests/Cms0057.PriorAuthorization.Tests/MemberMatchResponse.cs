using System.Net;
using System.Text.Json;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// What another payer reads back from $member-match: the HRex output Parameters with the Member's identifier here,
/// or an OperationOutcome saying why there was no match. Reads the raw JSON so the tests do not depend on the
/// service's own model.
/// </summary>
public sealed class MemberMatchResponse
{
    private readonly JsonElement _root;

    public MemberMatchResponse(HttpStatusCode status, string body)
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

    private JsonElement? Parameter(string name) =>
        ResourceType == "Parameters" && _root.TryGetProperty("parameter", out var parameters)
            ? parameters.EnumerateArray().Where(p => p.GetProperty("name").GetString() == name).Select(p => (JsonElement?)p).FirstOrDefault()
            : null;

    /// <summary>The Member's identifier with this plan, which the payer quotes in everything it asks for next.</summary>
    public string? MemberIdentifier => Parameter("MemberIdentifier")?.GetProperty("valueIdentifier").GetProperty("value").GetString();

    /// <summary>The identifier's type code; HRex fixes it to UMB, the unique member identifier.</summary>
    public string? MemberIdentifierType => Parameter("MemberIdentifier")?.GetProperty("valueIdentifier").GetProperty("type").GetProperty("coding")[0].GetProperty("code").GetString();

    /// <summary>The Member as a Patient reference here, for $everything and the searches.</summary>
    public string? MemberId => Parameter("MemberId")?.GetProperty("valueReference").GetProperty("reference").GetString();

    public string? ErrorDiagnostics =>
        ResourceType == "OperationOutcome" ? _root.GetProperty("issue")[0].GetProperty("diagnostics").GetString() : null;
}
