using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag;

namespace AI_Study_Hub_v2.Tests;

[TestFixture]
public sealed class RagContractTests
{
    [Test]
    public void RagOptions_Defaults_Should_Match_Sprint2Contract()
    {
        var options = new RagOptions();

        options.ChunkingStrategy.Should().Be("semantic");
        options.ChunkSizeChars.Should().Be(700);
        options.ChunkOverlapChars.Should().Be(70);
        options.MinChunkChars.Should().Be(200);
        options.MaxSectionChars.Should().Be(700);
        options.DefaultTopK.Should().Be(5);
        options.MaxTopK.Should().Be(10);
        options.EmbeddingDimensions.Should().Be(DocumentChunk.EmbeddingDimension);
        options.MaxContextChars.Should().Be(6000);
    }

    [Test]
    public void GroqOptions_Defaults_Should_Use_NonSecret_Runtime_Settings()
    {
        var options = new GroqOptions();

        options.ApiKey.Should().BeEmpty();
        options.Model.Should().Be("llama-3.3-70b-versatile");
        options.VisionModel.Should().Be("meta-llama/llama-4-scout-17b-16e-instruct");
        options.Endpoint.Should().Be("https://api.groq.com/openai/v1");
        options.Temperature.Should().Be(0.2);
        options.MaxTokens.Should().Be(4096);
    }

    [Test]
    public void RagSearchDtos_Should_Preserve_CitationFields()
    {
        var documentId = Guid.NewGuid();
        var request = new RagSearchRequest(
            Query: "What is AI Study Hub used for?",
            DocumentId: documentId,
            FolderId: null,
            SubjectCode: "SWP391",
            Semester: "SU26");
        var result = new RagSearchResultDto(
            SourceLabel: "S1",
            DocumentId: documentId,
            FileName: "swp391-rag-demo-su26.pdf",
            ChunkIndex: 0,
            PageNumber: 1,
            ContentExcerpt: "AI Study Hub is a platform for SWP391 students.",
            Score: 0.92);

        request.TopK.Should().Be(5);
        result.DocumentId.Should().Be(documentId);
        result.SourceLabel.Should().Be("S1");
        result.PageNumber.Should().Be(1);
        result.ContentExcerpt.Should().Contain("SWP391");
    }

    [Test]
    public void AiChatDtos_Should_Preserve_Answer_And_SourceContracts()
    {
        var documentId = Guid.NewGuid();
        var request = new AiChatAskRequest(
            Question: "Which semester is mentioned in the document?",
            DocumentId: null,
            FolderId: null,
            SubjectCode: "SWP391",
            Semester: "SU26");
        var source = new AiChatSourceDto(
            Label: "S1",
            DocumentId: documentId,
            FileName: "swp391-rag-demo-su26.pdf",
            ChunkIndex: 0,
            PageNumber: 1,
            Excerpt: "The demo semester is SU26.",
            Score: 0.95);
        var response = new AiChatAnswerResponse(
            Answer: "The semester mentioned is SU26. [S1]",
            Sources: new[] { source },
            RefusalReason: null,
            DurationMs: 120);

        request.TopK.Should().Be(5);
        response.Sources.Should().ContainSingle();
        response.Sources[0].Label.Should().Be("S1");
        response.Answer.Should().Contain("SU26");
        response.RefusalReason.Should().BeNull();
    }

    [Test]
    public void RagServiceContracts_Should_Keep_DocumentIngestionResultShape()
    {
        var documentId = Guid.NewGuid();
        var result = new DocumentIngestionResult(
            DocumentId: documentId,
            ChunkCount: 3,
            Success: true,
            ErrorMessage: null);

        result.DocumentId.Should().Be(documentId);
        result.ChunkCount.Should().Be(3);
        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }
}
