using System.Net;
using System.Text;

namespace DividendScreener.Infrastructure.Tests;

/// <summary>
/// Serves canned responses keyed by endpoint path (e.g. "profile"), and records every request URI.
/// Unregistered paths return 200 with an empty JSON array.
/// </summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.OrdinalIgnoreCase);

    public List<Uri> Requests { get; } = [];

    public StubHttpHandler Json(string path, string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes[path] = () => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return this;
    }

    public StubHttpHandler Status(string path, HttpStatusCode status, string body = "") =>
        Json(path, body, status);

    public StubHttpHandler Throw(string path, Exception exception)
    {
        _routes[path] = () => throw exception;
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        var path = request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
        var response = _routes.TryGetValue(path, out var route)
            ? route()
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
        return Task.FromResult(response);
    }
}

internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) =>
        new(handler, disposeHandler: false) { BaseAddress = new Uri("https://fmp.test/stable/") };
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
