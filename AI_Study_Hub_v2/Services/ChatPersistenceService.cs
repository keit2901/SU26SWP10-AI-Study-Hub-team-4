using System.Text.Json;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public sealed class ChatPersistenceService : IChatPersistenceService
{
    private async Task<User> ResolveProfileAsync(Guid supabaseUserId, CancellationToken ct)
    {
        var profile = await _db.Users
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, ct)
            ?? throw new AiChatException(404, "user_not_found",
                "User profile not found. Ensure your Supabase account is linked to a local profile.");
        return profile;
    }

    private sealed record MessageMetadata(
        string? ScopeLabel,
        string? RefusalReason,
        long? DurationMs,
        IReadOnlyList<AiChatSourceDto>? Sources,
        string? QuizId = null,
        string? QuizTitle = null,
        string? QuizStatus = null,
        int? QuizTotalQuestions = null,
        int? QuizScore = null);

    private readonly AppDbContext _db;
    private readonly ILogger<ChatPersistenceService> _logger;
    private readonly IAuditLogService _audit;

    public ChatPersistenceService(AppDbContext db, ILogger<ChatPersistenceService> logger, IAuditLogService audit)
    {
        _db = db;
        _logger = logger;
        _audit = audit;
    }

    public async Task<IReadOnlyList<ChatSessionDto>> ListSessionsAsync(Guid supabaseUserId, Guid? folderId = null, CancellationToken ct = default)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, ct);
        var sessions = await _db.ChatSessions
            .Where(s => s.UserId == profile.Id)
            .Where(s => s.FolderId == folderId)
            .OrderByDescending(s => s.UpdatedAt)
            .Select(s => new ChatSessionDto
            {
                Id = s.Id,
                FolderId = s.FolderId,
                Title = s.Title,
                Model = s.Model,
                TopK = s.TopK,
                MessageCount = s.Messages.Count,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt,
            })
            .ToListAsync(ct);

        return sessions;
    }

    public async Task<ChatSessionDto> CreateSessionAsync(Guid supabaseUserId, CreateChatSessionRequest request, CancellationToken ct = default)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, ct);
        if (request.FolderId.HasValue && !await _db.Folders.AnyAsync(f => f.Id == request.FolderId.Value && f.UserId == profile.Id, ct))
        {
            throw new AiChatException(404, "folder_not_found", "Folder not found.");
        }

        var now = DateTimeOffset.UtcNow;
        var session = new ChatSession
        {
            Id = Guid.NewGuid(),
            UserId = profile.Id,
            FolderId = request.FolderId,
            Title = request.Title,
            Model = request.Model,
            TopK = request.TopK > 0 ? request.TopK : 5,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.ChatSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        _audit.Add(supabaseUserId, "AI_CHAT_SESSION_CREATED", "ChatSession", session.Id.ToString(), "Low",
            afterJson: JsonSerializer.Serialize(new { session.Title, session.FolderId }));

        return new ChatSessionDto
        {
            Id = session.Id,
            FolderId = session.FolderId,
            Title = session.Title,
            Model = session.Model,
            TopK = session.TopK,
            MessageCount = 0,
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt,
        };
    }

    public async Task<IReadOnlyList<ChatMessageDto>> GetMessagesScopedAsync(Guid supabaseUserId, Guid sessionId, Guid? expectedFolderId, CancellationToken ct = default)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, ct);
        var session = await _db.ChatSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == profile.Id && s.FolderId == expectedFolderId, ct);

        if (session is null)
        {
            throw new AiChatException(404, "session_not_found", "Chat session not found.");
        }

        return await _db.ChatMessages
            .AsNoTracking()
            .Where(m => m.ChatSessionId == sessionId)
            .OrderBy(m => m.SequenceNumber)
            .Select(m => new ChatMessageDto
            {
                Id = m.Id,
                Role = m.Role,
                Content = m.Content,
                MetadataJson = m.MetadataJson,
                SequenceNumber = m.SequenceNumber,
                CreatedAt = m.CreatedAt,
            })
            .ToListAsync(ct);
    }

    public async Task DeleteSessionAsync(Guid supabaseUserId, Guid sessionId, Guid? expectedFolderId, CancellationToken ct = default)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, ct);
        var session = await _db.ChatSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == profile.Id && s.FolderId == expectedFolderId, ct);

        if (session is null)
        {
            throw new AiChatException(404, "session_not_found", "Chat session not found.");
        }

        _db.ChatSessions.Remove(session);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SaveExchangeAsync(Guid supabaseUserId, Guid sessionId, Guid? expectedFolderId, string question, string scopeLabel, AiChatAnswerResponse response, CancellationToken ct = default)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, ct);
        var session = await _db.ChatSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == profile.Id && s.FolderId == expectedFolderId, ct);

        if (session is null)
        {
            throw new AiChatException(404, "session_not_found", "Chat session not found.");
        }

        var nextSeq = await _db.ChatMessages
            .Where(m => m.ChatSessionId == sessionId)
            .MaxAsync(m => (int?)m.SequenceNumber, ct) ?? -1;

        var now = DateTimeOffset.UtcNow;
        var metadata = new MessageMetadata(scopeLabel, response.RefusalReason, response.DurationMs, response.Sources);
        var metadataJson = JsonSerializer.Serialize(metadata);

        var userMessage = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ChatSessionId = sessionId,
            Role = "user",
            Content = question,
            SequenceNumber = nextSeq + 1,
            CreatedAt = now,
        };

        var assistantMessage = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ChatSessionId = sessionId,
            Role = "assistant",
            Content = response.Answer,
            MetadataJson = metadataJson,
            SequenceNumber = nextSeq + 2,
            CreatedAt = now,
        };

        _db.ChatMessages.AddRange(userMessage, assistantMessage);
        session.UpdatedAt = now;

        if (string.IsNullOrWhiteSpace(session.Title))
        {
            session.Title = question.Length > 100 ? question[..100] + "..." : question;
        }

        await _db.SaveChangesAsync(ct);

        var truncatedQuestion = question.Length > 100 ? question[..100] : question;
        _audit.Add(supabaseUserId, "AI_CHAT_EXCHANGE", "ChatExchange", sessionId.ToString(), "Low",
            afterJson: JsonSerializer.Serialize(new { Question = truncatedQuestion, scopeLabel }));
    }

    public async Task SaveQuizExchangeAsync(Guid supabaseUserId, Guid sessionId, Guid? expectedFolderId, string scopeLabel, string userContent, Guid quizId, string quizTitle, string quizStatus, int? totalQuestions = null, int? score = null, CancellationToken ct = default)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, ct);
        var session = await _db.ChatSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == profile.Id && s.FolderId == expectedFolderId, ct);

        if (session is null)
        {
            throw new AiChatException(404, "session_not_found", "Chat session not found.");
        }

        var nextSeq = await _db.ChatMessages
            .Where(m => m.ChatSessionId == sessionId)
            .MaxAsync(m => (int?)m.SequenceNumber, ct) ?? -1;

        var now = DateTimeOffset.UtcNow;
        var metadata = new MessageMetadata(
            ScopeLabel: scopeLabel,
            RefusalReason: null,
            DurationMs: null,
            Sources: null,
            QuizId: quizId.ToString(),
            QuizTitle: quizTitle,
            QuizStatus: quizStatus,
            QuizTotalQuestions: totalQuestions,
            QuizScore: score);
        var metadataJson = JsonSerializer.Serialize(metadata);

        var userMessage = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ChatSessionId = sessionId,
            Role = "user",
            Content = userContent,
            SequenceNumber = nextSeq + 1,
            CreatedAt = now,
        };

        var assistantMessage = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ChatSessionId = sessionId,
            Role = "assistant",
            Content = string.Empty,
            MetadataJson = metadataJson,
            SequenceNumber = nextSeq + 2,
            CreatedAt = now,
        };

        _db.ChatMessages.AddRange(userMessage, assistantMessage);
        session.UpdatedAt = now;

        if (string.IsNullOrWhiteSpace(session.Title))
        {
            session.Title = userContent.Length > 100 ? userContent[..100] + "..." : userContent;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateQuizMetadataAsync(Guid supabaseUserId, Guid quizId, string quizStatus, int? totalQuestions = null, int? score = null, CancellationToken ct = default)
    {
        var quizIdStr = quizId.ToString();
        var assistantMessages = await _db.ChatMessages
            .FromSqlRaw(
                @"SELECT * FROM ""chat_messages""
                  WHERE ""metadata_json""->>'QuizId' = {0}",
                quizIdStr)
            .ToListAsync(ct);

        foreach (var msg in assistantMessages)
        {
            try
            {
                var meta = JsonSerializer.Deserialize<MessageMetadata>(msg.MetadataJson!);
                if (meta?.QuizId == quizIdStr)
                {
                    var updated = meta with
                    {
                        QuizStatus = quizStatus,
                        QuizTotalQuestions = totalQuestions ?? meta.QuizTotalQuestions,
                        QuizScore = score,
                    };
                    msg.MetadataJson = JsonSerializer.Serialize(updated);
                }
            }
            catch
            {
                // Skip malformed metadata
            }
        }

        if (assistantMessages.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }
    }
}
