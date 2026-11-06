namespace Cms0057.PriorAuthorization.Authentication;

/// <summary>
/// SMART App Launch 1.0 system scopes, <c>system/[Resource].[read|write|*]</c>, where <c>*</c> may stand for
/// the resource or the permission.
/// </summary>
public static class Scopes
{
    public static IReadOnlyList<string> Parse(string? scope) =>
        (scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Whether any of the <paramref name="granted"/> scopes covers the <paramref name="required"/> one.</summary>
    public static bool Cover(IEnumerable<string> granted, string required) => granted.Any(g => Covers(g, required));

    private static bool Covers(string granted, string required)
    {
        var (grantedResource, grantedPermission) = Split(granted);
        var (requiredResource, requiredPermission) = Split(required);
        return grantedResource is not null && requiredResource is not null
               && (grantedResource == "*" || grantedResource == requiredResource)
               && (grantedPermission == "*" || grantedPermission == requiredPermission);
    }

    private static (string? Resource, string? Permission) Split(string scope)
    {
        const string prefix = "system/";
        if (!scope.StartsWith(prefix, StringComparison.Ordinal)) return (null, null);
        var dot = scope.LastIndexOf('.');
        if (dot < prefix.Length + 1 || dot == scope.Length - 1) return (null, null);
        return (scope[prefix.Length..dot], scope[(dot + 1)..]);
    }
}
