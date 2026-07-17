using System.ComponentModel.DataAnnotations;
using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Tests.Dtos;

[TestFixture]
public sealed class RegisterRequestTests
{
    [Test]
    public void Validation_EmptyRegistrationOperationId_ReturnsStableFieldError()
    {
        var request = new RegisterRequest
        {
            Email = "alice@example.test", Username = "alice", FullName = "Alice", Password = "Password!1",
            RegistrationOperationId = Guid.Empty,
        };
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true).Should().BeFalse();

        results.Should().Contain(result => result.MemberNames.Contains(nameof(RegisterRequest.RegistrationOperationId))
            && result.ErrorMessage == "Registration operation id is required.");
    }
}
