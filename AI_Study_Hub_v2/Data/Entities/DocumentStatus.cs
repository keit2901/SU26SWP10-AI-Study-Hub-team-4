namespace AI_Study_Hub_v2.Data.Entities;

/// <summary>
/// Lifecycle of a document upload + ingestion pipeline.
/// Maps to PostgreSQL ENUM <c>public.document_status</c>.
/// </summary>
public enum DocumentStatus
{
    /// <summary>Storage upload in progress (multipart received, not yet committed).</summary>
    Uploading = 0,

    /// <summary>Stored OK, awaiting / queued for chunking + embedding.</summary>
    Ready = 1,

    /// <summary>Background ingestion (text extract → chunk → embed) in flight.</summary>
    Processing = 2,

    /// <summary>Pipeline failed; see <c>error_message</c> on the document row.</summary>
    Failed = 3,
}
