using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class SystemConfigSeederTests
{
    [Test]
    public async Task SeedAsync_EmptyDatabase_AddsDefaultsIncludingDisabledRegistration()
    {
        using var db = TestDb.CreateInMemory();

        await SystemConfigSeeder.SeedAsync(db, NullLogger.Instance);

        (await db.SystemConfigs.CountAsync()).Should().BeGreaterThan(1);
        (await db.SystemConfigs.SingleAsync(c => c.Key == "auth.allow_self_registration")).Value.Should().Be("false");
    }

    [Test]
    public async Task SeedAsync_PartialDatabase_PreservesExistingEnabledPolicy_AndAddsMissingDefaults()
    {
        using var db = TestDb.CreateInMemory();
        db.SystemConfigs.Add(new SystemConfig
        {
            Key = "auth.allow_self_registration", Value = "true", DefaultValue = "false", Category = "Security",
            DisplayName = "Allow self registration", ConfigType = "Boolean", IsCritical = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        await SystemConfigSeeder.SeedAsync(db, NullLogger.Instance);

        (await db.SystemConfigs.SingleAsync(c => c.Key == "auth.allow_self_registration")).Value.Should().Be("true");
        (await db.SystemConfigs.AnyAsync(c => c.Key == "ai.chat_model")).Should().BeTrue();
    }

    [Test]
    public async Task SeedAsync_PartialDatabaseWithoutRegistrationPolicy_AddsDisabledPolicy_AndPreservesExistingConfig()
    {
        using var db = TestDb.CreateInMemory();
        db.SystemConfigs.Add(new SystemConfig
        {
            Key = "ai.chat_model", Value = "custom-model", DefaultValue = "gpt-4o-mini", Category = "Model",
            DisplayName = "Chat model", ConfigType = "Text", IsCritical = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        await SystemConfigSeeder.SeedAsync(db, NullLogger.Instance);

        (await db.SystemConfigs.SingleAsync(c => c.Key == "auth.allow_self_registration")).Value.Should().Be("false");
        (await db.SystemConfigs.SingleAsync(c => c.Key == "ai.chat_model")).Value.Should().Be("custom-model");
    }
}
