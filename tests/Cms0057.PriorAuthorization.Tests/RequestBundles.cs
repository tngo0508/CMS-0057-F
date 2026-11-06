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
        string claimUse = "preauthorization") =>
        ForService("H0019", "U1", memberIdentifier, bundleIdentifier, claimUse);

    /// <summary>Outpatient individual counseling: H0004 with the outpatient modifier U7.</summary>
    public static string OutpatientCounseling(string modifier = "U7") =>
        ForService("H0004", modifier, KnownMember, null, "preauthorization");

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

    private static string ForService(
        string serviceCode,
        string modifier,
        string memberIdentifier,
        string? bundleIdentifier,
        string claimUse)
    {
        bundleIdentifier ??= Guid.NewGuid().ToString("N");

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
                "identifier": [
                  { "system": "http://example.org/PATIENT_EVENT_TRACE_NUMBER", "value": "{{bundleIdentifier}}" }
                ],
                "status": "active",
                "type": { "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/claim-type", "code": "institutional" }] },
                "use": "{{claimUse}}",
                "patient": { "reference": "Patient/MemberExample" },
                "created": "2026-11-06T09:59:00-08:00",
                "insurer": { "reference": "Organization/BehavioralHealthPlan" },
                "provider": { "reference": "Organization/TreatmentProvider" },
                "priority": { "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/processpriority", "code": "normal" }] },
                "insurance": [{ "sequence": 1, "focal": true, "coverage": { "reference": "Coverage/MemberCoverage" } }],
                "item": [
                  {
                    "extension": [
                      {
                        "url": "http://hl7.org/fhir/us/davinci-pas/StructureDefinition/extension-itemTraceNumber",
                        "valueIdentifier": { "system": "http://example.org/ITEM_TRACE_NUMBER", "value": "1122334" }
                      }
                    ],
                    "sequence": 1,
                    "category": { "coding": [{ "system": "https://codesystem.x12.org/005010/1365", "code": "AI", "display": "Substance Abuse" }] },
                    "productOrService": { "coding": [{ "system": "http://www.cms.gov/Medicare/Coding/HCPCSReleaseCodeSets", "code": "{{serviceCode}}" }] },
                    "modifier": [{ "coding": [{ "system": "http://www.cms.gov/Medicare/Coding/HCPCSReleaseCodeSets", "code": "{{modifier}}" }] }],
                    "locationCodeableConcept": { "coding": [{ "system": "https://www.cms.gov/Medicare/Coding/place-of-service-codes/Place_of_Service_Code_Set", "code": "55" }] }
                  }
                ]
              }
            },
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
