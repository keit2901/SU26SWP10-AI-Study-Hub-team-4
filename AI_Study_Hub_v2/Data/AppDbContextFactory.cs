using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Pgvector.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Data;

/// <summary>
/// Used by EF Core CLI tooling (dotnet ef migrations / database update).
/// Reads the connection string directly so the full Program.cs (which validates
/// Supabase secrets and other runtime options) does not need to execute during
/// design-time migration commands.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<AppDbContext>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Postgres is not configured. Set it in appsettings.Development.json or User Secrets " +
                "(host=localhost, port=5432, database=postgres, user=postgres, password=<from infra/supabase/.env>).");

        // Mirror Program.cs: register the document_status enum on a custom NpgsqlDataSource
        // so EF design-time tooling can scaffold migrations that reference the enum column.
        // pgvector type mapping is handled by npgsql.UseVector() at the EF Core level below.
        var npgsqlDataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
        npgsqlDataSourceBuilder.MapEnum<DocumentStatus>(pgName: "public.document_status");
        var dataSource = npgsqlDataSourceBuilder.Build();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(dataSource, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                npgsql.UseVector();
            })
            .Options;

        return new AppDbContext(options);
    }
}
