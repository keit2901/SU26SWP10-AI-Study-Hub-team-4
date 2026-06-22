# Backend Handoff — 2026-06-19

## Session Summary
This session focused on **deep research and architectural planning** for image understanding inside uploaded documents (.pdf/.docx/.pptx), free AI model selection for RAG, and multi-model comparison architecture.

No code was changed. All work is documented in the implementation plan.

---

## 5 Final Architectural Decisions

### Decision 1 — Keep Current Chunk Architecture
- **No changes.** Flat `DocumentChunk` entity stays.
- Avoid: separate `TextChunk`/`ImageChunk`/`TableChunk` tables.

### Decision 2 — Image Understanding (Inline Descriptions)
- Extract images from .pdf (PdfPig `TryGetPng`/`RawBytes`), .docx (OpenXml `ImagePart`), .pptx (OpenXml `SlidePart.ImageParts`)
- Send to Llama 4 Scout on Groq → get text description
- Append description inline into `ExtractedPage.Text` before chunking
- NOT stored as separate chunk type

### Decision 3 — Primary Model: DeepSeek V4 Flash
- **Primary production candidate**: DeepSeek V4 Flash (cheapest, OpenAI-compatible, 1M context)
- **Secondary**: Llama 3.3 70B Versatile (free Groq tier, best instruction following)
- **Image describer**: Llama 4 Scout (same Groq key, free tier, DocVQA 94.4)
- **Future vision**: Gemini Flash preferred but not committed (re-evaluate at Phase 4)

### Decision 4 — Keep Current Embedding
- 384-dim `FakeEmbeddingService` stays. No BGE-M3 migration.
- Re-evaluate only when multilingual docs exist AND retrieval quality is insufficient.

### Decision 5 — Future Multimodal Deferred
- Phase 1 = description → text chunk → text retrieval only
- Phase 2+ = vision embeddings, image retrieval, true multimodal RAG
- No structural changes needed now; the inline description approach is additive.

---

## AI Model Stack

| Role | Model | Provider | Cost |
|------|-------|----------|------|
| Primary RAG | `deepseek-v4-flash` | DeepSeek API | $0.14/$0.28 per 1M |
| Secondary RAG | `llama-3.3-70b-versatile` | Groq (free tier) | $0 |
| Image describer | `meta-llama/llama-4-scout-17b-16e-instruct` | Groq (free tier) | $0 |
| Speed baseline | `llama-3.1-8b-instant` | Groq (free tier) | $0 |

---

## Sprint Plan

### Sprint 1 — Image Understanding (4-5 days, highest ROI)
- Add `ExtractedImage` record + `IImageDescriptionService` interface
- Implement `GroqVisionDescriptionService` (reuses Groq endpoint)
- Extract images from PDF/DOCX/PPTX in `PdfTextExtractionService`
- Modify `DocumentIngestionService` to call image description and append to text
- Register in DI, add tests
- **Files changed**: `RagContracts.cs`, `PdfTextExtractionService.cs`, `DocumentIngestionService.cs`, `Program.cs`, test files
- **New files**: `GroqVisionDescriptionService.cs`
- **Risk**: LOW

### Sprint 2 — Model Upgrade + Comparison (3-4 days)
- Add `ModelName` to `AiChatCompletionRequest` + `AiChatAskRequest`
- Update `GroqChatCompletionClient` to use per-request model name
- Change default model to `llama-3.3-70b-versatile`
- Add `DeepSeekOptions` + `DeepSeekChatCompletionClient`
- Frontend model selector in `AiChat.razor`
- **Files changed**: `IAiChatCompletionClient.cs`, `AiChatDtos.cs`, `GroqChatCompletionClient.cs`, `GroqOptions.cs`, `SemanticKernelRagChatService.cs`, `Program.cs`, `AiChat.razor`, test files
- **New files**: `DeepSeekOptions.cs`, `DeepSeekChatCompletionClient.cs`
- **Risk**: LOW-MEDIUM (DeepSeek citation quality unverified)

### Sprint 3 — System Prompt + Polish (2-3 days)
- Add tutoring tone to `BuildSystemPrompt()`
- Tune image description prompt for academic diagrams
- Performance profiling + error handling hardening
- **Files changed**: `SemanticKernelRagChatService.cs`, `GroqVisionDescriptionService.cs`
- **Risk**: LOW

---

## Service Architecture (unchanged from previous handoff)

```
IAiChatService → SemanticKernelRagChatService
IAiChatCompletionClient → GroqChatCompletionClient (+ future DeepSeekChatCompletionClient)
IRagSearchService → RagSearchService
ITextExtractionService → PdfTextExtractionService (+ image extraction in Sprint 1)
IChunkingService → ChunkingService
IEmbeddingService → FakeEmbeddingService
IDocumentIngestionService → DocumentIngestionService (+ IImageDescriptionService in Sprint 1)
```

---

## New plan file
- `_PLAN_Sprint1_ImageUnderstanding_Sprint2_ModelComparison_Sprint3_SystemPrompt.md` — full implementation plan with file-level detail, effort estimates, risk assessment

---

## Known Issues (unchanged from previous handoff)
1. FakeEmbeddingService — deterministic feature-hash, not real semantic search
2. Synchronous ingestion — blocks request thread for large PDFs
3. No pagination on GET /documents
4. Supabase Storage dependency
5. Old DOCX files need "Reprocess"
6. `.doc`/`.ppt` not ingestion candidates
7. Groq vision API is "preview" — may change
8. DeepSeek V4 Flash RAG citation behavior unverified
