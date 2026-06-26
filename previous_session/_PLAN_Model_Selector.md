# Future Feature Plan: Production Model Selector

**Status: Implemented (2026-06-21).**

## 1. Objectives

- Allow users to choose which model answers their questions.
- Support experimentation between providers (Groq, Google Gemini).
- Preserve a single RAG pipeline (text-based, image-description-augmented).
- Keep the production default stable regardless of selector state.

## 2. Candidate Models

| Model | Provider | Default | Rationale |
|-------|----------|---------|-----------|
| `llama-3.3-70b-versatile` | Groq (free) | **Yes** | Production baseline. Strong instruction following, fast (684ms P50), free tier, 32K context. |
| `gemini-2.5-flash` | Google AI Studio (free) | No | Benchmark challenger. 1M context, native multimodal (future), free tier (~1,500 RPD), Jan 2025 cutoff. |
| Future providers | TBD | No | New providers added via `IAiChatCompletionClientFactory` pattern — register prefix, add client, done. |

## 3. UI Concept

- **Dropdown selector** in the chat sidebar (existing MudSelect on `AiChat.razor`).
- **Default**: `llama-3.3-70b-versatile` — hardcoded in `GroqOptions.Model`, also the fallback when no model is selected.
- **User preference persistence** (future): Save selected model to user profile in Supabase `profiles` table. Apply on login. Revert to default if the saved model is unavailable.
- **Visual indicator**: Show provider name and free/paid badge next to each option.

## 4. Architecture Impact

- **Routing**: Already handled by `IAiChatCompletionClientFactory` + `AiChatCompletionClientFactory`. Model prefix routes to the right client (`llama-*` → Groq, `gemini-*` → Gemini).
- **Factory usage**: No changes needed. The factory already supports extensible prefix-based routing.
- **Benchmark implications**: The `BenchmarkRunner` already uses `IAiChatCompletionClientFactory.GetProviderName()` for provider tracking. Adding Gemini = adding one route prefix.

## 5. Risks

- **Cost**: Users selecting paid-only models without warning. Mitigation: label each option with "(free)" or "(paid)".
- **Latency**: Different providers have different response times. Users may perceive the app as slow when a slow provider is selected.
- **User confusion**: Too many options overwhelm non-technical users. Mitigation: keep 2–3 options maximum, mark default clearly.
- **Provider availability**: If a provider's API key is not configured, the factory already falls back to Groq transparently.
