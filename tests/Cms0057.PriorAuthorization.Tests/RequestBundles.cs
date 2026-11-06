namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// Prior Authorization Request bundles as a provider system would send them. The shape is adapted from the
/// request examples in Da Vinci's br-payer Reference Implementation (Apache-2.0), cut down to one item and made
/// to conform to the PAS 2.2.1 profiles the service validates against.
/// </summary>
public static class RequestBundles
{
    /// <summary>One of the synthetic Members the service seeds in development.</summary>
    public const string KnownMember = "90000001A";

    /// <summary>Residential treatment at ASAM level 3.1: H0019 with modifier U1 (DMC-ODS Billing Manual).</summary>
    public static string ResidentialTreatment(
        string memberIdentifier = KnownMember,
        string? bundleIdentifier = null,
        string claimUse = "preauthorization",
        string priority = "normal",
        bool withPatientGender = true) =>
        ForItems([Residential], memberIdentifier, bundleIdentifier, claimUse, priority, withPatientGender: withPatientGender);

    /// <summary>Outpatient individual counseling: H0004 with the outpatient modifier U7.</summary>
    public static string OutpatientCounseling(string modifier = "U7") =>
        ForItems([("H0004", modifier)], KnownMember, null, "preauthorization", "normal");

    /// <summary>One request asking for several services at once, each as its own item in the order given.</summary>
    public static string ForServices(params (string Code, string Modifier)[] services) =>
        ForItems(services, KnownMember, null, "preauthorization", "normal");

    /// <summary>A request for one service, standard ("normal") or expedited ("stat").</summary>
    public static string For((string Code, string Modifier) service, string priority = "normal") =>
        ForItems([service], KnownMember, null, "preauthorization", priority);

    public static readonly (string Code, string Modifier) Residential = ("H0019", "U1");
    public static readonly (string Code, string Modifier) Counseling = ("H0004", "U7");
    public static readonly (string Code, string Modifier) GroupCounseling = ("H0005", "U7");

    /// <summary>Therapeutic Behavioral Services, one of the specialty mental health services under DHCS BHIN 26-030.</summary>
    public static readonly (string Code, string Modifier) TherapeuticBehavioralServices = ("H2019", "HA");

    /// <summary>The NPI of the treatment provider that sends every request in these tests.</summary>
    public const string TreatmentProviderNpi = "8189991234";

    private const string Pas = "http://hl7.org/fhir/us/davinci-pas/StructureDefinition/";
    private const string X12 = "https://codesystem.x12.org/005010/";
    private const string Hcpcs = "http://www.cms.gov/Medicare/Coding/HCPCSReleaseCodeSets";

    /// <summary>
    /// A provider system asking to be notified of decisions, built exactly as br-provider's
    /// pas-subscription-builder builds it.
    /// </summary>
    public static string Subscription(
        string orgIdentifier = TreatmentProviderNpi,
        string notificationUrl = "http://provider.example.org/api/pas/notification",
        bool withFilter = true) => $$"""
        {
          "resourceType": "Subscription",
          "meta": {
            "profile": [
              "http://hl7.org/fhir/us/davinci-pas/StructureDefinition/profile-subscription",
              "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/backport-subscription"
            ]
          },
          "status": "requested",
          "reason": "Monitor PAS authorization notifications via REST hook",
          "criteria": "http://hl7.org/fhir/us/davinci-pas/SubscriptionTopic/PASSubscriptionTopic",
          {{(withFilter ? $$"""
          "_criteria": {
            "extension": [
              {
                "url": "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/backport-filter-criteria",
                "valueString": "org-identifier={{orgIdentifier}}"
              }
            ]
          },
          """ : "")}}
          "channel": {
            "type": "rest-hook",
            "endpoint": "{{notificationUrl}}",
            "payload": "application/fhir+json",
            "_payload": {
              "extension": [
                { "url": "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/backport-payload-content", "valueCode": "full-resource" }
              ]
            }
          }
        }
        """;

