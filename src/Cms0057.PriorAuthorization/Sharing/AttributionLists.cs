using Hl7.Fhir.Model;

namespace Cms0057.PriorAuthorization.Sharing;

/// <summary>
/// An Attribution List as a provider system reads it: a Da Vinci PDex 2.1 Provider Group (built on the Member
/// Attribution List guide's Group), one per provider organization, naming the Members it may see. The list is what
/// the provider exports with Bulk Data to receive their prior authorization information.
/// </summary>
public static class AttributionLists
{
    public const string ProviderGroupProfile = "http://hl7.org/fhir/us/davinci-pdex/StructureDefinition/pdex-provider-group";
    private const string AttributionCodeSystem = "http://hl7.org/fhir/us/davinci-pdex/CodeSystem/PdexMemberAttributionCS";
    private const string AttributionListStatusUrl = "http://hl7.org/fhir/us/davinci-atr/StructureDefinition/ext-attributionListStatus";
    private const string NpiSystem = "http://hl7.org/fhir/sid/us-npi";
    private const string IdPrefix = "provider-";

    /// <summary>The Group id of a provider's list, from the NPI it is registered under.</summary>
    public static string IdFor(string providerNpi) => IdPrefix + providerNpi;

    /// <summary>The NPI a Group id names, or null when the id is not one of ours.</summary>
    public static string? ProviderOf(string groupId) =>
        groupId.StartsWith(IdPrefix, StringComparison.Ordinal) && groupId.Length > IdPrefix.Length ? groupId[IdPrefix.Length..] : null;

    /// <summary>The provider's list as it stands now: a Group of the Members it may see.</summary>
    public static Group ProviderGroup(string providerNpi, IEnumerable<string> memberIdentifiers, DateTimeOffset now)
    {
        var attributed = new CodeableConcept(AttributionCodeSystem, "pdexprovidergroup", "PDex Provider Group", null);
        var provider = new ResourceReference { Identifier = new Identifier(NpiSystem, providerNpi), Display = $"Provider NPI {providerNpi}" };
        var group = new Group
        {
            Id = IdFor(providerNpi),
            Meta = new Meta { Profile = [ProviderGroupProfile], LastUpdated = now },
            Identifier = [new Identifier(NpiSystem, providerNpi)],
            Active = true,
            Type = Group.GroupType.Person,
            Actual = true,
            Code = attributed,
            Name = $"Members attributed to provider NPI {providerNpi}",
            // The plan keeps the list; it is kept open because a request from the provider adds to it at any time.
            ManagingEntity = new ResourceReference("Organization/behavioral-health-plan") { Display = "The Behavioral Health Plan" },
            Characteristic =
            [
                new Group.CharacteristicComponent { Code = attributed, Value = provider, Exclude = false },
            ],
            Member = [.. memberIdentifiers.Select(member => new Group.MemberComponent
            {
                Entity = new ResourceReference("Patient/" + member),
            })],
        };
        group.Extension.Add(new Extension(AttributionListStatusUrl,
            new Code("open")));
        return group;
    }

    /// <summary>The Groups a system may see, as a search result.</summary>
    public static Bundle SearchResult(IEnumerable<Group> groups, string serverBase)
    {
        var bundle = new Bundle { Type = Bundle.BundleType.Searchset, Entry = [] };
        foreach (var group in groups)
            bundle.Entry.Add(new Bundle.EntryComponent
            {
                FullUrl = $"{serverBase}/Group/{group.Id}",
                Resource = group,
                Search = new Bundle.SearchComponent { Mode = Bundle.SearchEntryMode.Match },
            });
        bundle.Total = bundle.Entry.Count;
        return bundle;
    }
}
