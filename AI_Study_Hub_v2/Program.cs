using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using AI_Study_Hub_v2.Components;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Rag;
using AI_Study_Hub_v2.Services.Rag.Benchmarking;
using AI_Study_Hub_v2.Services.Supabase;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MudBlazor.Services;
using Npgsql;
using Pgvector.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
    builder.Logging.AddConsole();
    builder.Logging.AddDebug();
}

// Configuration ---------------------------------------------------------------
builder.Services
    .AddOptions<SupabaseOptions>()
    .Bind(builder.Configuration.GetSection(SupabaseOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Url), "Supabase:Url is required.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.JwtIssuer), "Supabase:JwtIssuer is required.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.JwtAudience), "Supabase:JwtAudience is required.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.JwtSecret) && o.JwtSecret.Length >= 32, "Supabase:JwtSecret is required and must be >= 32 characters. Set it via dotnet user-secrets.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.AnonKey), "Supabase:AnonKey is required. Set it via dotnet user-secrets.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ServiceRoleKey), "Supabase:ServiceRoleKey is required. Set it via dotnet user-secrets.")
    .ValidateOnStart();

builder.Services.Configure<SeedOptions>(builder.Configuration.GetSection(SeedOptions.SectionName));
builder.Services.Configure<RagOptions>(builder.Configuration.GetSection(RagOptions.SectionName));
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection("Ollama"));
builder.Services.Configure<GroqOptions>(builder.Configuration.GetSection(GroqOptions.SectionName));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.Configure<RecaptchaOptions>(builder.Configuration.GetSection(RecaptchaOptions.SectionName));
builder.Services.AddMemoryCache(options =>
{
    var ragCacheOptions = builder.Configuration.GetSection(RagOptions.SectionName).Get<RagOptions>() ?? new RagOptions();
    options.SizeLimit = Math.Max(1, ragCacheOptions.EmbeddingCacheMaxEntries);
});

var recaptchaBootstrap = builder.Configuration.GetSection(RecaptchaOptions.SectionName).Get<RecaptchaOptions>() ?? new();
if (!builder.Environment.IsDevelopment() && (!recaptchaBootstrap.Enabled || !recaptchaBootstrap.IsConfigured))
{
    throw new InvalidOperationException(
        "reCAPTCHA must be enabled and configured outside Development. Set Recaptcha:Enabled=true plus SiteKey and SecretKey via secure configuration.");
}

// Database --------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");

// Build a shared NpgsqlDataSource so we can register the public.document_status enum
// for the AppDbContext. EF Core uses this single data source for all AppDbContext
// instances (DI scope managed by AddDbContext).
//
// pgvector type registration is handled at the EF Core level via npgsql.UseVector()
// below — we don't need a data-source-level UseVector() in Pgvector 0.3.1.
var npgsqlDataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
npgsqlDataSourceBuilder.MapEnum<AI_Study_Hub_v2.Data.Entities.DocumentStatus>(
    pgName: "public.document_status");
npgsqlDataSourceBuilder.UseVector();
var npgsqlDataSource = npgsqlDataSourceBuilder.Build();
builder.Services.AddSingleton(npgsqlDataSource);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(npgsqlDataSource, npgsql =>
    {
        npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
        npgsql.UseVector();
    });
});

// Auth services ---------------------------------------------------------------
var supabaseBootstrap = builder.Configuration.GetSection(SupabaseOptions.SectionName).Get<SupabaseOptions>()
    ?? throw new InvalidOperationException("Supabase section is missing from configuration.");

if (string.IsNullOrWhiteSpace(supabaseBootstrap.JwtSecret) || supabaseBootstrap.JwtSecret.Length < 32)
{
    throw new InvalidOperationException(
        "Supabase:JwtSecret must be configured (>= 32 characters). Set it via 'dotnet user-secrets set Supabase:JwtSecret <value>'.");
}

builder.Services.AddHttpClient<IGoTrueClient, GoTrueClient>((sp, http) =>
{
    var opts = sp.GetRequiredService<IOptions<SupabaseOptions>>().Value;
    var baseUrl = opts.Url.TrimEnd('/') + "/auth/v1/";
    http.BaseAddress = new Uri(baseUrl);
    // Default apikey for non-admin endpoints. Admin requests override Authorization but keep apikey.
    http.DefaultRequestHeaders.TryAddWithoutValidation("apikey", opts.AnonKey);
    http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});

builder.Services.AddScoped<IAuthService, SupabaseAuthService>();

