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

        var ctx = new AppDbContext(options);

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
}
