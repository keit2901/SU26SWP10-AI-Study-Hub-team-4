using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services.Supabase;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, string? userAgent, string? ipAddress, CancellationToken cancellationToken = default);

    Task<AuthResponse> LoginAsync(LoginRequest request, string? userAgent, string? ipAddress, CancellationToken cancellationToken = default);

    Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, string? userAgent, string? ipAddress, CancellationToken cancellationToken = default);

    Task LogoutAsync(string accessToken, CancellationToken cancellationToken = default);

    Task<UserDto> GetCurrentUserAsync(Guid supabaseUserId, string? email = null, CancellationToken cancellationToken = default);

    Task<UserDto> UpdateUserAsync(Guid supabaseUserId, string accessToken, string? email, string? username, string? fullName, string? password, CancellationToken cancellationToken = default);
}

/// <summary>
/// Phase 2 (Supabase Local) implementation of <see cref="IAuthService"/>. Identity
/// (password hash, refresh token rotation, email uniqueness) is owned by GoTrue
/// in the <c>auth.*</c> schema. We mirror just enough into <c>public.users</c>
/// to keep app-specific fields like <c>username</c>, <c>full_name</c> and the
/// <c>role_id</c> FK to <c>public.roles</c>.
/// </summary>
public sealed class SupabaseAuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IGoTrueClient _gotrue;
    private readonly ILogger<SupabaseAuthService> _logger;

    public SupabaseAuthService(AppDbContext db, IGoTrueClient gotrue, ILogger<SupabaseAuthService> logger)
    {
        _db = db;
        _gotrue = gotrue;
        _logger = logger;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, string? userAgent, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var username = request.Username.Trim();
        var fullName = request.FullName.Trim();

        // 1. App-level uniqueness check on username (email uniqueness is enforced by GoTrue).
        var usernameTaken = await _db.Users.AnyAsync(u => u.Username == username, cancellationToken);
        if (usernameTaken)
        {
            throw new AuthException(409, "username_taken", "Username is already taken.");
        }

        var studentRole = await _db.Roles.FirstOrDefaultAsync(r => r.RoleName == Role.StudentRoleName, cancellationToken);
        if (studentRole is null)
        {
            throw new AuthException(500, "role_not_seeded", "Student role is missing from the database.");
        }

        // 2. Create the GoTrue identity. AUTOCONFIRM is on so we get a session back.
        var session = await _gotrue.SignUpAsync(
            email,
            request.Password,
            metadata: new Dictionary<string, object?>
            {
                ["username"] = username,
                ["full_name"] = fullName,
            },
            cancellationToken);

        if (session.User is null || session.User.Id == Guid.Empty)
        {
            throw new AuthException(500, "gotrue_no_user", "GoTrue did not return a user on signup.");
        }

        // 3. Mirror profile into public.users.
        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            RoleId = studentRole.Id,
            SupabaseUserId = session.User.Id,
            Username = username,
            FullName = fullName,
            TotalTokensUsed = 0,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Users.Add(user);

        var freePlan = await _db.Plans.FirstOrDefaultAsync(p => p.PlanKey == "free", cancellationToken);
        if (freePlan is not null)
        {
            _db.UserPlans.Add(new UserPlan
            {
                UserId = user.Id,
                PlanId = freePlan.Id,
                Status = "active",
                AssignedAt = now,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        user.Role = studentRole;

        return BuildAuthResponse(user, session);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? userAgent, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var session = await _gotrue.SignInWithPasswordAsync(email, request.Password, cancellationToken);
        if (session.User is null || session.User.Id == Guid.Empty)
        {
            throw new AuthException(401, "invalid_credentials", "Email or password is incorrect.");
        }

        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.SupabaseUserId == session.User.Id, cancellationToken);

        if (user is null)
        {
            // GoTrue knows the identity but no profile exists. This typically only happens for
            // users created out-of-band (e.g. an admin manually inserted into auth.users). For
            // Phase 1 we surface a clear error rather than auto-create a partial profile.
            throw new AuthException(409, "profile_missing", "Authentication succeeded but no application profile exists for this user.");
        }
        if (!user.IsActive)
        {
            throw new AuthException(403, "user_inactive", "User account is inactive.");
        }
        return BuildAuthResponse(user, session);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, string? userAgent, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw new AuthException(401, "missing_refresh_token", "Refresh token is required.");
        }

        var session = await _gotrue.RefreshAsync(request.RefreshToken, cancellationToken);
        if (session.User is null || session.User.Id == Guid.Empty)
        {
            throw new AuthException(401, "invalid_refresh_token", "Refresh token is invalid or has been revoked.");
        }

        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.SupabaseUserId == session.User.Id, cancellationToken);

        if (user is null)
        {
            throw new AuthException(409, "profile_missing", "Refresh succeeded but no application profile exists for this user.");
        }
        if (!user.IsActive)
        {
            throw new AuthException(403, "user_inactive", "User account is inactive.");
        }
        return BuildAuthResponse(user, session);
    }

    public async Task LogoutAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new AuthException(401, "missing_access_token", "Access token is required for logout.");
        }
        // Phase 1 mirrors the original "revoke ALL refresh tokens" behaviour.
        await _gotrue.SignOutAsync(accessToken, global: true, cancellationToken);
    }

    public async Task<UserDto> GetCurrentUserAsync(Guid supabaseUserId, string? email = null, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, cancellationToken);

        if (user is null)
        {
            throw new AuthException(404, "user_not_found", "User profile not found.");
        }

        return MapUser(user, email);
    }

    public async Task<UserDto> UpdateUserAsync(Guid supabaseUserId, string accessToken, string? email, string? username, string? fullName, string? password, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, cancellationToken);

        if (user is null)
        {
            throw new AuthException(404, "user_not_found", "User profile not found.");
        }

        if (!string.IsNullOrEmpty(username) && username != user.Username)
        {
            var usernameTaken = await _db.Users.AnyAsync(u => u.Username == username, cancellationToken);
            if (usernameTaken)
            {
                throw new AuthException(409, "username_taken", "Username is already taken.");
            }
            user.Username = username;
            user.FullName = username;
        }

        if (!string.IsNullOrEmpty(fullName))
        {
            user.FullName = fullName;
        }

        var metadata = new Dictionary<string, object?>();
        if (!string.IsNullOrEmpty(username)) metadata["username"] = username;
        if (!string.IsNullOrEmpty(fullName ?? (username))) metadata["full_name"] = fullName ?? username;

        var gotrueUser = await _gotrue.UpdateUserAsync(
            accessToken,
            email,
            password,
            metadata.Count > 0 ? metadata : null,
            cancellationToken);

        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return MapUser(user, gotrueUser.Email);
    }

    private static AuthResponse BuildAuthResponse(User user, GoTrueSession session)
    {
        var expiresAt = session.ExpiresAt > 0
            ? DateTimeOffset.FromUnixTimeSeconds(session.ExpiresAt)
            : DateTimeOffset.UtcNow.AddSeconds(session.ExpiresIn > 0 ? session.ExpiresIn : 900);

        return new AuthResponse
        {
            AccessToken = session.AccessToken,
            RefreshToken = session.RefreshToken,
            TokenType = "Bearer",
            ExpiresIn = session.ExpiresIn > 0 ? session.ExpiresIn : (int)(expiresAt - DateTimeOffset.UtcNow).TotalSeconds,
            ExpiresAt = expiresAt,
            User = MapUser(user, session.User?.Email),
        };
    }

    private static UserDto MapUser(User user, string? email) => new()
    {
        Id = user.Id,
        Email = email ?? string.Empty,
        Username = user.Username,
        FullName = user.FullName,
        Role = user.Role?.RoleName ?? string.Empty,
        IsActive = user.IsActive,
        CreatedAt = user.CreatedAt,
    };
}
