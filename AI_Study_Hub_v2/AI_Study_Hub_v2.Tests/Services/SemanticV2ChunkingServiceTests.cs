using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class SemanticV2ChunkingServiceTests
{
    [TestCase("", 0)]
    [TestCase("abc", 1)]
    [TestCase("xin chào", 4)]
    [TestCase("!!!", 3)]
    [TestCase("你好", 2)]
    public void Estimate_ReturnsConservativeMultilingualTokenCounts(string input, int expected)
    {
        var sut = new ConservativeTokenEstimator();

        sut.Estimate(input).Should().Be(expected);
    }

    [Test]
    public void Chunk_PreservesHeadingAndListBoundaries_UsesConfiguredTokenOverlap()
    {
        var options = new RagOptions
        {
            SemanticTargetTokens = 20,
            SemanticMinTokens = 10,
            SemanticMaxTokens = 32,
            SemanticOverlapTokens = 2,
        };
        var sut = new SemanticV2ChunkingService(new ConservativeTokenEstimator(), Microsoft.Extensions.Options.Options.Create(options));
        var documentId = Guid.NewGuid();

        var chunks = sut.Chunk(documentId, new[]
        {
            new ExtractedPage(1, """
                Chapter One

                This wrapped prose continues
                on the following line.

                - first item has enough content
                  with its continuation
                - second item has enough content
                """),
        });

        chunks.Should().NotBeEmpty();
        chunks.Select(chunk => chunk.ChunkIndex).Should().Equal(Enumerable.Range(0, chunks.Count));
        chunks.Should().OnlyContain(chunk => chunk.SectionTitle == "Chapter One");
        chunks.Should().OnlyContain(chunk => new ConservativeTokenEstimator().Estimate(chunk.Content) <= options.SemanticMaxTokens);
        chunks.Select(chunk => chunk.Content).Should().Contain(content => content.Contains("- first item has enough content\nwith its continuation"));
    }

    [Test]
    public void Chunk_DoesNotCrossPageOrHeadingBoundaries_AndAlwaysProgressesForLongCode()
    {
        var options = new RagOptions
        {
            SemanticTargetTokens = 6,
            SemanticMinTokens = 3,
            SemanticMaxTokens = 10,
            SemanticOverlapTokens = 2,
        };
        var sut = new SemanticV2ChunkingService(new ConservativeTokenEstimator(), Microsoft.Extensions.Options.Options.Create(options));

        var chunks = sut.Chunk(Guid.NewGuid(), new[]
        {
            new ExtractedPage(1, """
                FIRST SECTION

                ```
                abcdefghijklmnopqrstuvwxyz
                ```
                """),
            new ExtractedPage(2, "SECOND SECTION\n\nSecond page prose has content."),
        });

        chunks.Should().NotBeEmpty();
        chunks.Should().OnlyContain(chunk => chunk.Content.Length > 0);
        chunks.Where(chunk => chunk.PageNumber == 1).Should().OnlyContain(chunk => chunk.SectionTitle == "FIRST SECTION");
        chunks.Where(chunk => chunk.PageNumber == 2).Should().OnlyContain(chunk => chunk.SectionTitle == "SECOND SECTION");
        chunks.Select(chunk => chunk.ChunkIndex).Should().Equal(Enumerable.Range(0, chunks.Count));
    }

    [Test]
    public void Chunk_LongSentenceAndCjkContent_IsDeterministicBoundedAndDoesNotTreatProseAsHeading()
    {
        var options = new RagOptions
        {
            SemanticTargetTokens = 12,
            SemanticMinTokens = 6,
            SemanticMaxTokens = 18,
            SemanticOverlapTokens = 3,
        };
        var sut = new SemanticV2ChunkingService(new ConservativeTokenEstimator(), Microsoft.Extensions.Options.Options.Create(options));
        var pages = new[] { new ExtractedPage(1, "This is a normal sentence. 这是一个很长的中文段落用于验证不会破坏Unicode标量边界并且能够持续向前处理。") };

        var first = sut.Chunk(Guid.NewGuid(), pages);
        var second = sut.Chunk(Guid.NewGuid(), pages);

        first.Should().NotBeEmpty();
        first.Select(chunk => chunk.Content).Should().Equal(second.Select(chunk => chunk.Content));
        first.Should().OnlyContain(chunk => chunk.SectionTitle == null);
        first.Should().OnlyContain(chunk => new ConservativeTokenEstimator().Estimate(chunk.Content) <= options.SemanticMaxTokens);
        first.Select(chunk => chunk.Content).Should().NotContain(content => content.Contains('\uFFFD'));
    }

    [Test]
    public void Chunk_HardMaximumAndOversizedOverlap_AreAlwaysBounded()
    {
        var options = new RagOptions { SemanticTargetTokens = 144, SemanticMinTokens = 72, SemanticMaxTokens = 192, SemanticOverlapTokens = 24 };
        var estimator = new ConservativeTokenEstimator();
        var sut = new SemanticV2ChunkingService(estimator, Microsoft.Extensions.Options.Options.Create(options));
        var first = string.Join(' ', Enumerable.Repeat("aaa", 70));
        var second = string.Join(' ', Enumerable.Repeat("bbb", 130));
        var chunks = sut.Chunk(Guid.NewGuid(), [new ExtractedPage(1, first + "\n\n" + second)]);

        chunks.Should().HaveCountGreaterOrEqualTo(2);
        chunks.Should().OnlyContain(chunk => estimator.Estimate(chunk.Content) <= options.SemanticMaxTokens);
        chunks.Skip(1).Should().OnlyContain(chunk => estimator.Estimate(chunk.Content) - estimator.Estimate(second) <= options.SemanticOverlapTokens);
    }

    [Test]
    public void Chunk_HeadingOnlyAndListBoundary_EmitsHeadingAndDoesNotAbsorbFollowingHeading()
    {
        var options = new RagOptions { SemanticTargetTokens = 12, SemanticMinTokens = 6, SemanticMaxTokens = 24, SemanticOverlapTokens = 0 };
        var sut = new SemanticV2ChunkingService(new ConservativeTokenEstimator(), Microsoft.Extensions.Options.Options.Create(options));
        var chunks = sut.Chunk(Guid.NewGuid(), [new ExtractedPage(1, "- item continuation\nNEXT HEADING\nProse after heading")]);
        var headingOnly = sut.Chunk(Guid.NewGuid(), [new ExtractedPage(2, "ONLY HEADING")]);

        chunks.Should().Contain(chunk => chunk.SectionTitle == "NEXT HEADING" && chunk.Content.Contains("Prose after heading"));
        chunks.Should().Contain(chunk => chunk.Content.Contains("- item continuation") && !chunk.Content.Contains("NEXT HEADING"));
        headingOnly.Should().ContainSingle().Which.Content.Should().Be("ONLY HEADING");
    }

    [TestCase("𠀀", 1)]
    [TestCase("e\u0301", 2)]
    [TestCase("عربى", 4)]
    [TestCase("ไทย", 3)]
    [TestCase("Ж", 1)]
    [TestCase("👩‍💻", 3)]
    public void Estimate_NonLatinAndSupplementaryRunes_AreCountedPerRune(string input, int expected) =>
        new ConservativeTokenEstimator().Estimate(input).Should().Be(expected);

    [Test]
    public void Chunk_LongNumberedNonLatinHeading_PreservesHeadingProseAndListWithinLowestMax()
    {
        var options = new RagOptions { SemanticTargetTokens = 64, SemanticMinTokens = 32, SemanticMaxTokens = 96, SemanticOverlapTokens = 0 };
        var estimator = new ConservativeTokenEstimator();
        var heading = "1. ĐỀ CƯƠNG 日本語 العربية " + string.Concat(Enumerable.Repeat("非常に長い見出し", 12));
        var source = heading + "\n\nProse payload remains available.\n- list marker remains available";
        var chunks = new SemanticV2ChunkingService(estimator, Microsoft.Extensions.Options.Options.Create(options)).Chunk(Guid.NewGuid(), [new ExtractedPage(1, source)]);

        chunks.Should().OnlyContain(chunk => estimator.Estimate(chunk.Content) <= 96);
        string.Concat(chunks.Select(chunk => chunk.Content)).Should().Contain(heading);
        chunks.Select(chunk => chunk.Content).Should().Contain(content => content.Contains("- list marker"));
    }

    [Test]
    public void Chunk_ConsecutiveHeadings_PreservesBothBeforeBody()
    {
        var chunks = new SemanticV2ChunkingService(new ConservativeTokenEstimator(), Microsoft.Extensions.Options.Options.Create(new RagOptions { SemanticTargetTokens = 64, SemanticMinTokens = 32, SemanticMaxTokens = 96 })).Chunk(Guid.NewGuid(), [new ExtractedPage(1, "CHAPTER 1\nINTRODUCTION\nBody")]);
        var content = string.Join('\n', chunks.Select(chunk => chunk.Content));
        content.IndexOf("CHAPTER 1", StringComparison.Ordinal).Should().BeLessThan(content.IndexOf("INTRODUCTION", StringComparison.Ordinal));
        content.Should().Contain("Body");
    }

    [Test]
    public void Chunk_MultiChunkTable_RepeatsHeaderAndKeepsDataRowsOnce()
    {
        var options = new RagOptions { SemanticTargetTokens = 64, SemanticMinTokens = 32, SemanticMaxTokens = 96, SemanticOverlapTokens = 0 };
        var estimator = new ConservativeTokenEstimator();
        var header = "Name | Credits | Description";
        var rows = Enumerable.Range(1, 4).Select(index => $"Course{index} | 3 | detailed row content number {index} with additional descriptive material for deterministic chunk spilling").ToArray();
        var chunks = new SemanticV2ChunkingService(estimator, Microsoft.Extensions.Options.Options.Create(options)).Chunk(Guid.NewGuid(), [new ExtractedPage(1, string.Join('\n', new[] { header }.Concat(rows)))]);

        chunks.Should().HaveCountGreaterOrEqualTo(2);
        chunks.Should().OnlyContain(chunk => estimator.Estimate(chunk.Content) <= options.SemanticMaxTokens);
        chunks.Select(chunk => chunk.Content.StartsWith("Name | Credits |", StringComparison.Ordinal)).Should().OnlyContain(value => value);
        foreach (var row in rows)
        {
            chunks.Sum(chunk => chunk.Content.Split(new[] { '\n' }).Count(line => line == row)).Should().Be(1);
        }
    }

    [Test]
    public void Chunk_SingleChunkTable_DoesNotDuplicateHeader()
    {
        var header = "Name | Credits | Description";
        var chunks = new SemanticV2ChunkingService(new ConservativeTokenEstimator(), Microsoft.Extensions.Options.Options.Create(new RagOptions { SemanticTargetTokens = 64, SemanticMinTokens = 32, SemanticMaxTokens = 96, SemanticOverlapTokens = 0 })).Chunk(Guid.NewGuid(), [new ExtractedPage(1, header + "\nCourse | 3 | short")]);
        chunks.Should().ContainSingle();
        chunks[0].Content.Split('\n').Count(line => line == header).Should().Be(1);
    }

    [Test]
    public void Chunk_OversizedTableHeader_PreservesHeaderAndBoundsContinuationContext()
    {
        var options = new RagOptions { SemanticTargetTokens = 64, SemanticMinTokens = 32, SemanticMaxTokens = 96, SemanticOverlapTokens = 0 };
        var estimator = new ConservativeTokenEstimator();
        var header = "Header | " + string.Concat(Enumerable.Repeat("非常に長い列名", 14));
        var rows = new[] { "One | detailed row content", "Two | detailed row content", "Three | detailed row content" };
        var chunks = new SemanticV2ChunkingService(estimator, Microsoft.Extensions.Options.Options.Create(options)).Chunk(Guid.NewGuid(), [new ExtractedPage(1, string.Join('\n', new[] { header }.Concat(rows)))]);

        chunks.Should().OnlyContain(chunk => estimator.Estimate(chunk.Content) <= options.SemanticMaxTokens);
        string.Concat(chunks.Select(chunk => chunk.Content)).Replace("\n", string.Empty).Replace(" ", string.Empty).Should().Contain(header.Replace(" ", string.Empty));
        foreach (var row in rows) { chunks.Sum(chunk => chunk.Content.Split(new[] { '\n' }).Count(line => line == row)).Should().Be(1); }
    }

    [Test]
    public void Chunk_ClassifiesOnlyConsistentMultiRowPipeAndTabGroups_PreservingEmptyCellsAndSpaces()
    {
        var options = new RagOptions { SemanticTargetTokens = 64, SemanticMinTokens = 32, SemanticMaxTokens = 96, SemanticOverlapTokens = 0 };
        var source = "A||C\nD||F\n\nA\t\tC\nD\t\tF\n\nLeft  |  Middle  |  Right\nOne  |  Two  |  Three";

        var chunks = new SemanticV2ChunkingService(new ConservativeTokenEstimator(), Microsoft.Extensions.Options.Options.Create(options))
            .Chunk(Guid.NewGuid(), [new ExtractedPage(1, source)]);
        var content = string.Join('\n', chunks.Select(chunk => chunk.Content));

        content.Should().Contain("A||C");
        content.Should().Contain("D||F");
        content.Should().Contain("A\t\tC");
        content.Should().Contain("D\t\tF");
        content.Should().Contain("Left  |  Middle  |  Right");
        content.Should().Contain("One  |  Two  |  Three");
    }

    [Test]
    public void Chunk_SingleLinePipeProse_RemainsParagraphInsteadOfTable()
    {
        const string source = "This is prose | not a table and it remains prose.\nThe following sentence stays in the same paragraph.";
        var options = new RagOptions { SemanticTargetTokens = 64, SemanticMinTokens = 32, SemanticMaxTokens = 96, SemanticOverlapTokens = 4 };

        var chunks = new SemanticV2ChunkingService(new ConservativeTokenEstimator(), Microsoft.Extensions.Options.Options.Create(options))
            .Chunk(Guid.NewGuid(), [new ExtractedPage(1, source)]);

        chunks.Should().ContainSingle().Which.Content.Should().Be("This is prose | not a table and it remains prose. The following sentence stays in the same paragraph.");
    }

    [Test]
    public void Chunk_OversizedDataRow_ReconstructsFromUniqueFragmentsOnly()
    {
        const string header = "ID | Description";
        var row = "R1 | " + string.Concat(Enumerable.Repeat("oversized-data-fragment ", 30));
        var options = new RagOptions { SemanticTargetTokens = 16, SemanticMinTokens = 8, SemanticMaxTokens = 24, SemanticOverlapTokens = 0 };
        var estimator = new ConservativeTokenEstimator();
        var chunks = new SemanticV2ChunkingService(estimator, Microsoft.Extensions.Options.Options.Create(options))
            .Chunk(Guid.NewGuid(), [new ExtractedPage(1, header + "\n" + row)]);

        chunks.Should().OnlyContain(chunk => estimator.Estimate(chunk.Content) <= options.SemanticMaxTokens);
        var reconstructed = string.Concat(chunks.SelectMany(chunk => chunk.Content.Split('\n').Where(line => !line.StartsWith("ID |", StringComparison.Ordinal))));
        reconstructed.Should().Be(row.TrimEnd());
    }

    [Test]
    public void Chunk_OversizedHeader_UsesBoundedLeadingIdentityColumnsForDataContinuations()
    {
        var header = "ID | " + string.Concat(Enumerable.Repeat("very-long-header-detail ", 20));
        var rows = Enumerable.Range(1, 4).Select(index => $"R{index} | " + string.Concat(Enumerable.Repeat("row-data ", 12))).ToArray();
        var options = new RagOptions { SemanticTargetTokens = 24, SemanticMinTokens = 12, SemanticMaxTokens = 32, SemanticOverlapTokens = 0 };
        var estimator = new ConservativeTokenEstimator();
        var chunks = new SemanticV2ChunkingService(estimator, Microsoft.Extensions.Options.Options.Create(options))
            .Chunk(Guid.NewGuid(), [new ExtractedPage(1, string.Join('\n', new[] { header }.Concat(rows)))]);

        chunks.Should().OnlyContain(chunk => estimator.Estimate(chunk.Content) <= options.SemanticMaxTokens);
        chunks.Where(chunk => rows.Any(row => chunk.Content.Contains(row[..3], StringComparison.Ordinal)))
            .Should().OnlyContain(chunk => chunk.Content.StartsWith("ID |", StringComparison.Ordinal));
    }

    [Test]
    public void Chunk_RenderedTailWithRepeatedTableContext_DoesNotMergePastMaximum()
    {
        const string header = "ID | Description";
        var rows = Enumerable.Range(1, 3).Select(index => $"R{index} | " + string.Concat(Enumerable.Repeat("payload ", 10))).ToArray();
        var options = new RagOptions { SemanticTargetTokens = 22, SemanticMinTokens = 18, SemanticMaxTokens = 28, SemanticOverlapTokens = 0 };
        var estimator = new ConservativeTokenEstimator();

        var chunks = new SemanticV2ChunkingService(estimator, Microsoft.Extensions.Options.Options.Create(options))
            .Chunk(Guid.NewGuid(), [new ExtractedPage(1, string.Join('\n', new[] { header }.Concat(rows)))]);

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().OnlyContain(chunk => estimator.Estimate(chunk.Content) <= options.SemanticMaxTokens);
    }

    [Test]
    public void Chunk_NearMaximumNumberedNonLatinHeadingHierarchy_WithList_UsesRealPayloadCapacity()
    {
        var options = LowestValidOptions();
        var estimator = new ConservativeTokenEstimator();
        var firstHeading = "1. ĐỀ CƯƠNG " + string.Concat(Enumerable.Repeat("日本語見出し", 10));
        const string secondHeading = "SECOND CHAPTER";
        const string list = "- payload list item remains available exactly once";
        var chunks = new SemanticV2ChunkingService(estimator, Microsoft.Extensions.Options.Options.Create(options)).Chunk(Guid.NewGuid(), [new ExtractedPage(1, $"{firstHeading}\n{secondHeading}\n{list}")]);

        chunks.Should().OnlyContain(chunk => estimator.Estimate(chunk.Content) <= 96);
        chunks.Select(chunk => chunk.Content).Should().Contain(content => content.Contains(list, StringComparison.Ordinal));
        string.Join('\n', chunks.Select(chunk => chunk.Content)).Should().Contain(firstHeading).And.Contain(secondHeading);
    }

    [Test]
    public void Chunk_NearMaximumNumberedNonLatinHeadingHierarchy_WithTable_PreservesRows()
    {
        var options = LowestValidOptions();
        var estimator = new ConservativeTokenEstimator();
        var firstHeading = "1. ĐỀ CƯƠNG " + string.Concat(Enumerable.Repeat("日本語見出し", 10));
        const string secondHeading = "SECOND CHAPTER";
        var rows = new[] { "ID | Value", "A | first row payload", "B | second row payload" };
        var chunks = new SemanticV2ChunkingService(estimator, Microsoft.Extensions.Options.Options.Create(options)).Chunk(Guid.NewGuid(), [new ExtractedPage(1, string.Join('\n', new[] { firstHeading, secondHeading }.Concat(rows)))]);

        chunks.Should().OnlyContain(chunk => estimator.Estimate(chunk.Content) <= 96);
        foreach (var row in rows) { string.Join('\n', chunks.Select(chunk => chunk.Content)).Should().Contain(row); }
        string.Join('\n', chunks.Select(chunk => chunk.Content)).Should().Contain(firstHeading).And.Contain(secondHeading);
    }

    [Test]
    public void Chunk_TabularLeadingInternalAndTrailingEmptyCells_ReconstructsExactlyIncludingOversizedRow()
    {
        var options = new RagOptions { SemanticTargetTokens = 32, SemanticMinTokens = 16, SemanticMaxTokens = 96, SemanticOverlapTokens = 0 };
        var estimator = new ConservativeTokenEstimator();
        var rows = new[]
        {
            "\tA\t",
            "\t\tC",
            "A\t\t",
            "\t" + string.Concat(Enumerable.Repeat("oversized-tab-cell ", 24)) + "\t",
        };
        var chunks = new SemanticV2ChunkingService(estimator, Microsoft.Extensions.Options.Options.Create(options)).Chunk(Guid.NewGuid(), [new ExtractedPage(1, string.Join('\n', rows))]);
        var content = string.Join('\n', chunks.Select(chunk => chunk.Content));

        chunks.Should().OnlyContain(chunk => estimator.Estimate(chunk.Content) <= 96);
        content.Should().Contain("\tA\t").And.Contain("\t\tC").And.Contain("A\t\t");
        string.Concat(chunks.SelectMany(chunk => chunk.Content.Split('\n').Where(line => line != rows[0]))).Should().Contain(rows[3]);
        var reconstructed = rows[0] + string.Concat(chunks.SelectMany(chunk => chunk.Content.Split('\n').Where(line => line != rows[0])));
        reconstructed.Should().Be(string.Concat(rows));
    }

    [Test]
    public void Chunk_ProseImmediatelyBeforeAndAfterCodeFence_PreservesCodeBlockBoundaries()
    {
        const string source = "Before prose.\n```csharp\nvar answer = 42;\nConsole.WriteLine(answer);\n```\nAfter prose.";
        var options = new RagOptions { SemanticTargetTokens = 32, SemanticMinTokens = 16, SemanticMaxTokens = 96, SemanticOverlapTokens = 0 };
        var estimator = new ConservativeTokenEstimator();
        var chunks = new SemanticV2ChunkingService(estimator, Microsoft.Extensions.Options.Options.Create(options)).Chunk(Guid.NewGuid(), [new ExtractedPage(1, source)]);
        var content = string.Join('\n', chunks.Select(chunk => chunk.Content));

        chunks.Should().OnlyContain(chunk => estimator.Estimate(chunk.Content) <= 96);
        content.IndexOf("Before prose.", StringComparison.Ordinal).Should().BeLessThan(content.IndexOf("```csharp", StringComparison.Ordinal));
        content.IndexOf("```csharp", StringComparison.Ordinal).Should().BeLessThan(content.IndexOf("After prose.", StringComparison.Ordinal));
        content.Should().Contain("```csharp\nvar answer = 42;\nConsole.WriteLine(answer);\n```");
    }

    private static RagOptions LowestValidOptions() => new() { SemanticTargetTokens = 64, SemanticMinTokens = 32, SemanticMaxTokens = 96, SemanticOverlapTokens = 0 };

}
