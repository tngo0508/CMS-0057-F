using Hl7.Fhir.Model;

namespace Cms0057.PriorAuthorization.Dtr;

/// <summary>
/// Documentation Templates and Rules (Da Vinci DTR 2.2.0): a provider system's DTR app asks what documentation the
/// Behavioral Health Plan needs with a request, and is handed each Documentation Questionnaire that applies with a
/// started QuestionnaireResponse to complete. Mirrors br-payer's questionnaire package operation.
/// </summary>
public sealed class DocumentationRequirements(
    IAuthorizationRules rules,
    DocumentationQuestionnaires questionnaires,
    TimeProvider clock)
{
    private const string Dtr = "http://hl7.org/fhir/us/davinci-dtr/StructureDefinition/";
    public const string PackageBundleProfile = Dtr + "DTR-QPackageBundle";
    public const string PackageOutputProfile = Dtr + "dtr-qpackage-output-parameters";
    public const string QuestionnaireResponseProfile = Dtr + "dtr-questionnaireresponse";
    public const string CoverageExtension = Dtr + "qr-coverage";
    public const string ContextExtension = Dtr + "qr-context";
    public const string IntendedUseExtension = Dtr + "intendedUse";
    public const string InformationOriginExtension = Dtr + "information-origin";
    private const string CrdTempCodeSystem = "http://hl7.org/fhir/us/davinci-crd/CodeSystem/temp";

    /// <summary>The linkId of the item the plan fills in itself: the service the order asks for.</summary>
    public const string RequestedServiceItem = "requested-service";

    /// <summary>
    /// $questionnaire-package: the questionnaires named by canonical, plus those the plan requires for the orders
    /// given, each in its own package Bundle with a started QuestionnaireResponse about those orders. A form named
    /// outright stands in for the plan's own choice of the same questionnaire (asked for adaptively, say). Nothing
    /// matched is not an error; the outcome parameter says so.
    /// </summary>
    /// <exception cref="RequestRejected">The input has no (single) coverage, or an order the service cannot read.</exception>
    public Parameters Package(Parameters input, string serverBase)
    {
        var coverage = CoverageIn(input);
        var orders = input.Parameter.Where(p => p.Name == "order").Select(p => p.Resource).OfType<ServiceRequest>().ToList();
        var warnings = new List<string>();
        var wanted = new List<DocumentationForm>();

        foreach (var canonical in input.Parameter.Where(p => p.Name == "questionnaire").Select(p => (p.Value as Canonical)?.Value).OfType<string>())
        {
            if (questionnaires.Find(canonical) is { } found) wanted.Add(found);
            else warnings.Add($"The plan holds no questionnaire with canonical {canonical}.");
        }

        foreach (var order in orders)
        {
            if (rules.For(RequestedService.OfOrder(order)) is not AuthorizationRule.RequiresReview review) continue;
            foreach (var canonical in review.Questionnaires)
            {
                if (questionnaires.Find(canonical) is not { } found)
                    warnings.Add($"The plan's rules name questionnaire {canonical}, which it does not hold.");
                else if (!wanted.Any(w => w.Source.Url == found.Source.Url))
                    wanted.Add(found);
            }
        }

        var output = new Parameters { Meta = new Meta { Profile = [PackageOutputProfile] } };
        foreach (var form in wanted.DistinctBy(w => w.Url))
        {
            output.Parameter.Add(new Parameters.ParameterComponent
            {
                Name = "packagebundle",
                Resource = PackageBundle(form, coverage, orders, serverBase),
            });
        }

        if (output.Parameter.Count == 0)
            warnings.Insert(0, "No questionnaire matched the request: name one the plan holds, or an order for a service it prior-authorizes.");
        if (warnings.Count > 0)
            output.Parameter.Add(new Parameters.ParameterComponent { Name = "outcome", Resource = Outcome(warnings) });
        return output;
    }

    private Bundle PackageBundle(DocumentationForm form, Coverage coverage, IReadOnlyList<ServiceRequest> orders, string serverBase)
    {
        var questionnaire = form.Adaptive ? AdaptiveForms.Shell(form, serverBase) : form.Source;
        var response = StartedResponse(form, coverage, orders, serverBase);
        return new Bundle
        {
            Id = Guid.NewGuid().ToString(),
            Meta = new Meta { Profile = [PackageBundleProfile] },
            Type = Bundle.BundleType.Collection,
            Entry =
            [
                new Bundle.EntryComponent { FullUrl = questionnaire.Url, Resource = questionnaire },
                new Bundle.EntryComponent { FullUrl = "urn:uuid:" + response.Id, Resource = response },
            ],
        };
    }

    /// <summary>
    /// A QuestionnaireResponse for the provider to complete, carrying what DTR asks of one (the coverage and orders
    /// it is about, the intended use) and the answers the plan can give itself from the order. For an adaptive
    /// form it contains the Questionnaire with the first set of items and answers that instead.
    /// </summary>
    private QuestionnaireResponse StartedResponse(DocumentationForm form, Coverage coverage, IReadOnlyList<ServiceRequest> orders, string serverBase)
    {
        var order = orders.FirstOrDefault();
        var response = new QuestionnaireResponse
        {
            Id = Guid.NewGuid().ToString(),
            Meta = new Meta { Profile = [form.Adaptive ? AdaptiveForms.AdaptiveResponseProfile : QuestionnaireResponseProfile] },
            Status = QuestionnaireResponse.QuestionnaireResponseStatus.InProgress,
            Questionnaire = form.Url,
            Subject = coverage.Beneficiary,
            Authored = clock.GetUtcNow().ToString("o"),
        };
        var questionnaire = form.Source;
        if (form.Adaptive)
        {
            questionnaire = AdaptiveForms.Started(form, serverBase);
            response.Contained.Add(questionnaire);
            response.Questionnaire = "#" + questionnaire.Id;
        }
        response.Extension.Add(new Extension(CoverageExtension, new ResourceReference("Coverage/" + coverage.Id)));
        foreach (var each in orders)
            response.Extension.Add(new Extension(ContextExtension, new ResourceReference("ServiceRequest/" + each.Id)));
        response.Extension.Add(new Extension(IntendedUseExtension, new CodeableConcept(CrdTempCodeSystem, "withpa")));

        if (order is not null && questionnaire.Item.Any(i => i.LinkId == RequestedServiceItem))
        {
            var service = RequestedService.OfOrder(order);
            var answer = new QuestionnaireResponse.AnswerComponent
            {
                Value = new FhirString(string.Join(' ', [service.Code, .. service.Modifiers])),
            };
            var origin = new Extension { Url = InformationOriginExtension };
            origin.Extension.Add(new Extension("source", new Code("auto")));
            answer.Extension.Add(origin);
            response.Item.Add(new QuestionnaireResponse.ItemComponent { LinkId = RequestedServiceItem, Answer = [answer] });
        }

        return response;
    }

    private static Coverage CoverageIn(Parameters input)
    {
        var coverages = input.Parameter.Where(p => p.Name == "coverage").Select(p => p.Resource).ToList();
        return coverages switch
        {
            [Coverage one] => one,
            [] => throw new RequestRejected("The coverage parameter is required: the Member's Coverage, as a resource."),
            [not Coverage] => throw new RequestRejected("The coverage parameter must be a Coverage resource."),
            _ => throw new RequestRejected("One coverage parameter is expected, not several."),
        };
    }

    private static OperationOutcome Outcome(IEnumerable<string> warnings) => new()
    {
        Issue =
        [
            .. warnings.Select(w => new OperationOutcome.IssueComponent
            {
                Severity = OperationOutcome.IssueSeverity.Warning,
                Code = OperationOutcome.IssueType.NotFound,
                Diagnostics = w,
            }),
        ],
    };
}
