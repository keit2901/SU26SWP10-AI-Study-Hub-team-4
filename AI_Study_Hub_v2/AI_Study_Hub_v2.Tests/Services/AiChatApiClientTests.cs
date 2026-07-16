using System.Net;
using System.Text;
using AI_Study_Hub_v2.Services;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class AiChatApiClientTests
{
    [Test]
    public async Task SessionEndpoints_SerializeAndForwardExactNullableFolderScope()
    {
        var handler = new SessionEndpointHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        var sut = new AiChatApiClient(http);
        var sessionId = Guid.NewGuid();
        var folderId = Guid.NewGuid();

        await sut.ListSessionsAsync("access-token", null);
        await sut.GetSessionMessagesAsync("access-token", sessionId, null);
        await sut.DeleteSessionAsync("access-token", sessionId, null);
        await sut.ListSessionsAsync("access-token", folderId);
        await sut.GetSessionMessagesAsync("access-token", sessionId, folderId);
        await sut.DeleteSessionAsync("access-token", sessionId, folderId);

        handler.Requests.Select(request => request.PathAndQuery).Should().Equal(
            "/api/ai/chat/sessions",
            $"/api/ai/chat/sessions/{sessionId}",
            $"/api/ai/chat/sessions/{sessionId}",
            $"/api/ai/chat/sessions?folderId={folderId}",
            $"/api/ai/chat/sessions/{sessionId}?folderId={folderId}",
            $"/api/ai/chat/sessions/{sessionId}?folderId={folderId}");
        handler.Requests.Select(request => request.Method).Should().Equal(
            HttpMethod.Get,
            HttpMethod.Get,
            HttpMethod.Delete,
            HttpMethod.Get,
            HttpMethod.Get,
            HttpMethod.Delete);
    }

    private sealed class SessionEndpointHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string PathAndQuery)> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri?.PathAndQuery ?? string.Empty));
            return Task.FromResult(request.Method == HttpMethod.Delete
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json"),
                });
        }
    }
}
