using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

/// <summary>Tracks the immutable identity snapshot associated with a browser registration attempt.</summary>
public sealed class RegistrationAttemptState
{
    private RegistrationIdentitySnapshot? _lastDispatchedIdentity;
    private Guid _operationId;

    public Guid PrepareForDispatch(RegisterRequest request)
    {
        var identity = new RegistrationIdentitySnapshot(
            request.Email.Trim().ToLowerInvariant(),
            request.Username.Trim(),
            request.FullName.Trim());

        if (_lastDispatchedIdentity is null)
        {
            _operationId = request.RegistrationOperationId == Guid.Empty ? Guid.NewGuid() : request.RegistrationOperationId;
            _lastDispatchedIdentity = identity;
        }
        else if (_lastDispatchedIdentity != identity)
        {
            _operationId = Guid.NewGuid();
            _lastDispatchedIdentity = identity;
        }

        request.RegistrationOperationId = _operationId;
        return _operationId;
    }

    private sealed record RegistrationIdentitySnapshot(string Email, string Username, string FullName);
}
