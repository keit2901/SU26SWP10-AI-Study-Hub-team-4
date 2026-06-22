# Workflow Diagrams: AI Question Answering & Document Ingestion

---

## Workflow 1: AI Question Answering (RAG Chat)

### User Flow (Sequence Diagram)

```mermaid
sequenceDiagram
    actor User
    participant Browser as AiChat.razor
    participant API as AiChatController
    participant RAG as SemanticKernelRagChatService
    participant Search as RagSearchService
    participant Embed as FakeEmbeddingService
    participant DB as PostgreSQL (pgvector)
    participant Groq as Groq API

    User->>Browser: Types question + Clicks Send
    Browser->>API: POST /api/ai/chat/ask { question, folderId?, topK, documentIds? }
    API->>RAG: AskAsync(supabaseUserId, request)
    RAG->>RAG: Validate question non-empty
    RAG->>RAG: Normalize TopK [1..10], SubjectCode/Semester to uppercase
    
    RAG->>Search: SearchAsync(request)
    Search->>Embed: GenerateEmbeddingAsync(query)
    Embed-->>Search: float[384] query vector
    Search->>DB: CosineDistance(embedding, queryVec) < TopK
    Note over Search,DB: Filter: UserId + Status=Ready + FolderId/DocumentId/SubjectCode/Semester
    DB-->>Search: RagSearchResultDto[]
    
    Search-->>RAG: IReadOnlyList<RagSearchResultDto>
    
    RAG->>RAG: MapSources: truncate to MaxContextChars (6000), label S1..Sn
    RAG->>RAG: Build system prompt + user prompt with excerpts
    
    alt Has sources
        RAG->>Groq: POST /v1/chat/completions { systemPrompt, userPrompt }
        Groq-->>RAG: { choices[0].message.content }
    else No sources
        RAG->>Groq: POST /v1/chat/completions { "Use general knowledge" }
        Groq-->>RAG: Answer from general knowledge
    end
    
    alt Groq fails && UseLocalDemoFallback=true && has sources
        RAG->>RAG: Build fake answer from first source excerpt
    end
    
    RAG-->>API: AiChatAnswerResponse { answer, sources[], durationMs }
    API-->>Browser: 200 JSON response
    Browser->>User: Renders answer + source citations
```

### State Machine (Chat Session & Answer Flow)

```mermaid
stateDiagram-v2
    [*] --> Idle: User opens chat
    Idle --> AwaitingAnswer: User clicks Send
    AwaitingAnswer --> Idle: Answer received
    AwaitingAnswer --> Error: Network/API failure
    Error --> Idle: User retries
    AwaitingAnswer --> ProviderDown: Groq returns 503
    ProviderDown --> Idle: Fallback answer used
    ProviderDown --> Error: Fallback unavailable
    Idle --> [*]: User navigates away
```

### Constraint & Business Specification Table

| # | Constraint / Rule | Value | Enforcement Point | Notes |
|---|---|---|---|---|
| C1 | Max question length | Implicit (no hard limit) | `SemanticKernelRagChatService:44` | Empty question → 400 |
| C2 | TopK range | `[1, 10]` | `SemanticKernelRagChatService:54-55` | Default 5, clamped |
| C3 | Context budget for sources | 6000 chars total | `SemanticKernelRagChatService:76-86` | Truncated per-source |
| C4 | Embedding dimension | 384 | `RagSearchService:36` | Mismatch = InvalidOperationException |
| C5 | Auth requirement | JWT Bearer token | `DocumentsController`, `AiChatController` | Missing/invalid → 401 |
| C6 | User must be active | `User.IsActive == true` | `RagSearchService:48` | Inactive → 403 |
| C7 | Document ownership scoping | `Document.UserId == caller` | `RagSearchService:120-157` | Always enforced |
| C8 | Groq model | `llama-3.1-8b-instant` | `GroqOptions:9` | Configurable |
| C9 | Groq temperature | 0.2 | `GroqOptions:13` | Configurable |
| C10 | Groq max tokens | 1024 | `GroqOptions:15` | Configurable |
| C11 | Groq timeout | 2 minutes | `Program.cs:156` | HttpClient level |
| C12 | Demo fallback | `UseLocalDemoFallback` flag | `GroqOptions:20` | Only if flag + sources exist |
| C13 | Source excerpt truncation | 500 chars per excerpt | `RagSearchService:69` | Then further limited by budget |
| C14 | Document filter chain | `UserId` → `Status=Ready` → `DocumentId/DocumentIds` → `FolderId` → `SubjectCode/Semester` | `RagSearchService:120-157` | Priority order |
| C15 | Search result scoring | `1 - cosineDistance` | `RagSearchService:134` | Higher = more relevant |
| C16 | Groq unavailable error | 503 `ai_provider_unavailable` | `GroqChatCompletionClient:65` | Or fallback if enabled |
| C17 | Groq API key validation | Must be configured | `GroqChatCompletionClient:42` | Startup validation |
| C18 | SubjectCode normalization | Uppercased | `SemanticKernelRagChatService:57-58` | Case-insensitive match |
| C19 | Semester normalization | Uppercased | `SemanticKernelRagChatService:59-60` | Case-insensitive match |

