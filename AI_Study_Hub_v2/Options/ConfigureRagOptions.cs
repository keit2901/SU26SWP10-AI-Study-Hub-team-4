using AI_Study_Hub_v2.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Options;

public sealed class ConfigureRagOptions : IConfigureOptions<RagOptions>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ConfigureRagOptions(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
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
        }
        catch
        {
            // Fail-safe: if DB is not ready or migrated, keep appsettings defaults
        }
    }
}
