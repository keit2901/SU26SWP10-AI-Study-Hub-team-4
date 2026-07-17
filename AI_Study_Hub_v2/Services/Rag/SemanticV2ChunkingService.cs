using System.Text;
using System.Text.RegularExpressions;
using AI_Study_Hub_v2.Options;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services.Rag;

/// <summary>Token-budgeted, structure-preserving semantic chunking. The legacy semantic service remains untouched.</summary>
public sealed class SemanticV2ChunkingService : IChunkingService
{
    private static readonly Regex ListStart = new(@"^\s*((?:[-*+\u2022])|(?:\d+[.)]))\s+", RegexOptions.Compiled);
    private readonly ITokenEstimator _tokens;
    private readonly RagOptions _options;
    private readonly BlockParser _headings = new();

    public SemanticV2ChunkingService(ITokenEstimator tokens, IOptions<RagOptions> options)
    {
        _tokens = tokens;
        _options = options.Value;
    }

    public IReadOnlyList<DocumentChunkDraft> Chunk(Guid documentId, IReadOnlyList<ExtractedPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        var chunks = new List<V2Chunk>();

        foreach (var page in pages)
        {
            var section = (string?)null;
            var sectionIsContextOnly = false;
            var units = new List<V2Unit>();
            foreach (var block in ParsePage(page.Text))
            {
                if (block.Kind == V2BlockKind.Heading)
                {
                    FlushSection(page.PageNumber, section, units, chunks);
                    units.Clear();
                    if (!string.IsNullOrWhiteSpace(section))
                    {
                        var hierarchy = section + "\n" + block.Text;
                        if (_tokens.Estimate(hierarchy) <= _options.SemanticMaxTokens)
                        {
                            section = hierarchy;
                            sectionIsContextOnly = false;
                            continue;
                        }

                        if (!sectionIsContextOnly) { EmitHeading(page.PageNumber, section, chunks); }
                    }

                    if (_tokens.Estimate(block.Text) > _options.SemanticMaxTokens)
                    {
                        EmitHeading(page.PageNumber, block.Text, chunks);
                        section = TailWithinBudget(block.Text, Math.Min(24, _options.SemanticMaxTokens / 4));
                        sectionIsContextOnly = true;
                    }
                    else
                    {
                        section = block.Text;
                        sectionIsContextOnly = false;
                    }
                    continue;
                }

                section = EnsurePayloadSection(page.PageNumber, section, block, chunks, ref sectionIsContextOnly);
                units.AddRange(SplitToFit(block, section));
            }

            FlushSection(page.PageNumber, section, units, chunks);
            if (units.Count == 0 && !sectionIsContextOnly && !string.IsNullOrWhiteSpace(section))
            {
                var heading = BoundHeading(section);
                chunks.Add(new V2Chunk(page.PageNumber, heading, heading));
            }
        }

        return chunks.Select((chunk, index) => new DocumentChunkDraft(documentId, index, chunk.PageNumber, chunk.Content)
        {
            SectionTitle = chunk.SectionTitle,
        }).ToArray();
    }

    private IEnumerable<V2Block> ParsePage(string source)
    {
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        for (var index = 0; index < lines.Length;)
        {
            var line = lines[index].TrimEnd();
            if (string.IsNullOrWhiteSpace(line)) { index++; continue; }

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                var code = new List<string> { line };
                index++;
                while (index < lines.Length)
                {
                    code.Add(lines[index].TrimEnd());
                    if (lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal)) { index++; break; }
                    index++;
                }
                yield return new V2Block(V2BlockKind.Code, string.Join('\n', code));
                continue;
            }

            if (TryReadTableGroup(lines, index, out var rows))
            {
                index += rows.Count;
                var header = rows[0];
                yield return new V2Block(V2BlockKind.TableRow, header, TableHeader: header, IsTableHeader: true);
                foreach (var row in rows.Skip(1)) { yield return new V2Block(V2BlockKind.TableRow, row, TableHeader: header); }
                continue;
            }

