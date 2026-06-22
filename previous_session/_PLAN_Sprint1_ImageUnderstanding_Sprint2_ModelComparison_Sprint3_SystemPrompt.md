# Implementation Plan: Sprint 1-3

## Architectural Decisions

### Decision 1 — Keep Current Chunk Architecture

**Status: Zero changes needed.**

The current architecture uses:
- `DocumentChunk` — flat entity with `Content`, `Embedding` (384-dim), `ChunkIndex`, `PageNumber`
- `ChunkingService` — sliding window 1000/200 chars, paragraph-aware
- `RagSearchService` — single table, one `CosineDistance` query
- `DocumentIngestionService` — extraction → chunking → embedding

| File | Change | Why |
|------|--------|-----|
| None | — | No schema, entity, or search changes needed |

**Benefit**: Avoids 3-table migration, multi-index search fusion, and search-request rearchitecture. All 146 existing tests continue to pass unchanged.

---

### Decision 2 — Image Understanding (Inline Descriptions)

**Status: 7 files changed, ~300 new lines of code.**

#### Files affected

| # | File | Change | Effort |
|---|------|--------|--------|
| 1 | `Services/Rag/RagContracts.cs` | Add `byte[]? PngImage` to `ExtractedPage` | 5 min |
| 2 | `Services/Rag/RagContracts.cs` | Add `IImageDescriptionService` interface | 5 min |
| 3 | **New:** `Services/Rag/GroqVisionDescriptionService.cs` | `IImageDescriptionService` impl calling Groq Llama 4 Scout | 2-3 hrs |
| 4 | `Services/Rag/PdfTextExtractionService.cs` | Extract images from PDF via `page.GetImages()` | 3-4 hrs |
| 5 | `Services/Rag/PdfTextExtractionService.cs` | Extract images from DOCX via `ImagePart.GetStream()` | 1-2 hrs |
| 6 | `Services/Rag/PdfTextExtractionService.cs` | Extract images from PPTX via `SlidePart.ImageParts` | 1-2 hrs |
| 7 | `Services/Rag/DocumentIngestionService.cs` | After `_textExtraction.ExtractPagesAsync`, call `IImageDescriptionService` for each page with images, append description to `page.Text` | 2-3 hrs |
| 8 | `Program.cs` | Register `IImageDescriptionService` + named `HttpClient` for Groq vision | 15 min |
| 9 | **Tests:** `PdfTextExtractionServiceTests.cs` | Tests for image extraction from each format | 2-3 hrs |
| 10 | **Tests:** `DocumentIngestionServiceTests.cs` | Update `FakeTextExtractionService` + `BuildSut` to accept optional `IImageDescriptionService` | 30 min |

#### ExtractedPage change (minimal)

```csharp
// Current
public sealed record ExtractedPage(int? PageNumber, string Text);

// Updated
public sealed record ExtractedPage(
    int? PageNumber,
    string Text,
    IReadOnlyList<ExtractedImage>? Images = null);

public sealed record ExtractedImage(
    byte[] Data,
    string MimeType  // "image/png", "image/jpeg", etc.
);
```

#### Image description interface

```csharp
public interface IImageDescriptionService
{
    Task<string> DescribeAsync(
        IReadOnlyList<ExtractedImage> pageImages,
        CancellationToken cancellationToken = default);
}
```

#### GroqVisionDescriptionService

Reuses the existing OpenAI-compatible Groq endpoint at `https://api.groq.com/openai/v1` with:
- Model: `meta-llama/llama-4-scout-17b-16e-instruct`
- Content format: array of `{ type: "text" }` + `{ type: "image_url", url: "data:...;base64,..." }`
- Prompt: *"Describe the academic diagrams, charts, or figures in these images in 1-3 sentences. Focus on what a student needs to understand."*
- Batching: Pages with ≤5 images → single request; >5 images → split into batches of 5
- Error handling: Vision API fails → log warning, insert `[Image: description unavailable]`, continue ingestion

#### Image extraction by format

| Format | Method | Output | Edge case |
|--------|--------|--------|-----------|
| .pdf | `page.GetImages()` → `TryGetPng(out b)` → PNG bytes | `ExtractedImage(Data, "image/png")` | `TryGetPng` fails → `RawBytes` → JPEG bytes (for JPEG-in-PDF). JBIG2/JPX → skip with warning |
| .docx | `MainDocumentPart.ImageParts` → `.GetStream()` → byte array | `ExtractedImage(Data, imagePart.ContentType)` | No position mapping needed (all images on page 1 in current single-page-docx model) |
| .pptx | `SlidePart.ImageParts` → `.GetStream()` → byte array | `ExtractedImage(Data, imagePart.ContentType)` | Per-slide image grouping |

