using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public sealed class FolderService : IFolderService
{
    private readonly AppDbContext _db;
    private readonly ILogger<FolderService> _logger;

    public FolderService(AppDbContext db, ILogger<FolderService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<FolderDto>> ListAsync(
        Guid supabaseUserId,
        CancellationToken cancellationToken = default)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);

        var rows = await _db.Folders
            .AsNoTracking()
            .Where(f => f.UserId == profile.Id)
            .OrderByDescending(f => f.IsFavorite)
            .ThenByDescending(f => f.UpdatedAt)
            .Select(f => new FolderDto
            {
                Id = f.Id,
                Name = f.Name,
                Description = f.Description,
                DocumentCount = f.Documents.Count,
                IsFavorite = f.IsFavorite,
                IsShared = f.IsShared,
                SharedAt = f.SharedAt,
                Icon = f.Icon,
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<FolderDto> CreateAsync(
        Guid supabaseUserId,
        CreateFolderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);
        var name = NormalizeName(request.Name);
        var description = NormalizeDescription(request.Description);

        await EnsureUniqueNameAsync(profile.Id, name, excludeFolderId: null, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = profile.Id,
            Name = name,
            Description = description,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.Folders.Add(folder);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Folder created: id={Id} user={UserId} name={Name}", folder.Id, profile.Id, folder.Name);
        return ToDto(folder, documentCount: 0);
    }

    public async Task<FolderDto> UpdateAsync(
        Guid supabaseUserId,
        Guid folderId,
        UpdateFolderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);
        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == profile.Id, cancellationToken)
            ?? throw new DocumentException(404, "folder_not_found",
                "Folder does not exist or does not belong to the caller.");

        if (request.Name is not null)
        {
            var name = NormalizeName(request.Name);
            await EnsureUniqueNameAsync(profile.Id, name, excludeFolderId: folder.Id, cancellationToken);
            folder.Name = name;
        }
        if (request.Description is not null)
        {
            folder.Description = NormalizeDescription(request.Description);
        }
        if (request.Icon is not null)
        {
            folder.Icon = string.IsNullOrWhiteSpace(request.Icon) ? null : request.Icon.Trim();
        }
        if (request.IsFavorite.HasValue)
        {
            folder.IsFavorite = request.IsFavorite.Value;
        }
        if (request.IsShared.HasValue)
        {
            folder.IsShared = request.IsShared.Value;
            folder.SharedAt = request.IsShared.Value ? DateTimeOffset.UtcNow : null;
        }
        await _db.SaveChangesAsync(cancellationToken);

        var count = await _db.Documents.CountAsync(d => d.FolderId == folder.Id, cancellationToken);
        return ToDto(folder, count);
    }

    public async Task DeleteAsync(
        Guid supabaseUserId,
        Guid folderId,
        CancellationToken cancellationToken = default)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);
        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == profile.Id, cancellationToken)
            ?? throw new DocumentException(404, "folder_not_found",
                "Folder does not exist or does not belong to the caller.");

        var docIds = await _db.Documents
            .Where(d => d.FolderId == folder.Id)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        if (docIds.Count > 0)
        {
            var chunks = await _db.DocumentChunks
                .Where(c => docIds.Contains(c.DocumentId))
                .ExecuteDeleteAsync(cancellationToken);

            await _db.Documents
                .Where(d => d.FolderId == folder.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }

        _db.Folders.Remove(folder);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Folder deleted: id={Id} user={UserId} documents={DocCount}",
            folder.Id, profile.Id, docIds.Count);
    }

    public async Task<FolderDto> ToggleFavoriteAsync(
        Guid supabaseUserId,
        Guid folderId,
        CancellationToken cancellationToken = default)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);
        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == profile.Id, cancellationToken)
            ?? throw new DocumentException(404, "folder_not_found",
                "Folder does not exist or does not belong to the caller.");

        folder.IsFavorite = !folder.IsFavorite;
        await _db.SaveChangesAsync(cancellationToken);

        var count = await _db.Documents.CountAsync(d => d.FolderId == folder.Id, cancellationToken);
        return ToDto(folder, count);
    }

    public async Task<IReadOnlyList<FolderDto>> ListSharedAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.Folders
            .AsNoTracking()
            .Where(f => f.IsShared)
            .OrderByDescending(f => f.SharedAt)
            .ThenBy(f => f.Name)
            .Select(f => new FolderDto
            {
                Id = f.Id,
                Name = f.Name,
                Description = f.Description,
                DocumentCount = f.Documents.Count,
                IsFavorite = f.IsFavorite,
                IsShared = f.IsShared,
                SharedAt = f.SharedAt,
                Icon = f.Icon,
                OwnerName = f.User.FullName ?? f.User.Username,
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<FolderDto> ToggleShareAsync(
        Guid supabaseUserId,
        Guid folderId,
        CancellationToken cancellationToken = default)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);
        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == profile.Id, cancellationToken)
            ?? throw new DocumentException(404, "folder_not_found",
                "Folder does not exist or does not belong to the caller.");

        folder.IsShared = !folder.IsShared;
        folder.SharedAt = folder.IsShared ? DateTimeOffset.UtcNow : null;
        await _db.SaveChangesAsync(cancellationToken);

        var count = await _db.Documents.CountAsync(d => d.FolderId == folder.Id, cancellationToken);
        return ToDto(folder, count);
    }

    private async Task<User> ResolveProfileAsync(Guid supabaseUserId, CancellationToken cancellationToken)
    {
        var profile = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, cancellationToken)
            ?? throw new DocumentException(404, "user_not_found",
                "Authenticated user has no profile in public.users.");

        if (!profile.IsActive)
        {
            throw new DocumentException(403, "user_inactive",
                "User account is inactive and cannot manage folders.");
        }

        return profile;
    }

    private async Task EnsureUniqueNameAsync(
        Guid userId,
        string name,
        Guid? excludeFolderId,
        CancellationToken cancellationToken)
    {
        var normalized = name.ToUpperInvariant();
        var exists = await _db.Folders
            .AsNoTracking()
            .AnyAsync(f => f.UserId == userId
                && f.Name.ToUpper() == normalized
                && (!excludeFolderId.HasValue || f.Id != excludeFolderId.Value), cancellationToken);

        if (exists)
        {
            throw new DocumentException(409, "folder_name_taken",
                "You already have a folder with this name.");
        }
    }

    private static string NormalizeName(string? name)
    {
        var normalized = (name ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            throw new DocumentException(400, "folder_name_required", "Folder name is required.");
        }
        if (normalized.Length > 100)
        {
            throw new DocumentException(400, "folder_name_too_long", "Folder name must be 100 characters or fewer.");
        }
        return normalized;
    }

    private static string? NormalizeDescription(string? description)
    {
        var normalized = description?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static FolderDto ToDto(Folder folder, int documentCount) => new()
    {
        Id = folder.Id,
        Name = folder.Name,
        Description = folder.Description,
        DocumentCount = documentCount,
        IsFavorite = folder.IsFavorite,
        IsShared = folder.IsShared,
        SharedAt = folder.SharedAt,
        Icon = folder.Icon,
        CreatedAt = folder.CreatedAt,
        UpdatedAt = folder.UpdatedAt,
    };
}
