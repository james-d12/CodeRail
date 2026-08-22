using System.Net;

namespace CodeRail.Tooling.Tests.Executors;

/// <summary>A scripted <see cref="HttpMessageHandler"/> for exercising HTTP-calling executors
/// (currently only <c>SonarExecutor</c>) without a real server - the HTTP-call analogue of
/// <see cref="StubProcessRunner"/>.</summary>
internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
{
    public static FakeHttpMessageHandler Json(Func<HttpRequestMessage, string> handler) =>
        new(request => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(handler(request)) });

    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(handler(request));
    }
}