### Branching Conditions (Decision Tree)

```mermaid
flowchart TD
    A[User asks question] --> B{Question non-empty?}
    B -- No --> C[Return 400 BadRequest]
    B -- Yes --> D{User active?}
    D -- No --> E[Return 403 Forbidden]
    D -- Yes --> F{Embedding dims match?}
    F -- No --> G[Throw InvalidOperationException]
    F -- Yes --> H{Any chunks matched?}
    H -- No --> I[Call Groq with general knowledge instruction]
    H -- Yes --> J[Call Groq with source excerpts]
    I --> K{Groq succeeds?}
    J --> K
    K -- Yes --> L[Return answer + sources]
    K -- No --> M{UseLocalDemoFallback=true && sources exist?}
    M -- Yes --> N[Return first excerpt as answer]
    M -- No --> O[Return 503 ProviderUnavailable]
    N --> L
    C --> P[End]
    E --> P
    G --> P
    O --> P
    L --> P
```

---

## Workflow 2: Upload Source Ingestion / Knowledge Source Management

### User Flow (Sequence Diagram)

```mermaid
sequenceDiagram
    actor User
    participant Browser as DocumentUpload.razor
    participant API as DocumentsController
    participant DocSvc as DocumentService
    participant Storage as Supabase Storage
    participant Ingest as DocumentIngestionService
    participant Extract as PdfTextExtractionService
    participant Chunk as ChunkingService
    participant Embed as FakeEmbeddingService
    participant DB as PostgreSQL (pgvector)

    User->>Browser: Select file + fill SubjectCode/Semester + click Upload
    Browser->>Browser: Client validation: size ≤ 50MB, MIME allowed, reCAPTCHA
    
    Browser->>API: POST /api/documents/upload { file, subjectCode, semester, folderId? }
    API->>DocSvc: UploadAsync(supabaseUserId, file, metadata)
    
    DocSvc->>DocSvc: Validate file size (0..50MB), MIME type
    DocSvc->>DocSvc: Sanitize filename, build storage path
    
    DocSvc->>Storage: UploadAsync(path, stream)
    Storage-->>DocSvc: Success
    
    DocSvc->>DB: INSERT Document { Status = Ready }
    DB-->>DocSvc: Document row created
    
    alt Is ingestion candidate (PDF/DOCX/PPTX)
        DocSvc->>Ingest: IngestAsync(documentId, supabaseUserId)
        
        Ingest->>Storage: OpenReadAsync(document)
        Storage-->>Ingest: fileStream (MemoryStream via signed URL)
        
        Ingest->>Extract: ExtractPagesAsync(stream, mimeType)
        Extract->>Extract: PDF: PdfPig page extraction
        Extract->>Extract: DOCX: OpenXml paragraphs + headers/footers
        Extract->>Extract: PPTX: OpenXml slides text
        Extract-->>Ingest: IReadOnlyList<ExtractedPage>
        
        Ingest->>Chunk: Chunk(documentId, pages)
        Chunk->>Chunk: Sliding window 1000/200 chars, paragraph-aware
        Chunk-->>Ingest: IReadOnlyList<DocumentChunkDraft>
        
        loop Each chunk
            Ingest->>Embed: GenerateEmbeddingAsync(content)
            Embed-->>Ingest: float[384]
        end
        
        Ingest->>DB: BEGIN TRANSACTION
        Ingest->>DB: DELETE old chunks for document
        Ingest->>DB: INSERT new DocumentChunks with embeddings
        Ingest->>DB: UPDATE Document SET Status=Ready, PageCount=N
        Ingest->>DB: COMMIT
        
        DB-->>Ingest: Success
    end
    
    DocSvc-->>API: DocumentDto { id, status, pageCount? }
    API-->>Browser: 201 Created
    Browser->>User: Shows success + redirects to document list
```

### State Machine (Document Lifecycle)

```mermaid
stateDiagram-v2
    [*] --> Uploading: User starts upload
    Uploading --> Ready: Upload to Storage + DB insert success
    Uploading --> Failed: Storage/DB error
    Ready --> Processing: Re-ingest triggered (manual)
    Ready --> [*]: User deletes document
    Processing --> Ready: Chunking + embedding success
    Processing --> Failed: Extraction/chunking/embedding error
    Failed --> Processing: User clicks "Reprocess"
    Failed --> [*]: User deletes document
```

