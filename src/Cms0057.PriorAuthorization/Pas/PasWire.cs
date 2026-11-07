using Hl7.Fhir.Model;

namespace Cms0057.PriorAuthorization.Pas;

/// <summary>
/// The Da Vinci PAS wire format: reads a request Bundle and writes the response Bundle, mirroring what Da Vinci's
/// br-payer Reference Implementation puts on the wire (see docs/adr/0001).
/// </summary>
public static class PasWire
{
    private const string PasBase = "http://hl7.org/fhir/us/davinci-pas/StructureDefinition/";
    private const string ReviewActionUrl = PasBase + "extension-reviewAction";
    private const string ReviewActionCodeUrl = PasBase + "extension-reviewActionCode";
    private const string ItemTraceNumberUrl = PasBase + "extension-itemTraceNumber";
    private const string AdministrationReferenceNumberUrl = PasBase + "extension-administrationReferenceNumber";
    private const string AuthorizationNumberUrl = PasBase + "extension-authorizationNumber";
    private const string ItemPreAuthIssueDateUrl = PasBase + "extension-itemPreAuthIssueDate";
    private const string ItemPreAuthPeriodUrl = PasBase + "extension-itemPreAuthPeriod";
    private const string CertificationTypeUrl = PasBase + "extension-certificationType";
    private const string InfoCancelledUrl = PasBase + "modifierextension-infoCancelled";
    /// <summary>X12 1322 Certification Type Code 3: cancel.</summary>
    private const string CancelCertificationType = "3";
    private const string ResponseBundleProfile = PasBase + "profile-pas-response-bundle";
    private const string InquiryResponseBundleProfile = PasBase + "profile-pas-inquiry-response-bundle";
    private const string ClaimResponseProfile = PasBase + "profile-claimresponse";
    private const string ReviewActionSystem = "https://codesystem.x12.org/005010/306";
    private const string ReviewDecisionReasonSystem = "https://codesystem.x12.org/external/886";
    public const string AdjudicationSystem = "http://terminology.hl7.org/CodeSystem/adjudication";
    private const string IdentifierTypeSystem = "http://terminology.hl7.org/CodeSystem/v2-0203";
    private const string SubmissionIdentifierSystem = "http://example.org/SUBMITTER_TRANSACTION_IDENTIFIER";
    /// <summary>The system br-payer puts its ClaimResponse tracking identifier under; provider systems file the response by it.</summary>
    public const string TrackingIdentifierSystem = "http://example.org/PATIENT_EVENT_TRACE_NUMBER";
    private const string NpiSystem = "http://hl7.org/fhir/sid/us-npi";

    public static string Write(Resource resource) => FhirJson.Write(resource);

    /// <summary>
    /// Reads what the service needs from a PAS request Bundle: a new request (a renewal is one), an update, or a
    /// cancellation. As in br-payer, the kind is told from the Claim's structure, not its declared profile:
    /// Claim.related names a prior Claim for an update or cancellation, and a Claim-level certificationType of 3
    /// makes it a cancellation.
    /// </summary>
    /// <exception cref="RequestRejected">The body is not a PAS request this service can process.</exception>
    public static PasSubmission ReadSubmission(string requestBundle, PasProfiles profiles)
    {
        var bundle = ParseRequest(requestBundle);
        var claim = RequireClaim(bundle);
        var submission = ReadSubmission(bundle, claim, requestBundle);
        // Only after the structural checks: the PAS request Bundle profile and all it pulls in.
        profiles.Require(bundle, PasProfiles.RequestBundle);
        return submission;
    }

    private static PasSubmission ReadSubmission(Bundle bundle, Claim claim, string requestBundle)
    {
        if (claim.Related.FirstOrDefault()?.Claim is { Reference: not null } priorReference)
        {
            var prior = TryResolve<Claim>(bundle, priorReference)
                ?? throw new RequestRejected("The prior Claim referenced in Claim.related.claim must be included in the Bundle");
            Require(prior.Identifier.Count > 0, "Prior Claim must have an identifier for matching to stored authorization");
            var priorIdentifier = $"{prior.Identifier[0].System}|{prior.Identifier[0].Value}";

            if (HasCertificationType(claim.Extension, CancelCertificationType))
                return new RequestCancellation(priorIdentifier);

            return new RequestUpdate(
                priorIdentifier,
                [.. claim.Item.Select((item, index) => new ItemUpdate(
                    ServiceOf(item, index),
                    Cancelled: item.ModifierExtension.Any(e => e.Url == InfoCancelledUrl)))],
                requestBundle);
        }

        return ReadNewRequest(bundle, claim, requestBundle);
    }

