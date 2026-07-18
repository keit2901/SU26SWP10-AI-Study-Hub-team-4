using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AI_Study_Hub_v2.Tests.Options;

[TestFixture]
public sealed class ConfigureRagOptionsTests
{
    [Test]
    public void Configure_IncompleteGroup_FallsBackAndLogsMissingKeyWithoutValue()
    {
        using var db = TestDb.CreateInMemory(seedRoles: false);
        Seed(db, ("rag.semantic_target_tokens", "150"), ("rag.semantic_min_tokens", "80"), ("rag.semantic_max_tokens", "190"));
        var logger = new CapturingLogger<ConfigureRagOptions>();
        var options = Configure(db, logger);

        options.SemanticTargetTokens.Should().Be(144);
        logger.Warnings.Should().ContainSingle().Which.Should().Contain("incomplete or malformed").And.Contain("rag.semantic_overlap_tokens").And.NotContain("150");
    }

    [Test]
    public void Configure_MalformedAndInvalidAndValidGroups_HaveExpectedFallbackOrApplication()
    {
        using var malformed = TestDb.CreateInMemory(seedRoles: false);
        Seed(malformed, ("rag.semantic_target_tokens", "abc"), ("rag.semantic_min_tokens", "80"), ("rag.semantic_max_tokens", "190"), ("rag.semantic_overlap_tokens", "20"));
        var malformedLog = new CapturingLogger<ConfigureRagOptions>();
        Configure(malformed, malformedLog).SemanticTargetTokens.Should().Be(144);
        malformedLog.Warnings.Single().Should().Contain("rag.semantic_target_tokens").And.NotContain("abc");

        using var invalid = TestDb.CreateInMemory(seedRoles: false);
        Seed(invalid, ("rag.semantic_target_tokens", "120"), ("rag.semantic_min_tokens", "150"), ("rag.semantic_max_tokens", "190"), ("rag.semantic_overlap_tokens", "20"));
        var invalidLog = new CapturingLogger<ConfigureRagOptions>();
        Configure(invalid, invalidLog).SemanticMinTokens.Should().Be(72);
        invalidLog.Warnings.Single().Should().Contain("violates bounds");

        using var valid = TestDb.CreateInMemory(seedRoles: false);
        Seed(valid, ("rag.semantic_target_tokens", "150"), ("rag.semantic_min_tokens", "80"), ("rag.semantic_max_tokens", "190"), ("rag.semantic_overlap_tokens", "20"), ("rag.chunk_size", "800"));
        var validLog = new CapturingLogger<ConfigureRagOptions>();
        var applied = Configure(valid, validLog);
        applied.SemanticTargetTokens.Should().Be(150); applied.SemanticMinTokens.Should().Be(80); applied.SemanticMaxTokens.Should().Be(190); applied.SemanticOverlapTokens.Should().Be(20); applied.ChunkSizeChars.Should().Be(800);
        validLog.Warnings.Should().BeEmpty();
    }

    private static RagOptions Configure(AppDbContext db, CapturingLogger<ConfigureRagOptions> logger)
    {
        var services = new ServiceCollection(); services.AddSingleton(db);
        using var provider = services.BuildServiceProvider();
        var options = new RagOptions(); new ConfigureRagOptions(provider.GetRequiredService<IServiceScopeFactory>(), logger).Configure(options); return options;
    }

    private static void Seed(AppDbContext db, params (string Key, string Value)[] values)
    {
        db.SystemConfigs.AddRange(values.Select(value => new SystemConfig { Key = value.Key, Value = value.Value, DefaultValue = value.Value, Category = "Retrieval", DisplayName = value.Key, ConfigType = "Number" })); db.SaveChanges();
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { if (level == LogLevel.Warning) Warnings.Add(formatter(state, exception)); }
    }
}
