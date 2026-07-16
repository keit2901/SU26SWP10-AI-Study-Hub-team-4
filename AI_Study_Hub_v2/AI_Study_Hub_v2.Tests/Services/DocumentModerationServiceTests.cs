using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Tests.Support;
using FluentAssertions;
using Moq;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class DocumentModerationServiceTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task DeleteAsync_DelegatesToPrivilegedCoordinator(bool expected)
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var documentId = Guid.NewGuid();
        var coordinator = new Mock<IStorageDeletionCoordinator>(MockBehavior.Strict);
        coordinator.Setup(c => c.DeletePrivilegedDocumentAsync(documentId, It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var sut = new DocumentModerationService(db, coordinator.Object);

        (await sut.DeleteAsync(documentId, CancellationToken.None)).Should().Be(expected);
        coordinator.VerifyAll();
    }
}
