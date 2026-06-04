using System.Net;

namespace AiUsageHud.Core.Tests.Support;

/// <summary>
/// Minimal stub for <see cref="HttpMessageHandler"/> — maps a request path suffix
/// to a canned (status, body). Replaces the Rust <c>mockito</c> server in tests.
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly List<(string PathSuffix, HttpStatusCode Status, string Body)> _routes = [];
    public List<HttpRequestMessage> Requests { get; } = [];

    public FakeHttpHandler On(string pathSuffix, HttpStatusCode status, string body)
    {
        _routes.Add((pathSuffix, status, body));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        var path = request.RequestUri!.AbsolutePath;
        foreach (var (suffix, status, body) in _routes)
        {
            if (path.EndsWith(suffix, StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
                });
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($"no route for {path}"),
        });
    }

    public HttpClient Client() => new(this) { BaseAddress = new Uri("https://fake.local") };
}