    /// <summary>
    /// A provider system asking what has become of a Member's requests. The shape follows br-payer's
    /// PASClaimInquiryBundleExample.
    /// </summary>
    public static string Inquiry(
        string memberIdentifier = KnownMember,
        string providerNpi = TreatmentProviderNpi,
        bool withPatientGender = true) => $$"""
        {
          "resourceType": "Bundle",
          "meta": { "profile": ["{{Pas}}profile-pas-inquiry-request-bundle"] },
          "identifier": { "system": "http://example.org/SUBMITTER_TRANSACTION_IDENTIFIER", "value": "{{Guid.NewGuid():N}}" },
          "type": "collection",
          "timestamp": "2026-11-06T12:00:00-08:00",
          "entry": [
            {
              "fullUrl": "http://example.org/fhir/Claim/Inquiry",
              "resource": {
                "resourceType": "Claim",
                "id": "Inquiry",
                "meta": { "profile": ["{{Pas}}profile-claim-inquiry"] },
                "identifier": [{ "system": "http://example.org/PATIENT_EVENT_TRACE_NUMBER", "value": "111099" }],
                "status": "active",
                "type": { "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/claim-type", "code": "institutional" }] },
                "use": "preauthorization",
                "patient": { "reference": "Patient/MemberExample" },
                "created": "2026-11-06T12:00:00-08:00",
                "insurer": { "reference": "Organization/BehavioralHealthPlan" },
                "provider": { "reference": "Organization/TreatmentProvider" },
                "priority": { "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/processpriority", "code": "normal" }] },
                "insurance": [{ "sequence": 1, "focal": true, "coverage": { "reference": "Coverage/MemberCoverage" } }]
              }
            },
            {{Participants(memberIdentifier, providerNpi, withPatientGender)}}
          ]
        }
        """;

    /// <summary>One item of an update: the service, and whether the provider changed or withdrew it.</summary>
    public sealed record ItemChange((string Code, string Modifier) Service, bool Changed = false, bool Cancelled = false);

    /// <summary>
    /// An update to an earlier request, as PAS shapes it: Claim.related points at the prior Claim, which rides
    /// along in the Bundle under its original identifier; changed items carry infoChanged and withdrawn items
    /// the infoCancelled modifier extension.
    /// </summary>
    public static string Update(string priorBundleIdentifier, params ItemChange[] items) =>
        ForItems([.. items.Select(i => i.Service)], KnownMember, null, "preauthorization", "normal",
            priorBundleIdentifier, cancelWhole: false,
            changed: [.. items.Select(i => i.Changed)], cancelled: [.. items.Select(i => i.Cancelled)]);

    /// <summary>A cancellation of the whole earlier request: an update whose Claim carries certificationType 3.</summary>
    public static string Cancel(string priorBundleIdentifier) =>
        ForItems([Residential], KnownMember, null, "preauthorization", "normal", priorBundleIdentifier, cancelWhole: true);

    /// <summary>A renewal: a new request whose items carry certificationType R.</summary>
    public static string Renewal((string Code, string Modifier) service) =>
        ForItems([service], KnownMember, null, "preauthorization", "normal", renewal: true);

