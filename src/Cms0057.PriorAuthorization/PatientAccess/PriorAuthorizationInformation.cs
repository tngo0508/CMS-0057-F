using Cms0057.PriorAuthorization.Pas;
using Hl7.Fhir.Model;

namespace Cms0057.PriorAuthorization.PatientAccess;

/// <summary>
/// What the Patient Access API shows a Member about each Prior Authorization Request: its status, the date it was
/// approved or denied, when the authorization ends, the services asked for and approved, and the Denial Reason
/// (42 CFR 422.119(b)(1)(iv)). Written as a Da Vinci PDex 2.1 Prior Authorization ExplanationOfBenefit, the shape
/// the rule's recommended guide gives that information, so that the agency's Patient Access API can serve it on.
/// The ExplanationOfBenefit is the request's PAS ClaimResponse told again in PDex terms: the same review actions,
/// Authorization Number, period and reasons, so a Member and their provider read the same decision.
/// </summary>
public static class PriorAuthorizationInformation
{
    private const string PdexBase = "http://hl7.org/fhir/us/davinci-pdex/StructureDefinition/";
    public const string Profile = PdexBase + "pdex-priorauthorization";
    private const string ReviewActionUrl = PdexBase + "extension-reviewAction";
    private const string ReviewActionCodeUrl = PdexBase + "extension-reviewActionCode";
    private const string WhenAdjudicatedUrl = PdexBase + "base-ext-when-adjudicated";
    private const string ItemTraceNumberUrl = "http://hl7.org/fhir/us/davinci-pas/StructureDefinition/extension-itemTraceNumber";

    /// <summary>
    /// The search result for a Member's requests, as the Patient Access API reads it; narrowed to those whose
    /// status last changed within <paramref name="lastUpdated"/> when it asks for that.
    /// </summary>
    public static Bundle SearchResult(IEnumerable<PriorAuthorizationRequest> requests, string serverBase, DateSearch? lastUpdated = null)
    {
        var bundle = new Bundle { Type = Bundle.BundleType.Searchset, Entry = [] };
        foreach (var request in requests)
        {
            if (lastUpdated is { } wanted && !wanted.Matches(LastChanged(request))) continue;
            var information = Of(request, serverBase);
            bundle.Entry.Add(new Bundle.EntryComponent
            {
                FullUrl = $"{serverBase}/ExplanationOfBenefit/{information.Id}",
                Resource = information,
                Search = new Bundle.SearchComponent { Mode = Bundle.SearchEntryMode.Match },
            });
        }
        bundle.Total = bundle.Entry.Count;
        return bundle;
    }

