namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// Prior Authorization Request bundles as a provider system would send them. The shape is adapted from the
/// request examples in Da Vinci's br-payer Reference Implementation (Apache-2.0), cut down to one item.
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
        string priority = "normal") =>
        ForItems([Residential], memberIdentifier, bundleIdentifier, claimUse, priority);

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
    public static string Inquiry(string memberIdentifier = KnownMember, string providerNpi = TreatmentProviderNpi) => $$"""
        {
          "resourceType": "Bundle",
          "meta": { "profile": ["http://hl7.org/fhir/us/davinci-pas/StructureDefinition/profile-pas-inquiry-request-bundle"] },
          "identifier": { "system": "http://example.org/SUBMITTER_TRANSACTION_IDENTIFIER", "value": "{{Guid.NewGuid():N}}" },
          "type": "collection",
          "timestamp": "2026-11-06T12:00:00-08:00",
          "entry": [
            {
              "fullUrl": "http://example.org/fhir/Claim/Inquiry",
              "resource": {
                "resourceType": "Claim",
                "id": "Inquiry",
                "meta": { "profile": ["http://hl7.org/fhir/us/davinci-pas/StructureDefinition/profile-claim-inquiry"] },
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
                "status": "active",
                "subscriberId": "{{memberIdentifier}}",
                "beneficiary": { "reference": "Patient/MemberExample" },
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
                "name": [{ "family": "EXAMPLE", "given": ["ALEX"] }]
              }
            }
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
        bool renewal = false)
    {
        const string pas = "http://hl7.org/fhir/us/davinci-pas/StructureDefinition/";
        bundleIdentifier ??= Guid.NewGuid().ToString("N");

        var claimExtensions = cancelWhole
            ? $$"""
              "extension": [{ "url": "{{pas}}extension-certificationType", "valueCodeableConcept": { "coding": [{ "system": "https://codesystem.x12.org/005010/1322", "code": "3" }] } }],
              """
            : "";
        var related = priorBundleIdentifier is null
            ? ""
            : """
              "related": [{ "claim": { "reference": "Claim/PriorRequest" } }],
              """;
        var priorClaimEntry = priorBundleIdentifier is null
            ? ""
            : $$"""
              {
                "fullUrl": "http://example.org/fhir/Claim/PriorRequest",
                "resource": {
                  "resourceType": "Claim",
                  "id": "PriorRequest",
                  "identifier": [{ "system": "http://example.org/PATIENT_EVENT_TRACE_NUMBER", "value": "{{priorBundleIdentifier}}" }],
                  "status": "active",
                  "type": { "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/claim-type", "code": "institutional" }] },
                  "use": "preauthorization",
                  "patient": { "reference": "Patient/MemberExample" },
                  "created": "2026-11-06T09:59:00-08:00",
                  "insurer": { "reference": "Organization/BehavioralHealthPlan" },
                  "provider": { "reference": "Organization/TreatmentProvider" },
                  "priority": { "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/processpriority", "code": "normal" }] },
                  "insurance": [{ "sequence": 1, "focal": true, "coverage": { "reference": "Coverage/MemberCoverage" } }]
                }
              },
              """;

        var items = string.Join(",\n", services.Select((service, index) =>
        {
            var extensions = new List<string>
            {
                $$"""{ "url": "{{pas}}extension-itemTraceNumber", "valueIdentifier": { "system": "http://example.org/ITEM_TRACE_NUMBER", "value": "{{1122334 + index}}" } }""",
            };
            if (changed?[index] == true)
                extensions.Add($$"""{ "url": "{{pas}}extension-infoChanged", "valueCode": "changed" }""");
            if (renewal)
                extensions.Add($$"""{ "url": "{{pas}}extension-certificationType", "valueCodeableConcept": { "coding": [{ "system": "https://codesystem.x12.org/005010/1322", "code": "R" }] } }""");
            var modifierExtensions = cancelled?[index] == true
                ? $$"""
                  "modifierExtension": [{ "url": "{{pas}}modifierextension-infoCancelled", "valueBoolean": true }],
                  """
                : "";
            return $$"""
                  {
                    "extension": [ {{string.Join(", ", extensions)}} ],
                    {{modifierExtensions}}
                    "sequence": {{index + 1}},
                    "category": { "coding": [{ "system": "https://codesystem.x12.org/005010/1365", "code": "AI", "display": "Substance Abuse" }] },
                    "productOrService": { "coding": [{ "system": "http://www.cms.gov/Medicare/Coding/HCPCSReleaseCodeSets", "code": "{{service.Code}}" }] },
                    "modifier": [{ "coding": [{ "system": "http://www.cms.gov/Medicare/Coding/HCPCSReleaseCodeSets", "code": "{{service.Modifier}}" }] }],
                    "locationCodeableConcept": { "coding": [{ "system": "https://www.cms.gov/Medicare/Coding/place-of-service-codes/Place_of_Service_Code_Set", "code": "55" }] }
                  }
                  """;
        }));

        return $$"""
        {
          "resourceType": "Bundle",
          "meta": { "profile": ["http://hl7.org/fhir/us/davinci-pas/StructureDefinition/profile-pas-request-bundle"] },
          "identifier": { "system": "http://example.org/SUBMITTER_TRANSACTION_IDENTIFIER", "value": "{{bundleIdentifier}}" },
          "type": "collection",
          "timestamp": "2026-11-06T09:59:00-08:00",
          "entry": [
            {
              "fullUrl": "http://example.org/fhir/Claim/ResidentialRequest",
              "resource": {
                "resourceType": "Claim",
                "id": "ResidentialRequest",
                "meta": { "profile": ["http://hl7.org/fhir/us/davinci-pas/StructureDefinition/profile-claim"] },
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
            {
              "fullUrl": "http://example.org/fhir/Organization/TreatmentProvider",
              "resource": {
                "resourceType": "Organization",
                "id": "TreatmentProvider",
                "identifier": [{ "system": "http://hl7.org/fhir/sid/us-npi", "value": "8189991234" }],
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
                "status": "active",
                "subscriberId": "{{memberIdentifier}}",
                "beneficiary": { "reference": "Patient/MemberExample" },
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
                "name": [{ "family": "EXAMPLE", "given": ["ALEX"] }]
              }
            }
          ]
        }
        """;
    }
}