#### Ingestion pipeline change

```csharp
// In DocumentIngestionService.IngestAsync, after line 68:
var pages = await _textExtraction.ExtractPagesAsync(fileStream, document.MimeType, cancellationToken);

// NEW: Describe images and append to page text
if (_imageDescriptionService is not null)
{
    foreach (var page in pages)
    {
        if (page.Images is { Count: > 0 })
        {
            var description = await _imageDescriptionService.DescribeAsync(page.Images, cancellationToken);
            page = page with { Text = page.Text + "\n" + description };
        }
    }
}
```

#### Risk assessment for Decision 2

| Risk | Impact | Likelihood | Mitigation |
|------|--------|-----------|------------|
| Groq vision API is "preview" — behavior may change | Medium | Low | Pin model ID; log warnings on API changes |
| `TryGetPng` fails for exotic PDF filters | Low (images skipped) | Medium | Log warning, continue without description |
| 4MB base64 limit exceeded | Image skipped | Low | Check size before sending; skip if >3.5MB |
| Vision API rate limit (30 RPM) hit during ingestion | Ingestion slowed | Low | Add `Task.Delay` or retry with backoff |

---

### Decision 3 — DeepSeek V4 Flash Primary / Llama 3.3 70B Secondary

**Status: 8 files changed, ~200 new lines of code.**

#### Files affected

| # | File | Change | Effort |
|---|------|--------|--------|
| 1 | `Services/IAiChatCompletionClient.cs` | Add optional `string? ModelName` to `AiChatCompletionRequest` | 5 min |
| 2 | `Options/GroqOptions.cs` | Add `string? VisionModel` property for Llama 4 Scout | 5 min |
| 3 | **New:** `Options/DeepSeekOptions.cs` | `Endpoint`, `ApiKey`, `Model` config section | 10 min |
| 4 | `Services/GroqChatCompletionClient.cs` | Use `request.ModelName ?? _options.Model` in `BuildPayload` | 15 min |
| 5 | **New:** `Services/DeepSeekChatCompletionClient.cs` | Implements `IAiChatCompletionClient` — literally copies `GroqChatCompletionClient` with `_options.Endpoint` from `DeepSeekOptions`. ~80 lines | 1 hr |
| 6 | `Dtos/AiChatDtos.cs` | Add `string? Model` to `AiChatAskRequest` | 2 min |
| 7 | `Services/SemanticKernelRagChatService.cs` | Forward `request.Model` → `AiChatCompletionRequest` | 15 min |
| 8 | `Program.cs` | Register `DeepSeekOptions` + `DeepSeekChatCompletionClient` as named `IAiChatCompletionClient` | 15 min |
| 9 | **Tests:** `SemanticKernelRagChatServiceTests.cs` | Update test requests to include `Model` where needed | 15 min |
| 10 | **Tests:** new `DeepSeekChatCompletionClientTests.cs` | Basic HTTP-level tests | 1 hr |

#### Architecture for multi-model support

The cleanest approach with minimal refactoring:

```
IAiChatCompletionClient.CompleteAsync(AiChatCompletionRequest, CancellationToken)

AiChatCompletionRequest {
    string SystemPrompt,
    string UserPrompt,
    string? ModelName  // NEW — null means use default from config
}
```

`GroqChatCompletionClient` already has `_options.Model` with endpoint `api.groq.com/openai/v1`. The model is decided by `request.ModelName ?? _options.Model`:

- Current chat queries → `model: "llama-3.3-70b-versatile"` (default in config)
- Comparison queries → `model: "meta-llama/llama-4-scout-17b-16e-instruct"` (overridden per request)

`DeepSeekChatCompletionClient` is a near-identical class with a different base URL (`api.deepseek.com`) and different config options. It also implements `IAiChatCompletionClient`.

#### Model selection flow

```
Frontend asks:
  POST /api/ai/chat/ask { question, model: "deepseek-v4-flash" }

AiChatController → SemanticKernelRagChatService.AskAsync

  CompletionRequest.ModelName = request.Model  // "deepseek-v4-flash"

  // Currently: GroqChatCompletionClient always runs
  // Future: Router selects client based on ModelName prefix:
  //   "llama-*"  → GroqChatCompletionClient
  //   "deepseek-*" → DeepSeekChatCompletionClient
  //   default → GroqChatCompletionClient with config default
```

#### Cost analysis for primary models

