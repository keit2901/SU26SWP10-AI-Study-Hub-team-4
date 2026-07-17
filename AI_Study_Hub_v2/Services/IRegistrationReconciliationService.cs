namespace AI_Study_Hub_v2.Services;

public interface IRegistrationReconciliationService
{
    Task RunOnceAsync(CancellationToken cancellationToken = default);
}
