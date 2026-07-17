using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class SelfRegistrationPolicyTests
{
    [TestCase("true")]
    [TestCase("TRUE")]
    public async Task EnsureAllowedAsync_TrueValue_AllowsRegistration(string value)
    {
        using var db = TestDb.CreateInMemory();
        await AddPolicyAsync(db, value);

        await new SelfRegistrationPolicy(db, NullLogger<SelfRegistrationPolicy>.Instance).EnsureAllowedAsync();
    }

    [Test]
    public async Task EnsureAllowedAsync_FalseValue_ThrowsDisabled403()
    {
        using var db = TestDb.CreateInMemory();
        await AddPolicyAsync(db, "false");

        var act = () => new SelfRegistrationPolicy(db, NullLogger<SelfRegistrationPolicy>.Instance).EnsureAllowedAsync();

        var error = await act.Should().ThrowAsync<AuthException>();
        error.Which.StatusCode.Should().Be(403);
        error.Which.Code.Should().Be("self_registration_disabled");
    }

    [TestCase(null)]
    [TestCase("not-a-boolean")]
    public async Task EnsureAllowedAsync_MissingOrMalformedValue_ThrowsUnavailable503(string? value)
    {
        using var db = TestDb.CreateInMemory();
        if (value is not null)
        {
            await AddPolicyAsync(db, value);
        }

        var act = () => new SelfRegistrationPolicy(db, NullLogger<SelfRegistrationPolicy>.Instance).EnsureAllowedAsync();

        var error = await act.Should().ThrowAsync<AuthException>();
        error.Which.StatusCode.Should().Be(503);
        error.Which.Code.Should().Be("registration_policy_unavailable");
    }

    [Test]
    public async Task EnsureAllowedAsync_CancelledToken_PropagatesCancellation()
    {
        using var db = TestDb.CreateInMemory();
        await AddPolicyAsync(db, "true");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => new SelfRegistrationPolicy(db, NullLogger<SelfRegistrationPolicy>.Instance).EnsureAllowedAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static async Task AddPolicyAsync(Data.AppDbContext db, string value)
    {
        db.SystemConfigs.Add(new SystemConfig
        {
            Key = "auth.allow_self_registration", Value = value, DefaultValue = "false", Category = "Security",
            DisplayName = "Allow self registration", ConfigType = "Boolean", IsCritical = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
