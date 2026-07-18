using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class RegistrationAttemptStateTests
{
    [Test]
    public void PrepareForDispatch_IdenticalIdentityPayload_RetainsOperationId_WhenPasswordChanges()
    {
        var state = new RegistrationAttemptState();
        var request = Request(" Alice@example.test ", "alice", " Alice ", "Password!1");

        var first = state.PrepareForDispatch(request);
        request.Password = "DifferentPassword!2";
        var retry = state.PrepareForDispatch(request);

        retry.Should().Be(first);
        request.RegistrationOperationId.Should().Be(first);
    }

    [TestCase("email")]
    [TestCase("username")]
    [TestCase("fullName")]
    public void PrepareForDispatch_IdentityPayloadChanges_RotatesOperationId(string changedField)
    {
        var state = new RegistrationAttemptState();
        var request = Request("alice@example.test", "alice", "Alice", "Password!1");
        var first = state.PrepareForDispatch(request);
        switch (changedField)
        {
            case "email": request.Email = "other@example.test"; break;
            case "username": request.Username = "other"; break;
            default: request.FullName = "Other"; break;
        }

        state.PrepareForDispatch(request).Should().NotBe(first);
    }

    private static RegisterRequest Request(string email, string username, string fullName, string password) =>
        new() { Email = email, Username = username, FullName = fullName, Password = password, RegistrationOperationId = Guid.NewGuid() };
}