    /// <summary>One request as prior authorization information, in its current state.</summary>
    public static ExplanationOfBenefit Of(PriorAuthorizationRequest request, string serverBase)
    {
        var claim = PasWire.ClaimOf(request);

        var information = new ExplanationOfBenefit
        {
            Id = request.Id.ToString(),
            Meta = new Meta { Profile = [Profile], LastUpdated = LastChanged(request) },
            Identifier = [new Identifier(PasWire.TrackingIdentifierSystem, request.Id.ToString())],
            // Withdrawn by the provider is what FHIR's "cancelled" means; every other request stands.
            Status = request.State == RequestState.Cancelled
                ? ExplanationOfBenefit.ExplanationOfBenefitStatus.Cancelled
                : ExplanationOfBenefit.ExplanationOfBenefitStatus.Active,
            Type = (CodeableConcept)claim.Type.DeepCopy(),
            Use = ClaimUseCode.Preauthorization,
            Patient = new ResourceReference("Patient/" + request.MemberIdentifier),
            CreatedElement = new FhirDateTime(request.ReceivedAt),
            Insurer = Participant(request, claim.Insurer),
            Provider = Participant(request, claim.Provider),
            Priority = claim.Priority is null ? null : (CodeableConcept)claim.Priority.DeepCopy(),
            // Queued while the Reviewer has it; complete once answered, whichever way.
            Outcome = request.State == RequestState.Pended ? ClaimProcessingCodes.Queued : ClaimProcessingCodes.Complete,
            Insurance = [.. claim.Insurance.Select(i => new ExplanationOfBenefit.InsuranceComponent
            {
                Focal = i.Focal ?? true,
                Coverage = (ResourceReference)i.Coverage.DeepCopy(),
            })],
        };

        if (request.State == RequestState.Approved)
        {
            var decidedOn = DateOnly.FromDateTime(request.DecidedAt!.Value.UtcDateTime);
            information.PreAuthRefPeriod =
            [
                new Period { Start = decidedOn.ToString("yyyy-MM-dd"), End = request.AuthorizationEnds!.Value.ToString("yyyy-MM-dd") },
            ];
        }

        // The decision on the request as a whole, then on each item (items are answered one by one, see
        // PriorAuthorizationRequest.StateOf).
        information.Adjudication.Add(Adjudication(request, request.State, claim.Total));

        foreach (var (requested, index) in claim.Item.Select((item, index) => (item, index)))
        {
            var sequence = requested.Sequence ?? index + 1;
            var state = request.StateOf(sequence);
            var item = new ExplanationOfBenefit.ItemComponent
            {
                Sequence = sequence,
                Category = requested.Category is null ? null : (CodeableConcept)requested.Category.DeepCopy(),
                ProductOrService = (CodeableConcept)requested.ProductOrService.DeepCopy(),
                Modifier = [.. requested.Modifier.Select(m => (CodeableConcept)m.DeepCopy())],
                Quantity = requested.Quantity is null ? null : (Quantity)requested.Quantity.DeepCopy(),
                Location = requested.Location is CodeableConcept place ? (CodeableConcept)place.DeepCopy() : null,
            };
            foreach (var trace in requested.Extension.Where(e => e.Url == ItemTraceNumberUrl))
                item.Extension.Add((Extension)trace.DeepCopy());
            item.Extension.AddRange(PasWire.AuthorizationPeriod(request, state));

            if (PasWire.ProcessNote(request, state) is { } note)
            {
                var number = information.ProcessNote.FirstOrDefault(n => n.Text == note)?.Number ?? AddNote(information, note);
                item.NoteNumber = [number];
            }

            item.Adjudication.Add(Adjudication(request, state, requested.Net));
            information.Item.Add(item);
        }

        return information;
    }

    /// <summary>The last status change: receipt, or the decision or cancellation since. What a Patient Access API polls on.</summary>
    public static DateTimeOffset LastChanged(PriorAuthorizationRequest request) => request.DecidedAt ?? request.ReceivedAt;

    /// <summary>
    /// The adjudication that carries a review action: the decision's code, number and reason, and the date it was
    /// made. PDex's review action is PAS's under PDex's own URLs. PDex wants a Money on this adjudication; a prior
    /// authorization states no amount unless the provider priced what it asked for (<paramref name="submitted"/>,
    /// the Claim's net), so otherwise the Money names the currency and no value, rather than a zero that would read
    /// as a statement.
    /// </summary>
    private static ExplanationOfBenefit.AdjudicationComponent Adjudication(PriorAuthorizationRequest request, RequestState state, Money? submitted)
    {
        var adjudication = new ExplanationOfBenefit.AdjudicationComponent
        {
            Category = new CodeableConcept(PasWire.AdjudicationSystem, "submitted", "Submitted Amount", null),
            Amount = submitted is null ? new Money { Currency = Money.Currencies.USD } : (Money)submitted.DeepCopy(),
        };
        adjudication.Extension.Add(PasWire.ReviewAction(request, state, ReviewActionUrl, ReviewActionCodeUrl));
        if (request.DecidedAt is { } decidedAt)
            adjudication.Extension.Add(new Extension(WhenAdjudicatedUrl, new FhirDateTime(decidedAt)));
        return adjudication;
    }

    /// <summary>
    /// The plan or the provider as the request named it: the same reference, with the organization's name as its
    /// display, since the reference points into the request Bundle rather than at a resource this server holds.
    /// </summary>
    private static ResourceReference Participant(PriorAuthorizationRequest request, ResourceReference reference)
    {
        var participant = (ResourceReference)reference.DeepCopy();
        participant.Display ??= PasWire.InRequestBundle<Resource>(request, reference) switch
        {
            Organization organization => organization.Name,
            Practitioner practitioner => practitioner.Name.FirstOrDefault()?.Text,
            _ => null,
        };
        return participant;
    }

    private static int AddNote(ExplanationOfBenefit information, string text)
    {
        var number = information.ProcessNote.Count + 1;
        information.ProcessNote.Add(new ExplanationOfBenefit.NoteComponent { Number = number, Type = NoteType.Display, Text = text });
        return number;
    }
}
