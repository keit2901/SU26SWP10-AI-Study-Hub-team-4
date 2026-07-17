using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public static class SystemConfigSeeder
{
    public static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        var existing = (await db.SystemConfigs.Select(config => config.Key).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var missing = Defaults().Where(config => !existing.Contains(config.Key)).ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        db.SystemConfigs.AddRange(missing);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} missing system configs.", missing.Length);
    }

    private static SystemConfig[] Defaults() =>
    [
        new() { Key = "ai.chat_model", Value = "gpt-4o-mini", DefaultValue = "gpt-4o-mini", Category = "Model", DisplayName = "Chat model", Description = "Model identifier used by the RAG answer generation pipeline.", ConfigType = "Text", IsCritical = true },
        new() { Key = "ai.embedding_model", Value = "text-embedding-3-small", DefaultValue = "text-embedding-3-small", Category = "Model", DisplayName = "Embedding model", Description = "Provider model identifier used to embed document_chunks.", ConfigType = "Text", IsCritical = true },
        new() { Key = "rag.chunk_size", Value = "700", DefaultValue = "700", Category = "Retrieval", DisplayName = "Chunk size", Description = "Maximum characters or tokens per source chunk before embedding.", ConfigType = "Number", IsCritical = true },
        new() { Key = "rag.chunk_overlap", Value = "70", DefaultValue = "70", Category = "Retrieval", DisplayName = "Chunk overlap", Description = "Overlap between consecutive chunks to preserve context.", ConfigType = "Number", IsCritical = true },
        new() { Key = "rag.max_chunks", Value = "8", DefaultValue = "8", Category = "Retrieval", DisplayName = "Max retrieval chunks", Description = "Maximum document chunks sent to the answer generation pipeline.", ConfigType = "Number", IsCritical = true },
        new() { Key = "generation.temperature", Value = "0.2", DefaultValue = "0.2", Category = "Generation", DisplayName = "Temperature", Description = "Controls answer randomness for study assistant responses.", ConfigType = "Number", IsCritical = true },
        new() { Key = "generation.top_p", Value = "0.9", DefaultValue = "0.9", Category = "Generation", DisplayName = "Top-p", Description = "Nucleus sampling parameter used by the RAG answer generation pipeline.", ConfigType = "Number", IsCritical = true },
        new() { Key = "generation.system_prompt", Value = "You are AI Study Hub.", DefaultValue = "You are AI Study Hub.", Category = "Generation", DisplayName = "System prompt", Description = "Instruction block applied to every RAG chat response.", ConfigType = "Text", IsCritical = true },
        new() { Key = "quota.default_student_daily_tokens", Value = "25000", DefaultValue = "25000", Category = "Quota", DisplayName = "Student daily quota", Description = "Default daily token quota assigned to new student profiles.", ConfigType = "Number", IsCritical = false },
        new() { Key = "quota.default_admin_daily_tokens", Value = "75000", DefaultValue = "75000", Category = "Quota", DisplayName = "Admin daily quota", Description = "Default daily token quota assigned to administrator profiles.", ConfigType = "Number", IsCritical = false },
        new() { Key = "auth.allow_self_registration", Value = "false", DefaultValue = "false", Category = "Security", DisplayName = "Allow self registration", Description = "Controls whether new users can create accounts without an invitation.", ConfigType = "Boolean", IsCritical = true },
        new() { Key = "documents.allowed_extensions", Value = "[\".pdf\", \".docx\"]", DefaultValue = "[\".pdf\", \".docx\"]", Category = "Documents", DisplayName = "Allowed document extensions", Description = "JSON array of file extensions accepted by upload validation.", ConfigType = "Json", IsCritical = false },
        new() { Key = "moderation.report_reasons", Value = "[\"Wrong subject\"]", DefaultValue = "[\"Wrong subject\"]", Category = "Moderation", DisplayName = "Report reasons", Description = "JSON options shown when a user reports a document.", ConfigType = "Json", IsCritical = false },
        new() { Key = "audit.retention_days", Value = "365", DefaultValue = "365", Category = "Governance", DisplayName = "Audit retention days", Description = "Number of days audit_logs remain visible before archival.", ConfigType = "Number", IsCritical = false },
    ];
}
