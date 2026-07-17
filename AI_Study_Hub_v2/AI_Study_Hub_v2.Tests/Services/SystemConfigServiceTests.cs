using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Tests.Support;
using FluentAssertions;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class SystemConfigServiceTests
{
    [Test]
    public async Task GetAllAsync_ReturnsAllConfigs_OrderedByCategoryThenKey()
    {
        await using var db = TestDb.CreateInMemory(seedRoles: false);
        SeedConfigs(db);

        var sut = new SystemConfigService(db, new AuditLogService(db));

        var result = await sut.GetAllAsync();

        result.Should().HaveCount(4);
        result[0].Category.Should().Be("Generation");
        result[0].Key.Should().Be("generation.temperature");
        result[1].Category.Should().Be("Model");
        result[1].Key.Should().Be("ai.chat_model");
        result[2].Category.Should().Be("Model");
        result[2].Key.Should().Be("ai.embedding_model");
        result[3].Category.Should().Be("Retrieval");
        result[3].Key.Should().Be("rag.chunk_size");
    }

    [Test]
    public async Task GetAllAsync_ReturnsMappedFields()
    {
        await using var db = TestDb.CreateInMemory(seedRoles: false);
        SeedConfigs(db);

        var sut = new SystemConfigService(db, new AuditLogService(db));

        var result = await sut.GetAllAsync();

        var config = result.Should().ContainSingle(c => c.Key == "ai.chat_model").Subject;
        config.Value.Should().Be("gpt-4o-mini");
        config.DefaultValue.Should().Be("gpt-4o-mini");
        config.Category.Should().Be("Model");
        config.DisplayName.Should().Be("Chat model");
        config.ConfigType.Should().Be("Text");
        config.IsCritical.Should().BeTrue();
        config.UpdatedAt.Should().BeNull();
        config.UpdatedBy.Should().BeNull();
    }

    [Test]
    public async Task UpdateValueAsync_HappyPath_UpdatesValueAndReturnsDto()
    {
        await using var db = TestDb.CreateInMemory(seedRoles: false);
        SeedConfigs(db);

        var sut = new SystemConfigService(db, new AuditLogService(db));

        var result = await sut.UpdateValueAsync("ai.chat_model", "claude-3-5-sonnet", "admin@test.edu");

        result.Key.Should().Be("ai.chat_model");
        result.Value.Should().Be("claude-3-5-sonnet");
        result.UpdatedBy.Should().Be("admin@test.edu");
        result.UpdatedAt.Should().NotBeNull();

        var persisted = await db.SystemConfigs.FindAsync("ai.chat_model");
        persisted!.Value.Should().Be("claude-3-5-sonnet");
        persisted.UpdatedBy.Should().Be("admin@test.edu");
    }

    [Test]
    public async Task UpdateValueAsync_HappyPath_WritesAuditLog()
    {
        await using var db = TestDb.CreateInMemory(seedRoles: false);
        SeedConfigs(db);

        var sut = new SystemConfigService(db, new AuditLogService(db));

        await sut.UpdateValueAsync("rag.chunk_size", "1200", "admin@test.edu");

        db.AuditLogs.Should().ContainSingle(log =>
            log.Action == "CONFIG_UPDATE"
            && log.EntityType == "system_configs"
            && log.EntityId == "rag.chunk_size"
            && log.Severity == "Medium");
    }

    [Test]
    public async Task UpdateValueAsync_ConfigNotFound_ThrowsAdminException()
    {
        await using var db = TestDb.CreateInMemory(seedRoles: false);
        var sut = new SystemConfigService(db, new AuditLogService(db));

        var act = () => sut.UpdateValueAsync("nonexistent.key", "value", null);

        var ex = await act.Should().ThrowAsync<AdminException>();
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Code.Should().Be("config_not_found");
    }

    [Test]
    public async Task UpdateValueAsync_InvalidSemanticRelationship_LeavesConfigAndAuditUnchanged()
    {
        await using var db = TestDb.CreateInMemory(seedRoles: false);
        SeedConfigs(db);
        db.SystemConfigs.AddRange(SemanticConfigs());
        await db.SaveChangesAsync();
        var sut = new SystemConfigService(db, new AuditLogService(db));

        var act = () => sut.UpdateValueAsync("rag.semantic_overlap_tokens", "72", "admin@test.edu");

        var error = await act.Should().ThrowAsync<AdminException>();
        error.Which.StatusCode.Should().Be(400);
        (await db.SystemConfigs.FindAsync("rag.semantic_overlap_tokens"))!.Value.Should().Be("24");
        db.AuditLogs.Should().BeEmpty();
    }

    [Test]
    public async Task GetAllAsync_EmptyTable_ReturnsEmptyList()
    {
        await using var db = TestDb.CreateInMemory(seedRoles: false);
        var sut = new SystemConfigService(db, new AuditLogService(db));

        var result = await sut.GetAllAsync();

        result.Should().BeEmpty();
    }

    private static void SeedConfigs(AppDbContext db)
    {
        db.SystemConfigs.AddRange(
            new SystemConfig
            {
                Key = "ai.chat_model", Value = "gpt-4o-mini", DefaultValue = "gpt-4o-mini",
                Category = "Model", DisplayName = "Chat model", ConfigType = "Text", IsCritical = true
            },
            new SystemConfig
            {
                Key = "ai.embedding_model", Value = "text-embedding-3-small", DefaultValue = "text-embedding-3-small",
                Category = "Model", DisplayName = "Embedding model", ConfigType = "Text", IsCritical = true
            },
            new SystemConfig
            {
                Key = "generation.temperature", Value = "0.2", DefaultValue = "0.2",
                Category = "Generation", DisplayName = "Temperature", ConfigType = "Number", IsCritical = true
            },
            new SystemConfig
            {
                Key = "rag.chunk_size", Value = "800", DefaultValue = "800",
                Category = "Retrieval", DisplayName = "Chunk size", ConfigType = "Number", IsCritical = true
            });
        db.SaveChanges();
    }

    private static IEnumerable<SystemConfig> SemanticConfigs() =>
    [
        new() { Key = "rag.semantic_target_tokens", Value = "144", DefaultValue = "144", Category = "Retrieval", DisplayName = "target", ConfigType = "Number" },
        new() { Key = "rag.semantic_min_tokens", Value = "72", DefaultValue = "72", Category = "Retrieval", DisplayName = "min", ConfigType = "Number" },
        new() { Key = "rag.semantic_max_tokens", Value = "192", DefaultValue = "192", Category = "Retrieval", DisplayName = "max", ConfigType = "Number" },
        new() { Key = "rag.semantic_overlap_tokens", Value = "24", DefaultValue = "24", Category = "Retrieval", DisplayName = "overlap", ConfigType = "Number" },
    ];
}
