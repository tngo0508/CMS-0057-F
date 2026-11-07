namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// What another payer sends to find a Member here: the HRex $member-match Parameters, with the Member's
/// demographics as the other payer knows them, the Coverage it believes this plan gave the Member, and the
/// Member's Consent to the exchange (the Payer-to-Payer opt-in, 42 CFR 422.121(b)(1)(ii)).
/// </summary>
public static class MemberMatchRequests
{
    public const string OtherPayerNpi = PriorAuthorizationApi.OtherPayerIdentifier;

    /// <summary>A match request for a person the other payer knows by this plan's member identifier and name.</summary>
    /// <param name="withConsent">False to leave the Consent out, as a payer that has no opt-in might.</param>
    public static string For(string memberIdentifier, string family, string given, bool withConsent = true, string consentEnds = "2027-11-06") => $$"""
    {
      "resourceType": "Parameters",
      "parameter": [
        {
          "name": "MemberPatient",
          "resource": {
            "resourceType": "Patient",
            "id": "member-as-the-other-payer-knows-them",
            "identifier": [
              {
                "type": { "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/v2-0203", "code": "MB" }] },
                "system": "http://other-payer.example.org/fhir/sid/member-identifier",
                "value": "OP-{{memberIdentifier}}"
              }
            ],
            "name": [{ "family": "{{family}}", "given": ["{{given}}"] }],
            "gender": "female",
            "birthDate": "1990-01-15"
          }
        },
        {
          "name": "CoverageToMatch",
          "resource": {
            "resourceType": "Coverage",
            "id": "coverage-with-the-behavioral-health-plan",
            "identifier": [
              {
                "type": { "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/v2-0203", "code": "MB" }] },
                "system": "http://example.org/fhir/sid/member-identifier",
                "value": "{{memberIdentifier}}"
              }
            ],
            "status": "active",
            "subscriberId": "{{memberIdentifier}}",
            "beneficiary": { "reference": "Patient/member-as-the-other-payer-knows-them" },
            "relationship": { "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/subscriber-relationship", "code": "self" }] },
            "period": { "start": "2024-01-01" },
            "payor": [{ "identifier": { "system": "http://hl7.org/fhir/sid/us-npi", "value": "1234567893" }, "display": "SYNTHETIC BEHAVIORAL HEALTH PLAN" }]
          }
        }{{(withConsent ? "," + Consent(consentEnds) : "")}}
      ]
    }
    """;

    /// <summary>The Member's Consent to the exchange, shaped to HRex's Consent profile: this plan discloses, the other payer receives.</summary>
    private static string Consent(string ends) => $$"""
        {
          "name": "Consent",
          "resource": {
            "resourceType": "Consent",
            "id": "payer-to-payer-opt-in",
            "status": "active",
            "scope": { "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/consentscope", "code": "patient-privacy" }] },
            "category": [{ "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/v3-ActCode", "code": "IDSCL" }] }],
            "patient": { "reference": "Patient/member-as-the-other-payer-knows-them" },
            "performer": [{ "reference": "Patient/member-as-the-other-payer-knows-them" }],
            "sourceReference": { "reference": "DocumentReference/signed-opt-in-form" },
            "policy": [{ "uri": "http://hl7.org/fhir/us/davinci-hrex/StructureDefinition-hrex-consent.html#regular" }],
            "provision": {
              "type": "permit",
              "period": { "start": "2026-11-01", "end": "{{ends}}" },
              "actor": [
                {
                  "role": { "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/provenance-participant-type", "code": "performer" }] },
                  "reference": { "identifier": { "system": "http://hl7.org/fhir/sid/us-npi", "value": "1234567893" }, "display": "SYNTHETIC BEHAVIORAL HEALTH PLAN" }
                },
                {
                  "role": { "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/v3-ParticipationType", "code": "IRCP" }] },
                  "reference": { "identifier": { "system": "http://hl7.org/fhir/sid/us-npi", "value": "{{OtherPayerNpi}}" }, "display": "SYNTHETIC OTHER PAYER" }
                }
              ],
              "action": [{ "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/consentaction", "code": "disclose" }] }]
            }
          }
        }
    """;
}
