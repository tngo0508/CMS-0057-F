using System.Text.Json;
using System.Text.Json.Nodes;
using Cms0057.PriorAuthorization.Pas;
using Hl7.Fhir.Model;

namespace Cms0057.PriorAuthorization.Crd;

/// <summary>
/// Coverage Requirements Discovery (Da Vinci CRD 2.2.1 over CDS Hooks): as a clinician signs an order, the EHR asks
/// whether the Behavioral Health Plan requires prior authorization. The answer goes back as coverage information
/// attached to the order, as br-payer does; it is never repeated as a card.
/// </summary>
public sealed class CoverageRequirements(IAuthorizationRules rules, TimeProvider clock)
{
    public const string ServiceId = "order-sign-crd";
    private const string Hook = "order-sign";
    private const string CoverageInformationUrl =
        "http://hl7.org/fhir/us/davinci-crd/StructureDefinition/ext-coverage-information";

    /// <summary>The CDS Hooks discovery document.</summary>
    public static JsonObject Discovery() => new()
    {
        ["services"] = new JsonArray(new JsonObject
        {
            ["id"] = ServiceId,
            ["hook"] = Hook,
            ["title"] = "CRD Order Sign Hook",
            ["description"] = "Guidance related to orders being signed",
            ["prefetch"] = new JsonObject
            {
                ["patient"] = "Patient/{{context.patientId}}",
                ["coverage"] = "Coverage?patient={{context.patientId}}&status=active",
            },
            ["extension"] = new JsonObject { ["davinci-crd.version"] = new JsonArray("2.2") },
        }),
    };

    /// <summary>Answers an order-sign request.</summary>
    /// <exception cref="RequestRejected">The request is not one this service can answer.</exception>
    public JsonObject OrderSign(string hookRequest)
    {
        var request = ReadHookRequest(hookRequest);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).ToString("yyyy-MM-dd");

        var systemActions = new JsonArray();
        foreach (var order in request.Orders.OfType<ServiceRequest>())
        {
            var rule = rules.For(RequestedServiceOf(order));
            var updated = (ServiceRequest)order.DeepCopy();
            updated.Extension.RemoveAll(e => e.Url == CoverageInformationUrl);
            updated.Extension.Add(CoverageInformation(request.Coverage, rule, today));

            systemActions.Add(new JsonObject
            {
                ["type"] = "update",
                ["description"] = "Add coverage information to " + order.TypeName,
                ["resource"] = JsonNode.Parse(FhirJson.Write(updated)),
            });
        }

        return new JsonObject { ["cards"] = new JsonArray(), ["systemActions"] = systemActions };
    }

    private static Extension CoverageInformation(Coverage coverage, AuthorizationRule rule, string today)
    {
        var information = new Extension { Url = CoverageInformationUrl };
        information.Extension.Add(new Extension("coverage", new ResourceReference("Coverage/" + coverage.Id)));
        information.Extension.Add(new Extension("covered", new Code("covered")));
        information.Extension.Add(new Extension("pa-needed",
            new Code(rule is AuthorizationRule.RequiresReview ? "auth-needed" : "no-auth")));
        information.Extension.Add(new Extension("date", new Date(today)));
        information.Extension.Add(new Extension("coverage-assertion-id", new FhirString(Guid.NewGuid().ToString())));
        return information;
    }

    /// <summary>
    /// The billed service: the order's code, with any HCPCS modifiers the EHR put in orderDetail. That is this
    /// project's convention; ServiceRequest has no modifier element.
    /// </summary>
    private static RequestedService RequestedServiceOf(ServiceRequest order)
    {
        var code = order.Code?.Coding.FirstOrDefault()?.Code
                   ?? throw new RequestRejected($"ServiceRequest/{order.Id} has no code");
        return new RequestedService(code, [.. order.OrderDetail.SelectMany(d => d.Coding).Select(c => c.Code)]);
    }

    private static HookRequest ReadHookRequest(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject ?? throw new RequestRejected("The request body must be a JSON object");
        }
        catch (JsonException e)
        {
            throw new RequestRejected("The request body is not valid JSON: " + e.Message);
        }

        var hook = root["hook"]?.GetValue<string>();
        if (hook != Hook)
            throw new RequestRejected($"Mismatched hook in request. Expected: {Hook}, Actual: {hook}");

        var context = root["context"] as JsonObject ?? throw new RequestRejected("Missing required field 'context'.");
        RequireContext(context, "userId");
        RequireContext(context, "patientId");
        var draftOrders = RequireContext(context, "draftOrders") as JsonObject
                          ?? throw new RequestRejected($"Context field 'draftOrders' must be a Bundle for hook '{Hook}'.");

        var orders = FhirJson.Read<Bundle>(draftOrders.ToJsonString()).Entry
            .Select(e => e.Resource)
            .OfType<DomainResource>()
            .ToList();
        if (orders.Count == 0)
            throw new RequestRejected("draftOrders context is required but was empty, missing, or could not be resolved.");

        var coverage = CoverageIn(root["prefetch"] as JsonObject);
        return new HookRequest(orders, coverage);
    }

    /// <summary>
    /// The Member's primary Coverage, from prefetch only. br-payer also queries the EHR when prefetch is missing;
    /// this service has no credentials for that yet.
    /// </summary>
    private static Coverage CoverageIn(JsonObject? prefetch)
    {
        var coverages = new List<Coverage>();
        if (prefetch?["coverage"] is JsonObject prefetched)
        {
            var resource = FhirJson.Read<Resource>(prefetched.ToJsonString());
            if (resource is Coverage one) coverages.Add(one);
            if (resource is Bundle bundle) coverages.AddRange(bundle.Entry.Select(e => e.Resource).OfType<Coverage>());
        }

        return coverages.Count switch
        {
            0 => throw new RequestRejected(
                "No Coverage resource is accessible for this patient. A Coverage resource with a valid payer identifier is required."),
            1 => coverages[0],
            _ => throw new RequestRejected(
                "Multiple Coverage resources are accessible for this patient. CRD requires a single primary Coverage in the request."),
        };
    }

    private static JsonNode RequireContext(JsonObject context, string key) =>
        context[key] ?? throw new RequestRejected($"Missing required context field '{key}' for hook '{Hook}'.");

    private sealed record HookRequest(IReadOnlyList<DomainResource> Orders, Coverage Coverage);
}
