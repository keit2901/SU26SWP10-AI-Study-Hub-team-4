using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AI_Study_Hub_v2.Services;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public class AdminDashboardApiClientTests
{
    [TestCase(HttpStatusCode.Unauthorized, "", "Unauthorized")]
    [TestCase(HttpStatusCode.Forbidden, "not-json", "Forbidden")]
    [TestCase(HttpStatusCode.BadRequest, "{\"code\":\"invalid_scope\",\"message\":\"Folder is invalid.\"}", "Bad Request")]
    public async Task Error_responses_always_throw_typed_exception_with_status(
        HttpStatusCode status,
        string body,
        string reasonPhrase)
    {
        using var http = new HttpClient(new StaticResponseHandler(status, body, reasonPhrase))
        {
            BaseAddress = new Uri("https://example.test/")
        };
        var client = new AdminDashboardApiClient(http);

        var act = () => client.GetModerationAnalyticsAsync("token", null, 1, 10);

        var exception = await act.Should().ThrowAsync<DocumentApiException>();
        exception.Which.StatusCode.Should().Be((int)status);
        if (body.StartsWith('{'))
        {
            exception.Which.Code.Should().Be("invalid_scope");
            exception.Which.Message.Should().Be("Folder is invalid.");
        }
        else
        {
            exception.Which.Code.Should().Be("request_failed");
            exception.Which.Message.Should().Be(reasonPhrase);
        }
    }

    [Test]
    public async Task Valid_json_error_is_used_for_non_moderation_dashboard_requests()
    {
        using var http = new HttpClient(new StaticResponseHandler(
            HttpStatusCode.Forbidden,
            "{\"code\":\"moderation_forbidden\",\"message\":\"Active moderator required.\"}",
            "Forbidden"))
        {
            BaseAddress = new Uri("https://example.test/")
        };
        var client = new AdminDashboardApiClient(http);

        var act = () => client.GetAdminStatsAsync("token");

        var exception = await act.Should().ThrowAsync<DocumentApiException>();
        exception.Which.StatusCode.Should().Be((int)HttpStatusCode.Forbidden);
        exception.Which.Code.Should().Be("moderation_forbidden");
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        private readonly string _reasonPhrase;

        public StaticResponseHandler(HttpStatusCode status, string body, string reasonPhrase)
        {
            _status = status;
            _body = body;
            _reasonPhrase = reasonPhrase;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status)
            {
                ReasonPhrase = _reasonPhrase,
                Content = new StringContent(_body)
            });
    }
}
