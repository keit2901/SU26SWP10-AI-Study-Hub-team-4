namespace AI_Study_Hub_v2.Services.Rag.Benchmarking;

/// <summary>Small source-controlled corpus; expected phrases make relevance deterministic without a live embedding provider.</summary>
public static class ChunkingBenchmarkDataset
{
    public static IReadOnlyList<ChunkingBenchmarkScenario> All { get; } = Build().AsReadOnly();

    private static List<ChunkingBenchmarkScenario> Build() =>
    [
        Scenario("VN-RAG", "Vietnamese RAG notes", [
            new ExtractedPage(1, "CHUONG 1: TONG QUAN RAG\n\nRetrieval-Augmented Generation ket hop truy xuat tai lieu voi sinh cau tra loi.\n\n- Chunking theo ngu nghia giu tron y cau.\n- Chunking co dinh de tach ngang cau."),
            new ExtractedPage(2, "CHUONG 2: VECTOR SEARCH\n\nCosine similarity do muc do gan nhau giua hai vector embedding.")],
            ("VN-01", "semantic benefit", "Chunking theo ngu nghia giu tron y cau."),
            ("VN-02", "cosine similarity", "Cosine similarity do muc do gan nhau"),
            ("VN-03", "what combines retrieval", "Retrieval-Augmented Generation ket hop truy xuat")),
        Scenario("EN-ARCH", "English architecture guide", [
            new ExtractedPage(1, "SECTION 1: INGESTION PIPELINE\n\nThe ingestion pipeline downloads the file, extracts text, creates chunks, generates embeddings, and stores vectors.\n\nSECTION 2: RETRIEVAL\n\nRetrieval ranks candidate chunks by vector similarity.")],
            ("EN-01", "ingestion steps", "downloads the file, extracts text, creates chunks"),
            ("EN-02", "how selected", "Retrieval ranks candidate chunks"),
            ("EN-03", "where vectors stored", "stores vectors")),
        Scenario("CODE-DOC", "Code and API reference", [
            new ExtractedPage(1, "API ENDPOINTS\n\nPOST /api/documents/{id}/ingest triggers manual re-ingestion for a document owned by the caller.\n\n```csharp\nvar result = await ingestionService.IngestAsync(documentId, userId, cancellationToken);\nif (!result.Success) { logger.LogWarning(\"Document ingestion failed.\"); }\n```")],
            ("CODE-01", "manual ingestion endpoint", "POST /api/documents/{id}/ingest triggers manual re-ingestion"),
            ("CODE-02", "ingestion call", "await ingestionService.IngestAsync(documentId, userId, cancellationToken);"),
            ("CODE-03", "failure log", "Document ingestion failed.")),
        Scenario("WRAPPED-PDF", "Wrapped PDF prose", [new ExtractedPage(1, "This PDF paragraph has no blank lines and wraps across extracted lines\nwhile preserving the statement about deterministic benchmark phrase matching\nfor repeatable evaluation and release gates.")],
            ("PDF-01", "what wraps", "wraps across extracted lines"), ("PDF-02", "matching", "deterministic benchmark phrase matching"), ("PDF-03", "gates", "repeatable evaluation and release gates")),
        Scenario("MIXED-LIST", "Mixed prose and lists", [new ExtractedPage(1, "Study plan\n\nThe plan separates prose from action items.\n- Read the chapter\n  before attempting exercises.\n- Review answers after class.")],
            ("LIST-01", "plan purpose", "separates prose from action items"), ("LIST-02", "read action", "Read the chapter"), ("LIST-03", "review action", "Review answers after class.")),
        Scenario("TABLE-LIKE", "Table-like records", [new ExtractedPage(1, "Course | Credits | Semester\nSWP391 | 3 | SU26\nDatabase Systems | 3 | SU26")],
            ("TAB-01", "course credits", "SWP391 | 3 | SU26"), ("TAB-02", "database course", "Database Systems | 3 | SU26"), ("TAB-03", "header", "Course | Credits | Semester")),
        Scenario("PPTX-BULLETS", "PPTX bullet slide", [new ExtractedPage(1, "Sprint Review\n\n• Demo the upload flow\n• Collect student feedback\n• Publish retrospective notes")],
            ("PPT-01", "demo", "Demo the upload flow"), ("PPT-02", "feedback", "Collect student feedback"), ("PPT-03", "retrospective", "Publish retrospective notes")),
        Scenario("DOCX-BLOCKS", "DOCX-like blocks", [new ExtractedPage(1, "Introduction\n\nA DOCX paragraph explains stable option binding.\n\nRequirements\n\n1. Preserve legacy character settings.\n2. Add opt-in token budgets.")],
            ("DOCX-01", "binding", "stable option binding"), ("DOCX-02", "legacy", "Preserve legacy character settings."), ("DOCX-03", "budgets", "Add opt-in token budgets.")),
        Scenario("LONG-ATOMS", "Long sentence, list and row", [new ExtractedPage(1, "This unusually long sentence deliberately contains enough detail about bounded recursive splitting, Unicode scalar safety, and forward progress to require segmentation without losing the requested phrase.\n\n- This long list item includes a continuation requirement that must remain meaningful when it is split into smaller units for embedding.\n\nMetric | Value | Explanation | Deterministic benchmark rows remain atomic when they fit the configured token budget")],
            ("LONG-01", "unicode safety", "Unicode scalar safety"), ("LONG-02", "list continuation", "continuation requirement"), ("LONG-03", "atomic rows", "benchmark rows remain atomic")),
        Scenario("SHORT", "Short documents", [new ExtractedPage(1, "One concise fact."), new ExtractedPage(2, "Second page fact.")],
            ("SHORT-01", "first", "One concise fact."), ("SHORT-02", "second", "Second page fact."), ("SHORT-03", "concise", "concise fact.")),
    ];

    private static ChunkingBenchmarkScenario Scenario(
        string id,
        string title,
        IReadOnlyList<ExtractedPage> pages,
        params (string Id, string Query, string Phrase)[] cases) =>
        new(id, title, pages, cases.Select(item => new ChunkingBenchmarkCase(item.Id, item.Query, [item.Phrase])).ToArray());
}
