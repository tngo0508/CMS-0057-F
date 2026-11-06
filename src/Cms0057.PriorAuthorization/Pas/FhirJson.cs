using System.Text.Json;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

namespace Cms0057.PriorAuthorization.Pas;

/// <summary>FHIR R4 JSON in and out, through the Firely SDK.</summary>
public static class FhirJson
{
    private static readonly JsonSerializerOptions Options =
        new JsonSerializerOptions().ForFhir(ModelInfo.ModelInspector);

    /// <exception cref="RequestRejected">The text is not valid FHIR JSON for <typeparamref name="T"/>.</exception>
    public static T Read<T>(string json) where T : Resource
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options)
                   ?? throw new RequestRejected("A FHIR resource is required");
        }
        catch (Exception e) when (e is JsonException or DeserializationFailedException)
        {
            throw new RequestRejected($"The request body is not a valid FHIR {typeof(T).Name}: " + e.Message);
        }
    }

    public static string Write(Resource resource) => JsonSerializer.Serialize(resource, Options);
}
