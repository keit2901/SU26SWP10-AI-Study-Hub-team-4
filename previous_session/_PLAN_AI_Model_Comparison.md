# Plan: Multi-Model AI Comparison Feature

## Goal
Enable side-by-side comparison of AI model answers when answering questions with RAG-scoped documents, to evaluate chunking technique effectiveness across different models.

## Proposed Models for Comparison

### Tier 1: Groq-hosted (zero migration — just add API model name)

| Model | Knowledge Cutoff | Context | Speed (TPS) | Input/Output per 1M | Why |
|-------|-----------------|---------|-------------|---------------------|-----|
| **llama-3.1-8b-instant** (current) | Dec 2023 | 128K | 840 | $0.05 / $0.08 | Baseline — fastest, cheapest |
| **llama-4-scout-17b-16e-instruct** | ~2025 | 128K | 594 | $0.11 / $0.34 | Newer architecture — tests recency impact on chunk interpretation |
| **llama-3.3-70b-versatile** | ~early 2024 | 128K | 394 | $0.59 / $0.79 | Larger same-family — tests if bigger model extracts more from chunks |

### Tier 2: External API (requires separate API key + new client)

| Model | Provider | Cutoff | Context | Input/Output per 1M | Integration Cost |
|-------|----------|--------|---------|---------------------|------------------|
| **Gemini 3.5 Flash** | Google | **Jan 2025** | 1M | $1.50 / $9.00 | Free tier available; Google AI SDK |
| **GPT-4o mini** | OpenAI | Oct 2023 | 128K | $0.15 / $0.60 | OpenAI SDK; API key needed |
| **DeepSeek V4 Flash** | DeepSeek | ~2025 | 1M | $0.14 / $0.28 | OpenAI-compatible endpoint; very cheap |

### Recommended first addition (easiest)
**Llama 4 Scout** — already on Groq, zero infrastructure change, just add the model ID. Newest knowledge cutoff among Groq models.

## Architecture Changes Needed

### Backend
1. **`AiChatCompletionRequest`** — add `string? ModelName` property (nullable, falls back to config)
2. **`GroqChatCompletionClient.BuildPayload`** — use `request.ModelName ?? _options.Model`
3. **`AiChatAskRequest`** — add `string? Model` for frontend to pass model choice
4. **`SemanticKernelRagChatService.AskAsync`** — forward model from request to completion request
5. **`AiChatController.Ask`** — accept model param in query/body

### Frontend (AiChat.razor)
1. **Model selector** dropdown in chat header
2. **Compare toggle** — when on, sends question to 2 selected models simultaneously
3. **Comparison table** — side-by-side answers with model labels, source markers preserved
4. **Optionally** — save comparison results to DB for later review

### Data Model (optional)
- `AiComparisonResult` table: `Id`, `Question`, `ChunksUsed`, `ModelA_Id`, `ModelA_Answer`, `ModelB_Id`, `ModelB_Answer`, `CreatedAt`
- API endpoint to list/export comparisons

## Implementation Order
1. Add model selector (single-model switch) to AiChat page
2. Add compare mode toggle + dual API calls
3. Add comparison result rendering side-by-side
4. Add export/save comparison results
5. Integrate Groq-hosted alternatives (Llama 4 Scout, Llama 3.3 70B)
6. Future: add Google Gemini / OpenAI clients for broader comparison

## Open Questions
- Should comparison mode use the same RAG chunks for both models, or run RAG independently per model?
  → *Recommended: same chunks for fair comparison of answer generation, not retrieval*
- Should we limit comparison mode to two fixed models (e.g., current + Llama 4 Scout) or allow any pair?
- How many simultaneous questions per user to prevent rate-limit issues?
