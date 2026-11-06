using Cms0057.PriorAuthorization.Pas;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Options;

namespace Cms0057.PriorAuthorization.Dtr;

/// <summary>
/// The Documentation Questionnaires the Behavioral Health Plan has authored, read once from the questionnaire
/// folder (one FHIR Questionnaire per JSON file) and looked up by canonical URL. Each is offered as it is written
/// and as an Adaptive Questionnaire under its canonical with <see cref="AdaptiveSuffix"/>.
/// </summary>
public sealed class DocumentationQuestionnaires
{
    public const string AdaptiveSuffix = "-adaptive";

    private readonly IReadOnlyDictionary<string, Questionnaire> _byUrl;

    public DocumentationQuestionnaires(IOptions<PriorAuthorizationOptions> options, IHostEnvironment environment, ILogger<DocumentationQuestionnaires> log)
    {
        var folder = Path.GetFullPath(options.Value.QuestionnaireFolder ?? "questionnaires", environment.ContentRootPath);
        var questionnaires = new Dictionary<string, Questionnaire>();
        var files = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.json").Order() : Enumerable.Empty<string>();
        foreach (var file in files)
        {
            var questionnaire = FhirJson.Read<Questionnaire>(File.ReadAllText(file));
            if (string.IsNullOrEmpty(questionnaire.Url))
                throw new InvalidOperationException($"The questionnaire in {file} has no canonical url.");
            questionnaires[questionnaire.Url] = questionnaire;
        }
        log.LogInformation("{Count} documentation questionnaire(s) read from {Folder}", questionnaires.Count, folder);
        _byUrl = questionnaires;
    }

    /// <summary>
    /// The form with this canonical URL, with or without a <c>|version</c> suffix (any version the plan holds is
    /// the one it holds). Null when the plan has none by that URL.
    /// </summary>
    public DocumentationForm? Find(string canonical)
    {
        var url = canonical.Split('|')[0];
        if (_byUrl.GetValueOrDefault(url) is { } asWritten) return new DocumentationForm(asWritten, Adaptive: false);
        if (url.EndsWith(AdaptiveSuffix) && _byUrl.GetValueOrDefault(url[..^AdaptiveSuffix.Length]) is { } source)
            return new DocumentationForm(source, Adaptive: true);
        return null;
    }
}

/// <summary>A questionnaire the plan holds, taken as written or as an Adaptive Questionnaire.</summary>
public sealed record DocumentationForm(Questionnaire Source, bool Adaptive)
{
    public string Url => Adaptive ? Source.Url + DocumentationQuestionnaires.AdaptiveSuffix : Source.Url;

    /// <summary>The source questionnaire's version-specific canonical.</summary>
    public string SourceCanonical => string.IsNullOrEmpty(Source.Version) ? Source.Url : $"{Source.Url}|{Source.Version}";
}
