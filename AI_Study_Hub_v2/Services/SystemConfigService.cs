using System.Text.Json;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using Microsoft.EntityFrameworkCore;

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
}
