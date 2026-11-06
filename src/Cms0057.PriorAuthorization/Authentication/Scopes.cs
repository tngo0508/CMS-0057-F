namespace Cms0057.PriorAuthorization.Authentication;

/// <summary>
/// SMART App Launch system scopes, <c>system/[Resource].[permissions]</c>, in both spellings: version 1 writes the
/// permissions as <c>read</c>, <c>write</c> or <c>*</c>; version 2 (which br-provider uses) as letters from
/// <c>cruds</c>: create, read, update, delete, search. The two are compared on the letters, <c>read</c> being
/// <c>rs</c> and <c>write</c> <c>cud</c>. The resource may be <c>*</c> for any.
/// </summary>
public static class Scopes
{
    private const string Prefix = "system/";
    private const string Letters = "cruds";

    public static IReadOnlyList<string> Parse(string? scope) =>
        (scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Whether the <paramref name="granted"/> scopes, taken together, cover what an operation <paramref name="required"/>.
    /// The operations here that need <c>write</c> create and update (a request, its updates and cancellations, a
    /// subscription) and never delete, so <c>c</c> and <c>u</c> satisfy them: br-provider asks for <c>Claim.crus</c>.
    /// </summary>
    public static bool Cover(IEnumerable<string> granted, string required)
    {
        if (Read(required) is not { } need) return false;
        var held = Held(granted, need.Resource);
        var needed = need.Spelling == "write" ? "cu" : need.Permissions;
        return needed.All(held.Contains);
    }

    /// <summary>
    /// The part of an <paramref name="asked"/> scope that the <paramref name="registered"/> scopes allow, spelled as
    /// the client spelled it when all of it is allowed and as letters when only some is; nothing when none is. SMART
    /// lets a server grant fewer scopes than were asked for. A scope asked for every resource (<c>system/*.…</c>) is
    /// answered resource by resource.
    /// </summary>
    public static IEnumerable<string> Granted(IReadOnlyList<string> registered, string asked)
    {
        if (Read(asked) is not { } want) yield break;
        var resources = want.Resource == "*"
            ? registered.Select(Read).OfType<Scope>().Select(s => s.Resource).Distinct()
            : [want.Resource];
        foreach (var resource in resources)
        {
            var held = Held(registered, resource);
            var permissions = string.Concat(want.Permissions.Where(held.Contains));
            if (permissions.Length == 0) continue;
            yield return Prefix + resource + "." + (permissions == want.Permissions ? want.Spelling : permissions);
        }
    }

    /// <summary>Every permission letter the scopes grant on the resource, through its own name or <c>*</c>.</summary>
    private static HashSet<char> Held(IEnumerable<string> scopes, string resource)
    {
        var held = new HashSet<char>();
        foreach (var scope in scopes.Select(Read).OfType<Scope>())
            if (scope.Resource == "*" || scope.Resource == resource)
                held.UnionWith(scope.Permissions);
        return held;
    }

    /// <summary>A scope taken apart; Permissions are the letters in <c>cruds</c> order, Spelling what was written.</summary>
    private sealed record Scope(string Resource, string Permissions, string Spelling);

    private static Scope? Read(string scope)
    {
        if (!scope.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var dot = scope.LastIndexOf('.');
        if (dot < Prefix.Length + 1 || dot == scope.Length - 1) return null;
        var resource = scope[Prefix.Length..dot];
        var spelling = scope[(dot + 1)..];
        var permissions = spelling switch
        {
            "read" => "rs",
            "write" => "cud",
            "*" => Letters,
            _ when spelling.All(Letters.Contains) && spelling.Distinct().Count() == spelling.Length
                => string.Concat(Letters.Where(spelling.Contains)),
            _ => null,
        };
        return permissions is null ? null : new Scope(resource, permissions, spelling);
    }
}
