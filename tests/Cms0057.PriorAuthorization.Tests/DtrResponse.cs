using System.Net;
using System.Text.Json;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// What a DTR app reads from the payer's answer: the questionnaire packages (each a Bundle with a Questionnaire
/// and a QuestionnaireResponse), or the outcome that says why there is none. Reads the raw JSON.
/// </summary>
public sealed class DtrResponse
{
    private const string Dtr = "http://hl7.org/fhir/us/davinci-dtr/StructureDefinition/";
    private const string AdaptiveExtension = "http://hl7.org/fhir/uv/sdc/StructureDefinition/sdc-questionnaire-questionnaireAdaptive";

    private readonly JsonElement _root;

    public DtrResponse(HttpStatusCode status, string body)
    {
        Status = status;
        Body = body;
        _root = JsonDocument.Parse(body).RootElement.Clone();
        ResourceType = _root.GetProperty("resourceType").GetString()!;
    }

    public HttpStatusCode Status { get; }
    public string Body { get; }
    public string ResourceType { get; }

    /// <summary>The packagebundle parameters: one per questionnaire the payer wants completed.</summary>
    public IReadOnlyList<JsonElement> Packages =>
        [.. Parameters("packagebundle").Select(p => p.GetProperty("resource"))];

    public string? QuestionnaireUrl => Questionnaire?.GetProperty("url").GetString();
    public string? QuestionnaireTitle => Questionnaire?.GetProperty("title").GetString();

    /// <summary>The linkIds of the questionnaire's top-level items, in order.</summary>
    public IReadOnlyList<string> QuestionnaireItems => ItemsOf(Questionnaire);

    /// <summary>The QuestionnaireResponse as handed out, to be completed and sent back.</summary>
    public string? QuestionnaireResponseJson => QuestionnaireResponse?.GetRawText();

    public string? QuestionnaireResponseStatus => QuestionnaireResponse?.GetProperty("status").GetString();
    public string? QuestionnaireResponseQuestionnaire => QuestionnaireResponse?.GetProperty("questionnaire").GetString();
    public string? QuestionnaireResponseSubject =>
        QuestionnaireResponse?.GetProperty("subject").GetProperty("reference").GetString();

    /// <summary>DTR qr-coverage: the Coverage the response is being completed for.</summary>
    public string? QuestionnaireResponseCoverage =>
        Extension(QuestionnaireResponse, Dtr + "qr-coverage")?.GetProperty("valueReference").GetProperty("reference").GetString();

    /// <summary>DTR qr-context: the order the response is about.</summary>
    public string? QuestionnaireResponseContext =>
        Extension(QuestionnaireResponse, Dtr + "qr-context")?.GetProperty("valueReference").GetProperty("reference").GetString();

    /// <summary>DTR intendedUse: withpa, withorder, ...</summary>
    public string? IntendedUse =>
        Extension(QuestionnaireResponse, Dtr + "intendedUse")?.GetProperty("valueCodeableConcept").GetProperty("coding")[0].GetProperty("code").GetString();

    /// <summary>For an adaptive form: the linkIds of the items the contained Questionnaire holds so far.</summary>
    public IReadOnlyList<string> ContainedQuestionnaireItems => ItemsOf(ContainedQuestionnaire);

    /// <summary>For an adaptive form: where the contained Questionnaire says the next question comes from.</summary>
    public string? NextQuestionEndpoint =>
        Extension(ContainedQuestionnaire, AdaptiveExtension)?.GetProperty("valueUrl").GetString();

    public string? ContainedQuestionnaireDerivedFrom =>
        ContainedQuestionnaire?.GetProperty("derivedFrom")[0].GetString();

    /// <summary>The first answer given to an item of the response, as a string, or null when unanswered.</summary>
    public string? Answer(string linkId)
    {
        if (QuestionnaireResponse is not { } response || !response.TryGetProperty("item", out var items)) return null;
        foreach (var item in items.EnumerateArray())
        {
            if (item.GetProperty("linkId").GetString() != linkId) continue;
            if (!item.TryGetProperty("answer", out var answers) || answers.GetArrayLength() == 0) return null;
            var answer = answers[0];
            foreach (var property in answer.EnumerateObject())
                if (property.Name.StartsWith("value")) return property.Value.ToString();
        }
        return null;
    }

    /// <summary>The outcome parameter's first issue, or an OperationOutcome answer's.</summary>
    public string? OutcomeMessage
    {
        get
        {
            var outcome = ResourceType == "OperationOutcome" ? _root
                : Parameters("outcome").Select(p => p.GetProperty("resource")).Cast<JsonElement?>().FirstOrDefault();
            if (outcome is not { } o) return null;
            var issue = o.GetProperty("issue")[0];
            return issue.TryGetProperty("diagnostics", out var d) ? d.GetString()
                : issue.TryGetProperty("details", out var details) ? details.GetProperty("text").GetString()
                : null;
        }
    }

    private JsonElement? Questionnaire => InPackage("Questionnaire");

    private JsonElement? QuestionnaireResponse => ResourceType switch
    {
        "QuestionnaireResponse" => _root,
        "Parameters" => Parameters("return").Select(p => p.GetProperty("resource")).Cast<JsonElement?>().FirstOrDefault()
                        ?? InPackage("QuestionnaireResponse"),
        _ => null,
    };

    private JsonElement? ContainedQuestionnaire
    {
        get
        {
            if (QuestionnaireResponse is not { } response || !response.TryGetProperty("contained", out var contained)) return null;
            foreach (var resource in contained.EnumerateArray())
                if (resource.GetProperty("resourceType").GetString() == "Questionnaire") return resource;
            return null;
        }
    }

    private JsonElement? InPackage(string resourceType)
    {
        if (Packages.Count == 0 || !Packages[0].TryGetProperty("entry", out var entries)) return null;
        foreach (var entry in entries.EnumerateArray())
        {
            var resource = entry.GetProperty("resource");
            if (resource.GetProperty("resourceType").GetString() == resourceType) return resource;
        }
        return null;
    }

    private IEnumerable<JsonElement> Parameters(string name)
    {
        if (ResourceType != "Parameters" || !_root.TryGetProperty("parameter", out var parameters)) yield break;
        foreach (var parameter in parameters.EnumerateArray())
            if (parameter.GetProperty("name").GetString() == name) yield return parameter;
    }

    private static IReadOnlyList<string> ItemsOf(JsonElement? questionnaire) =>
        questionnaire is { } q && q.TryGetProperty("item", out var items)
            ? [.. items.EnumerateArray().Select(i => i.GetProperty("linkId").GetString()!)]
            : [];

    private static JsonElement? Extension(JsonElement? parent, string url)
    {
        if (parent is not { } p || !p.TryGetProperty("extension", out var extensions)) return null;
        foreach (var extension in extensions.EnumerateArray())
            if (extension.GetProperty("url").GetString() == url) return extension;
        return null;
    }
}
