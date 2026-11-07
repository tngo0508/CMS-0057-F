using Hl7.Fhir.Model;

namespace Cms0057.PriorAuthorization;

/// <summary>
/// The server's CapabilityStatement (<c>GET /fhir/metadata</c>): what every FHIR server publishes about itself, and
/// what a client such as Da Vinci's br-provider reads to see that the payer is there. It names the PAS and DTR
/// operations this service offers, the resources behind them, and that the server is SMART-secured, with the token
/// endpoint in the SMART <c>oauth-uris</c> extension.
/// </summary>
public static class ServerCapabilities
{
    private const string Pas = "http://hl7.org/fhir/us/davinci-pas/OperationDefinition/";
    private const string Dtr = "http://hl7.org/fhir/us/davinci-dtr/OperationDefinition/";
    private const string Sdc = "http://hl7.org/fhir/uv/sdc/OperationDefinition/";
    private const string BulkData = "http://hl7.org/fhir/uv/bulkdata/OperationDefinition/";
    private const string OAuthUrisExtension = "http://fhir-registry.smarthealthit.org/StructureDefinition/oauth-uris";

    public static CapabilityStatement For(string serverRoot, DateTimeOffset now)
    {
        var security = new CapabilityStatement.SecurityComponent
        {
            Service = [new CodeableConcept("http://terminology.hl7.org/CodeSystem/restful-security-service", "SMART-on-FHIR")],
            Description = "SMART App Launch Backend Services: a registered system signs a client assertion and trades it "
                          + "for an access token at the token endpoint; see /.well-known/smart-configuration.",
        };
        var oauthUris = new Extension { Url = OAuthUrisExtension };
        oauthUris.Extension.Add(new Extension("token", new FhirUri(serverRoot + Authentication.SmartConfiguration.TokenPath)));
        security.Extension.Add(oauthUris);

        return new CapabilityStatement
        {
            Status = PublicationStatus.Active,
            Date = now.ToString("yyyy-MM-dd"),
            Kind = CapabilityStatementKind.Instance,
            Software = new CapabilityStatement.SoftwareComponent { Name = "CMS-0057-F Prior Authorization API (proof of concept)" },
            Implementation = new CapabilityStatement.ImplementationComponent
            {
                Description = "Payer-side Prior Authorization API of a synthetic Behavioral Health Plan: Da Vinci PAS, CRD and DTR.",
                Url = serverRoot + "/fhir",
            },
            FhirVersion = FHIRVersion.N4_0_1,
            Format = ["application/fhir+json"],
            Rest =
            [
                new CapabilityStatement.RestComponent
                {
                    Mode = CapabilityStatement.RestfulCapabilityMode.Server,
                    Security = security,
                    Resource =
                    [
                        Resource("Claim", operations: [("submit", Pas + "Claim-submit"), ("inquire", Pas + "Claim-inquire")]),
                        Resource("ClaimResponse", interactions: [CapabilityStatement.TypeRestfulInteraction.Read]),
                        Resource("Subscription", interactions: [CapabilityStatement.TypeRestfulInteraction.Create, CapabilityStatement.TypeRestfulInteraction.SearchType]),
                        Resource("Questionnaire", operations: [("questionnaire-package", Dtr + "questionnaire-package"), ("next-question", Sdc + "Questionnaire-next-question")]),
                        Resource("ExplanationOfBenefit",
                            interactions: [CapabilityStatement.TypeRestfulInteraction.Read, CapabilityStatement.TypeRestfulInteraction.SearchType],
                            supportedProfile: PatientAccess.PriorAuthorizationInformation.Profile,
                            searchParameters: [("patient", SearchParamType.Reference), ("_lastUpdated", SearchParamType.Date)]),
                        Resource("Group",
                            interactions: [CapabilityStatement.TypeRestfulInteraction.Read, CapabilityStatement.TypeRestfulInteraction.SearchType],
                            operations: [("export", BulkData + "group-export")],
                            supportedProfile: Sharing.AttributionLists.ProviderGroupProfile),
                    ],
                },
            ],
        };
    }

    private static CapabilityStatement.ResourceComponent Resource(
        string type,
        IEnumerable<CapabilityStatement.TypeRestfulInteraction>? interactions = null,
        IEnumerable<(string Name, string Definition)>? operations = null,
        string? supportedProfile = null,
        IEnumerable<(string Name, SearchParamType Type)>? searchParameters = null) => new()
    {
        Type = type,
        SupportedProfile = supportedProfile is null ? [] : [supportedProfile],
        Interaction = [.. (interactions ?? []).Select(i => new CapabilityStatement.ResourceInteractionComponent { Code = i })],
        Operation = [.. (operations ?? []).Select(o => new CapabilityStatement.OperationComponent { Name = o.Name, Definition = o.Definition })],
        SearchParam = [.. (searchParameters ?? []).Select(p => new CapabilityStatement.SearchParamComponent { Name = p.Name, Type = p.Type })],
    };
}
