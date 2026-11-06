using System.Net;
using System.Text.Json;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// What an EHR reads from a CDS Hooks response: the coverage information the payer attached to the draft order.
/// </summary>
public sealed class HookResponse(HttpStatusCode status, string body)
{
    private const string CoverageInformationUrl =
        "http://hl7.org/fhir/us/davinci-crd/StructureDefinition/ext-coverage-information";

    private readonly JsonElement _root = JsonDocument.Parse(body.Length == 0 ? "{}" : body).RootElement.Clone();

    public HttpStatusCode Status { get; } = status;
    public string Body { get; } = body;

    public int CardCount => _root.TryGetProperty("cards", out var cards) ? cards.GetArrayLength() : 0;

    public int SystemActionCount =>
        _root.TryGetProperty("systemActions", out var actions) ? actions.GetArrayLength() : 0;

    public string SystemActionType => SystemAction.GetProperty("type").GetString()!;

    public string UpdatedResourceType => UpdatedResource.GetProperty("resourceType").GetString()!;

    public string UpdatedResourceId => UpdatedResource.GetProperty("id").GetString()!;

    /// <summary>CRD: no-auth, auth-needed, satisfied, performpa or conditional.</summary>
    public string? PriorAuthorizationNeeded => CoverageInformation("pa-needed")?.GetProperty("valueCode").GetString();

    public string? Covered => CoverageInformation("covered")?.GetProperty("valueCode").GetString();

    public string? DocumentationNeeded => CoverageInformation("doc-needed")?.GetProperty("valueCode").GetString();

    public string? CoverageReference =>
        CoverageInformation("coverage")?.GetProperty("valueReference").GetProperty("reference").GetString();

    public string? ErrorMessage =>
        _root.TryGetProperty("detail", out var detail) ? detail.GetString() : null;

    private JsonElement SystemAction => _root.GetProperty("systemActions")[0];

    private JsonElement UpdatedResource => SystemAction.GetProperty("resource");

    private JsonElement? CoverageInformation(string subExtension)
    {
        if (!UpdatedResource.TryGetProperty("extension", out var extensions)) return null;
        foreach (var extension in extensions.EnumerateArray())
        {
            if (extension.GetProperty("url").GetString() != CoverageInformationUrl) continue;
            foreach (var inner in extension.GetProperty("extension").EnumerateArray())
                if (inner.GetProperty("url").GetString() == subExtension) return inner;
        }
        return null;
    }
}