    private static Submission ReadNewRequest(Bundle bundle, Claim claim, string requestBundle)
    {

        // The same structural checks, with the same wording, as br-payer's PasBundleValidator.
        Require(claim.Item.Count > 0, "Claim must have at least one item");
        foreach (var each in claim.Item)
        {
            Require(each.Category is not null, $"Claim.item.category is required for item {each.Sequence}");
            Require(each.Location is not null, $"Claim.item.location is required for item {each.Sequence}");
        }
        Require(bundle.Identifier?.Value is not null, "Bundle.identifier is required");
        Require(bundle.Timestamp is not null, "Bundle.timestamp is required");
        Require(bundle.Entry.All(e => !string.IsNullOrWhiteSpace(e.FullUrl)), "All Bundle.entry elements must have a fullUrl");

        // Beyond what br-payer checks: every item must say which service it asks for.
        foreach (var each in claim.Item)
            Require(each.ProductOrService?.Coding.FirstOrDefault()?.Code is not null,
                $"Claim.item.productOrService is required for item {each.Sequence}");
        Require(claim.Identifier.Count > 0, "Claim.identifier is required");
        var member = Resolve<Patient>(bundle, claim.Patient);
        Require(member.Identifier.Count > 0, "The Patient must carry a member identifier");

        return new Submission(
            SubmissionIdentifier: $"{bundle.Identifier!.System}|{bundle.Identifier.Value}",
            ClaimIdentifier: $"{claim.Identifier[0].System}|{claim.Identifier[0].Value}",
            MemberIdentifier: MemberIdentifier(member),
            ProviderIdentifier: ProviderIdentifier(bundle, claim.Provider),
            // PAS marks an expedited request with the "stat" process priority.
            Expedited: claim.Priority?.Coding.Any(c => c.Code == "stat") == true,
            Services: [.. claim.Item.Select(ServiceOf)],
            RequestBundle: requestBundle);
    }

    private static RequestedService ServiceOf(Claim.ItemComponent item, int index) => new(
        item.Sequence ?? index + 1,
        item.ProductOrService!.Coding[0].Code!,
        [.. item.Modifier.SelectMany(m => m.Coding).Select(c => c.Code).OfType<string>()]);

    private static bool HasCertificationType(IEnumerable<Extension> extensions, string code) =>
        extensions.Any(e => e.Url == CertificationTypeUrl
                            && e.Value is CodeableConcept concept
                            && concept.Coding.Any(c => c.Code == code));

    /// <summary>Reads what the service needs from a PAS inquiry Bundle.</summary>
    /// <exception cref="RequestRejected">The body is not a PAS inquiry this service can process.</exception>
    public static Inquiry ReadInquiry(string inquiryBundle, PasProfiles profiles)
    {
        var bundle = ParseRequest(inquiryBundle);
        var claim = RequireClaim(bundle);

        // The same structural checks, with the same wording, as br-payer's PasBundleValidator.
        Require(bundle.Identifier?.Value is not null, "Bundle.identifier is required");
        Require(bundle.Timestamp is not null, "Bundle.timestamp is required");
        Require(bundle.Entry.All(e => !string.IsNullOrWhiteSpace(e.FullUrl)), "All Bundle.entry elements must have a fullUrl");
        Require(claim.Identifier.Count > 0, "Claim.identifier is required for inquiry");
        Require(claim.Status == FinancialResourceStatusCodes.Active, $"Claim.status must be 'active', got: {claim.StatusElement?.ObjectValue}");
        Require(claim.Created is not null, "Claim.created is required");
        var member = Resolve<Patient>(bundle, claim.Patient);
        Require(HasMemberIdentifier(member), "Patient member identifier (type=MB) is required for inquiry");

        var numbers = claim.Extension.Concat(claim.Item.SelectMany(i => i.Extension))
            .Where(e => e.Url is AuthorizationNumberUrl or AdministrationReferenceNumberUrl)
            .Select(e => (e.Value as FhirString)?.Value)
            .OfType<string>();

        // Only after the structural checks: the PAS inquiry Bundle profile and all it pulls in.
        profiles.Require(bundle, PasProfiles.InquiryRequestBundle);
        return new Inquiry(MemberIdentifier(member), ProviderIdentifier(bundle, claim.Provider), [.. numbers]);
    }

