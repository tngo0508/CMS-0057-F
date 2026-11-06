using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Firely.Fhir.Packages;
using Firely.Fhir.Validation;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Specification.Terminology;
using Microsoft.Extensions.Options;

namespace Cms0057.PriorAuthorization.Pas;

/// <summary>
/// Validation against the Da Vinci PAS 2.2.1 profiles, done by the Firely validator over the published FHIR
/// packages. The packages are fetched from the configured package server the first time the service starts
/// and kept as .tgz files in the package folder, where the Firely SDK also unpacks them.
/// </summary>
public sealed class PasProfiles
{
    private const string PasBase = "http://hl7.org/fhir/us/davinci-pas/StructureDefinition/";
    public const string RequestBundle = PasBase + "profile-pas-request-bundle";
    public const string InquiryRequestBundle = PasBase + "profile-pas-inquiry-request-bundle";

    /// <summary>The package the profiles come from.</summary>
    public const string Package = "hl7.fhir.us.davinci-pas@2.2.1";

    private const string CrossVersionPackage = "hl7.fhir.uv.xver-r5.r4@0.1.0";

    /// <summary>
    /// The PAS package and the dependencies its manifest names, in the versions it names (US Core 3.1.1 and
    /// 6.1.0 as the packages themselves rather than the manifest's alias packages, which only point at them).
    /// Deliberately not the transitive closure: US Core 6 and 7 depend on VSAC value-set packages that no public
    /// package server carries, and the profiles validated here do not need them.
    /// </summary>
    public static readonly string[] Packages =
    [
        Package,
        "hl7.fhir.r4.core@4.0.1",
        "hl7.terminology.r4@7.1.0",
        "hl7.fhir.uv.extensions.r4@5.2.0",
        "hl7.fhir.us.core@3.1.1",
        "hl7.fhir.us.core@6.1.0",
        "hl7.fhir.us.core@7.0.0",
        "hl7.fhir.uv.sdc@4.0.0",
        "hl7.fhir.uv.xver-r5.r4@0.1.0",
        "hl7.fhir.us.davinci-crd@2.2.1",
        "hl7.fhir.us.davinci-hrex@1.2.0",
        "hl7.fhir.us.davinci-cdex@2.1.0",
        "hl7.fhir.uv.subscriptions-backport.r4@1.1.0",
        "hl7.fhir.uv.tools.r4@1.1.2",
    ];

    // Compiling the profiles is the expensive part, and the test suite starts the service many times over, so
    // one validator per process serves every host that uses the same package source.
    private static readonly ConcurrentDictionary<(string Server, string Folder), Lazy<Validator>> Validators = new();

    // The Firely validator and the resolvers under it are not thread-safe: one validation at a time.
    private static readonly Lock Gate = new();

    private readonly bool _enabled;
    private readonly Lazy<Validator> _validator;
    private readonly ILogger<PasProfiles> _log;

    public PasProfiles(IOptions<PriorAuthorizationOptions> options, IHostEnvironment environment, ILogger<PasProfiles> log)
    {
        var settings = options.Value;
        _enabled = settings.ValidateProfiles;
        _log = log;
        var folder = Path.GetFullPath(settings.FhirPackageFolder ?? "fhir-packages", environment.ContentRootPath);
        _validator = Validators.GetOrAdd(
            (settings.FhirPackageServer, folder),
            source => new Lazy<Validator>(() => Build(source.Server, source.Folder)));
    }

    /// <summary>
    /// Fetches the packages and compiles the profiles, which the first request would otherwise wait seconds for.
    /// The validator compiles a profile when it first meets an element it applies to, so the warm-up Bundle has
    /// the shape of a request: a Claim pointing at a Patient, a Coverage and two Organizations.
    /// </summary>
    public void WarmUp()
    {
        if (!_enabled) return;
        var watch = Stopwatch.StartNew();
        lock (Gate)
        {
            _validator.Value.Validate(WarmUpBundle(), RequestBundle);
            _validator.Value.Validate(WarmUpBundle(), InquiryRequestBundle);
        }
        _log.LogInformation("PAS profiles ready in {Elapsed} ms", watch.ElapsedMilliseconds);
    }

    private static Bundle WarmUpBundle()
    {
        var claim = new Claim
        {
            Patient = new ResourceReference("Patient/member"),
            Insurer = new ResourceReference("Organization/plan"),
            Provider = new ResourceReference("Organization/provider"),
            Insurance = [new Claim.InsuranceComponent { Coverage = new ResourceReference("Coverage/coverage") }],
            Item = [new Claim.ItemComponent()],
        };
        var bundle = new Bundle { Type = Bundle.BundleType.Collection };
        foreach (var resource in new Resource[] { claim, new Patient { Id = "member" }, new Coverage { Id = "coverage" }, new Organization { Id = "plan" }, new Organization { Id = "provider" } })
            bundle.Entry.Add(new Bundle.EntryComponent { FullUrl = $"urn:example:{resource.TypeName}/{resource.Id ?? "claim"}", Resource = resource });
        return bundle;
    }