| Model | Input/1M | Output/1M | Monthly (1K queries) | Note |
|-------|----------|-----------|---------------------|------|
| **DeepSeek V4 Flash** | $0.14 | $0.28 | ~$0.17 after free credits | Primary — cheapest capable model |
| **Llama 3.3 70B** | $0.59 | $0.79 | **$0** (free tier) | Secondary — within 100K TPD |
| **Llama 4 Scout vision** | $0.11 | $0.34 | **$0** (free tier) | Image describer — within 500K TPD |

#### Risk assessment for Decision 3

| Risk | Impact | Likelihood | Mitigation |
|------|--------|-----------|------------|
| DeepSeek V4 Flash RAG citation quality unverified | Medium — may need prompt tuning | Medium | Test empirically before making default. Keep Llama 3.3 70B as fallback |
| DeepSeek free tier rate limits unknown | Low — throttled queries | Medium | Add retry logic; fall back to Groq |
| Chinese-hosted API latency | Low — acceptable for async | Low | Acceptable for student tool |
| Two `IAiChatCompletionClient` implementations need routing | Medium — more complex DI | Low | Start simple: frontend sends model name, single `GroqChatCompletionClient` handles Groq models. Add DeepSeek as separate route later |

---

### Decision 4 — Keep Current Embedding

**Status: Zero changes needed.**

| File | Change | Why |
|------|--------|-----|
| None | — | 384-dim `FakeEmbeddingService`, `DocumentChunk.EmbeddingDimension`, `RagSearchService` checks all stay |

**Benefit**: Avoids DB migration (`vector(384)` → `vector(1024)`), re-ingestion of all documents, embedding service replacement, and test updates (10+ files). Zero risk.

---

### Decision 5 — Future Multimodal Not Phase 1

**Status: Zero changes needed for Phase 1.**

The "describe once at ingestion" pattern naturally supports future phases without structural changes:

| Phase | How it works | Impact on current code |
|-------|-------------|----------------------|
| Phase 1 (now) | Image → description text → inline → chunk → embed | This sprint |
| Phase 2 (future) | Store raw image bytes alongside chunk + add vision embedding model | Add `ImageRawBytes` column to `DocumentChunk` (nullable) |
| Phase 3 (future) | Image retrieval + text retrieval fusion | Add second vector index for images |
| Phase 4 (future) | True multimodal model sees image + text | Add `image_url` to completion request content array |

**Benefit of inline approach**: When Phase 2-4 arrives, the existing inline descriptions remain as text-indexed content. New capabilities are additive, not replacement.

---

## Sprint Plan

---

### Sprint 1 — Image Understanding (highest ROI)

**Goal**: Ship image description for all 3 document formats.

**Estimated effort**: 4-5 developer-days.

| Day | Task | Files |
|-----|------|-------|
| 1 | Add `ExtractedImage` record + `IImageDescriptionService` interface | `RagContracts.cs` |
| 1 | Implement `GroqVisionDescriptionService` | **New:** `Services/Rag/GroqVisionDescriptionService.cs` |
| 2 | Extract images from PDF in `PdfTextExtractionService` | `PdfTextExtractionService.cs` (method `ExtractPdfImages`) |
| 2 | Extract images from DOCX + PPTX | `PdfTextExtractionService.cs` (methods `ExtractDocxImages`, `ExtractPptxImages`) |
| 3 | Modify `DocumentIngestionService` to call image description | `DocumentIngestionService.cs` |
| 3 | Register in DI + wire up config | `Program.cs`, `appsettings.json` |
| 4 | Unit tests for all new code | Test files |
| 4 | Build + verify all 146 tests pass | `dotnet test` |
| 5 | Manual testing with real .pdf/.docx/.pptx containing images | — |

**Acceptance criteria:**
- PDF with 3 embedded images → each described and appended to page text before chunking
- DOCX with inline images → described and appended
- PPTX with slide images → described and appended
- Document without images → behavior unchanged
- Image extraction failure → log warning, continue ingestion (no crash)
- All 146 existing tests pass

**Risk level**: LOW. PdfPig `TryGetPng` and OpenXml `ImagePart.GetStream` are well-documented APIs. The Groq vision API is the only new dependency, and it reuses the existing endpoint.

---

### Sprint 2 — Model Upgrade + Comparison Foundation

**Goal**: Switch primary model, add model comparison capability.

**Estimated effort**: 3-4 developer-days.

