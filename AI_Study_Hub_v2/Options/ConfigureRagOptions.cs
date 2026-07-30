using AI_Study_Hub_v2.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Options;

public sealed class ConfigureRagOptions : IConfigureOptions<RagOptions>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConfigureRagOptions> _logger;

    public ConfigureRagOptions(IServiceScopeFactory scopeFactory, ILogger<ConfigureRagOptions> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Configure(RagOptions options)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            var chunkSizeConfig = db.SystemConfigs
                .AsNoTracking()
                .FirstOrDefault(c => c.Key == "rag.chunk_size");

            if (chunkSizeConfig != null && int.TryParse(chunkSizeConfig.Value, out var chunkSize))
            {
                options.ChunkSizeChars = chunkSize;
                options.MaxSectionChars = chunkSize;
            }

            var chunkOverlapConfig = db.SystemConfigs
                .AsNoTracking()
                .FirstOrDefault(c => c.Key == "rag.chunk_overlap");

            if (chunkOverlapConfig != null && int.TryParse(chunkOverlapConfig.Value, out var chunkOverlap))
            {
                options.ChunkOverlapChars = chunkOverlap;
            }

            var configured = db.SystemConfigs
                .AsNoTracking()
                .Where(c => c.Key == "rag.semantic_target_tokens"
                    || c.Key == "rag.semantic_min_tokens"
                    || c.Key == "rag.semantic_max_tokens"
                    || c.Key == "rag.semantic_overlap_tokens")
                .ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);
            var semanticKeys = new[] { "rag.semantic_target_tokens", "rag.semantic_min_tokens", "rag.semantic_max_tokens", "rag.semantic_overlap_tokens" };
            var malformedKeys = semanticKeys.Where(key => !configured.TryGetValue(key, out var raw) || !int.TryParse(raw, out _)).ToArray();
            if (configured.Count > 0 && malformedKeys.Length > 0)
            {
                _logger.LogWarning("Ignoring incomplete or malformed persisted semantic-v2 configuration keys: {Keys}; using application defaults.", string.Join(", ", malformedKeys));
                return;
            }
            if (configured.Count == 0) { return; }

            var candidate = new RagOptions
            {
                SemanticTargetTokens = options.SemanticTargetTokens,
                SemanticMinTokens = options.SemanticMinTokens,
                SemanticMaxTokens = options.SemanticMaxTokens,
                SemanticOverlapTokens = options.SemanticOverlapTokens,
            };

            ApplyInt(configured, "rag.semantic_target_tokens", value => candidate.SemanticTargetTokens = value);
            ApplyInt(configured, "rag.semantic_min_tokens", value => candidate.SemanticMinTokens = value);
            ApplyInt(configured, "rag.semantic_max_tokens", value => candidate.SemanticMaxTokens = value);
            ApplyInt(configured, "rag.semantic_overlap_tokens", value => candidate.SemanticOverlapTokens = value);

            if (RagOptions.HasValidSemanticV2Bounds(candidate))
            {
                options.SemanticTargetTokens = candidate.SemanticTargetTokens;
                options.SemanticMinTokens = candidate.SemanticMinTokens;
                options.SemanticMaxTokens = candidate.SemanticMaxTokens;
                options.SemanticOverlapTokens = candidate.SemanticOverlapTokens;
            }
            else
            {
                _logger.LogWarning("Ignoring persisted semantic-v2 configuration that violates bounds; using application defaults.");
            }
        }
        catch (Exception exception)
        {
            // Fail-safe: if DB is not ready or migrated, keep appsettings defaults
            _logger.LogWarning(exception, "Unable to load persisted RAG options; using application defaults.");
        }
    }

    private static void ApplyInt(IReadOnlyDictionary<string, string> configured, string key, Action<int> apply)
    {
        if (configured.TryGetValue(key, out var raw) && int.TryParse(raw, out var value))
        {
            apply(value);
        }
    }
}
