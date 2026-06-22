# Backend Handoff — 2026-06-20

## Session Summary
Replaced DeepSeek with Gemini 2.5 Flash as the benchmark challenger. Implemented provider routing factory, built/tested the Gemini client, but encountered free tier quota exhaustion (20 req/day) preventing the full 60-question benchmark.

## Completed
- **DeepSeek fully purged** — 2 files deleted (`DeepSeekOptions.cs`, `DeepSeekChatCompletionClient.cs`), DI and UI references cleaned, benchmark question updated
- **Gemini 2.5 Flash** — `GeminiOptions.cs`, `GeminiChatCompletionClient.cs` (Google `generateContent` API), factory routing in `IAiChatCompletionClientFactory`
- **Build** — 0 errors, 146/147 tests pass
- **Smoke test** (5 questions) — Gemini returns 429 (quota exhausted), routing confirmed working

## Current AI Stack

| Role | Model | Provider | Cost |
|------|-------|----------|------|
| Default RAG | `llama-3.3-70b-versatile` | Groq (free) | $0 |
| Challenger | `gemini-2.5-flash` | Google (free tier) | $0 (20 req/day limit) |
| Image describer | `meta-llama/llama-4-scout-17b-16e-instruct` | Groq (free) | $0 |

## Key Decisions
1. **DeepSeek replaced** with Gemini — both free, Gemini has working key, DeepSeek requires payment
2. **Gemini benchmark deferred** — free tier quota insufficient for 60 questions in one run
3. **Llama 3.3 70B stays default** — best available option (51.5% overall, 0 cost)

## Active Blockers
- Gemini 2.5 Flash free tier: 20 req/day/project — not enough for 60-question benchmark
- Solution options: wait 24h, create new Google project, or try gemini-1.5-flash (higher free limit)

## Service Architecture
```
IAiChatService → SemanticKernelRagChatService
  └─ IAiChatCompletionClientFactory → routes by prefix:
       └─ "gemini-*" → GeminiChatCompletionClient (Google generateContent API)
       └─ default    → GroqChatCompletionClient (OpenAI-compatible)
IRagSearchService → RagSearchService
ITextExtractionService → PdfTextExtractionService (+ image extraction)
IImageDescriptionService → GroqVisionDescriptionService
IChunkingService → ChunkingService
IEmbeddingService → FakeEmbeddingService
IDocumentIngestionService → DocumentIngestionService
```

## Key Files
- `Services/GeminiChatCompletionClient.cs` (new) — Google API client
- `Options/GeminiOptions.cs` (new) — Gemini config section
- `Services/IAiChatCompletionClientFactory.cs` (new) — routing abstraction
- `Services/AiChatCompletionClientFactory.cs` (updated) — Gemini routing
- `Program.cs` (updated) — Gemini DI registration
- `Components/Pages/AiChat.razor` (updated) — model selector

## Next Session
- Option A: Re-run Gemini benchmark after quota reset
- Option B: Test `gemini-1.5-flash` (60 req/min free tier)
- Option C: Create new Google AI project for fresh quota
- Add "Reprocess" button for old DOCX files
- Improve TableInterpretation / ArchitectureUnderstanding categories