    private static string ForItems(
        IReadOnlyList<(string Code, string Modifier)> services,
        string memberIdentifier,
        string? bundleIdentifier,
        string claimUse,
        string priority,
        string? priorBundleIdentifier = null,
        bool cancelWhole = false,
        bool[]? changed = null,
        bool[]? cancelled = null,
        bool renewal = false,
        bool withPatientGender = true)
    {
        bundleIdentifier ??= Guid.NewGuid().ToString("N");
        var isUpdate = priorBundleIdentifier is not null;

        var claimExtensions = cancelWhole
            ? $$"""
              "extension": [{ "url": "{{Pas}}extension-certificationType", "valueCodeableConcept": { "coding": [{ "system": "{{X12}}1322", "code": "3", "display": "Cancel" }] } }],
              """
            : "";
        var related = isUpdate
            ? """
              "related": [{ "claim": { "reference": "Claim/PriorRequest" }, "relationship": { "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/ex-relatedclaimrelationship", "code": "prior" }] } }],
              """
            : "";
        // The prior Claim, as it was sent: the update profile wants it in the Bundle, conforming to profile-claim.
        var priorClaimEntry = isUpdate
            ? $$"""
              {
                "fullUrl": "http://example.org/fhir/Claim/PriorRequest",
                "resource": {
                  "resourceType": "Claim",
                  "id": "PriorRequest",
                  "meta": { "profile": ["{{Pas}}profile-claim"] },
                  "identifier": [{ "system": "http://example.org/PATIENT_EVENT_TRACE_NUMBER", "value": "{{priorBundleIdentifier}}" }],
                  "status": "active",
                  "type": { "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/claim-type", "code": "institutional" }] },
                  "use": "preauthorization",
                  "patient": { "reference": "Patient/MemberExample" },
                  "created": "2026-11-06T09:59:00-08:00",
                  "insurer": { "reference": "Organization/BehavioralHealthPlan" },
                  "provider": { "reference": "Organization/TreatmentProvider" },
                  "priority": { "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/processpriority", "code": "normal" }] },
                  "insurance": [{ "sequence": 1, "focal": true, "coverage": { "reference": "Coverage/MemberCoverage" } }],
                  "item": [
                    {{Item(Residential, 1)}}
                  ]
                }
              },
              """
            : "";

        var items = string.Join(",\n", services.Select((service, index) => Item(
            service,
            sequence: index + 1,
            certificationType: renewal ? "R" : "I",
            changed: changed?[index] == true,
            cancelled: cancelled?[index] == true)));

        return $$"""
        {
          "resourceType": "Bundle",
          "meta": { "profile": ["{{Pas}}profile-pas-request-bundle"] },
          "identifier": { "system": "http://example.org/SUBMITTER_TRANSACTION_IDENTIFIER", "value": "{{bundleIdentifier}}" },
          "type": "collection",
          "timestamp": "2026-11-06T09:59:00-08:00",
          "entry": [
            {
              "fullUrl": "http://example.org/fhir/Claim/ResidentialRequest",
              "resource": {
                "resourceType": "Claim",
                "id": "ResidentialRequest",
                "meta": { "profile": ["{{Pas}}{{(isUpdate ? "profile-claim-update" : "profile-claim")}}"] },
                {{claimExtensions}}
                "identifier": [
                  { "system": "http://example.org/PATIENT_EVENT_TRACE_NUMBER", "value": "{{bundleIdentifier}}" }
                ],
                {{related}}
                "status": "active",
                "type": { "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/claim-type", "code": "institutional" }] },
                "use": "{{claimUse}}",
                "patient": { "reference": "Patient/MemberExample" },
                "created": "2026-11-06T09:59:00-08:00",
                "insurer": { "reference": "Organization/BehavioralHealthPlan" },
                "provider": { "reference": "Organization/TreatmentProvider" },
                "priority": { "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/processpriority", "code": "{{priority}}" }] },
                "insurance": [{ "sequence": 1, "focal": true, "coverage": { "reference": "Coverage/MemberCoverage" } }],
                "item": [
        {{items}}
                ]
              }
            },
            {{priorClaimEntry}}
            {{Participants(memberIdentifier, TreatmentProviderNpi, withPatientGender)}}
          ]
        }
        """;
    }