// Storage services -----------------------------------------------------------
// Phase 2 (SCRUM-13): Supabase Storage HTTP wrapper + document service.
// HttpClient is preconfigured with the storage base URL + service-role key so
// the rest of the codebase can call it via the abstraction only.
builder.Services.AddHttpClient<ISupabaseStorageClient, SupabaseStorageClient>((sp, http) =>
{
    var opts = sp.GetRequiredService<IOptions<SupabaseOptions>>().Value;
    var baseUrl = opts.Url.TrimEnd('/') + "/storage/v1/";
    http.BaseAddress = new Uri(baseUrl);
    http.DefaultRequestHeaders.TryAddWithoutValidation("apikey", opts.ServiceRoleKey);
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.ServiceRoleKey);
    http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});

builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IDocumentModerationService, DocumentModerationService>();
builder.Services.AddScoped<IEscalationService, EscalationService>();
builder.Services.AddScoped<IFolderService, FolderService>();
builder.Services.AddScoped<ICommunityService, CommunityService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<IAdminUserService, AdminUserService>();
builder.Services.AddScoped<IAiQuotaService, AiQuotaService>();

// Sprint 2 RAG services -------------------------------------------------------
builder.Services.AddScoped<ITextExtractionService, PdfTextExtractionService>();
builder.Services.AddScoped<BlockParser>();
builder.Services.AddScoped<SentenceSplitter>();
builder.Services.AddScoped<ChunkMerger>();
builder.Services.AddScoped<ChunkingService>();
builder.Services.AddScoped<FixedSizeChunkingService>();
builder.Services.AddScoped<IChunkingService>(sp =>
{
    var ragOptions = sp.GetRequiredService<IOptions<RagOptions>>().Value;
    return string.Equals(ragOptions.ChunkingStrategy, "fixed", StringComparison.OrdinalIgnoreCase)
        ? sp.GetRequiredService<FixedSizeChunkingService>()
        : sp.GetRequiredService<ChunkingService>();
});
builder.Services.AddHttpClient(nameof(SupabaseDocumentStorageReadService));
builder.Services.AddScoped<IDocumentStorageReadService, SupabaseDocumentStorageReadService>();
builder.Services.AddScoped<IDocumentIngestionService, DocumentIngestionService>();

builder.Services.AddScoped<IReRankService, ReRankService>();
builder.Services.AddScoped<IRagSearchService, RagSearchService>();
builder.Services.AddScoped<IAiChatService, SemanticKernelRagChatService>();
builder.Services.AddScoped<IAiChatCompletionClientFactory, AiChatCompletionClientFactory>();
builder.Services.AddHttpClient<GroqChatCompletionClient>();
builder.Services.AddHttpClient<GeminiChatCompletionClient>();
builder.Services.AddHttpClient<IImageDescriptionService, GroqVisionDescriptionService>();
builder.Services.AddHttpClient<OllamaEmbeddingService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<IEmbeddingService>(sp =>
{
    var ragOptions = sp.GetRequiredService<IOptions<RagOptions>>().Value;
    var ollama = sp.GetRequiredService<OllamaEmbeddingService>();
    if (!ragOptions.EmbeddingCacheEnabled)
    {
        return ollama;
    }

    return ActivatorUtilities.CreateInstance<CachingEmbeddingService>(sp, ollama);
});
builder.Services.AddHostedService<OllamaHealthCheck>();
// Sprint 3 services ----------------------------------------------------------
builder.Services.AddScoped<IAiAnswerReportService, AiAnswerReportService>();
builder.Services.AddScoped<IQuizService, QuizService>();

// Benchmarking services ------------------------------------------------------
builder.Services.AddSingleton<BenchmarkEvaluator>();
builder.Services.AddScoped<BenchmarkRunner>();
builder.Services.AddScoped<ChunkingBenchmarkService>();
builder.Services.AddHostedService<BenchmarkAutomationHostedService>();

// Demo UI: typed HttpClient targeting our own backend + per-circuit session state
static Uri ResolveDemoUiBackendBaseUrl(IServiceProvider sp)
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var baseUrl = cfg["DemoUi:BackendBaseUrl"];
    if (string.IsNullOrWhiteSpace(baseUrl))
    {
        // Fallback to the first HTTP endpoint declared in launch settings.
        baseUrl = "http://localhost:5240/";
    }
    if (!baseUrl.EndsWith('/'))
    {
        baseUrl += "/";
    }
    return new Uri(baseUrl);
}

