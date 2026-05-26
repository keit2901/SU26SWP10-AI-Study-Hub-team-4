using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Rag;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public class SemanticKernelRagChatServiceTests
{
    private static SemanticKernelRagChatService BuildSut(
        IRagSearchService ragSearchService,
        IAiChatCompletionClient completionClient,
        RagOptions? options = null) =>
        new(
            ragSearchService,
            completionClient,
            Microsoft.Extensions.Options.Options.Create(options ?? new RagOptions()),
            NullLogger<SemanticKernelRagChatService>.Instance);

    [Test]
    public async Task AskAsync_EmptyQuestion_Throws400_AndSkipsRetrievalAndProvider()
    {
        var rag = new Mock<IRagSearchService>(MockBehavior.Strict);
        var completion = new Mock<IAiChatCompletionClient>(MockBehavior.Strict);
        var sut = BuildSut(rag.Object, completion.Object);

        var act = () => sut.AskAsync(Guid.NewGuid(), new AiChatAskRequest("   ", null, null, null, null));

        var ex = await act.Should().ThrowAsync<AiChatException>();
        ex.Which.StatusCode.Should().Be(400);
        ex.Which.Code.Should().Be("question_required");
        rag.VerifyNoOtherCalls();
        completion.VerifyNoOtherCalls();
    }

    [Test]
    public async Task AskAsync_NoSources_ReturnsFallback_AndDoesNotCallProvider()
    {
        var supabaseUserId = Guid.NewGuid();
        RagSearchRequest? capturedRequest = null;
        Guid? capturedUserId = null;

        var rag = new Mock<IRagSearchService>(MockBehavior.Strict);
        rag.Setup(s => s.SearchAsync(
                It.IsAny<Guid>(),
                It.IsAny<RagSearchRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, RagSearchRequest, CancellationToken>((uid, request, _) =>
            {
                capturedUserId = uid;
                capturedRequest = request;
            })
            .ReturnsAsync(Array.Empty<RagSearchResultDto>());

        var completion = new Mock<IAiChatCompletionClient>(MockBehavior.Strict);
        var sut = BuildSut(rag.Object, completion.Object);

        var response = await sut.AskAsync(supabaseUserId, new AiChatAskRequest(
            "  Explain RAG  ",
            DocumentId: null,
            FolderId: null,
            SubjectCode: " swp391 ",
            Semester: " su26 "));

        response.Answer.Should().Be(SemanticKernelRagChatService.NoSourcesAnswer);
        response.RefusalReason.Should().Be("no_sources");
        response.Sources.Should().BeEmpty();
        response.DurationMs.Should().NotBeNull();
        capturedUserId.Should().Be(supabaseUserId);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Query.Should().Be("Explain RAG");
        capturedRequest.SubjectCode.Should().Be("SWP391");
        capturedRequest.Semester.Should().Be("SU26");
        capturedRequest.TopK.Should().Be(5);
        rag.VerifyAll();
        completion.VerifyNoOtherCalls();
    }

    [Test]
    public async Task AskAsync_WhitespaceSourceExcerpts_ReturnsFallback_AndDoesNotCallProvider()
    {
        var rag = new Mock<IRagSearchService>(MockBehavior.Strict);
        rag.Setup(s => s.SearchAsync(
                It.IsAny<Guid>(),
                It.IsAny<RagSearchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new RagSearchResultDto("", Guid.NewGuid(), "blank-1.pdf", 0, null, "   ", 0.8),
                new RagSearchResultDto("", Guid.NewGuid(), "blank-2.pdf", 1, null, "\r\n\t", 0.7),
            });

        var completion = new Mock<IAiChatCompletionClient>(MockBehavior.Strict);
        var sut = BuildSut(rag.Object, completion.Object);

        var response = await sut.AskAsync(Guid.NewGuid(), new AiChatAskRequest("Explain the notes", null, null, null, null));

        response.Answer.Should().Be(SemanticKernelRagChatService.NoSourcesAnswer);
        response.RefusalReason.Should().Be("no_sources");
        response.Sources.Should().BeEmpty();
        rag.VerifyAll();
        completion.VerifyNoOtherCalls();
    }

    [Test]
    public async Task AskAsync_WithSources_CallsProvider_WithGroundedPrompt_AndReturnsCitations()
    {
        var supabaseUserId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var sources = new List<RagSearchResultDto>
        {
            new("", documentId, "rag-notes.pdf", 3, 2, "RAG retrieves relevant chunks before generation.", 0.91),
        };

        Guid? capturedUserId = null;
        RagSearchRequest? capturedRagRequest = null;
        AiChatCompletionRequest? capturedCompletionRequest = null;

        var rag = new Mock<IRagSearchService>(MockBehavior.Strict);
        rag.Setup(s => s.SearchAsync(
                It.IsAny<Guid>(),
                It.IsAny<RagSearchRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, RagSearchRequest, CancellationToken>((uid, request, _) =>
            {
                capturedUserId = uid;
                capturedRagRequest = request;
            })
            .ReturnsAsync(sources);

        var completion = new Mock<IAiChatCompletionClient>(MockBehavior.Strict);
        completion.Setup(c => c.CompleteAsync(
                It.IsAny<AiChatCompletionRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<AiChatCompletionRequest, CancellationToken>((request, _) => capturedCompletionRequest = request)
            .ReturnsAsync("RAG retrieves chunks before generation. [S1]");

        var sut = BuildSut(rag.Object, completion.Object, new RagOptions { MaxTopK = 3 });

        var response = await sut.AskAsync(supabaseUserId, new AiChatAskRequest(
            "How does RAG work?",
            documentId,
            FolderId: null,
            SubjectCode: null,
            Semester: null,
            TopK: 50));

        response.Answer.Should().Be("RAG retrieves chunks before generation. [S1]");
        response.RefusalReason.Should().BeNull();
        response.Sources.Should().ContainSingle();
        response.Sources[0].Label.Should().Be("S1");
        response.Sources[0].DocumentId.Should().Be(documentId);
        response.Sources[0].FileName.Should().Be("rag-notes.pdf");
        response.Sources[0].PageNumber.Should().Be(2);
        response.Sources[0].ChunkIndex.Should().Be(3);
        response.Sources[0].Score.Should().Be(0.91);
        capturedUserId.Should().Be(supabaseUserId);
        capturedRagRequest.Should().NotBeNull();
        capturedRagRequest!.TopK.Should().Be(3);
        capturedCompletionRequest.Should().NotBeNull();
        capturedCompletionRequest!.SystemPrompt.Should().Contain("Answer only using the provided source excerpts");
        capturedCompletionRequest.SystemPrompt.Should().Contain("Do not invent citations");
        capturedCompletionRequest.UserPrompt.Should().Contain("[S1]");
        capturedCompletionRequest.UserPrompt.Should().Contain("rag-notes.pdf");
        capturedCompletionRequest.UserPrompt.Should().Contain("Answer only from the source excerpts above");
        capturedCompletionRequest.UserPrompt.Should().Contain("Do not use outside knowledge");
        rag.VerifyAll();
        completion.VerifyAll();
    }

    [Test]
    public async Task AskAsync_RagDocumentException_MapsToAiChatException()
    {
        var rag = new Mock<IRagSearchService>(MockBehavior.Strict);
        rag.Setup(s => s.SearchAsync(
                It.IsAny<Guid>(),
                It.IsAny<RagSearchRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DocumentException(403, "user_inactive", "inactive"));

        var completion = new Mock<IAiChatCompletionClient>(MockBehavior.Strict);
        var sut = BuildSut(rag.Object, completion.Object);

        var act = () => sut.AskAsync(Guid.NewGuid(), new AiChatAskRequest("RAG?", null, null, null, null));

        var ex = await act.Should().ThrowAsync<AiChatException>();
        ex.Which.StatusCode.Should().Be(403);
        ex.Which.Code.Should().Be("user_inactive");
        completion.VerifyNoOtherCalls();
    }

    [Test]
    public async Task AskAsync_ProviderFailure_Throws503_AiProviderUnavailable()
    {
        var rag = new Mock<IRagSearchService>(MockBehavior.Strict);
        rag.Setup(s => s.SearchAsync(
                It.IsAny<Guid>(),
                It.IsAny<RagSearchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new RagSearchResultDto("", Guid.NewGuid(), "rag.pdf", 0, null, "Semantic Kernel calls Groq after retrieval.", 0.5),
            });

        var completion = new Mock<IAiChatCompletionClient>(MockBehavior.Strict);
        completion.Setup(c => c.CompleteAsync(
                It.IsAny<AiChatCompletionRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiChatProviderException("groq_http_error", "unavailable"));

        var sut = BuildSut(rag.Object, completion.Object);

        var act = () => sut.AskAsync(Guid.NewGuid(), new AiChatAskRequest("How is Groq used?", null, null, null, null));

        var ex = await act.Should().ThrowAsync<AiChatException>();
        ex.Which.StatusCode.Should().Be(503);
        ex.Which.Code.Should().Be("ai_provider_unavailable");
        rag.VerifyAll();
        completion.VerifyAll();
    }
}
