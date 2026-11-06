using System.Security.Claims;
using Cms0057.PriorAuthorization.Pas;
using Hl7.Fhir.Model;
using Task = System.Threading.Tasks.Task;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Cms0057.PriorAuthorization.Authentication;

/// <summary>
/// How the Prior Authorization API checks the bearer token on each call: it must be one this service issued
/// (signed with <see cref="AccessTokens.SigningKey"/>), still good by the service's clock, and carry the scope the
/// operation needs. A call on <c>/fhir</c> that fails is answered with a FHIR OperationOutcome.
/// </summary>
public static class BearerTokens
{
    public static IServiceCollection AddBearerTokens(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<TimeProvider>((bearer, clock) =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    IssuerSigningKey = AccessTokens.SigningKey,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha384],
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    RequireExpirationTime = true,
                    LifetimeValidator = (notBefore, expires, _, _) =>
                        expires is { } exp
                        && exp > clock.GetUtcNow().UtcDateTime
                        && (notBefore is null || notBefore <= clock.GetUtcNow().UtcDateTime),
                };
                bearer.Events = new JwtBearerEvents { OnChallenge = Challenge, OnForbidden = Forbid };
            });

        services.AddAuthorization(authorization =>
        {
            foreach (var scope in SmartConfiguration.ScopesSupported)
                authorization.AddPolicy(scope, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireAssertion(context => Scopes.Cover(Scopes.Parse(context.User.FindFirstValue("scope")), scope)));
        });
        return services;
    }

    /// <summary>No token, or one this service cannot accept: 401 with the reason, as an OperationOutcome on /fhir.</summary>
    private static Task Challenge(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        var expired = context.AuthenticateFailure is SecurityTokenExpiredException or SecurityTokenInvalidLifetimeException;
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = context.AuthenticateFailure is null
            ? "Bearer"
            : $"Bearer error=\"invalid_token\", error_description=\"{(expired ? "The access token has expired" : "The access token is not valid")}\"";
        return WriteOutcome(context.HttpContext, expired
            ? Outcome(OperationOutcome.IssueType.Expired, "The access token has expired; obtain a new one from the token endpoint")
            : Outcome(OperationOutcome.IssueType.Login, "A bearer token from this service's token endpoint is required"));
    }

    /// <summary>A good token without the scope the operation needs: 403.</summary>
    private static Task Forbid(ForbiddenContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return WriteOutcome(context.HttpContext,
            Outcome(OperationOutcome.IssueType.Forbidden, "The access token does not carry the scope this operation needs"));
    }

    private static async Task WriteOutcome(HttpContext http, OperationOutcome outcome)
    {
        if (!http.Request.Path.StartsWithSegments("/fhir")) return;
        http.Response.ContentType = "application/fhir+json";
        await http.Response.WriteAsync(FhirJson.Write(outcome));
    }

    private static OperationOutcome Outcome(OperationOutcome.IssueType code, string diagnostics) => new()
    {
        Issue =
        [
            new OperationOutcome.IssueComponent
            {
                Severity = OperationOutcome.IssueSeverity.Error,
                Code = code,
                Diagnostics = diagnostics,
            },
        ],
    };
}
