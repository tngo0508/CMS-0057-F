namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// CDS Hooks order-sign requests as a provider's EHR would send them when a clinician signs an order: the draft
/// order in context and the Member's Patient and Coverage in prefetch. Follows the prefetch keys br-payer declares.
/// </summary>
public static class HookRequests
{
    public static string OrderSign(string serviceCode = "H0019", string modifier = "U1", string hook = "order-sign") =>
        Build(hook, DraftOrders(serviceCode, modifier), Prefetch(includeCoverage: true));

    public static string OrderSignWithoutCoverage() =>
        Build("order-sign", DraftOrders("H0019", "U1"), Prefetch(includeCoverage: false));

    public static string OrderSignWithoutDraftOrders() =>
        Build("order-sign", draftOrders: null, Prefetch(includeCoverage: true));

    private static string Build(string hook, string? draftOrders, string prefetch) => $$"""
        {
          "hookInstance": "{{Guid.NewGuid()}}",
          "fhirServer": "http://example.org/ehr/fhir",
          "hook": "{{hook}}",
          "context": {
            "userId": "PractitionerRole/ReferringClinician",
            "patientId": "MemberExample",
            "encounterId": "IntakeVisit"{{(draftOrders is null ? "" : ",\n    \"draftOrders\": " + draftOrders)}}
          },
          "prefetch": {{prefetch}}
        }
        """;

    private static string DraftOrders(string serviceCode, string modifier) => $$"""
        {
              "resourceType": "Bundle",
              "type": "collection",
              "entry": [
                {
                  "resource": {
                    "resourceType": "ServiceRequest",
                    "id": "ResidentialOrder",
                    "status": "draft",
                    "intent": "order",
                    "code": {
                      "coding": [{ "system": "http://www.cms.gov/Medicare/Coding/HCPCSReleaseCodeSets", "code": "{{serviceCode}}" }]
                    },
                    "orderDetail": [
                      { "coding": [{ "system": "http://www.cms.gov/Medicare/Coding/HCPCSReleaseCodeSets", "code": "{{modifier}}" }] }
                    ],
                    "subject": { "reference": "Patient/MemberExample" },
                    "authoredOn": "2026-11-06",
                    "requester": { "reference": "PractitionerRole/ReferringClinician" },
                    "insurance": [{ "reference": "Coverage/MemberCoverage" }]
                  }
                }
              ]
            }
        """;

    private static string Prefetch(bool includeCoverage) => $$"""
        {
            "patient": {
              "resourceType": "Patient",
              "id": "MemberExample",
              "identifier": [
                {
                  "type": { "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/v2-0203", "code": "MB" }] },
                  "system": "http://example.org/fhir/sid/member-identifier",
                  "value": "90000001A"
                }
              ],
              "name": [{ "family": "EXAMPLE", "given": ["ALEX"] }]
            }{{(includeCoverage ? "," : "")}}
            {{(includeCoverage ? """
            "coverage": {
              "resourceType": "Bundle",
              "type": "searchset",
              "entry": [
                {
                  "resource": {
                    "resourceType": "Coverage",
                    "id": "MemberCoverage",
                    "status": "active",
                    "subscriberId": "90000001A",
                    "beneficiary": { "reference": "Patient/MemberExample" },
                    "payor": [{ "identifier": { "system": "http://hl7.org/fhir/sid/us-npi", "value": "1234567893" }, "display": "SYNTHETIC BEHAVIORAL HEALTH PLAN" }]
                  }
                }
              ]
            }
            """ : "")}}
          }
        """;
}