### Constraint & Business Specification Table

| # | Constraint / Rule | Value | Enforcement Point | Notes |
|---|---|---|---|---|
| C1 | Max file size | 50 MB (52,428,800 bytes) | `DocumentsController:22`, `DocumentService:20` | 413 if exceeded |
| C2 | Min file size | > 0 bytes | `DocumentService:76` | 400 if empty |
| C3 | Allowed MIME types (upload) | PDF, DOCX, PPTX, DOC, PPT | `DocumentService:28-35` | 415 if unsupported |
| C4 | Ingestion candidates (chunked) | PDF, DOCX, PPTX only | `DocumentService:476-482` | Legacy DOC/PPT skipped |
| C5 | Filename sanitization | `[a-zA-Z0-9._-]`, max 80 chars | `DocumentService:515` | Used in storage path |
| C6 | Display filename max length | 255 chars | `DocumentService:333` | For rename |
| C7 | SubjectCode regex | `^[A-Z]{2,4}\d{3,4}$` | `DocumentUpload.razor` + backend | Client + server |
| C8 | Semester regex | `^(SPR\|SU\|FA\|M1)\d{2}$` | `DocumentUpload.razor` + backend | Client + server |
| C9 | Max files per upload batch | 50 | `DocumentUpload.razor:550` | Client-side only |
| C10 | Chunk size | 1000 chars | `RagOptions:7`, `ChunkingService` | Configurable |
| C11 | Chunk overlap | 200 chars | `RagOptions:9`, `ChunkingService` | Configurable |
| C12 | Embedding dimension | 384 | `RagSearchService:36`, `DocumentChunk:13` | Fixed in migration |
| C13 | Token estimate formula | `ceil(content.Length / 4)` | `DocumentIngestionService:158` | Rough estimate |
| C14 | Error message max length | 1000 chars | `DocumentIngestionService:12` | Trimmed if longer |
| C15 | Signed URL TTL | 300 seconds (5 min) | `DocumentService:23` | For download + ingestion |
| C16 | Storage path pattern | `users/{userId:N}/{yyyy}/{docId:N}-{slug}` | `DocumentService:127` | Year-based partitioning |
| C17 | Ownership verification | Every endpoint | `DocumentService` throughout | `Document.UserId == callerId` |
| C18 | Folder ownership | Must belong to caller | `DocumentService:102` | If `FolderId` specified |
| C19 | User must be active | `User.IsActive == true` | `DocumentService:72` | 403 if inactive |
| C20 | reCAPTCHA required | In non-Development | `Program.cs:38-43` | Configurable `Recaptcha:Enabled` |
| C21 | Concurrent upload files | Processed sequentially | `DocumentUpload.razor:601-618` | One-by-one, not parallel |
| C22 | MIME fallback | `application/octet-stream` → `.pdf`/`.docx`/`.pptx` extension mapping | `DocumentService:420-440` | Browser MIME sniffing workaround |
| C23 | Request size limits | 50 MB ASP.NET limit | `DocumentsController:22` | `RequestSizeLimit` + `RequestFormLimits` |
| C24 | Max storage path components | `users/{userId}/{year}/{docId}-{slug}` | `DocumentService:127-130` | 4 segments |
| C25 | DB cleanup on storage failure | Storage uploaded but DB insert failed → delete storage object best-effort | `DocumentService:155-170` | No rollback |

### Branching Conditions (Decision Tree)

```mermaid
flowchart TD
    A[User uploads file] --> B{File size OK?}
    B -- 0 bytes --> C[400 BadRequest]
    B -- >50MB --> D[413 PayloadTooLarge]
    B -- OK --> E{MIME type supported?}
    E -- No --> F[415 UnsupportedMediaType]
    E -- Yes --> G{User active?}
    G -- No --> H[403 Forbidden]
    G -- Yes --> I{Folder specified?}
    I -- Yes --> J{Folder owned by user?}
    J -- No --> K[404 Folder not found]
    J -- Yes --> L[Upload to Storage]
    I -- No --> L
    L --> M{Storage upload success?}
    M -- No --> N[500 Storage error]
    M -- Yes --> O[Insert DB row]
    O --> P{DB insert success?}
    P -- No --> Q[Best-effort cleanup storage + 500]
    P -- Yes --> R{Is ingestion candidate?}
    R -- No (DOC/PPT/TXT) --> S[Return 201, Status=Ready, no chunks]
    R -- Yes (PDF/DOCX/PPTX) --> T[Call IngestionService]
    T --> U{Extraction has non-empty pages?}
    U -- No --> V[Status=Failed: No extractable text]
    U -- Yes --> W{Chunks produced?}
    W -- No --> X[Status=Failed: No chunks]
    W -- Yes --> Y[Delete old chunks + insert new + set Status=Ready]
    Y --> Z[Return 201 with chunks]
    V --> AA[Return 201 but document has Failed status]
    X --> AA
    AA --> AB[User can click Reprocess]
    AB --> T
```

