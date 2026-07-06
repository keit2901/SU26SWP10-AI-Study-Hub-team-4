using System.Text.RegularExpressions;
using AI_Study_Hub_v2.Options;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services.Rag;

/// <summary>
/// Legacy fixed-size chunking retained for rollback and A/B comparisons.
/// </summary>
public sealed class FixedSizeChunkingService : IChunkingService
{
    private static readonly Regex HorizontalWhitespace = new(@"[ \t\f\v]+", RegexOptions.Compiled);

    private readonly RagOptions _options;

    public FixedSizeChunkingService(IOptions<RagOptions> options)
    {
        _options = options.Value;
    }

    public IReadOnlyList<DocumentChunkDraft> Chunk(Guid documentId, IReadOnlyList<ExtractedPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var chunkSize = Math.Max(1, _options.ChunkSizeChars);
        var overlap = Math.Clamp(_options.ChunkOverlapChars, 0, Math.Max(0, chunkSize - 1));
        var chunks = new List<DocumentChunkDraft>();
        var chunkIndex = 0;

        foreach (var page in pages)
        {
            var text = Normalize(page.Text);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var start = 0;
            while (start < text.Length)
            {
                var remaining = text.Length - start;
                var end = remaining <= chunkSize
                    ? text.Length
                    : FindChunkBoundary(text, start, start + chunkSize);

                if (end <= start)
                {
                    end = Math.Min(text.Length, start + chunkSize);
                }

                var content = text[start..end].Trim();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    chunks.Add(new DocumentChunkDraft(documentId, chunkIndex++, page.PageNumber, content));
                }

                if (end >= text.Length)
                {
                    break;
                }

                var nextStart = Math.Max(start + 1, end - overlap);
                while (nextStart < text.Length && char.IsWhiteSpace(text[nextStart]))
                {
                    nextStart++;
                }

                start = nextStart;
            }
        }

        return chunks;
    }

    private static int FindChunkBoundary(string text, int start, int hardEnd)
    {
        var minBoundary = start + Math.Max(1, (hardEnd - start) / 2);

        foreach (var boundary in new[] { "\n\n", "\n", ". ", "? ", "! ", "; ", ", " })
        {
            var index = text.LastIndexOf(boundary, hardEnd - 1, hardEnd - minBoundary, StringComparison.Ordinal);
            if (index >= minBoundary)
            {
                return index + boundary.Length;
            }
        }

        var whitespace = -1;
        for (var i = hardEnd - 1; i >= minBoundary; i--)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                whitespace = i + 1;
                break;
            }
        }

        return whitespace > start ? whitespace : hardEnd;
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized
            .Split('\n')
            .Select(line => HorizontalWhitespace.Replace(line.Trim(), " "));

        return string.Join('\n', lines).Trim();
    }
}
