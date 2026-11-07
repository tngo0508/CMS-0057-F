using System.Collections.Concurrent;
using Cms0057.PriorAuthorization.Pas;
using Hl7.Fhir.Model;

namespace Cms0057.PriorAuthorization.BulkData;

/// <summary>
/// FHIR Bulk Data Access 1.0 exports, the standard the rule names for the Provider Access and Payer-to-Payer APIs:
/// a kick-off that answers 202 with where to poll, a status request that answers the manifest once complete, and
/// the output files, newline-delimited JSON of one resource type each. What this service holds is small, so an
/// export is complete the moment it starts; the jobs are kept in memory for the life of the process, which is all
/// a single instance needs. The status and file requests take the same access token as the kick-off, and only the
/// system that started an export may read it.
/// </summary>
public sealed class BulkExports
{
    public const string NdjsonMediaType = "application/fhir+ndjson";
    private static readonly string[] OutputFormats = [NdjsonMediaType, "application/ndjson", "ndjson"];

    private readonly ConcurrentDictionary<string, Job> _jobs = new();

    /// <summary>A completed export: its files by resource type, with their line counts.</summary>
    public sealed record Job(string Id, string ClientId, DateTimeOffset TransactionTime, string Request, IReadOnlyDictionary<string, (int Count, string Ndjson)> Files);

    /// <summary>What a kick-off asked for, read from its query string.</summary>
    /// <param name="Types">The resource types wanted; empty for every type the server exports.</param>
    /// <param name="Since">Only resources changed on or after this instant.</param>
    public sealed record Request(IReadOnlyList<string> Types, DateTimeOffset? Since)
    {
        /// <summary>Reads <c>_outputFormat</c>, <c>_type</c> and <c>_since</c>; the types the server can export are named by the caller.</summary>
        /// <exception cref="RequestRejected">A format the server does not write, a type it does not export, or an unreadable _since.</exception>
        public static Request Read(IQueryCollection query, IReadOnlyCollection<string> exportable)
        {
            var format = query["_outputFormat"].ToString();
            if (format.Length > 0 && !OutputFormats.Contains(format, StringComparer.OrdinalIgnoreCase))
                throw new RequestRejected($"_outputFormat {format} is not one this server writes; it writes {NdjsonMediaType}");

            var types = query["_type"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var type in types.Where(t => !exportable.Contains(t)))
                throw new RequestRejected($"This server does not export {type}; it exports {string.Join(", ", exportable)}");

            DateTimeOffset? since = null;
            var sinceParameter = query["_since"].ToString();
            if (sinceParameter.Length > 0)
            {
                if (!DateTimeOffset.TryParse(sinceParameter, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                    throw new RequestRejected($"_since must be an instant; got: {sinceParameter}");
                since = parsed;
            }
            return new Request(types, since);
        }

        public bool Wants(string type) => Types.Count == 0 || Types.Contains(type);
    }

    /// <summary>Starts (and, the data being small, completes) an export of the given resources for the client.</summary>
    public Job Start(string clientId, string request, DateTimeOffset now, IEnumerable<Resource> resources)
    {
        var files = resources
            .GroupBy(r => r.TypeName)
            .ToDictionary(g => g.Key, g => (g.Count(), string.Concat(g.Select(r => FhirJson.Write(r) + "\n"))));
        var job = new Job(Guid.NewGuid().ToString("N"), clientId, now, request, files);
        _jobs[job.Id] = job;
        return job;
    }

    /// <summary>The export, when it exists and the client is the one that started it.</summary>
    public Job? Find(string id, string clientId) =>
        _jobs.TryGetValue(id, out var job) && job.ClientId == clientId ? job : null;

    /// <summary>Deletes the export, as a client does when it has what it needs. False when there is none to delete.</summary>
    public bool Delete(string id, string clientId) =>
        Find(id, clientId) is not null && _jobs.TryRemove(id, out _);

    /// <summary>The Bulk Data manifest: when the data was as of, what was asked, and the files.</summary>
    public static object Manifest(Job job, string serverBase) => new
    {
        transactionTime = job.TransactionTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffK"),
        request = job.Request,
        requiresAccessToken = true,
        output = job.Files.Select(f => new { type = f.Key, url = FileUrl(job, f.Key, serverBase), count = f.Value.Count }),
        error = Array.Empty<object>(),
    };

    public static string StatusUrl(Job job, string serverBase) => $"{serverBase}/$export-status/{job.Id}";

    private static string FileUrl(Job job, string type, string serverBase) => $"{serverBase}/$export-files/{job.Id}/{type}.ndjson";
}