    /// <summary>Checks the Bundle against one of the PAS profiles. Warnings are logged; errors reject.</summary>
    /// <exception cref="ProfileViolation">The Bundle breaks the profile; the exception carries the validator's findings.</exception>
    public void Require(Bundle bundle, string profile)
    {
        if (!_enabled) return;

        var watch = Stopwatch.StartNew();
        OperationOutcome outcome;
        lock (Gate) outcome = _validator.Value.Validate(bundle, profile);
        _log.LogInformation("Validated against {Profile} in {Elapsed} ms: {Errors} error(s), {Warnings} warning(s)",
            profile, watch.ElapsedMilliseconds, outcome.Errors + outcome.Fatals, outcome.Warnings);
        foreach (var warning in outcome.Issue.Where(i => i.Severity == OperationOutcome.IssueSeverity.Warning))
            _log.LogDebug("Validation warning at {Location}: {Message}", string.Join(", ", warning.Expression), warning.Details?.Text);

        var errors = outcome.Issue
            .Where(i => i.Severity is OperationOutcome.IssueSeverity.Error or OperationOutcome.IssueSeverity.Fatal)
            .Select(AsRejectionIssue)
            .ToList();
        if (errors.Count > 0) throw new ProfileViolation(profile, errors);
    }

    private Validator Build(string packageServer, string packageFolder)
    {
        var files = Packages.ToDictionary(package => package, package => Fetch(packageServer, packageFolder, package));
        // The packages are unpacked into caches of their own under the package folder, so that what the validator
        // sees is exactly the packages listed above, whatever else the machine's FHIR package cache holds. The
        // cross-version package gets a cache to itself and comes last: it carries R5's own definitions of
        // Coverage, Patient and the rest under their plain canonicals, which would otherwise shadow R4's, and
        // it is only wanted for what nothing else defines, the R5 extensions PAS borrows.
        var current = new FhirPackageSource(ModelInfo.ModelInspector,
            [.. files.Where(f => f.Key != CrossVersionPackage).Select(f => f.Value)], Path.Combine(packageFolder, "cache"));
        var crossVersion = new FhirPackageSource(ModelInfo.ModelInspector,
            [files[CrossVersionPackage]], Path.Combine(packageFolder, "cache-cross-version"));
        var resolver = new CachedResolver(new DiscriminatorPathsFirelyCanRead(new MultiResolver(current, crossVersion)));
        return new Validator(resolver, new LocalTerminologyService(resolver));
    }

    /// <summary>The package's .tgz in the package folder, fetched from the package server if it is not there yet.</summary>
    private string Fetch(string packageServer, string packageFolder, string package)
    {
        var file = Path.Combine(packageFolder, package.Replace('@', '-') + ".tgz");
        if (File.Exists(file)) return file;

        _log.LogInformation("Fetching FHIR package {Package} from {Server} into {Folder}", package, packageServer, packageFolder);
        Directory.CreateDirectory(packageFolder);
        using var client = PackageClient.Create(packageServer);
        var bytes = client.GetPackage(PackageReference.Parse(package)).GetAwaiter().GetResult();
        var incoming = $"{file}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(incoming, bytes);
        File.Move(incoming, file, overwrite: true);
        return file;
    }

    /// <summary>
    /// The validator's finding as the sender reads it: its message in diagnostics (the validator puts an element
    /// trace there and the message in details), the place in expression, and its coded details as they came.
    /// </summary>
    private static OperationOutcome.IssueComponent AsRejectionIssue(OperationOutcome.IssueComponent found)
    {
        var issue = (OperationOutcome.IssueComponent)found.DeepCopy();
        issue.Diagnostics = found.Details?.Text ?? found.Diagnostics;
        return issue;
    }
}

/// <summary>
/// Hands out the packages' StructureDefinitions with one change: PAS slices Claim.careTeam on the value
/// discriminator <c>extension('…careTeamClaimScope').value.ofType(boolean)</c>, and the Firely validator (3.6.0)
/// cannot compile a discriminator path that ends in <c>ofType()</c>, which left every Claim profile unusable.
/// Without the <c>ofType()</c> the path names the same element, whose fixed boolean the validator then reads.
/// </summary>
internal sealed partial class DiscriminatorPathsFirelyCanRead(IAsyncResourceResolver inner) : IAsyncResourceResolver
{
    [GeneratedRegex(@"^(extension\('[^']+'\)\.value)\.ofType\(\w+\)$")]
    private static partial Regex ExtensionValueOfType();

    public async Task<ResolverResult> TryResolveByCanonicalUriAsync(string uri) => Patched(await inner.TryResolveByCanonicalUriAsync(uri));

    public async Task<ResolverResult> TryResolveByUriAsync(string uri) => Patched(await inner.TryResolveByUriAsync(uri));

    public async Task<Resource?> ResolveByCanonicalUriAsync(string uri) => (await TryResolveByCanonicalUriAsync(uri)).Value;

    public async Task<Resource?> ResolveByUriAsync(string uri) => (await TryResolveByUriAsync(uri)).Value;

    private static ResolverResult Patched(ResolverResult result)
    {
        if (result.Value is not StructureDefinition definition) return result;
        var elements = (definition.Snapshot?.Element ?? []).Concat(definition.Differential?.Element ?? []);
        foreach (var discriminator in elements.SelectMany(e => e.Slicing?.Discriminator ?? []))
        {
            if (discriminator.Type != ElementDefinition.DiscriminatorType.Value || discriminator.Path is null) continue;
            var match = ExtensionValueOfType().Match(discriminator.Path);
            if (match.Success) discriminator.Path = match.Groups[1].Value;
        }
        return result;
    }
}

/// <summary>A request Bundle that does not conform to a PAS profile, with the validator's findings.</summary>
public sealed class ProfileViolation(string profile, IReadOnlyList<OperationOutcome.IssueComponent> issues)
    : RequestRejected($"The Bundle does not conform to {profile}")
{
    public IReadOnlyList<OperationOutcome.IssueComponent> Issues { get; } = issues;
}