### Knowledge Source Management Sub-Workflows

```mermaid
sequenceDiagram
    actor User
    participant Browser as Document Pages
    participant API as DocumentsController
    participant Svc as DocumentService
    participant Storage as Supabase Storage
    participant DB as PostgreSQL

    %% List documents
    User->>Browser: Navigate to /documents
    Browser->>API: GET /api/documents?folderId=&subjectCode=&semester=&q=
    API->>Svc: ListAsync(supabaseUserId, query)
    Svc->>DB: SELECT WHERE UserId + optional filters
    DB-->>Svc: DocumentDto[]
    Svc-->>API: List<DocumentDto>
    API-->>Browser: 200 JSON
    Browser->>User: Renders document list with filters

    %% View document detail
    User->>Browser: Click document card
    Browser->>API: GET /api/documents/{id}
    API->>Svc: GetByIdAsync(supabaseUserId, id)
    Svc->>DB: SELECT Document + chunks count
    DB-->>Svc: DocumentDto
    Svc-->>API: DocumentDto
    API-->>Browser: 200 JSON
    Browser->>User: Shows detail with chunks + status + download/delete actions

    %% Move to folder
    User->>Browser: Select "Move to folder"
    Browser->>API: PUT /api/documents/{id}/folder { folderId }
    API->>Svc: MoveToFolderAsync(supabaseUserId, id, folderId)
    Svc->>Svc: Verify ownership + folder ownership
    Svc->>DB: UPDATE Document SET FolderId
    DB-->>Svc: Success
    Svc-->>API: DocumentDto (updated)
    API-->>Browser: 200 JSON

    %% Rename
    User->>Browser: Click "Rename"
    Browser->>API: PUT /api/documents/{id}/rename { fileName }
    API->>Svc: RenameAsync(supabaseUserId, id, fileName)
    Svc->>Svc: Verify ownership, validate length [1..255]
    Svc->>DB: UPDATE Document SET FileName
    DB-->>Svc: Success
    Svc-->>API: DocumentDto (updated)
    API-->>Browser: 200 JSON

    %% Delete
    User->>Browser: Click "Delete" + confirm
    Browser->>API: DELETE /api/documents/{id}
    API->>Svc: DeleteAsync(supabaseUserId, id)
    Svc->>Svc: Verify ownership
    Svc->>Storage: DeleteAsync(storagePath) [best-effort]
    Svc->>DB: DELETE Document (cascade chunks)
    DB-->>Svc: Success
    Svc-->>API: 204 NoContent
    API-->>Browser: 204
    Browser->>User: Removed from list

    %% Re-ingest (Reprocess)
    User->>Browser: Click "Reprocess"
    Browser->>API: POST /api/documents/{id}/ingest
    API->>Svc: _ingestionService.IngestAsync(id, userId)
    Note over Svc,DB: Same flow as initial ingestion
    Svc-->>API: DocumentIngestionResult
    API-->>Browser: 200 JSON with new status
    Browser->>User: Shows updated status/chunks
```

### Constraint Violation Error Codes

```mermaid
flowchart LR
    subgraph Upload Errors
        A[400] --> A1[Empty file]
        B[413] --> B1[File >50MB]
        C[415] --> C1[Unsupported MIME type]
        D[401] --> D1[Missing/invalid JWT]
        E[403] --> E1[User inactive]
        F[404] --> F1[Folder not found]
        G[500] --> G1[Storage/DB error]
    end
    
    subgraph Management Errors
        H[404] --> H1[Document not found]
        I[403] --> I1[Not document owner]
        J[400] --> J1[Invalid rename (empty/too long)]
        K[204] --> K1[Delete success (no body)]
    end
```

---

## Implementation Notes

### Workflow 1 — AI QA Key Integration Points
- All search + QA logic is **synchronous** (request thread blocks on Groq API call)
- Chat history is **in-memory only** (`AiChatSessionState`) — lost on page refresh
- No per-user rate limiting (relies on Groq's free-tier 30 req/min)
- Source citation labels (`S1`, `S2`, ...) are sequential, not document-based

### Workflow 2 — Ingestion Key Integration Points
- Ingestion is **synchronous inline** — large documents block upload response
- No background job queue yet (planned Phase 3/4)
- 0 non-empty pages → not a soft error; it throws → document goes to `Failed` status
- Legacy DOC/PPT are accepted for upload but **not ingested** (no chunks created)
- Re-ingest replaces all chunks atomically (DB transaction)
- Storage object is NOT deleted on re-ingest — only chunks are replaced
- Signed URLs expire in 5 minutes — ingestion must complete within that window
