namespace AI_Study_Hub_v2.Services.Rag.Benchmarking;

public static class ChunkingBenchmarkDataset
{
    public static IReadOnlyList<ChunkingBenchmarkScenario> All { get; } = Build().AsReadOnly();

    private static List<ChunkingBenchmarkScenario> Build() =>
        new()
        {
            new(
                "VN-RAG",
                "Vietnamese RAG notes",
                new[]
                {
                    new ExtractedPage(1, """
                        CHUONG 1: TONG QUAN RAG

                        Retrieval-Augmented Generation ket hop truy xuat tai lieu voi sinh cau tra loi. He thong se tim cac doan lien quan truoc khi sinh dap an.

                        - Chunking theo ngu nghia giu tron y cau.
                        - Chunking co dinh de tach ngang cau neu kich thuoc qua nho.
                        """),
                    new ExtractedPage(2, """
                        CHUONG 2: VECTOR SEARCH

                        Cosine similarity do muc do gan nhau giua hai vector embedding. Chi so nay thuong duoc dung de xep hang chunk trong vector search.
                        """)
                },
                new[]
                {
                    new ChunkingBenchmarkCase(
                        "VN-01",
                        "Chunking theo ngu nghia co loi ich gi?",
                        new[] { "Chunking theo ngu nghia giu tron y cau." }),
                    new ChunkingBenchmarkCase(
                        "VN-02",
                        "Cosine similarity duoc dung de lam gi?",
                        new[] { "Cosine similarity do muc do gan nhau giua hai vector embedding." })
                }),
            new(
                "EN-ARCH",
                "English architecture guide",
                new[]
                {
                    new ExtractedPage(1, """
                        SECTION 1: INGESTION PIPELINE

                        The ingestion pipeline downloads the file, extracts text, creates chunks, generates embeddings, and stores the final vectors in PostgreSQL.

                        SECTION 2: RETRIEVAL

                        Retrieval ranks candidate chunks by vector similarity and then sends the top sources into the answer generation prompt.
                        """)
                },
                new[]
                {
                    new ChunkingBenchmarkCase(
                        "EN-01",
                        "What steps are in the ingestion pipeline?",
                        new[] { "downloads the file, extracts text, creates chunks, generates embeddings, and stores the final vectors in PostgreSQL" }),
                    new ChunkingBenchmarkCase(
                        "EN-02",
                        "How are chunks selected during retrieval?",
                        new[] { "Retrieval ranks candidate chunks by vector similarity" })
                }),
            new(
                "CODE-DOC",
                "Code and API reference",
                new[]
                {
                    new ExtractedPage(1, """
                        API ENDPOINTS

                        POST /api/documents/{id}/ingest triggers manual re-ingestion for a document owned by the caller.

                        EXAMPLE

                        var result = await ingestionService.IngestAsync(documentId, userId, cancellationToken);
                        if (!result.Success) { logger.LogWarning("Document ingestion failed."); }
                        """)
                },
                new[]
                {
                    new ChunkingBenchmarkCase(
                        "CODE-01",
                        "Which endpoint triggers manual re-ingestion?",
                        new[] { "POST /api/documents/{id}/ingest triggers manual re-ingestion" }),
                    new ChunkingBenchmarkCase(
                        "CODE-02",
                        "How is ingestion called in code?",
                        new[] { "await ingestionService.IngestAsync(documentId, userId, cancellationToken);" })
                })
        };
}
