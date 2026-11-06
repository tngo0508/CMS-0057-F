using System.Net;
using System.Text.Json;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// What a provider system reads from a PAS response: the HTTP status and, for the single requested item, the
/// review action and what comes with it. Reads the raw JSON so the tests do not depend on the service's own model.
/// </summary>
public sealed class PasResponse
{
    private const string Pas = "http://hl7.org/fhir/us/davinci-pas/StructureDefinition/";

    private readonly JsonElement? _claimResponse;

    public PasResponse(HttpStatusCode status, string body)
    {
        Status = status;
        Body = body;
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement.Clone();
        ResourceType = root.GetProperty("resourceType").GetString()!;
        _claimResponse = FindClaimResponse(root);
    }

    public HttpStatusCode Status { get; }
    public string Body { get; }
    public string ResourceType { get; }

    /// <summary>False when an inquiry found no request to report on.</summary>
    public bool HasAnswer => _claimResponse is not null;

    public string ClaimResponseId => ClaimResponse.GetProperty("id").GetString()!;

    /// <summary>The id and status of a Subscription the service accepted.</summary>
    public (string? Id, string? Status) Subscription
    {
        get
        {
            var root = JsonDocument.Parse(Body).RootElement;
            return (root.TryGetProperty("id", out var id) ? id.GetString() : null,
                root.TryGetProperty("status", out var status) ? status.GetString() : null);
        }
    }

    /// <summary>For a subscription notification: the first entry's resource type and the topic it names.</summary>
    public (string ResourceType, string? Topic) NotificationStatus
    {
        get
        {
            var first = JsonDocument.Parse(Body).RootElement.GetProperty("entry")[0].GetProperty("resource");
            string? topic = null;
            if (first.TryGetProperty("parameter", out var parameters))
                foreach (var parameter in parameters.EnumerateArray())
                    if (parameter.GetProperty("name").GetString() == "topic")
                        topic = parameter.GetProperty("valueCanonical").GetString();
            return (first.GetProperty("resourceType").GetString()!, topic);
        }
    }

    public string BundleType => JsonDocument.Parse(Body).RootElement.GetProperty("type").GetString()!;

    /// <summary>X12 306 review action: A1 certified, A2 not certified, A3 not required, A4 pended.</summary>
    public string ReviewActionCode => ReviewActionCodeOf(ReviewAction);

    /// <summary>The review action of every item, in item order.</summary>
    public IReadOnlyList<string> ReviewActionCodes =>
        [.. ClaimResponse.GetProperty("item").EnumerateArray().Select(item => ReviewActionCodeOf(ReviewActionOf(item)))];

    private static string ReviewActionCodeOf(JsonElement reviewAction) =>
        Extension(reviewAction, Pas + "extension-reviewActionCode")!.Value
            .GetProperty("valueCodeableConcept").GetProperty("coding")[0].GetProperty("code").GetString()!;

    public string? AuthorizationNumber =>
        Extension(ReviewAction, "number")?.GetProperty("valueString").GetString();

    public string? AuthorizationEnds =>
        Extension(Item, Pas + "extension-itemPreAuthPeriod")?.GetProperty("valuePeriod").GetProperty("end").GetString();

    public string? DenialReason =>
        ClaimResponse.TryGetProperty("processNote", out var notes) ? notes[0].GetProperty("text").GetString() : null;

    /// <summary>The coded reason on the review action (PAS reasonCode), and the text that rides with it.</summary>
    public (string? Code, string? Text) DenialReasonCode
    {
        get
        {
            var reason = Extension(ReviewAction, "reasonCode")?.GetProperty("valueCodeableConcept");
            if (reason is not { } concept) return (null, null);
            var code = concept.TryGetProperty("coding", out var codings) ? codings[0].GetProperty("code").GetString() : null;
            var text = concept.TryGetProperty("text", out var t) ? t.GetString() : null;
            return (code, text);
        }
    }

    public string? ErrorDiagnostics =>
        ResourceType == "OperationOutcome"
            ? JsonDocument.Parse(Body).RootElement.GetProperty("issue")[0].GetProperty("diagnostics").GetString()
            : null;

    /// <summary>Every issue of an OperationOutcome, each as its location (when given) and its diagnostics.</summary>
    public IReadOnlyList<string> ErrorIssues
    {
        get
        {
            if (ResourceType != "OperationOutcome") return [];
            var issues = new List<string>();
            foreach (var issue in JsonDocument.Parse(Body).RootElement.GetProperty("issue").EnumerateArray())
            {
                var location = issue.TryGetProperty("expression", out var expressions)
                    ? string.Join(", ", expressions.EnumerateArray().Select(e => e.GetString()))
                    : null;
                var diagnostics = issue.TryGetProperty("diagnostics", out var d) ? d.GetString() : null;
                issues.Add(location is null ? diagnostics ?? "" : $"{location}: {diagnostics}");
            }
            return issues;
        }
    }

    private JsonElement ClaimResponse =>
        _claimResponse ?? throw new InvalidOperationException($"No ClaimResponse in the response: {Body}");

    private JsonElement Item => ClaimResponse.GetProperty("item")[0];

    private JsonElement ReviewAction => ReviewActionOf(Item);

    private JsonElement ReviewActionOf(JsonElement item) =>
        Extension(item.GetProperty("adjudication")[0], Pas + "extension-reviewAction")
        ?? throw new InvalidOperationException($"No review action in the response: {Body}");

    private static JsonElement? Extension(JsonElement parent, string url)
    {
        if (!parent.TryGetProperty("extension", out var extensions)) return null;
        foreach (var extension in extensions.EnumerateArray())
            if (extension.GetProperty("url").GetString() == url) return extension;
        return null;
    }

    /// <summary>Finds the ClaimResponse in a response Bundle, or in the Bundle a Parameters resource wraps.</summary>
    private static JsonElement? FindClaimResponse(JsonElement resource)
    {
        switch (resource.GetProperty("resourceType").GetString())
        {
            case "ClaimResponse":
                return resource;
            case "Bundle" when resource.TryGetProperty("entry", out var entries):
                foreach (var entry in entries.EnumerateArray())
                    if (FindClaimResponse(entry.GetProperty("resource")) is { } found) return found;
                return null;
            case "Parameters" when resource.TryGetProperty("parameter", out var parameters):
                foreach (var parameter in parameters.EnumerateArray())
                    if (parameter.TryGetProperty("resource", out var inner) && FindClaimResponse(inner) is { } found)
                        return found;
                return null;
            default:
                return null;
        }
    }
}