    /// <summary>
    /// Writes the answer to an inquiry: as in br-payer, a Parameters resource holding one response Bundle for each
    /// request found, and none when nothing was found.
    /// </summary>
    public static Parameters InquiryResponse(IEnumerable<PriorAuthorizationRequest> found, string serverBase)
    {
        var parameters = new Parameters();
        foreach (var request in found)
        {
            var bundle = Response(request, serverBase);
            bundle.Meta.Profile = [InquiryResponseBundleProfile];
            parameters.Parameter.Add(new Parameters.ParameterComponent { Name = "responseBundle", Resource = bundle });
        }
        return parameters;
    }

    /// <summary>The ClaimResponse alone, as a provider system reads it at the URL a response or a notification named.</summary>
    public static ClaimResponse ClaimResponseOf(PriorAuthorizationRequest request, string serverBase) =>
        (ClaimResponse)Response(request, serverBase).Entry[0].Resource;

    /// <summary>Writes the PAS response Bundle for a request in its current state.</summary>
    public static Bundle Response(PriorAuthorizationRequest request, string serverBase)
    {
        var claim = (Claim)ReadBundle(request.RequestBundle).Entry[0].Resource;
        var created = request.DecidedAt ?? request.ReceivedAt;

        var claimResponse = new ClaimResponse
        {
            Id = request.Id.ToString(),
            Meta = new Meta { Profile = [ClaimResponseProfile] },
            // The payer's tracking identifier for the response, the same for the life of the request.
            Identifier = [new Identifier(TrackingIdentifierSystem, request.Id.ToString())],
            Status = FinancialResourceStatusCodes.Active,
            Type = (CodeableConcept)claim.Type.DeepCopy(),
            Use = ClaimUseCode.Preauthorization,
            Patient = (ResourceReference)claim.Patient.DeepCopy(),
            CreatedElement = new FhirDateTime(created),
            Insurer = (ResourceReference)claim.Insurer.DeepCopy(),
            Requestor = (ResourceReference)claim.Provider.DeepCopy(),
            Request = new ResourceReference("Claim/" + claim.Id),
            // As in br-payer, the outcome is always complete; a pend is conveyed by the item's A4 review action.
            Outcome = ClaimProcessingCodes.Complete,
        };

        foreach (var (requested, index) in claim.Item.Select((item, index) => (item, index)))
        {
            // Each item is answered on its own; see PriorAuthorizationRequest.StateOf.
            var state = request.StateOf(requested.Sequence ?? index + 1);

            var item = new ClaimResponse.ItemComponent { ItemSequence = requested.Sequence };
            foreach (var trace in requested.Extension.Where(e => e.Url == ItemTraceNumberUrl))
                item.Extension.Add((Extension)trace.DeepCopy());
            if (state == RequestState.Pended)
                item.Extension.Add(new Extension(AdministrationReferenceNumberUrl, new FhirString(request.ReferenceNumber)));

            var reviewAction = ReviewAction(request, state, ReviewActionUrl, ReviewActionCodeUrl);
            // As in br-payer: the Authorization Number rides on the review action, the period on the item.
            item.Extension.AddRange(AuthorizationPeriod(request, state));

            // A process note says in words why an item is not certified, where any ClaimResponse reader will find
            // it; that placement is ours.
            if (ProcessNote(request, state) is { } note)
            {
                var number = claimResponse.ProcessNote.FirstOrDefault(n => n.Text == note)?.Number
                             ?? AddNote(claimResponse, note);
                item.NoteNumber = [number];
            }

            var adjudication = new ClaimResponse.AdjudicationComponent
            {
                Category = new CodeableConcept(AdjudicationSystem, "submitted", "Submitted Amount", null),
            };
            adjudication.Extension.Add(reviewAction);
            item.Adjudication.Add(adjudication);
            claimResponse.Item.Add(item);
        }

        var response = new Bundle
        {
            Meta = new Meta { Profile = [ResponseBundleProfile] },
            Type = Bundle.BundleType.Collection,
            Timestamp = created,
            Identifier = new Identifier(SubmissionIdentifierSystem, Guid.NewGuid().ToString()),
        };
        response.Entry.Add(new Bundle.EntryComponent
        {
            FullUrl = $"{serverBase}/ClaimResponse/{claimResponse.Id}",
            Resource = claimResponse,
        });
        return response;
    }

