using AI_Study_Hub_v2.Data;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public interface ISelfRegistrationPolicy
{
    Task EnsureAllowedAsync(CancellationToken cancellationToken = default);
}

public sealed class SelfRegistrationPolicy : ISelfRegistrationPolicy
{
    private const string ConfigKey = "auth.allow_self_registration";
    private readonly AppDbContext _db;
    private readonly ILogger<SelfRegistrationPolicy> _logger;

    public SelfRegistrationPolicy(AppDbContext db, ILogger<SelfRegistrationPolicy> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task EnsureAllowedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var value = await _db.SystemConfigs.AsNoTracking()
                .Where(config => config.Key == ConfigKey)
                .Select(config => config.Value)
                .SingleOrDefaultAsync(cancellationToken);

            if (!bool.TryParse(value, out var allowed))
            {
                _logger.LogError("Self-registration policy is unavailable because its configuration is missing or invalid.");
                throw Unavailable();
            }

            if (!allowed)
            {
                throw new AuthException(403, "self_registration_disabled", "Self-registration is currently disabled.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AuthException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Self-registration policy could not be read.");
            throw Unavailable();
        }
    }

    private static AuthException Unavailable() =>
        new(503, "registration_policy_unavailable", "Registration is temporarily unavailable.");
}