            if (_headings.IsHeading(line.Trim()))
            {
                yield return new V2Block(V2BlockKind.Heading, line.Trim());
                index++;
                continue;
            }

            var list = ListStart.Match(line);
            if (list.Success)
            {
                var item = new List<string> { line.Trim() };
                index++;
                while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]) && !ListStart.IsMatch(lines[index]) && !StartsTableGroup(lines, index) && !_headings.IsHeading(lines[index].Trim()) && !IsCodeFence(lines[index]))
                {
                    item.Add(lines[index].Trim());
                    index++;
                }
                yield return new V2Block(V2BlockKind.ListItem, string.Join('\n', item), list.Groups[1].Value);
                continue;
            }

            var paragraph = new List<string>();
            while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]) && !ListStart.IsMatch(lines[index]) && !StartsTableGroup(lines, index) && !_headings.IsHeading(lines[index].Trim()) && !IsCodeFence(lines[index]))
            {
                paragraph.Add(lines[index].Trim());
                index++;
            }
            if (paragraph.Count > 0)
            {
                yield return new V2Block(V2BlockKind.Paragraph, string.Join(' ', paragraph));
            }
        }
    }

    private IEnumerable<V2Unit> SplitToFit(V2Block block, string? section)
    {
        var capacity = PayloadCapacity(section);
        var tableContext = block.IsTableHeader || block.TableHeader is null ? null : BoundedTableContext(block.TableHeader);
        if (tableContext is not null) { capacity -= _tokens.Estimate(tableContext); }
        var parts = block.IsTableHeader
            ? (Fits(block.Text, capacity) ? new[] { block.Text } : HardSplit(block.Text, capacity))
            : SplitText(block.Text, block.Kind, capacity, block.Marker);
        foreach (var part in parts)
        {
            yield return new V2Unit(block.Kind, part, tableContext, block.IsTableHeader);
        }
    }

    private IEnumerable<string> SplitText(string text, V2BlockKind kind, int capacity, string? marker)
    {
        if (kind == V2BlockKind.TableRow)
        {
            foreach (var fragment in SplitTableRowPreserving(text, capacity)) { yield return fragment; }
            yield break;
        }
        if (Fits(text, capacity)) { yield return text; yield break; }

        var candidates = kind == V2BlockKind.Code
            ? text.Split('\n', StringSplitOptions.None)
            : kind == V2BlockKind.Paragraph
                ? Regex.Split(text, @"(?<=[.!?;:—,])\s+")
                : text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var candidate in candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
        {
            if (Fits(candidate, capacity)) { yield return candidate.Trim(); continue; }

            foreach (var piece in SplitWordsThenRunes(candidate.Trim(), capacity, kind == V2BlockKind.ListItem ? marker : null))
            {
                yield return piece;
            }
        }
    }

    private IEnumerable<string> SplitWordsThenRunes(string text, int capacity, string? marker)
    {
        var prefix = string.IsNullOrWhiteSpace(marker) ? string.Empty : marker + " ";
        if (_tokens.Estimate(prefix) >= capacity)
        {
            foreach (var piece in HardSplit(text, capacity)) { yield return piece; }
            yield break;
        }
        var words = text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries) : text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = new StringBuilder(prefix);
        foreach (var word in words)
        {
            var candidate = current.Length == prefix.Length ? prefix + word : current + " " + word;
            if (Fits(candidate, capacity)) { current.Clear(); current.Append(candidate); continue; }
            if (current.Length > prefix.Length) { yield return current.ToString(); current.Clear(); current.Append(prefix); }

            foreach (var hard in HardSplit(word, capacity - _tokens.Estimate(prefix)))
            {
                var withPrefix = prefix + hard;
                if (Fits(withPrefix, capacity)) { yield return withPrefix; }
            }
        }
        if (current.Length > prefix.Length) { yield return current.ToString(); }
    }

    private IEnumerable<string> HardSplit(string text, int capacity)
    {
        var piece = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            if (piece.Length > 0 && !Fits(piece.ToString() + rune, Math.Max(1, capacity)))
            {
                yield return piece.ToString();
                piece.Clear();
            }
            piece.Append(rune.ToString());
        }
        if (piece.Length > 0) { yield return piece.ToString(); }
    }

    private void FlushSection(int? page, string? section, IReadOnlyList<V2Unit> units, ICollection<V2Chunk> destination)
    {
        if (units.Count == 0) { return; }
        var groups = Pack(units, section);
        IReadOnlyList<V2Unit>? previous = null;
        foreach (var group in groups)
        {
            var overlap = previous is null ? Array.Empty<V2Unit>() : SelectOverlap(previous, group, section);
            var rendered = overlap.Concat(group).ToArray();
            if (EstimateRenderedGroup(section, rendered) > _options.SemanticMaxTokens) throw new InvalidOperationException("Semantic-v2 rendered group exceeded its token budget.");
            var content = Compose(section, WithTableHeader(SourceUnits(rendered)));
            destination.Add(new V2Chunk(page, section, content));
            previous = group;
        }
    }

    private List<IReadOnlyList<V2Unit>> Pack(IReadOnlyList<V2Unit> units, string? section)
    {
        var groups = new List<IReadOnlyList<V2Unit>>();
        var current = new List<V2Unit>();
        foreach (var unit in units)
        {
            var candidate = current.Append(unit).ToArray();
            var candidateTokens = EstimateRenderedGroup(section, candidate);
            var currentTokens = EstimateRenderedGroup(section, current);
            if (current.Count > 0 && candidateTokens > _options.SemanticMaxTokens)
            {
                groups.Add(current.ToArray());
                current.Clear();
            }
            else if (current.Count > 0 && candidateTokens > _options.SemanticTargetTokens && currentTokens >= _options.SemanticMinTokens)
            {
                groups.Add(current.ToArray());
                current.Clear();
            }
            current.Add(unit);
        }
        if (current.Count > 0) { groups.Add(current.ToArray()); }

        if (groups.Count > 1 && EstimateRenderedGroup(section, groups[^1]) < _options.SemanticMinTokens)
        {
            var merged = groups[^2].Concat(groups[^1]).ToArray();
            if (EstimateRenderedGroup(section, merged) <= _options.SemanticMaxTokens)
            {
                groups[^2] = merged;
                groups.RemoveAt(groups.Count - 1);
            }
        }
        return groups;
    }

    private IReadOnlyList<V2Unit> SelectOverlap(IReadOnlyList<V2Unit> previous, IReadOnlyList<V2Unit> payload, string? section)
    {
        if (_options.SemanticOverlapTokens == 0 || previous.Any(unit => unit.TableHeader is not null) || payload.Any(unit => unit.TableHeader is not null)) { return Array.Empty<V2Unit>(); }
        var selected = new List<V2Unit>();
        for (var index = previous.Count - 1; index >= 0; index--)
        {
            var candidate = previous[index];
            var selectedTokens = _tokens.Estimate(Compose(null, selected));
            var remaining = _options.SemanticOverlapTokens - selectedTokens;
            if (remaining <= 0) { break; }
            if (_tokens.Estimate(candidate.Text) > remaining)
            {
                var tail = TailWithinBudget(candidate.Text, remaining);
                if (!string.IsNullOrEmpty(tail) && _tokens.Estimate(Compose(section, new[] { new V2Unit(candidate.Kind, tail) }.Concat(payload))) <= _options.SemanticMaxTokens)
                {
                    selected.Insert(0, new V2Unit(candidate.Kind, tail));
                }
                break;
            }
            var next = new[] { candidate }.Concat(selected).ToArray();
            if (_tokens.Estimate(Compose(null, next)) > _options.SemanticOverlapTokens) { break; }
            if (_tokens.Estimate(Compose(section, next.Concat(payload))) > _options.SemanticMaxTokens) { break; }
            selected.Insert(0, candidate);
            if (_tokens.Estimate(Compose(null, selected)) >= _options.SemanticOverlapTokens) { break; }
        }
        return selected;
    }

    private int PayloadCapacity(string? section) => _options.SemanticMaxTokens - _tokens.Estimate(section ?? string.Empty);
    private bool Fits(string value, int tokenCapacity) => _tokens.Estimate(value) <= tokenCapacity && value.Length <= _options.MaxSectionChars;
    private static bool IsCodeFence(string line) => line.TrimStart().StartsWith("```", StringComparison.Ordinal);
    private static bool StartsTableGroup(IReadOnlyList<string> lines, int start) => TryReadTableGroup(lines, start, out _);

    private static bool TryReadTableGroup(IReadOnlyList<string> lines, int start, out IReadOnlyList<string> rows)
    {
        rows = Array.Empty<string>();
        if (start >= lines.Count || !TryGetTableShape(lines[start], out var separator, out var columnCount)) { return false; }

        var group = new List<string>();
        for (var index = start; index < lines.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]) || !TryGetTableShape(lines[index], out var candidateSeparator, out var candidateColumnCount) || candidateSeparator != separator || candidateColumnCount != columnCount)
            {
                break;
            }
            group.Add(TrimNonStructuralRowBoundaryWhitespace(lines[index]));
        }

        if (group.Count < 2) { return false; }
        rows = group;
        return true;
    }

    private static bool TryGetTableShape(string line, out char separator, out int columnCount)
    {
        var pipeCount = line.Count(character => character == '|');
        var tabCount = line.Count(character => character == '\t');
        if (pipeCount == 0 && tabCount == 0)
        {
            separator = default;
            columnCount = 0;
            return false;
        }

        separator = pipeCount >= tabCount ? '|' : '\t';
        columnCount = (separator == '|' ? pipeCount : tabCount) + 1;
        return columnCount >= 2;
    }

    private static string TrimNonStructuralRowBoundaryWhitespace(string line) => line.Trim(' ');

    private static string Compose(string? section, IEnumerable<V2Unit> units)
    {
        var body = string.Join('\n', units.Select(unit => unit.Text));
        return string.IsNullOrWhiteSpace(section) ? body : section + "\n" + body;
    }

    private IReadOnlyList<V2Unit> WithTableHeader(IReadOnlyList<V2Unit> units)
    {
        if (units.Count == 0 || units[0].IsTableHeader) { return units; }

        var boundedContext = units.FirstOrDefault(unit => !unit.IsContextOnly && unit.TableHeader is not null)?.TableHeader;
        if (boundedContext is null) { return units; }

        // The generated unit is presentation-only; source header fragments are always ordinary units.
        return new[] { new V2Unit(V2BlockKind.TableRow, boundedContext, IsContextOnly: true) }.Concat(units).ToArray();
    }

    private int EstimateRenderedGroup(string? section, IReadOnlyList<V2Unit> units) => _tokens.Estimate(Compose(section, WithTableHeader(SourceUnits(units))));

    private static IReadOnlyList<V2Unit> SourceUnits(IEnumerable<V2Unit> units) => units.Where(unit => !unit.IsContextOnly).ToArray();

    // Table rows are source evidence: split only contiguous original substrings, never rebuild cells.
    private IEnumerable<string> SplitTableRowPreserving(string text, int capacity)
    {
        if (Fits(text, capacity)) { yield return text; yield break; }
        var remaining = text;
        while (remaining.Length > 0)
        {
            var prefix = new StringBuilder();
            var best = 0;
            var preferred = 0;
            foreach (var rune in remaining.EnumerateRunes())
            {
                var candidate = prefix + rune.ToString();
                if (!Fits(candidate, capacity)) { break; }
                prefix.Append(rune.ToString());
                best = prefix.Length;
                if (rune.Value is '|' or '\t' or ',' or ';' or ':' || Rune.IsWhiteSpace(rune)) { preferred = best; }
            }
            var cut = preferred > 0 ? preferred : best;
            if (cut == 0) { cut = remaining.EnumerateRunes().First().Utf16SequenceLength; }
            yield return remaining[..cut];
            remaining = remaining[cut..];
        }
    }

    private string TailWithinBudget(string text, int budget)
    {
        var runes = text.EnumerateRunes().ToArray();
        var tail = new StringBuilder();
        for (var index = runes.Length - 1; index >= 0; index--)
        {
            var candidate = runes[index].ToString() + tail;
            if (_tokens.Estimate(candidate) > budget) { break; }
            tail.Insert(0, runes[index].ToString());
        }
        return tail.ToString();
    }

    private string BoundHeading(string heading)
    {
        if (_tokens.Estimate(heading) <= _options.SemanticMaxTokens) { return heading; }
        return TailSafePrefix(heading, _options.SemanticMaxTokens);
    }

    private string BoundedTableContext(string header) => LeadingPrefixWithinBudget(header, Math.Max(1, _options.SemanticMaxTokens / 4));

    private string? EnsurePayloadSection(int? page, string? section, V2Block block, ICollection<V2Chunk> destination, ref bool sectionIsContextOnly)
    {
        if (string.IsNullOrWhiteSpace(section)) { return section; }

        var tableContextTokens = block.IsTableHeader || block.TableHeader is null ? 0 : _tokens.Estimate(BoundedTableContext(block.TableHeader));
        var requiredPayloadTokens = Math.Max(1, block.Kind == V2BlockKind.ListItem && !string.IsNullOrWhiteSpace(block.Marker)
            ? _tokens.Estimate(block.Marker + " ") + 1
            : 1);
        var maximumContextTokens = _options.SemanticMaxTokens - tableContextTokens - requiredPayloadTokens;
        if (maximumContextTokens > 0 && _tokens.Estimate(section) <= maximumContextTokens) { return section; }

        if (!sectionIsContextOnly) { EmitHeading(page, section, destination); }
        sectionIsContextOnly = true;
        return maximumContextTokens <= 0 ? null : PrefixWithinBudget(section, maximumContextTokens);
    }

    private string LeadingPrefixWithinBudget(string value, int budget)
    {
        var prefix = new StringBuilder();
        var cellEnd = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (_tokens.Estimate(prefix + rune.ToString()) > budget) break;
            prefix.Append(rune.ToString());
            if (rune.Value is '|' or '\t') cellEnd = prefix.Length;
        }
        return cellEnd > 0 ? prefix.ToString()[..cellEnd] : prefix.ToString();
    }

    private string PrefixWithinBudget(string value, int budget)
    {
        var prefix = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            if (_tokens.Estimate(prefix + rune.ToString()) > budget) { break; }
            prefix.Append(rune.ToString());
        }
        return prefix.ToString();
    }

    private void EmitHeading(int? page, string heading, ICollection<V2Chunk> destination)
    {
        foreach (var part in HardSplit(heading, _options.SemanticMaxTokens))
        {
            destination.Add(new V2Chunk(page, null, part));
        }
    }

    private string TailSafePrefix(string value, int budget)
    {
        var result = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            if (_tokens.Estimate(result + rune.ToString()) > budget) { break; }
            result.Append(rune.ToString());
        }
        return result.ToString();
    }

    private enum V2BlockKind { Heading, Paragraph, ListItem, TableRow, Code }
    private sealed record V2Block(V2BlockKind Kind, string Text, string? Marker = null, string? TableHeader = null, bool IsTableHeader = false);
    private sealed record V2Unit(V2BlockKind Kind, string Text, string? TableHeader = null, bool IsTableHeader = false, bool IsContextOnly = false);
    private sealed record V2Chunk(int? PageNumber, string? SectionTitle, string Content);
}
