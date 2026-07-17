using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Folder> Folders => Set<Folder>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();

    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();

    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    public DbSet<AiAnswerReport> AiAnswerReports => Set<AiAnswerReport>();

    public DbSet<Quiz> Quizzes => Set<Quiz>();

    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();

    public DbSet<FolderReaction> FolderReactions => Set<FolderReaction>();

    public DbSet<CommunityReport> CommunityReports => Set<CommunityReport>();

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<UserPlan> UserPlans => Set<UserPlan>();

    public DbSet<SharedFolderCopyOperation> SharedFolderCopyOperations => Set<SharedFolderCopyOperation>();

    public DbSet<RegistrationOperation> RegistrationOperations => Set<RegistrationOperation>();

    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<BenchmarkRunRecord> BenchmarkRuns => Set<BenchmarkRunRecord>();

    public DbSet<SystemConfig> SystemConfigs => Set<SystemConfig>();

    public DbSet<DocumentEscalation> DocumentEscalations => Set<DocumentEscalation>();

    public DbSet<DocumentEscalationItem> DocumentEscalationItems => Set<DocumentEscalationItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // pgcrypto provides gen_random_uuid(); pgvector provides the vector(N) type.
        // Both ship preinstalled in the supabase/postgres image but we declare them
        // on the model so EF generates idempotent CREATE EXTENSION IF NOT EXISTS in migrations.
        modelBuilder.HasPostgresExtension("pgcrypto");
        modelBuilder.HasPostgresExtension("vector");

        // PostgreSQL ENUM for Document.Status. Npgsql discovers .NET enum mappings via
        // HasPostgresEnum + the matching MapEnum() call on NpgsqlDataSourceBuilder (Program.cs).
        modelBuilder.HasPostgresEnum<Entities.DocumentStatus>(
            schema: "public",
            name: "document_status");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateTimestamps()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<User>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<Folder>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<Document>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<ChatSession>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<Quiz>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<SystemConfig>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<RegistrationOperation>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
