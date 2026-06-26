# Live Log: Phase 3 — Wire Quiz into Chat History

**Date:** 2026-06-21
**Branch:** sprint2/integration
**Status:** In Progress

## Problem
Quiz generation was not persisted to chat history. `QuizService.GenerateAsync` created a `Quiz` entity in the `quizzes` table but never called `SaveExchangeAsync` to save a quiz exchange to `chat_messages`. After page refresh, quiz cards (from chat history) would not render because no `chat_messages` entries had quiz metadata.

Additionally, quiz cards reconstructed from chat history had only lightweight `QuizDto` (empty questions list) — opening them showed a broken dialog.

## Root Cause
`IChatPersistenceService.SaveExchangeAsync` was designed for regular Q&A exchanges. Quiz generation bypassed the chat persistence layer entirely. The `ChatPersistenceService.MessageMetadata` record already had `QuizId`/`QuizTitle`/`QuizStatus` fields (added in Phase 1), but no code path ever set them.

## Changes

### 1. `Services/IChatPersistenceService.cs`
- Added `SaveQuizExchangeAsync(Guid supabaseUserId, Guid sessionId, string scopeLabel, string userContent, Guid quizId, string quizTitle, string quizStatus, CancellationToken ct)` — creates a (user, assistant) message pair with quiz metadata in `metadata_json`.

### 2. `Services/ChatPersistenceService.cs`
- Implemented `SaveQuizExchangeAsync`: creates user message with `userContent`, assistant message with empty content + metadata containing `QuizId`, `QuizTitle`, `QuizStatus`. Updates session `UpdatedAt` and auto-generates title if blank.

### 3. `Dtos/QuizDtos.cs`
- Added `string? ScopeLabel = null` to `GenerateQuizRequest` — allows client to pass document scope context to the service for chat history messages.

### 4. `Services/QuizService.cs`
- Added `IChatPersistenceService _chatPersistence` dependency.
- After saving quiz entity, calls `_chatPersistence.SaveQuizExchangeAsync` with scope label, user message ("Generate quiz: {title}"), and quiz metadata.
- Added `BuildScopeLabel(GenerateQuizRequest)` helper.
- Added `GetByIdAsync(Guid supabaseUserId, Guid quizId, CancellationToken)` for fetching a single quiz by ID.

### 5. `Services/IQuizService.cs`
- Added `GetByIdAsync` method signature.

### 6. `Controllers/QuizController.cs`
- Added `GET /api/quiz/{quizId:guid}` endpoint — returns full `QuizDto` by ID with ownership validation. Returns 404 if not found.

### 7. `Services/AiChatApiClient.cs`
- Added `GetQuizAsync(string accessToken, Guid quizId, CancellationToken)` — calls `GET /api/quiz/{quizId}`. Returns null on 404.

### 8. `Components/Pages/AiChat.razor`
- `GenerateQuizAsync`: Passes `ScopeLabel: BuildScopeLabel()` in `GenerateQuizRequest`.
- `OpenQuizDialogAsync`: Before opening dialog, checks if `quiz.Questions.Count == 0` (lightweight from chat history). If so, fetches full quiz from API via `GetQuizAsync` and caches it.

## Build
- 0 errors, 149/150 tests pass (1 pre-existing skip)
- No regressions

## Next Steps
- P2: Add rate limiting error message (check Groq 429 responses)
- P2: `QuizCard` still uses lightweight `QuizDto` from `ConvertMessagesToExchanges` — the `OpenQuizDialogAsync` fetches full data on demand. Could optimize by fetching earlier.