builder.Services.AddHttpClient<AuthApiClient>((sp, http) =>
{
    http.BaseAddress = ResolveDemoUiBackendBaseUrl(sp);
});
// SCRUM-12/26: Blazor upload form posts here (multipart). Same backend base URL.
builder.Services.AddHttpClient<DocumentApiClient>((sp, http) =>
{
    http.BaseAddress = ResolveDemoUiBackendBaseUrl(sp);
    // 50 MB body + slow Kestrel re-entry: bump above default 100s for big PDFs.
    http.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddHttpClient<FolderApiClient>((sp, http) =>
{
    http.BaseAddress = ResolveDemoUiBackendBaseUrl(sp);
});
builder.Services.AddHttpClient<CommunityApiClient>((sp, http) =>
{
    http.BaseAddress = ResolveDemoUiBackendBaseUrl(sp);
});
builder.Services.AddHttpClient<AdminApiClient>((sp, http) =>
{
    http.BaseAddress = ResolveDemoUiBackendBaseUrl(sp);
});
builder.Services.AddHttpClient<AiChatApiClient>((sp, http) =>
{
    http.BaseAddress = ResolveDemoUiBackendBaseUrl(sp);
    http.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddHttpClient<QuizApiClient>((sp, http) =>
{
    http.BaseAddress = ResolveDemoUiBackendBaseUrl(sp);
    http.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddHttpClient<AdminDashboardApiClient>((sp, http) =>
{
    http.BaseAddress = ResolveDemoUiBackendBaseUrl(sp);
});
builder.Services.AddHttpClient<BenchmarkApiClient>((sp, http) =>
{
    http.BaseAddress = ResolveDemoUiBackendBaseUrl(sp);
});
builder.Services.AddHttpClient<IRecaptchaVerificationService, RecaptchaVerificationService>(http =>
{
    http.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped<IRoleCatalogService, RoleCatalogService>();
builder.Services.AddScoped<AuthSessionState>();
builder.Services.AddScoped<AuthPersistenceService>();
builder.Services.AddScoped<AiChatSessionState>();
builder.Services.AddScoped<IChatPersistenceService, ChatPersistenceService>();
builder.Services.AddScoped<IQuizService, QuizService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();

// Authentication / Authorization ---------------------------------------------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = supabaseBootstrap.JwtIssuer,
            ValidAudience = supabaseBootstrap.JwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(supabaseBootstrap.JwtSecret)),
            ClockSkew = TimeSpan.FromSeconds(30),
            // GoTrue stores roles in app_metadata.role and emits a top-level "role"
            // claim of "authenticated". Map ClaimTypes.Role from app_metadata so
            // [Authorize(Roles = "...")] works against domain roles like Admin/Student.
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.NameIdentifier,
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = ctx =>
            {
                if (ctx.Principal?.Identity is ClaimsIdentity identity)
                {
                    // Promote app_metadata.role (set by the seed step + admin endpoints) to a real Role claim.
                    var appMetaRole = identity.FindFirst("app_metadata")?.Value;
                    if (!string.IsNullOrEmpty(appMetaRole))
                    {
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(appMetaRole);
                            if (doc.RootElement.TryGetProperty("role", out var roleProp) && roleProp.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                var role = roleProp.GetString();
                                if (!string.IsNullOrEmpty(role) && !identity.HasClaim(ClaimTypes.Role, role))
                                {
                                    identity.AddClaim(new Claim(ClaimTypes.Role, role));
                                }
                            }
                        }
                        catch
                        {
                            // ignore parse errors
                        }
                    }
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// MVC + Razor Components ------------------------------------------------------
builder.Services.AddControllers();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

var app = builder.Build();

// Migrate + seed default admin (idempotent) -----------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var goTrue = scope.ServiceProvider.GetRequiredService<IGoTrueClient>();
    var seedOptions = scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        await db.Database.MigrateAsync();
        startupLogger.LogInformation("Database migrations applied.");
        await EnsurePhase3SchemaAsync(db, startupLogger);

        await SeedDefaultAdminAsync(db, goTrue, seedOptions, startupLogger);
        await SeedDefaultModeratorAsync(db, goTrue, seedOptions, startupLogger);
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "Failed to apply migrations or seed default accounts.");
        throw;
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    var agentationPath = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "../../agentation/node_modules"));
    if (Directory.Exists(agentationPath))
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(agentationPath),
            RequestPath = "/agentation-static"
        });
    }
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static async Task SeedDefaultAdminAsync(AppDbContext db, IGoTrueClient gotrue, SeedOptions seedOptions, ILogger logger)
{
    var existingAdmin = await db.Users
        .Include(u => u.Role)
        .AnyAsync(u => u.Role.RoleName == Role.AdminRoleName);

    if (existingAdmin)
    {
        logger.LogInformation("Default admin seed skipped: at least one admin already exists.");
        return;
    }

    var cfg = seedOptions.DefaultAdmin;
    if (cfg is null
        || string.IsNullOrWhiteSpace(cfg.Email)
        || string.IsNullOrWhiteSpace(cfg.Username)
        || string.IsNullOrWhiteSpace(cfg.FullName)
        || string.IsNullOrWhiteSpace(cfg.Password))
    {
        logger.LogWarning("Default admin seed skipped: Seed:DefaultAdmin is not fully configured (Email/Username/FullName/Password). Set Seed:DefaultAdmin:Password via dotnet user-secrets.");
        return;
    }

    var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.RoleName == Role.AdminRoleName);
    if (adminRole is null)
    {
        logger.LogError("Default admin seed failed: Admin role is not present in database.");
        return;
    }

    var emailLower = cfg.Email.Trim().ToLowerInvariant();
    var usernameTrim = cfg.Username.Trim();

    // Avoid duplicate insert into public.users when GoTrue already has the identity but EF was reset.
    var existing = await gotrue.AdminGetUserByEmailAsync(emailLower);
    Guid supabaseUserId;
    if (existing is not null && existing.Id != Guid.Empty)
    {
        supabaseUserId = existing.Id;
        logger.LogInformation("Default admin already exists in GoTrue: {Email}. Will reuse identity.", emailLower);
    }
    else
    {
        var created = await gotrue.AdminCreateUserAsync(
            emailLower,
            cfg.Password,
            userMetadata: new Dictionary<string, object?>
            {
                ["username"] = usernameTrim,
                ["full_name"] = cfg.FullName.Trim(),
            },
            appMetadata: new Dictionary<string, object?>
            {
                ["role"] = Role.AdminRoleName,
            });
        supabaseUserId = created.Id;
        logger.LogInformation("Default admin created in GoTrue: {Email}", emailLower);
    }

    var profileExists = await db.Users.AnyAsync(u => u.SupabaseUserId == supabaseUserId);
    if (profileExists)
    {
        logger.LogInformation("Default admin profile already exists in public.users — skip insert.");
        return;
    }

    var clash = await db.Users.AnyAsync(u => u.Username == usernameTrim);
    if (clash)
    {
        logger.LogWarning("Default admin seed skipped: a user already exists with the same username.");
        return;
    }

    var now = DateTimeOffset.UtcNow;
    var admin = new User
    {
        Id = Guid.NewGuid(),
        RoleId = adminRole.Id,
        SupabaseUserId = supabaseUserId,
        Username = usernameTrim,
        FullName = cfg.FullName.Trim(),
        TotalTokensUsed = 0,
        IsActive = true,
        CreatedAt = now,
        UpdatedAt = now,
    };

    db.Users.Add(admin);
    await db.SaveChangesAsync();
    logger.LogInformation("Default admin profile inserted: {Email} (supabase_user_id={Id})", emailLower, supabaseUserId);
}

static async Task EnsurePhase3SchemaAsync(AppDbContext db, ILogger logger)
{
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS benchmark_results (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            model_name character varying(100) NOT NULL,
            provider character varying(50) NOT NULL,
            run_at timestamp with time zone NOT NULL,
            overall_score double precision NOT NULL,
            citation_accuracy double precision NOT NULL,
            hallucination_rate double precision NOT NULL,
            refusal_accuracy double precision NOT NULL,
            tutoring_quality double precision NOT NULL,
            diagram_accuracy double precision NOT NULL,
            p50_latency_ms bigint NOT NULL,
            p95_latency_ms bigint NOT NULL,
            total_questions integer NOT NULL,
            passed_questions integer NOT NULL,
            failed_questions integer NOT NULL,
            is_automated boolean NOT NULL,
            alert_triggered boolean NOT NULL,
            payload_json jsonb NOT NULL DEFAULT '{{}}'::jsonb,
            created_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
        );
        """);

    await db.Database.ExecuteSqlRawAsync("""
        CREATE INDEX IF NOT EXISTS ix_benchmark_results_is_automated
        ON benchmark_results (is_automated);
        """);

    await db.Database.ExecuteSqlRawAsync("""
        CREATE INDEX IF NOT EXISTS ix_benchmark_results_model_run_at
        ON benchmark_results (model_name, run_at);
        """);

    await db.Database.ExecuteSqlRawAsync("""
        CREATE INDEX IF NOT EXISTS ix_benchmark_results_run_at
        ON benchmark_results (run_at);
        """);

    await db.Database.ExecuteSqlRawAsync("""
        ALTER TABLE document_chunks
        ADD COLUMN IF NOT EXISTS search_vector tsvector
        GENERATED ALWAYS AS (to_tsvector('simple', coalesce(content, ''))) STORED;
        """);

    await db.Database.ExecuteSqlRawAsync("""
        CREATE INDEX IF NOT EXISTS ix_document_chunks_search_vector
        ON document_chunks USING GIN (search_vector);
        """);

    logger.LogInformation("Phase 3 schema bootstrap completed.");
}

static async Task SeedDefaultModeratorAsync(AppDbContext db, IGoTrueClient gotrue, SeedOptions seedOptions, ILogger logger)
{
    var existingModerator = await db.Users
        .Include(u => u.Role)
        .AnyAsync(u => u.Role.RoleName == Role.ModeratorRoleName);

    if (existingModerator)
    {
        logger.LogInformation("Default moderator seed skipped: at least one moderator already exists.");
        return;
    }

    var cfg = seedOptions.DefaultModerator;
    if (cfg is null
        || string.IsNullOrWhiteSpace(cfg.Email)
        || string.IsNullOrWhiteSpace(cfg.Username)
        || string.IsNullOrWhiteSpace(cfg.FullName)
        || string.IsNullOrWhiteSpace(cfg.Password))
    {
        logger.LogWarning("Default moderator seed skipped: Seed:DefaultModerator is not fully configured (Email/Username/FullName/Password). Set Seed:DefaultModerator:Password via dotnet user-secrets.");
        return;
    }

    var moderatorRole = await db.Roles.FirstOrDefaultAsync(r => r.RoleName == Role.ModeratorRoleName);
    if (moderatorRole is null)
    {
        logger.LogError("Default moderator seed failed: Moderator role is not present in database.");
        return;
    }

    var emailLower = cfg.Email.Trim().ToLowerInvariant();
    var usernameTrim = cfg.Username.Trim();

    // Avoid duplicate insert into public.users when GoTrue already has the identity but EF was reset.
    var existing = await gotrue.AdminGetUserByEmailAsync(emailLower);
    Guid supabaseUserId;
    if (existing is not null && existing.Id != Guid.Empty)
    {
        supabaseUserId = existing.Id;
        logger.LogInformation("Default moderator already exists in GoTrue: {Email}. Will reuse identity.", emailLower);
    }
    else
    {
        var created = await gotrue.AdminCreateUserAsync(
            emailLower,
            cfg.Password,
            userMetadata: new Dictionary<string, object?>
            {
                ["username"] = usernameTrim,
                ["full_name"] = cfg.FullName.Trim(),
            },
            appMetadata: new Dictionary<string, object?>
            {
                ["role"] = Role.ModeratorRoleName,
            });
        supabaseUserId = created.Id;
        logger.LogInformation("Default moderator created in GoTrue: {Email}", emailLower);
    }

    var profileExists = await db.Users.AnyAsync(u => u.SupabaseUserId == supabaseUserId);
    if (profileExists)
    {
        logger.LogInformation("Default moderator profile already exists in public.users — skip insert.");
        return;
    }

    var clash = await db.Users.AnyAsync(u => u.Username == usernameTrim);
    if (clash)
    {
        logger.LogWarning("Default moderator seed skipped: a user already exists with the same username.");
        return;
    }

    var now = DateTimeOffset.UtcNow;
    var moderator = new User
    {
        Id = Guid.NewGuid(),
        RoleId = moderatorRole.Id,
        SupabaseUserId = supabaseUserId,
        Username = usernameTrim,
        FullName = cfg.FullName.Trim(),
        TotalTokensUsed = 0,
        IsActive = true,
        CreatedAt = now,
        UpdatedAt = now,
    };

    db.Users.Add(moderator);
    await db.SaveChangesAsync();
    logger.LogInformation("Default moderator profile inserted: {Email} (supabase_user_id={Id})", emailLower, supabaseUserId);
}
