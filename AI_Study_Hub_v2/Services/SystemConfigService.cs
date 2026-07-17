using System.Text.Json;
using System.Data;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace AI_Study_Hub_v2.Services;

public interface ISystemConfigService
{
    Task<IReadOnlyList<SystemConfigDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<SystemConfigDto> UpdateValueAsync(string key, string value, string? updatedBy, CancellationToken cancellationToken = default);
}

public sealed class SystemConfigService : ISystemConfigService
{
    private readonly AppDbContext _db;
    private readonly IAuditLogService _audit;

    public SystemConfigService(AppDbContext db, IAuditLogService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<SystemConfigDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _db.SystemConfigs
            .AsNoTracking()
            .OrderBy(c => c.Category)
            .ThenBy(c => c.Key)
            .Select(c => new SystemConfigDto(
                c.Key,
                c.Value,
                c.DefaultValue,
                c.Category,
                c.DisplayName,
                c.Description,
                c.ConfigType,
                c.IsCritical,
                c.UpdatedAt,
                c.UpdatedBy,
                c.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<SystemConfigDto> UpdateValueAsync(string key, string value, string? updatedBy, CancellationToken cancellationToken = default)
    {
        if (IsSemanticV2Key(key))
        {
            return await UpdateSemanticV2Async(key, value, updatedBy, cancellationToken);
        }

        var config = await _db.SystemConfigs
            .FirstOrDefaultAsync(c => c.Key == key, cancellationToken)
            ?? throw new AdminException(404, "config_not_found", $"System config '{key}' not found.");

        var previousValue = config.Value;
        config.Value = value;
        config.UpdatedBy = updatedBy;

        _audit.Add(
            null,
            "CONFIG_UPDATE",
            "system_configs",
            key,
            "Medium",
            JsonSerializer.Serialize(new { value = previousValue }),
            JsonSerializer.Serialize(new { value }),
            null,
            null,
            null);

        await _db.SaveChangesAsync(cancellationToken);

        return new SystemConfigDto(
            config.Key,
            config.Value,
            config.DefaultValue,
            config.Category,
            config.DisplayName,
            config.Description,
            config.ConfigType,
            config.IsCritical,
            config.UpdatedAt,
            config.UpdatedBy,
            config.CreatedAt);
    }

    private async Task<SystemConfigDto> UpdateSemanticV2Async(string key, string value, string? updatedBy, CancellationToken cancellationToken)
    {
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? ownedTransaction = null;
        if (_db.Database.IsRelational())
        {
            var current = _db.Database.CurrentTransaction;
            if (current is null)
            {
                ownedTransaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            }
            else if (current.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            {
                throw new AdminException(409, "config_update_conflict", "Semantic-v2 configuration requires a serializable transaction.");
            }
        }

        try
        {
            var configs = await _db.SystemConfigs.Where(config => SemanticV2Keys.Contains(config.Key)).ToDictionaryAsync(config => config.Key, cancellationToken);
            if (configs.Count != 4)
            {
                throw new AdminException(400, "invalid_semantic_v2_config", "Semantic-v2 token settings must be complete integers.");
            }
            if (!configs.TryGetValue(key, out var config))
            {
                throw new AdminException(404, "config_not_found", $"System config '{key}' not found.");
            }
            var prospective = configs.ToDictionary(item => item.Key, item => item.Value.Value, StringComparer.Ordinal);
            prospective[key] = value;
            ValidateSemanticV2(prospective);

            var previousValue = config.Value;
            config.Value = value;
            config.UpdatedBy = updatedBy;
            _audit.Add(null, "CONFIG_UPDATE", "system_configs", key, "Medium", JsonSerializer.Serialize(new { value = previousValue }), JsonSerializer.Serialize(new { value }), null, null, null);
            await _db.SaveChangesAsync(cancellationToken);
            if (ownedTransaction is not null) { await ownedTransaction.CommitAsync(cancellationToken); }
            return new SystemConfigDto(config.Key, config.Value, config.DefaultValue, config.Category, config.DisplayName, config.Description, config.ConfigType, config.IsCritical, config.UpdatedAt, config.UpdatedBy, config.CreatedAt);
        }
        catch (Exception exception)
        {
            if (ownedTransaction is not null)
            {
                try
                {
                    await ownedTransaction.RollbackAsync(CancellationToken.None);
                }
                catch when (exception is OperationCanceledException || IsSerializationFailure(exception))
                {
                    // Preserve the original cancellation/conflict if PostgreSQL already ended the transaction.
                }
            }

            if (IsSerializationFailure(exception))
            {
                throw new AdminException(409, "config_update_conflict", "Semantic-v2 configuration changed concurrently. Reload the settings and retry.");
            }

            throw;
        }
        finally
        {
            if (ownedTransaction is not null) { await ownedTransaction.DisposeAsync(); }
        }
    }

    private static void ValidateSemanticV2(IReadOnlyDictionary<string, string> configs)
    {
        if (!configs.All(item => int.TryParse(item.Value, out _))) { throw new AdminException(400, "invalid_semantic_v2_config", "Semantic-v2 token settings must be complete integers."); }
        var candidate = new RagOptions { SemanticOverlapTokens = int.Parse(configs["rag.semantic_overlap_tokens"]), SemanticMinTokens = int.Parse(configs["rag.semantic_min_tokens"]), SemanticTargetTokens = int.Parse(configs["rag.semantic_target_tokens"]), SemanticMaxTokens = int.Parse(configs["rag.semantic_max_tokens"]) };
        if (!RagOptions.HasValidSemanticV2Bounds(candidate)) { throw new AdminException(400, "invalid_semantic_v2_config", "Semantic-v2 settings must satisfy overlap < min <= target <= max within supported ranges."); }
    }

    private static bool IsSemanticV2Key(string key) => SemanticV2Keys.Contains(key);
    private static bool IsSerializationFailure(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure }
        || exception is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.SerializationFailure } };

    private static readonly HashSet<string> SemanticV2Keys = new(StringComparer.Ordinal)
    {
        "rag.semantic_overlap_tokens", "rag.semantic_min_tokens", "rag.semantic_target_tokens", "rag.semantic_max_tokens"
    };
}
