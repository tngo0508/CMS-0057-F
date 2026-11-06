using System.Text.Json.Nodes;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// DTR operation inputs as a provider system's DTR app sends them, in the shapes br-provider builds: the
/// Member's Coverage as a resource parameter, the order as a resource parameter, questionnaires as canonicals.
/// </summary>
public static class DtrRequests
{
    /// <summary>A $questionnaire-package request. Null leaves the parameter out.</summary>
    public static string QuestionnairePackage(
        string? orderCode = "H0019",
        string? orderModifier = "U1",
        string? questionnaire = null,
        bool coverage = true)
    {
        var parameters = new List<string>();
        if (coverage) parameters.Add($$"""{ "name": "coverage", "resource": {{Coverage}} }""");
        if (questionnaire is not null) parameters.Add($$"""{ "name": "questionnaire", "valueCanonical": "{{questionnaire}}" }""");
        if (orderCode is not null) parameters.Add($$"""{ "name": "order", "resource": {{Order(orderCode, orderModifier)}} }""");
        return $$"""{ "resourceType": "Parameters", "parameter": [ {{string.Join(",\n", parameters)}} ] }""";
    }

    /// <summary>
    /// A $next-question request: the QuestionnaireResponse as the payer handed it out (its questionnaire contained),
    /// with the given answers filled in, wrapped in Parameters as br-provider does. Each answer is the linkId and
    /// the answer's JSON, e.g. <c>{ "valueBoolean": true }</c>.
    /// </summary>
    public static string NextQuestion(string questionnaireResponse, params (string LinkId, string Answer)[] answers)
    {
        var response = JsonNode.Parse(questionnaireResponse)!.AsObject();
        var items = response["item"]?.AsArray() ?? [];
        response["item"] = items;
        foreach (var (linkId, answer) in answers)
        {
            var item = items.FirstOrDefault(i => i?["linkId"]?.GetValue<string>() == linkId)?.AsObject();
            if (item is null)
            {
                item = new JsonObject { ["linkId"] = linkId };
                items.Add(item);
            }
            item["answer"] = new JsonArray(JsonNode.Parse(answer));
        }
        return $$"""{ "resourceType": "Parameters", "parameter": [ { "name": "questionnaire-response", "resource": {{response.ToJsonString()}} } ] }""";
    }

    private const string Coverage = """
        {
          "resourceType": "Coverage",
          "id": "MemberCoverage",
          "status": "active",
          "subscriberId": "90000001A",
          "beneficiary": { "reference": "Patient/MemberExample" },
          "relationship": { "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/subscriber-relationship", "code": "self" }] },
          "payor": [{ "identifier": { "system": "http://hl7.org/fhir/sid/us-npi", "value": "1234567893" }, "display": "SYNTHETIC BEHAVIORAL HEALTH PLAN" }]
        }
        """;

    private static string Order(string code, string? modifier) => $$"""
        {
          "resourceType": "ServiceRequest",
          "id": "ResidentialOrder",
          "status": "active",
          "intent": "order",
          "code": { "coding": [{ "system": "http://www.cms.gov/Medicare/Coding/HCPCSReleaseCodeSets", "code": "{{code}}" }] },
          {{(modifier is null ? "" : $$"""
          "orderDetail": [{ "coding": [{ "system": "http://www.cms.gov/Medicare/Coding/HCPCSReleaseCodeSets", "code": "{{modifier}}" }] }],
          """)}}
          "subject": { "reference": "Patient/MemberExample" },
          "authoredOn": "2026-11-06",
          "requester": { "reference": "PractitionerRole/ReferringClinician" },
          "insurance": [{ "reference": "Coverage/MemberCoverage" }]
        }
        """;
}
