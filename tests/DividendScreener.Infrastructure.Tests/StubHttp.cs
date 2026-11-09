using System.Net;
using System.Text;

namespace DividendScreener.Infrastructure.Tests;

/// <summary>
/// Serves canned responses keyed by endpoint path segments ("profile", "equity/profile"), matched against the
/// request path, and records every request. Unregistered paths return 200 with an empty JSON array.
/// </summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.OrdinalIgnoreCase);

    public List<Uri> Requests { get; } = [];

    public List<string?> AuthorizationHeaders { get; } = [];

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
        AuthorizationHeaders.Add(request.Headers.Authorization?.ToString());

        var path = request.RequestUri!.AbsolutePath;
        var route = _routes.FirstOrDefault(r =>
            path.EndsWith("/" + r.Key, StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/" + r.Key + "/", StringComparison.OrdinalIgnoreCase)).Value;
        var response = route is not null
            ? route()
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
        return Task.FromResult(response);
    }
}

internal sealed class StubHttpClientFactory(HttpMessageHandler handler, string baseAddress = "https://fmp.test/stable/")
    : IHttpClientFactory
{
    public HttpClient CreateClient(string name) =>
        new(handler, disposeHandler: false) { BaseAddress = new Uri(baseAddress) };
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
