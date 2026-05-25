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
}
