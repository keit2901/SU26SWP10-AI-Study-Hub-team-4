using AI_Study_Hub_v2.Controllers;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AI_Study_Hub_v2.Tests.Controllers;

[TestFixture]
public sealed class RagControllerTests
{
    [Test]
    public void Scoring_ReturnsConfiguredRagScoringInfo()
    {
        var options = new RagOptions
        {
            ChunkingStrategy = "semantic",
            ChunkSizeChars = 1200,
            ChunkOverlapChars = 180,
            MinChunkChars = 120,
            MaxSectionChars = 900,
            EmbeddingDimensions = 384,
            DefaultTopK = 6,
            MaxTopK = 12,
            EmbeddingCacheEnabled = true,
            ReRankEnabled = true,
            ReRankCandidateCount = 30,
            ReRankTopN = 7,
            HybridSearchEnabled = true,
            VectorWeight = 0.65d,
            SearchMode = "hybrid",
        };
        var sut = new RagController(
            Mock.Of<IRagSearchService>(),
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<RagController>.Instance);

        var result = sut.Scoring();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<RagScoringInfoResponse>().Subject;
        dto.ChunkSizeChars.Should().Be(1200);
        dto.ChunkOverlapChars.Should().Be(180);
        dto.ChunkingStrategy.Should().Be("semantic");
        dto.MinChunkChars.Should().Be(120);
        dto.MaxSectionChars.Should().Be(900);
        dto.EmbeddingDimensions.Should().Be(384);
        dto.DefaultTopK.Should().Be(6);
        dto.MaxTopK.Should().Be(12);
        dto.EmbeddingCacheEnabled.Should().BeTrue();
        dto.ReRankEnabled.Should().BeTrue();
        dto.ReRankCandidateCount.Should().Be(30);
        dto.ReRankTopN.Should().Be(7);
        dto.HybridSearchEnabled.Should().BeTrue();
        dto.VectorWeight.Should().Be(0.65d);
        dto.SearchMode.Should().Be("hybrid");
        dto.ScoreMeaning.Should().Contain("Lower score");
    }

    [Test]
    public void RagController_IsJwtProtected()
    {
        typeof(RagController)
            .GetCustomAttributes(inherit: false)
            .Should().Contain(attribute => attribute.GetType().Name == "AuthorizeAttribute");
    }
}
