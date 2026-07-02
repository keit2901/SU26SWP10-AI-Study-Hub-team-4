namespace AI_Study_Hub_v2.Services.Rag;

public enum TextBlockKind
{
    Heading,
    Paragraph,
    List
}

public sealed record TextBlock(int? PageNumber, string Text, TextBlockKind Kind);

public sealed record SplitBlock(
    int? PageNumber,
    string Text,
    TextBlockKind Kind,
    IReadOnlyList<string> Units);

public sealed record MergedChunk(
    int? PageNumber,
    string Content,
    string? SectionTitle,
    bool IsHeading);
