using System.Net;
using System.Text;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
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

    [Test]
    public async Task SaveQuizAsync_SendsOnlyProgressFields_AndReturnsServerQuiz()
    {
        var quizId = Guid.NewGuid();
        var handler = new SessionEndpointHandler("""
            {"id":"QUIZ_ID","title":"Quiz","status":1,"currentQuestionIndex":0,"totalQuestions":1,"questions":[],"answers":{"0":"A"},"submitted":{"0":true},"score":null,"createdAt":"2026-01-01T00:00:00+00:00"}
            """.Replace("QUIZ_ID", quizId.ToString(), StringComparison.Ordinal));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        var sut = new AiChatApiClient(http);

        var result = await sut.SaveQuizAsync("access-token", quizId,
            new SaveQuizRequest(0, new Dictionary<int, string?> { [0] = "A" }));

        result.Id.Should().Be(quizId);
        handler.Requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Patch);
        handler.Requests.Single().PathAndQuery.Should().Be($"/api/quiz/{quizId}/save");
        handler.RequestBodies.Single().Should().Contain("currentQuestionIndex").And.Contain("answers");
        handler.RequestBodies.Single().Should().NotContain("submitted").And.NotContain("score").And.NotContain("status");
    }

    private sealed class SessionEndpointHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string PathAndQuery)> Requests { get; } = new();
        public List<string> RequestBodies { get; } = new();

        private readonly string? _responseBody;

        public SessionEndpointHandler(string? responseBody = null)
        {
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri?.PathAndQuery ?? string.Empty));
            RequestBodies.Add(request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? string.Empty);
            return Task.FromResult(request.Method == HttpMethod.Delete
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_responseBody ?? "[]", Encoding.UTF8, "application/json"),
                });
        }
    }
}
