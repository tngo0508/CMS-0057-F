using System.Net;
using System.Text.Json;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// What the Patient Access API reads from this service: the HTTP status and the prior authorization information
/// (PDex Prior Authorization ExplanationOfBenefit resources) a search or a read answered with. Reads the raw JSON so
/// the tests do not depend on the service's own model.
/// </summary>
public sealed class PatientAccessResponse
{
    private readonly JsonElement _root;

    public PatientAccessResponse(HttpStatusCode status, string body)
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

    public string BundleType => _root.GetProperty("type").GetString()!;

    /// <summary>For a search: how many matched.</summary>
    public int Total => _root.GetProperty("total").GetInt32();

    /// <summary>The prior authorizations found by a search, in the order answered; a read's one resource.</summary>
    public IReadOnlyList<PriorAuthorizationInformation> PriorAuthorizations
    {
        get
        {
            if (ResourceType == "ExplanationOfBenefit") return [new PriorAuthorizationInformation(_root)];
            if (ResourceType != "Bundle" || !_root.TryGetProperty("entry", out var entries)) return [];
            return [.. entries.EnumerateArray()
                .Select(e => e.GetProperty("resource"))
                .Where(r => r.GetProperty("resourceType").GetString() == "ExplanationOfBenefit")
                .Select(r => new PriorAuthorizationInformation(r))];
        }
    }

    /// <summary>The resource types of a Bundle's entries, in order: what else came with the prior authorizations.</summary>
    public IReadOnlyList<string?> ResourceTypes =>
        ResourceType == "Bundle" && _root.TryGetProperty("entry", out var entries)
            ? [.. entries.EnumerateArray().Select(e => e.GetProperty("resource").GetProperty("resourceType").GetString())]
            : [];

    /// <summary>The one prior authorization a search found or a read answered with.</summary>
    public PriorAuthorizationInformation Single => PriorAuthorizations.Single();

    public string? ErrorDiagnostics =>
        ResourceType == "OperationOutcome" ? _root.GetProperty("issue")[0].GetProperty("diagnostics").GetString() : null;
}

/// <summary>One prior authorization as the Patient Access API reads it from an ExplanationOfBenefit.</summary>
public sealed class PriorAuthorizationInformation(JsonElement eob)
{
    private const string Pdex = "http://hl7.org/fhir/us/davinci-pdex/StructureDefinition/";

    public string Id => eob.GetProperty("id").GetString()!;
    public string? Profile => eob.GetProperty("meta").GetProperty("profile")[0].GetString();
    public string? LastUpdated => eob.GetProperty("meta").TryGetProperty("lastUpdated", out var v) ? v.GetString() : null;
    public string? Use => eob.GetProperty("use").GetString();
    public string? ResourceStatus => eob.GetProperty("status").GetString();
    public string? Outcome => eob.GetProperty("outcome").GetString();
    public string? Patient => eob.GetProperty("patient").GetProperty("reference").GetString();
    public string? Created => eob.GetProperty("created").GetString();

    /// <summary>The services asked for, each as its billing code followed by its modifiers.</summary>
    public IReadOnlyList<string> Services =>
        [.. eob.GetProperty("item").EnumerateArray().Select(item =>
        {
            var code = item.GetProperty("productOrService").GetProperty("coding")[0].GetProperty("code").GetString();
            var modifiers = item.TryGetProperty("modifier", out var m)
                ? m.EnumerateArray().Select(x => x.GetProperty("coding")[0].GetProperty("code").GetString())
                : [];
            return string.Join(' ', new[] { code }.Concat(modifiers));
        })];

    /// <summary>X12 306 review action for the request as a whole: A1 certified, A2 not certified, A3 not required, A4 pended.</summary>
    public string? ReviewActionCode => ReviewActionCodeOf(HeaderReviewAction);

    /// <summary>The review action of every item, in item order.</summary>
    public IReadOnlyList<string?> ItemReviewActionCodes =>
        [.. eob.GetProperty("item").EnumerateArray().Select(item => ReviewActionCodeOf(ReviewActionOf(item.GetProperty("adjudication"))))];

    public string? AuthorizationNumber =>
        HeaderReviewAction is { } action ? Extension(action, "number")?.GetProperty("valueString").GetString() : null;

    /// <summary>When the decision was made: the adjudication's action date.</summary>
    public string? DecidedAt =>
        Extension(eob.GetProperty("adjudication")[0], Pdex + "base-ext-when-adjudicated")?.GetProperty("valueDateTime").GetString();

    /// <summary>The period the authorization covers: preAuthRefPeriod.</summary>
    public (string? Start, string? End) AuthorizationPeriod
    {
        get
        {
            if (!eob.TryGetProperty("preAuthRefPeriod", out var periods) || periods.GetArrayLength() == 0) return (null, null);
            var period = periods[0];
            return (period.TryGetProperty("start", out var s) ? s.GetString() : null,
                period.TryGetProperty("end", out var e) ? e.GetString() : null);
        }
    }

    /// <summary>The Denial Reason in the Reviewer's words, and the plan's code for it when one was given.</summary>
    public (string? Code, string? Text) DenialReason
    {
        get
        {
            var reason = HeaderReviewAction is { } action ? Extension(action, "reasonCode")?.GetProperty("valueCodeableConcept") : null;
            if (reason is not { } concept) return (null, null);
            var code = concept.TryGetProperty("coding", out var codings) ? codings[0].GetProperty("code").GetString() : null;
            var text = concept.TryGetProperty("text", out var t) ? t.GetString() : null;
            return (code, text);
        }
    }

    public IReadOnlyList<string?> ProcessNotes =>
        eob.TryGetProperty("processNote", out var notes) ? [.. notes.EnumerateArray().Select(n => n.GetProperty("text").GetString())] : [];

    private JsonElement? HeaderReviewAction =>
        eob.TryGetProperty("adjudication", out var adjudications) ? ReviewActionOf(adjudications) : null;

    private static JsonElement? ReviewActionOf(JsonElement adjudications)
    {
        foreach (var adjudication in adjudications.EnumerateArray())
            if (Extension(adjudication, Pdex + "extension-reviewAction") is { } action) return action;
        return null;
    }

    private static string? ReviewActionCodeOf(JsonElement? reviewAction) =>
        reviewAction is { } action
            ? Extension(action, Pdex + "extension-reviewActionCode")?.GetProperty("valueCodeableConcept").GetProperty("coding")[0].GetProperty("code").GetString()
            : null;

    private static JsonElement? Extension(JsonElement parent, string url)
    {
        if (!parent.TryGetProperty("extension", out var extensions)) return null;
        foreach (var extension in extensions.EnumerateArray())
            if (extension.GetProperty("url").GetString() == url) return extension;
        return null;
    }
}