    private static int AddNote(ClaimResponse claimResponse, string text)
    {
        var number = claimResponse.ProcessNote.Count + 1;
        claimResponse.ProcessNote.Add(new ClaimResponse.NoteComponent { Number = number, Type = NoteType.Display, Text = text });
        return number;
    }

    /// <summary>
    /// The review action for a request, or one of its items, in the given <paramref name="state"/>: the X12 306
    /// code; the Authorization Number when approved; and when denied the reason, a coded X12 886 reason when the
    /// plan gave one and the Reviewer's words as the concept's text either way. PAS and PDex define the extension
    /// alike under their own URLs, so the caller names them.
    /// </summary>
    public static Extension ReviewAction(PriorAuthorizationRequest request, RequestState state, string extensionUrl, string codeUrl)
    {
        var (code, display) = ReviewAction(state);
        var reviewAction = new Extension { Url = extensionUrl };
        reviewAction.Extension.Add(new Extension(codeUrl, new CodeableConcept(ReviewActionSystem, code, display, null)));

        if (state == RequestState.Approved)
            reviewAction.Extension.Add(new Extension("number", new FhirString(request.AuthorizationNumber)));

        if (state == RequestState.Denied)
        {
            var reason = new CodeableConcept { Text = request.DenialReason };
            if (request.DenialReasonCode is not null)
                reason.Coding.Add(new Coding(ReviewDecisionReasonSystem, request.DenialReasonCode, request.DenialReasonDisplay));
            reviewAction.Extension.Add(new Extension("reasonCode", reason));
        }
        return reviewAction;
    }

    /// <summary>
    /// For an approved item: the PAS item extensions saying when the authorization was issued and the period it
    /// covers, from the day of the decision to the end date the Reviewer gave. Nothing for any other state.
    /// </summary>
    public static IEnumerable<Extension> AuthorizationPeriod(PriorAuthorizationRequest request, RequestState state)
    {
        if (state != RequestState.Approved) yield break;
        var decidedOn = DateOnly.FromDateTime(request.DecidedAt!.Value.UtcDateTime);
        yield return new Extension(ItemPreAuthIssueDateUrl, new Date(decidedOn.ToString("yyyy-MM-dd")));
        yield return new Extension(ItemPreAuthPeriodUrl, new Period
        {
            Start = decidedOn.ToString("yyyy-MM-dd"),
            End = request.AuthorizationEnds!.Value.ToString("yyyy-MM-dd"),
        });
    }

    /// <summary>What a process note says about an item that is not certified; null when there is nothing to say.</summary>
    public static string? ProcessNote(PriorAuthorizationRequest request, RequestState state) => state switch
    {
        RequestState.Denied => request.DenialReason,
        RequestState.Cancelled => "Cancelled at the provider's request",
        _ => null,
    };

    /// <summary>The Claim a request was sent as, read from the Bundle kept with the request.</summary>
    public static Claim ClaimOf(PriorAuthorizationRequest request) => (Claim)ReadBundle(request.RequestBundle).Entry[0].Resource;

    /// <summary>The resource a reference in the kept request Bundle points at, or null when it is not in the Bundle.</summary>
    public static T? InRequestBundle<T>(PriorAuthorizationRequest request, ResourceReference? reference) where T : Resource =>
        reference is null ? null : TryResolve<T>(ReadBundle(request.RequestBundle), reference);

    /// <summary>X12 306 has no code for a cancellation; as in br-payer, a cancelled item is Not Certified.</summary>
    private static (string Code, string Display) ReviewAction(RequestState state) => state switch
    {
        RequestState.Approved => ("A1", "Certified in total"),
        RequestState.Denied or RequestState.Cancelled => ("A2", "Not Certified"),
        RequestState.NotRequired => ("A3", "Not Required"),
        _ => ("A4", "Pended"),
    };

