using System.Net;
using System.Text;
using System.Text.Json;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Supabase;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class GoTrueClientTests
{
    [Test]
    public async Task AdminDeleteUserAsync_SendsServiceRoleDeleteRequest()
    {
        var userId = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        var client = BuildClient(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        await client.AdminDeleteUserAsync(userId);

        captured!.Method.Should().Be(HttpMethod.Delete);
        captured.RequestUri!.PathAndQuery.Should().Be($"/auth/v1/admin/users/{userId}");
        captured.Headers.Authorization!.Scheme.Should().Be("Bearer");
        captured.Headers.Authorization.Parameter.Should().Be("test-service-role");
        captured.Headers.GetValues("apikey").Should().ContainSingle().Which.Should().Be("test-service-role");
    }

    [Test]
    public async Task AdminCreateUserAsync_DuplicateProviderResponse_MapsToStableConflict()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("{\"message\":\"User already registered\",\"code\":\"user_already_exists\"}", Encoding.UTF8, "application/json"),
        });

        var act = () => client.AdminCreateUserAsync("alice@example.test", "Password!1", null, null);

        var error = await act.Should().ThrowAsync<AuthException>();
        error.Which.StatusCode.Should().Be(409);
        error.Which.Code.Should().Be("email_already_registered");
        error.Which.Message.Should().Be("Email is already registered.");
    }

    [Test]
    public async Task AdminCreateUserAsync_GenericProviderFailure_IsSanitized()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"code\":\"provider_internal\",\"message\":\"raw provider detail\"}", Encoding.UTF8, "application/json"),
        });

        var act = () => client.AdminCreateUserAsync("alice@example.test", "Password!1", null, null);

        var error = await act.Should().ThrowAsync<AuthException>();
        error.Which.StatusCode.Should().Be(503);
        error.Which.Code.Should().Be("registration_identity_create_failed");
        error.Which.Message.Should().Be("Registration could not be completed.");
    }

    [Test]
    public async Task AdminCreateUserAsync_SendsServiceRoleHeaders_EmailConfirmation_AndBothMetadataGroups()
    {
        var operationId = Guid.NewGuid();
        string? body = null;
        HttpRequestMessage? captured = null;
        var client = BuildClient(request =>
        {
            captured = request;
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"id\":\"{Guid.NewGuid()}\",\"email\":\"alice@example.test\"}}", Encoding.UTF8, "application/json"),
            };
        });

        await client.AdminCreateUserAsync("alice@example.test", "Password!1",
            new Dictionary<string, object?> { ["username"] = "alice", ["full_name"] = "Alice", [GoTrueMetadata.RegistrationOperationIdKey] = operationId.ToString() },
            new Dictionary<string, object?> { [GoTrueMetadata.RegistrationOperationIdKey] = operationId.ToString() });

        captured!.Headers.Authorization!.Parameter.Should().Be("test-service-role");
        captured.Headers.GetValues("apikey").Should().ContainSingle().Which.Should().Be("test-service-role");
        using var document = JsonDocument.Parse(body!);
        document.RootElement.GetProperty("email_confirm").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("user_metadata").GetProperty("username").GetString().Should().Be("alice");
        document.RootElement.GetProperty("user_metadata").GetProperty(GoTrueMetadata.RegistrationOperationIdKey).GetString().Should().Be(operationId.ToString());
        document.RootElement.GetProperty("app_metadata").GetProperty(GoTrueMetadata.RegistrationOperationIdKey).GetString().Should().Be(operationId.ToString());
    }

    [TestCase("email_exists")]
    [TestCase("user_already_exists")]
    [TestCase("user_already_registered")]
    public async Task AdminCreateUserAsync_KnownDuplicateCode_MapsToStableConflict(string providerCode)
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent($"{{\"code\":\"{providerCode}\"}}", Encoding.UTF8, "application/json"),
        });

        Func<Task> act = () => client.AdminCreateUserAsync("alice@example.test", "Password!1", null, null);
        var error = await act.Should().ThrowAsync<AuthException>();

        error.Which.StatusCode.Should().Be(409);
        error.Which.Code.Should().Be("email_already_registered");
    }

    [Test]
    public async Task AdminCreateUserAsync_CancellationDuringErrorParsing_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        var client = BuildClient(_ =>
        {
            cancellation.Cancel();
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("provider response", Encoding.UTF8),
            };
        });

        var act = () => client.AdminCreateUserAsync("alice@example.test", "Password!1", null, null, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task AdminGetUserByEmailAsync_PreservesMetadataMarkersAndUsesServiceRoleHeaders()
    {
        var operationId = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        var client = BuildClient(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"users\":[{{\"id\":\"{Guid.NewGuid()}\",\"email\":\"alice@example.test\",\"user_metadata\":{{\"registration_operation_id\":\"{operationId}\"}},\"app_metadata\":{{\"registration_operation_id\":\"{operationId}\"}}}}]}}", Encoding.UTF8, "application/json"),
            };
        });

        var user = await client.AdminGetUserByEmailAsync("alice@example.test");

        captured!.Headers.Authorization!.Parameter.Should().Be("test-service-role");
        captured.Headers.GetValues("apikey").Should().ContainSingle().Which.Should().Be("test-service-role");
        GoTrueMetadata.TryGetRegistrationOperationId(user!.UserMetadata, out var userMarker).Should().BeTrue();
        GoTrueMetadata.TryGetRegistrationOperationId(user.AppMetadata, out var appMarker).Should().BeTrue();
        userMarker.Should().Be(operationId);
        appMarker.Should().Be(operationId);
    }

    [Test]
    public async Task AdminGetUserByEmailAsync_ValidEmptyUsers_ReturnsNull()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"users\":[]}", Encoding.UTF8, "application/json"),
        });

        var user = await client.AdminGetUserByEmailAsync("alice@example.test");

        user.Should().BeNull();
    }

    [TestCase("not json")]
    [TestCase("{}")]
    [TestCase("{\"users\":{}}")]
    public async Task AdminGetUserByEmailAsync_InvalidResponse_IsSanitized(string body)
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });

        Func<Task> act = () => client.AdminGetUserByEmailAsync("alice@example.test");

        var error = await act.Should().ThrowAsync<AuthException>();
        error.Which.StatusCode.Should().Be(503);
        error.Which.Code.Should().Be("registration_identity_lookup_failed");
        error.Which.Message.Should().Be("Registration identity status could not be confirmed.");
    }

    [Test]
    public async Task AdminGetUserByEmailAsync_ProviderFailure_IsSanitized()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("raw list failure", Encoding.UTF8),
        });

        Func<Task> act = () => client.AdminGetUserByEmailAsync("alice@example.test");

        var error = await act.Should().ThrowAsync<AuthException>();
        error.Which.StatusCode.Should().Be(503);
        error.Which.Code.Should().Be("registration_identity_lookup_failed");
    }

    [Test]
    public async Task AdminGetUserByEmailAsync_MatchingInvalidUserRecord_IsSanitized()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"users\":[{\"id\":\"not-a-guid\",\"email\":\"alice@example.test\"}]}", Encoding.UTF8, "application/json"),
        });

        Func<Task> act = () => client.AdminGetUserByEmailAsync("alice@example.test");

        var error = await act.Should().ThrowAsync<AuthException>();
        error.Which.Code.Should().Be("registration_identity_lookup_failed");
    }

    [TestCase("{\"users\":[null]}")]
    [TestCase("{\"users\":[42]}")]
    [TestCase("{\"users\":[{\"id\":\"00000000-0000-0000-0000-000000000000\",\"email\":\"other@example.test\"}]}")]
    [TestCase("{\"users\":[{\"email\":\"other@example.test\"}]}")]
    [TestCase("{\"users\":[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"email\":42}]}")]
    [TestCase("{\"users\":[{\"id\":\"11111111-1111-1111-1111-111111111111\"}]}")]
    [TestCase("{\"users\":[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"email\":\"not-an-email\"}]}")]
    public async Task AdminGetUserByEmailAsync_MalformedArrayElement_IsSanitized(string body)
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });

        Func<Task> act = () => client.AdminGetUserByEmailAsync("alice@example.test");

        var error = await act.Should().ThrowAsync<AuthException>();
        error.Which.StatusCode.Should().Be(503);
        error.Which.Code.Should().Be("registration_identity_lookup_failed");
    }

    [Test]
    public async Task AdminGetUserByEmailAsync_WellFormedUnrelatedUser_ReturnsNull()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"users\":[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"email\":\"other@example.test\"}]}", Encoding.UTF8, "application/json"),
        });

        var user = await client.AdminGetUserByEmailAsync("alice@example.test");

        user.Should().BeNull();
    }

    [Test]
    public async Task AdminDeleteUserAsync_ProviderFailure_IsSanitized()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("raw cleanup failure", Encoding.UTF8),
        });

        Func<Task> act = () => client.AdminDeleteUserAsync(Guid.NewGuid());

        var error = await act.Should().ThrowAsync<AuthException>();
        error.Which.StatusCode.Should().Be(503);
        error.Which.Code.Should().Be("registration_identity_cleanup_failed");
        error.Which.Message.Should().Be("Registration cleanup could not be completed.");
    }

    [TestCase("lookup")]
    [TestCase("delete")]
    public async Task AdminLookupAndDelete_CancellationDuringErrorParsing_Propagates(string operation)
    {
        using var cancellation = new CancellationTokenSource();
        var client = BuildClient(_ =>
        {
            cancellation.Cancel();
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("provider response", Encoding.UTF8),
            };
        });
        Func<Task> act = operation == "lookup"
            ? () => client.AdminGetUserByEmailAsync("alice@example.test", cancellation.Token)
            : () => client.AdminDeleteUserAsync(Guid.NewGuid(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task AdminCreateUserAsync_DuplicateMessageOn500_IsSanitizedInsteadOfConflict()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"message\":\"already exists\"}", Encoding.UTF8, "application/json"),
        });

        Func<Task> act = () => client.AdminCreateUserAsync("alice@example.test", "Password!1", null, null);

        var error = await act.Should().ThrowAsync<AuthException>();
        error.Which.StatusCode.Should().Be(503);
        error.Which.Code.Should().Be("registration_identity_create_failed");
    }

    [Test]
    public void TryGetRegistrationOperationId_AcceptsStringAndJsonElement()
    {
        var operationId = Guid.NewGuid();

        GoTrueMetadata.TryGetRegistrationOperationId(new Dictionary<string, object?> { [GoTrueMetadata.RegistrationOperationIdKey] = operationId.ToString() }, out var stringValue).Should().BeTrue();
        using var json = JsonDocument.Parse($"\"{operationId}\"");
        GoTrueMetadata.TryGetRegistrationOperationId(new Dictionary<string, object?> { [GoTrueMetadata.RegistrationOperationIdKey] = json.RootElement.Clone() }, out var jsonValue).Should().BeTrue();

        stringValue.Should().Be(operationId);
        jsonValue.Should().Be(operationId);
    }

    [TestCaseSource(nameof(InvalidMarkerValues))]
    public void TryGetRegistrationOperationId_InvalidValues_FailsClosed(object? value)
    {
        GoTrueMetadata.TryGetRegistrationOperationId(new Dictionary<string, object?> { [GoTrueMetadata.RegistrationOperationIdKey] = value }, out var result).Should().BeFalse();
        result.Should().Be(Guid.Empty);
    }

    [Test]
    public void TryGetRegistrationOperationId_MissingMarker_FailsClosed()
    {
        GoTrueMetadata.TryGetRegistrationOperationId(new Dictionary<string, object?>(), out var result).Should().BeFalse();
        result.Should().Be(Guid.Empty);
    }

    private static IEnumerable<object?[]> InvalidMarkerValues()
    {
        yield return new object?[] { null };
        yield return new object?[] { string.Empty };
        yield return new object?[] { "not-a-guid" };
        yield return new object?[] { Guid.Empty.ToString() };
        yield return new object?[] { 42 };
        yield return new object?[] { true };
        yield return new object?[] { new { marker = "value" } };
        yield return new object?[] { new[] { "value" } };
        yield return new object?[] { JsonDocument.Parse("null").RootElement.Clone() };
        yield return new object?[] { JsonDocument.Parse("42").RootElement.Clone() };
        yield return new object?[] { JsonDocument.Parse("true").RootElement.Clone() };
        yield return new object?[] { JsonDocument.Parse("{}").RootElement.Clone() };
        yield return new object?[] { JsonDocument.Parse("[]").RootElement.Clone() };
    }

    private static GoTrueClient BuildClient(Func<HttpRequestMessage, HttpResponseMessage> response)
    {
        var http = new HttpClient(new StubHandler(response)) { BaseAddress = new Uri("http://localhost/auth/v1/") };
        http.DefaultRequestHeaders.TryAddWithoutValidation("apikey", "test-anon-key");
        return new GoTrueClient(http, Microsoft.Extensions.Options.Options.Create(new SupabaseOptions { ServiceRoleKey = "test-service-role" }));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }
}
