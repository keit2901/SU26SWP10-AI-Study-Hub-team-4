using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class ReRankServiceTests
{
    [Test]
    public async Task ReRankAsync_PrioritizesExactPhraseMatch()
    {
        var sut = new ReRankService(
            OptionsFactory.Create(new RagOptions { ReRankEnabled = true }),
            NullLogger<ReRankService>.Instance);

        var candidates = new[]
        {
            new ReRankCandidate(Guid.NewGuid(), "a.pdf", 0, null, "general architecture overview", 0.95d, 0.05d, 0.95d, null),
            new ReRankCandidate(Guid.NewGuid(), "b.pdf", 0, null, "SWP391 capstone plan and checklist", 0.20d, 1.00d, 0.20d, null)
        };

        var results = await sut.ReRankAsync("SWP391 plan", candidates, 2);

        results.Should().HaveCount(2);
        results[0].FileName.Should().Be("b.pdf");
        results[0].ReRankScore.Should().HaveValue();
        results[1].ReRankScore.Should().HaveValue();
        results[0].ReRankScore!.Value.Should().BeGreaterThan(results[1].ReRankScore!.Value);
    }
}