    /// <summary>
    /// One Claim.item as PAS shapes it. Every item says what kind of review it asks for (X12 1525, HS: Health
    /// Services Review) and what kind of certification (X12 1322: I initial, R renewal), and carries the
    /// provider's trace number.
    /// </summary>
    private static string Item(
        (string Code, string Modifier) service,
        int sequence,
        string certificationType = "I",
        bool changed = false,
        bool cancelled = false)
    {
        var extensions = new List<string>
        {
            $$"""{ "url": "{{Pas}}extension-serviceItemRequestType", "valueCodeableConcept": { "coding": [{ "system": "{{X12}}1525", "code": "HS", "display": "Health Services Review" }] } }""",
            $$"""{ "url": "{{Pas}}extension-certificationType", "valueCodeableConcept": { "coding": [{ "system": "{{X12}}1322", "code": "{{certificationType}}" }] } }""",
            $$"""{ "url": "{{Pas}}extension-itemTraceNumber", "valueIdentifier": { "system": "http://example.org/ITEM_TRACE_NUMBER", "value": "{{1122333 + sequence}}" } }""",
        };
        if (changed)
            extensions.Add($$"""{ "url": "{{Pas}}extension-infoChanged", "valueCode": "changed" }""");
        var modifierExtensions = cancelled
            ? $$"""
              "modifierExtension": [{ "url": "{{Pas}}modifierextension-infoCancelled", "valueBoolean": true }],
              """
            : "";
        return $$"""
              {
                "extension": [ {{string.Join(", ", extensions)}} ],
                {{modifierExtensions}}
                "sequence": {{sequence}},
                "category": { "coding": [{ "system": "{{X12}}1365", "code": "AI", "display": "Substance Abuse" }] },
                "productOrService": { "coding": [{ "system": "{{Hcpcs}}", "code": "{{service.Code}}" }] },
                "modifier": [{ "coding": [{ "system": "{{Hcpcs}}", "code": "{{service.Modifier}}" }] }],
                "locationCodeableConcept": { "coding": [{ "system": "https://www.cms.gov/Medicare/Coding/place-of-service-codes/Place_of_Service_Code_Set", "code": "55" }] }
              }
              """;
    }

    /// <summary>
    /// The entries every request carries besides the Claim: the sending provider, the plan, the Member's Coverage
    /// and the Member, shaped to PAS's requestor, insurer, coverage and beneficiary profiles (US Core underneath:
    /// hence the Patient's gender and the Coverage's member identifier and relationship). PAS would also like the
    /// X12 relationship code (1069, "18" for self) beside the FHIR one, but the validator cannot tell that coding
    /// slice apart without X12's licensed value set and counts every coding against it; so the Coverage carries
    /// the FHIR code alone and names the subscriber, as PAS's self-beneficiary rule then asks.
    /// </summary>
    private static string Participants(string memberIdentifier, string providerNpi, bool withPatientGender) => $$"""
            {
              "fullUrl": "http://example.org/fhir/Organization/TreatmentProvider",
              "resource": {
                "resourceType": "Organization",
                "id": "TreatmentProvider",
                "identifier": [{ "system": "http://hl7.org/fhir/sid/us-npi", "value": "{{providerNpi}}" }],
                "active": true,
                "name": "SYNTHETIC RECOVERY HOUSE"
              }
            },
            {
              "fullUrl": "http://example.org/fhir/Organization/BehavioralHealthPlan",
              "resource": {
                "resourceType": "Organization",
                "id": "BehavioralHealthPlan",
                "identifier": [{ "system": "http://hl7.org/fhir/sid/us-npi", "value": "1234567893" }],
                "active": true,
                "name": "SYNTHETIC BEHAVIORAL HEALTH PLAN"
              }
            },
            {
              "fullUrl": "http://example.org/fhir/Coverage/MemberCoverage",
              "resource": {
                "resourceType": "Coverage",
                "id": "MemberCoverage",
                "identifier": [
                  {
                    "type": { "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/v2-0203", "code": "MB" }] },
                    "system": "http://example.org/fhir/sid/member-identifier",
                    "value": "{{memberIdentifier}}"
                  }
                ],
                "status": "active",
                "subscriberId": "{{memberIdentifier}}",
                "subscriber": { "reference": "Patient/MemberExample" },
                "beneficiary": { "reference": "Patient/MemberExample" },
                "relationship": { "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/subscriber-relationship", "code": "self" }] },
                "payor": [{ "reference": "Organization/BehavioralHealthPlan" }]
              }
            },
            {
              "fullUrl": "http://example.org/fhir/Patient/MemberExample",
              "resource": {
                "resourceType": "Patient",
                "id": "MemberExample",
                "identifier": [
                  {
                    "type": { "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/v2-0203", "code": "MB" }] },
                    "system": "http://example.org/fhir/sid/member-identifier",
                    "value": "{{memberIdentifier}}"
                  }
                ],
                "name": [{ "family": "EXAMPLE", "given": ["ALEX"] }]{{(withPatientGender ? ", \"gender\": \"female\"" : "")}}
              }
            }
    """;
}