    private static Bundle ReadBundle(string json) => FhirJson.Read<Bundle>(json);

    private static string MemberIdentifier(Patient member) =>
        (member.Identifier.FirstOrDefault(IsMemberIdentifier) ?? member.Identifier[0]).Value;

    private static bool HasMemberIdentifier(Patient member) => member.Identifier.Any(IsMemberIdentifier);

    /// <summary>The sending organization's identifier, its NPI when it has one; null when the Bundle gives none.</summary>
    private static string? ProviderIdentifier(Bundle bundle, ResourceReference provider)
    {
        var identifiers = TryResolve<Resource>(bundle, provider) switch
        {
            Organization organization => organization.Identifier,
            Practitioner practitioner => practitioner.Identifier,
            PractitionerRole role => role.Identifier,
            _ => [],
        };
        return (identifiers.FirstOrDefault(i => i.System == NpiSystem) ?? identifiers.FirstOrDefault())?.Value;
    }

    private static bool IsMemberIdentifier(Identifier identifier) =>
        identifier.Type?.Coding.Any(c => c.System == IdentifierTypeSystem && c.Code == "MB") == true;

    /// <summary>
    /// Writes the OperationOutcome that tells a provider system why its request was rejected: the validator's
    /// findings when a profile was broken, otherwise the one reason.
    /// </summary>
    public static OperationOutcome Rejection(RequestRejected rejected) => rejected is ProfileViolation violation
        ? new OperationOutcome { Issue = [.. violation.Issues] }
        : Rejection(rejected.Message);

    /// <summary>Writes the OperationOutcome for a resource the service does not hold.</summary>
    public static OperationOutcome NotFound(string what) => new()
    {
        Issue =
        [
            new OperationOutcome.IssueComponent
            {
                Severity = OperationOutcome.IssueSeverity.Error,
                Code = OperationOutcome.IssueType.NotFound,
                Diagnostics = what,
            },
        ],
    };

    /// <summary>Writes the OperationOutcome that tells a provider system why its request was rejected.</summary>
    public static OperationOutcome Rejection(string reason) => new()
    {
        Issue =
        [
            new OperationOutcome.IssueComponent
            {
                Severity = OperationOutcome.IssueSeverity.Error,
                Code = OperationOutcome.IssueType.Invalid,
                Diagnostics = reason,
            },
        ],
    };

    private static Bundle ParseRequest(string json) => FhirJson.Read<Bundle>(json);

    private static Claim RequireClaim(Bundle bundle)
    {
        Require(bundle.Type == Bundle.BundleType.Collection, $"Bundle type must be 'collection', got: {bundle.TypeElement?.ObjectValue}");
        Require(bundle.Entry.Count > 0, "Bundle must have at least one entry");
        var first = bundle.Entry[0].Resource;
        Require(first is Claim, $"First bundle entry must be a Claim, got: {first?.TypeName ?? "null"}");
        var claim = (Claim)first!;
        Require(claim.Use == ClaimUseCode.Preauthorization, $"Claim.use must be 'preauthorization', got: {claim.UseElement?.ObjectValue}");
        Require(claim.Patient?.Reference is not null, "Claim.patient is required");
        Require(claim.Insurer?.Reference is not null, "Claim.insurer is required");
        Require(claim.Provider?.Reference is not null, "Claim.provider is required");
        Require(claim.Insurance.Count > 0, "Claim.insurance is required with at least one coverage");
        return claim;
    }

    private static void Require(bool condition, string otherwise)
    {
        if (!condition) throw new RequestRejected(otherwise);
    }

    private static T Resolve<T>(Bundle bundle, ResourceReference reference) where T : Resource =>
        TryResolve<T>(bundle, reference) ?? throw new RequestRejected($"{reference.Reference} is not in the Bundle");

    private static T? TryResolve<T>(Bundle bundle, ResourceReference reference) where T : Resource =>
        bundle.Entry
            .Where(e => e.Resource is T)
            .FirstOrDefault(e => e.FullUrl == reference.Reference
                                 || e.FullUrl?.EndsWith("/" + reference.Reference, StringComparison.Ordinal) == true
                                 || $"{e.Resource.TypeName}/{e.Resource.Id}" == reference.Reference)
            ?.Resource as T;
}