| Day | Task | Files |
|-----|------|-------|
| 1 | Add `ModelName` to `AiChatCompletionRequest` + `Model` to `AiChatAskRequest` | `IAiChatCompletionClient.cs`, `AiChatDtos.cs` |
| 1 | Update `GroqChatCompletionClient.BuildPayload` to use `request.ModelName ?? _options.Model` | `GroqChatCompletionClient.cs` |
| 2 | Change `GroqOptions.Model` default to `llama-3.3-70b-versatile` | `GroqOptions.cs` |
| 2 | Forward model from `AskAsync` → `AiChatCompletionRequest` | `SemanticKernelRagChatService.cs` |
| 3 | Add `DeepSeekOptions` config + `DeepSeekChatCompletionClient` | **New:** `Options/DeepSeekOptions.cs`, `Services/DeepSeekChatCompletionClient.cs` |
| 3 | Register in DI (named `IAiChatCompletionClient` registrations) | `Program.cs` |
| 4 | Frontend: model selector dropdown in `AiChat.razor` | `AiChat.razor` |
| 4 | Tests + build verification | Test files |

**Acceptance criteria:**
- Default query → `llama-3.3-70b-versatile` (one-line config change)
- Query with `modelName: "meta-llama/llama-4-scout-17b-16e-instruct"` → different model
- DeepSeek V4 Flash client works (with own API key)
- Model selector appears in chat UI
- All existing tests pass

**Risk level**: LOW-MEDIUM. DeepSeek V4 Flash RAG citation quality needs empirical testing. The code structure is straightforward (OpenAI-compatible endpoint reuse).

---

### Sprint 3 — System Prompt Tuning + Polish

**Goal**: Optimize RAG quality, add tutoring tone, performance profiling.

**Estimated effort**: 2-3 developer-days.

| Day | Task | Files |
|-----|------|-------|
| 1 | Update `BuildSystemPrompt()` — add tutoring tone | `SemanticKernelRagChatService.cs` |
| 1 | Tune image description prompt for academic diagrams | `GroqVisionDescriptionService.cs` |
| 2 | Performance profiling: image extraction time, vision API latency, chunk size impact | — |
| 2 | Error handling hardening: vision timeout, rate limit backoff | `GroqVisionDescriptionService.cs` |
| 3 | Update `_PLAN_AI_Model_Comparison.md` | `previous_session/_PLAN_AI_Model_Comparison.md` |
| 3 | Update session log + documentation | `_CURRENT_SESSION.md` |

**Acceptance criteria:**
- System prompt includes tutoring tone rules
- Image descriptions are accurate for architecture diagrams, UML, charts
- Ingestion with images completes within 2x time of text-only ingestion
- Rate limit exceeded → graceful backoff + warning, not crash
- All 146 tests pass

**Risk level**: LOW. Tuning only, no structural changes.

---

### Optional Future Work (Phase 2+)

| Feature | When | Effort | Prerequisite |
|---------|------|--------|-------------|
| Store raw image bytes in `DocumentChunk.ImageRawBytes` | When vision embedding is planned | 1 day | DB migration + nullable column |
| BGE-M3 embedding migration | When multilingual docs exist + retrieval quality is insufficient | 3-4 days | DB migration + re-ingest all docs + test updates |
| Vision embedding (CLIP/siglip) | When image retrieval is required | 5-7 days | New embedding service + second vector index |
| Multi-index search fusion | When Phase 3 starts | 3-5 days | New search service with cross-type ranking |
| Background job queue for ingestion | When >10 concurrent users | 5-7 days | Redis/RabbitMQ + worker service |
| Gemini Flash integration | When true multimodal RAG is funded | 3-5 days | Google AI SDK + new `IAiChatCompletionClient` |

---

## Summary of Changes by Decision

| Decision | Files Changed | New Files | Tests Changed | Effort | Risk |
|----------|--------------|-----------|---------------|--------|------|
| 1. Keep chunk architecture | 0 | 0 | 0 | 0 | None |
| 2. Image understanding | 4 | 1 | 2 | 4-5 days | LOW |
| 3. Model upgrade + comparison | 6 | 3 | 2 | 3-4 days | LOW-MED |
| 4. Keep embedding | 0 | 0 | 0 | 0 | None |
| 5. Future multimodal (Phase 2+) | 0 | 0 | 0 | 0 | None |
| **Total sprint 1-3** | **10** | **4** | **4** | **~10 days** | **LOW** |

---

## Key Metrics

| Metric | Current | After Sprint 1-3 |
|--------|---------|------------------|
| Supported formats | PDF, DOCX, PPTX, TXT | Same + images within all 3 |
| Chunk architecture | Flat `DocumentChunk` | Unchanged |
| Embedding dimension | 384 | Unchanged |
| Primary model | `llama-3.1-8b-instant` | `deepseek-v4-flash` (or `llama-3.3-70b-versatile` as fallback) |
| Image handling | None | Llama 4 Scout descriptions inline before chunking |
| Model comparison | None | Model name field in request; selector in UI |
| Passing tests | 146 | 146+ (new tests added, none removed) |
