using AI_Study_Hub_v2.Services.Rag;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public class BlockParserTests
{
    [Test]
    public void Parse_RecognizesHeadingParagraphAndListBlocks()
    {
        var sut = new BlockParser();
        var page = new ExtractedPage(2, """
            CHUONG 2: MACHINE LEARNING

            Day la doan van mo ta tong quan ve chu de.

            - Y thu nhat
            - Y thu hai
            """);

        var blocks = sut.Parse(new[] { page });

        blocks.Should().HaveCount(3);
        blocks[0].Kind.Should().Be(TextBlockKind.Heading);
        blocks[1].Kind.Should().Be(TextBlockKind.Paragraph);
        blocks[2].Kind.Should().Be(TextBlockKind.List);
        blocks.Should().OnlyContain(block => block.PageNumber == 2);
    }

    [Test]
    public void IsHeading_DetectsNumberedAndUppercaseTitles()
    {
        var sut = new BlockParser();

        sut.IsHeading("1.2 Retrieval Strategy").Should().BeTrue();
        sut.IsHeading("CHUONG 1: GIOI THIEU").Should().BeTrue();
        sut.IsHeading("This is a normal sentence.").Should().BeFalse();
    }
}
