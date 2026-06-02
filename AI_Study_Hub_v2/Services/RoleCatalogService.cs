using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public interface IRoleCatalogService
{
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken = default);
}

public sealed class RoleCatalogService : IRoleCatalogService
{
    private readonly AppDbContext _db;

    public RoleCatalogService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Roles
            .AsNoTracking()
            .OrderBy(role => role.Id)
            .Select(role => new RoleDto
            {
                Id = role.Id,
                RoleName = role.RoleName,
                Description = role.Description
            })
            .ToListAsync(cancellationToken);
    }
}
