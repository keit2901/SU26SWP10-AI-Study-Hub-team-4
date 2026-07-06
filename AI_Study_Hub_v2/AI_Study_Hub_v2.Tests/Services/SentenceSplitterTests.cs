using AI_Study_Hub_v2.Services.Rag;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public class SentenceSplitterTests
{
    [Test]
    public void SplitIntoSentences_KeepsVietnameseAbbreviationsInsideSentence()
    {
        var sut = new SentenceSplitter();

        var result = sut.SplitIntoSentences("TS. Nguyen huong dan de tai. Sinh vien tiep tuc nghien cuu.");

        result.Should().HaveCount(2);
        result[0].Should().Be("TS. Nguyen huong dan de tai.");
        result[1].Should().Be("Sinh vien tiep tuc nghien cuu.");
    }

    [Test]
    public void Split_ListBlock_RemovesBulletMarkers()
    {
        var sut = new SentenceSplitter();
        var blocks = new[]
        {
            new TextBlock(1, "- Muc dau tien\n- Muc thu hai", TextBlockKind.List)
        };

        var splitBlocks = sut.Split(blocks);

        splitBlocks.Should().ContainSingle();
        splitBlocks[0].Units.Should().Equal("Muc dau tien", "Muc thu hai");
    }
}
