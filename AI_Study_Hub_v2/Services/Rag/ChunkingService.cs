namespace AI_Study_Hub_v2.Services.Rag;

/// <summary>
/// Orchestrates semantic chunking: page text -> structural blocks -> sentence/list units -> merged chunks.
/// </summary>
public sealed class ChunkingService : IChunkingService
{
    private readonly BlockParser _blockParser;
    private readonly SentenceSplitter _sentenceSplitter;
    private readonly ChunkMerger _chunkMerger;

    public ChunkingService(
        BlockParser blockParser,
        SentenceSplitter sentenceSplitter,
        ChunkMerger chunkMerger)
    {
        _blockParser = blockParser;
        _sentenceSplitter = sentenceSplitter;
        _chunkMerger = chunkMerger;
    }

    public IReadOnlyList<DocumentChunkDraft> Chunk(Guid documentId, IReadOnlyList<ExtractedPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var blocks = _blockParser.Parse(pages);
        var splitBlocks = _sentenceSplitter.Split(blocks);
        var mergedChunks = _chunkMerger.Merge(splitBlocks);

        return mergedChunks
            .Select((chunk, index) => new DocumentChunkDraft(documentId, index, chunk.PageNumber, chunk.Content)
            {
                SectionTitle = chunk.SectionTitle
            })
            .ToList();
    }
}
