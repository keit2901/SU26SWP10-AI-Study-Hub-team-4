using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services.Supabase;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public sealed class FolderService : IFolderService
{
    private readonly AppDbContext _db;
    private readonly ILogger<FolderService> _logger;
    private readonly ISupabaseStorageClient _storage;

    public FolderService(AppDbContext db, ILogger<FolderService> logger, ISupabaseStorageClient storage)
    {
        _db = db;
        _logger = logger;
        _storage = storage;
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
                LikeCount = f.Reactions.Count(r => r.IsLike),
                DislikeCount = f.Reactions.Count(r => !r.IsLike),
            })
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<IReadOnlyList<FolderDto>> ListPersonalSharedAsync(
        Guid supabaseUserId,
        CancellationToken cancellationToken = default)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);

        var rows = await _db.Folders
            .AsNoTracking()
            .Where(f => f.UserId == profile.Id && f.IsShared)
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
                LikeCount = f.Reactions.Count(r => r.IsLike),
                DislikeCount = f.Reactions.Count(r => !r.IsLike),
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

    public async Task<FolderDto> VoteAsync(
        Guid supabaseUserId,
        Guid folderId,
        bool isLike,
        CancellationToken cancellationToken = default)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);
        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == folderId, cancellationToken)
            ?? throw new DocumentException(404, "folder_not_found", "Folder not found.");

        var existing = await _db.FolderReactions
            .FirstOrDefaultAsync(r => r.FolderId == folderId && r.UserId == profile.Id, cancellationToken);

        if (existing is not null)
        {
            if (existing.IsLike == isLike)
            {
                _db.FolderReactions.Remove(existing);
            }
            else
            {
                existing.IsLike = isLike;
            }
        }
        else
        {
            _db.FolderReactions.Add(new FolderReaction
            {
                FolderId = folderId,
                UserId = profile.Id,
                IsLike = isLike,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        var likeCount = await _db.FolderReactions.CountAsync(r => r.FolderId == folderId && r.IsLike, cancellationToken);
        var dislikeCount = await _db.FolderReactions.CountAsync(r => r.FolderId == folderId && !r.IsLike, cancellationToken);
        var currentVote = await _db.FolderReactions
            .Where(r => r.FolderId == folderId && r.UserId == profile.Id)
            .Select(r => (bool?)r.IsLike)
            .FirstOrDefaultAsync(cancellationToken);
        var docCount = await _db.Documents.CountAsync(d => d.FolderId == folderId, cancellationToken);

        return new FolderDto
        {
            Id = folder.Id,
            Name = folder.Name,
            Description = folder.Description,
            DocumentCount = docCount,
            IsFavorite = folder.IsFavorite,
            IsShared = folder.IsShared,
            SharedAt = folder.SharedAt,
            Icon = folder.Icon,
            OwnerName = profile.FullName ?? profile.Username,
            CreatedAt = folder.CreatedAt,
            UpdatedAt = folder.UpdatedAt,
            LikeCount = likeCount,
            DislikeCount = dislikeCount,
            CurrentUserVote = currentVote,
        };
    }

    public async Task<FolderDto> CopySharedFolderAsync(
        Guid supabaseUserId,
        Guid sharedFolderId,
        CancellationToken cancellationToken = default)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);

        var source = await _db.Folders
            .AsNoTracking()
            .Include(f => f.Documents)
            .FirstOrDefaultAsync(f => f.Id == sharedFolderId && f.IsShared, cancellationToken)
            ?? throw new DocumentException(404, "folder_not_found",
                "Shared folder not found.");

        var now = DateTimeOffset.UtcNow;
        var name = source.Name;
        var existingCount = await _db.Folders.CountAsync(f => f.UserId == profile.Id && f.Name.StartsWith(name), cancellationToken);
        if (existingCount > 0)
            name = $"{name} ({existingCount})";

        var newFolder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = profile.Id,
            Name = name,
            Description = source.Description,
            Icon = source.Icon,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Folders.Add(newFolder);

        foreach (var doc in source.Documents)
        {
            var documentId = Guid.NewGuid();
            var slug = SanitizeFileName(doc.FileName);
            var newStoragePath = $"users/{profile.Id:N}/{now.Year}/{documentId:N}-{slug}";

            await CopyStorageFileAsync(doc.StoragePath, newStoragePath, cancellationToken);

            var newDoc = new Document
            {
                Id = documentId,
                UserId = profile.Id,
                FolderId = newFolder.Id,
                FileName = doc.FileName,
                StoragePath = newStoragePath,
                FileSizeBytes = doc.FileSizeBytes,
                MimeType = doc.MimeType,
                SubjectCode = doc.SubjectCode,
                Semester = doc.Semester,
                PageCount = doc.PageCount,
                Status = DocumentStatus.Ready,
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.Documents.Add(newDoc);
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Folder copied: sharedFolderId={SourceId} newFolderId={NewId} user={UserId} name={Name} documents={DocCount}",
            sharedFolderId, newFolder.Id, profile.Id, newFolder.Name, source.Documents.Count);

        return new FolderDto
        {
            Id = newFolder.Id,
            Name = newFolder.Name,
            Description = newFolder.Description,
            DocumentCount = source.Documents.Count,
            IsFavorite = false,
            IsShared = false,
            Icon = source.Icon,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private async Task CopyStorageFileAsync(string sourcePath, string destPath, CancellationToken ct)
    {
        try
        {
            var (stream, contentType) = await _storage.DownloadFileAsync(DocumentService.BucketName, sourcePath, ct);
            await using (stream)
            {
                stream.Position = 0;
                await _storage.UploadAsync(DocumentService.BucketName, destPath, stream, contentType, upsert: false, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy storage file from {Source} to {Dest}; proceeding with path reference only", sourcePath, destPath);
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var trimmed = Path.GetFileName(fileName).Trim();
        if (string.IsNullOrEmpty(trimmed))
            return "upload.bin";
        var safe = new string(trimmed.Select(c =>
            char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_').ToArray());
        return safe.Length > 80 ? safe[..80] : safe;
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
