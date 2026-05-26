using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AI_Study_Hub_v2.Tests.Support;

/// <summary>
/// Builds an in-memory <see cref="AppDbContext"/> per test, pre-seeded with the same
/// two roles the migration installs. Each call uses a unique database name so
/// tests are fully isolated.
/// </summary>
internal static class TestDb
{
    public static AppDbContext CreateInMemory(string? databaseName = null, bool seedRoles = true)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            // Suppress the warning about the InMemory provider not supporting transactions.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        // Use a test-only subclass so the InMemory provider doesn't try to map
        // the pgvector / Postgres-ENUM properties (which only the Npgsql provider
        // understands). Auth-layer tests don't touch documents/chunks anyway.
        var ctx = new TestAppDbContext(options);

        if (seedRoles)
        {
            ctx.Roles.AddRange(
                new Role
                {
                    Id = 1,
                    RoleName = Role.AdminRoleName,
                    Description = "Admin",
                    CreatedAt = new DateTimeOffset(2026, 5, 23, 0, 0, 0, TimeSpan.Zero)
                },
                new Role
                {
                    Id = 2,
                    RoleName = Role.StudentRoleName,
                    Description = "Student",
                    CreatedAt = new DateTimeOffset(2026, 5, 23, 0, 0, 0, TimeSpan.Zero)
                });
            ctx.SaveChanges();
        }

        return ctx;
    }

    /// <summary>
    /// Builds an InMemory DbContext that DOES map <see cref="Document"/> and
    /// <see cref="Folder"/> (D5 document-service tests need them) but still hides
    /// <see cref="DocumentChunk"/> because its <c>Pgvector.Vector</c> column type
    /// is not understood by the InMemory provider. The Postgres ENUM mapping for
    /// <c>Document.Status</c> is also dropped here — InMemory persists the .NET
    /// enum value directly, which is what D5 service-level tests want.
    /// </summary>
    public static AppDbContext CreateInMemoryWithDocuments(string? databaseName = null, bool seedRoles = true)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var ctx = new TestDocsDbContext(options);

        if (seedRoles)
        {
            ctx.Roles.AddRange(
                new Role
                {
                    Id = 1,
                    RoleName = Role.AdminRoleName,
                    Description = "Admin",
                    CreatedAt = new DateTimeOffset(2026, 5, 23, 0, 0, 0, TimeSpan.Zero)
                },
                new Role
                {
                    Id = 2,
                    RoleName = Role.StudentRoleName,
                    Description = "Student",
                    CreatedAt = new DateTimeOffset(2026, 5, 23, 0, 0, 0, TimeSpan.Zero)
                });
            ctx.SaveChanges();
        }

        return ctx;
    }

    /// <summary>
    /// Test-time DbContext that hides Phase 2 entities the InMemory provider can't map:
    /// - <see cref="DocumentChunk"/> uses <c>Pgvector.Vector</c> (Npgsql-only)
    /// - <see cref="Document"/> uses the <c>public.document_status</c> Postgres enum
    /// - Postgres extensions (pgcrypto/vector) and the enum declaration are ignored
    /// </summary>
    private sealed class TestAppDbContext : AppDbContext
    {
        public TestAppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<DocumentChunk>();
            modelBuilder.Ignore<Document>();
            modelBuilder.Ignore<Folder>();
        }
    }

    /// <summary>
    /// Test-time DbContext used by D5 document-service tests: keeps Document + Folder
    /// mapped (they have no Npgsql-only types beyond a .NET enum) but still drops
    /// <see cref="DocumentChunk"/> (pgvector). The PG-specific column type hint
    /// declared on <c>Document.Status</c> is ignored by the InMemory provider, which
    /// stores the enum as its underlying int.
    /// </summary>
    private sealed class TestDocsDbContext : AppDbContext
    {
        public TestDocsDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<DocumentChunk>();
        }
    }
}
