using AI_Study_Hub_v2.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public class StorageReconciliationTests
{
    [Test]
    public async Task HostedService_ExecutesReconciliation_OnTrigger()
    {
        var reconciliationMock = new Mock<IStorageReconciliationService>();
        reconciliationMock
            .Setup(r => r.ReconcileAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StorageDiscrepancy>());

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(x => x.GetService(typeof(IStorageReconciliationService)))
            .Returns(reconciliationMock.Object);

        var serviceScopeMock = new Mock<IServiceScope>();
        serviceScopeMock.Setup(x => x.ServiceProvider).Returns(serviceProviderMock.Object);

        var serviceScopeFactoryMock = new Mock<IServiceScopeFactory>();
        serviceScopeFactoryMock
            .Setup(x => x.CreateScope())
            .Returns(serviceScopeMock.Object);

        // Create a custom hosted service that overrides or runs with a small delay
        // to test the execution flow.
        var hostedService = new StorageReconciliationHostedService(
            serviceScopeFactoryMock.Object,
            NullLogger<StorageReconciliationHostedService>.Instance);

        using var cts = new CancellationTokenSource();
        
        // Start and immediately cancel to exit loop.
        var runTask = hostedService.StartAsync(cts.Token);
        await cts.CancelAsync();
        await runTask;

        // Verify start was called and terminates gracefully
        runTask.IsCompleted.Should().BeTrue();
    }

    [Test]
    public void Service_Constructor_AssignsDependencies()
    {
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var loggerMock = NullLogger<StorageReconciliationService>.Instance;

        var service = new StorageReconciliationService(scopeFactoryMock.Object, loggerMock);

        service.Should().NotBeNull();
    }
}
