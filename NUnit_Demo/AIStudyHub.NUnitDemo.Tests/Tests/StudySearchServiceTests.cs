using AIStudyHub.NUnitDemo.Tests.Domain;
using Moq;

namespace AIStudyHub.NUnitDemo.Tests.Tests;

[TestFixture]
public sealed class StudySearchServiceTests
{
    private static readonly StudyDocument AlgebraGuide = new(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        "Linear Algebra Notes",
        "Vector spaces, matrix multiplication, eigenvalues, and basis transformations for exam review.",
        "student-1",
        IsProcessed: true,
        new[] { "math", "algebra" });

    private static readonly StudyDocument RagGuide = new(
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        "RAG Architecture Summary",
        "Retrieval augmented generation uses document chunks, embeddings, ranking, and citations.",
        "student-1",
        IsProcessed: true,
        new[] { "ai", "rag" });

    private static readonly StudyDocument OtherUserGuide = new(
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        "Private RAG Notes",
        "This document belongs to another student and must not leak into the current user's search results.",
        "student-2",
        IsProcessed: true,
        new[] { "ai", "privacy" });

    private static readonly StudyDocument PendingGuide = new(
        Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
        "Pending RAG Upload",
        "This upload is not processed yet, so demo search should ignore it.",
        "student-1",
        IsProcessed: false,
        new[] { "ai" });

    [Test]
    public async Task SearchAsync_Returns_Processed_Documents_For_Current_User_Only()
    {
        var embeddingClient = new Mock<IEmbeddingClient>(MockBehavior.Strict);
        embeddingClient
            .Setup(client => client.EmbedAsync("RAG", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { 0.8, 0.1, 0.1 });

        var service = CreateService(embeddingClient.Object);

        var results = await service.SearchAsync("RAG", "student-1");

        results.Should().ContainSingle();
        results[0].DocumentId.Should().Be(RagGuide.Id);
        results[0].Title.Should().Be("RAG Architecture Summary");
        results[0].Snippet.Should().Contain("Retrieval augmented generation");
        embeddingClient.Verify(client => client.EmbedAsync("RAG", It.IsAny<CancellationToken>()), Times.Once);
        embeddingClient.VerifyNoOtherCalls();
    }

    [TestCase("matrix", "Linear Algebra Notes")]
    [TestCase("RAG", "RAG Architecture Summary")]
    public async Task SearchAsync_Supports_Parameterized_Query_Examples(string query, string expectedTitle)
    {
        var embeddingClient = new Mock<IEmbeddingClient>();
        embeddingClient
            .Setup(client => client.EmbedAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { 0.2, 0.2, 0.2 });

        var service = CreateService(embeddingClient.Object);

        var results = await service.SearchAsync(query, "student-1");

        results.Select(result => result.Title).Should().Contain(expectedTitle);
    }

    [Test]
    public void SearchAsync_Rejects_Empty_Query()
    {
        var embeddingClient = new Mock<IEmbeddingClient>();
        var service = CreateService(embeddingClient.Object);

        var act = async () => await service.SearchAsync("   ", "student-1");

        act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("query")
            .WithMessage("Query must not be empty.*");
    }

    private static StudySearchService CreateService(IEmbeddingClient embeddingClient)
    {
        return new StudySearchService(
            new[] { AlgebraGuide, RagGuide, OtherUserGuide, PendingGuide },
            embeddingClient);
    }
}
