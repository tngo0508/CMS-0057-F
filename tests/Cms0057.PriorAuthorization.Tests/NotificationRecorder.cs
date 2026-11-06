using System.Net;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// Stands in for a provider system's notification endpoint, at the service's outbound HTTP boundary. Records what
/// the service POSTs and answers with whatever status a test sets.
/// </summary>
public sealed class NotificationRecorder : HttpMessageHandler
{
    public sealed record Delivery(Uri Url, string ContentType, string Body);

    public List<Delivery> Received { get; } = [];

    public HttpStatusCode RespondWith { get; set; } = HttpStatusCode.OK;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Received.Add(new Delivery(
            request.RequestUri!,
            request.Content?.Headers.ContentType?.MediaType ?? "",
            request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
        return new HttpResponseMessage(RespondWith);
    }
}
