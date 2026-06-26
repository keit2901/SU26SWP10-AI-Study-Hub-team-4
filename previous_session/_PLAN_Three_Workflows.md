# Implementation Plan: Three New Workflows

---

# Workflow A: AI-Generated Flashcards from Documents
*(Replaces the current plain-text Quiz prompt with a structured, persistent workflow)*

## Current State
The "Generate Quiz" feature is a single suggested prompt in `AiChat.razor:748` that sends raw text to the AI:
```
"Generate 8 multiple-choice quiz questions from my selected context..."
```
The AI returns unstructured text. Nothing is parsed, saved, or tracked. Each session starts fresh.

## Target Workflow

```
User selects document(s) + clicks "Generate Flashcards"
  → System sends chunks + structured prompt to AI
  → AI returns JSON array of Q&A pairs
  → System parses JSON, creates FlashcardDeck (Status=Draft)
  → User reviews, edits, deletes individual cards
  → User publishes deck (Status=Active)
  → User studies cards: flip, mark correct/incorrect
  → System tracks per-card stats (spaced repetition)
  → User can restart, reshuffle, or archive
```

## State Machine

```
FlashcardDeck: Generating → Draft → Active → Archived
                          ↘ Failed
Flashcard:      New → Learning → Known → Mastered
                                        ↘ Forgotten → Learning
```

## Actors
- **User**: trigger generation, review cards, study
- **AI (Groq LLM)**: generate Q&A pairs from document chunks
- **System**: parse AI output, manage deck lifecycle, track study stats

## Branching Conditions
1. AI returns invalid/malformed JSON → retry with stricter prompt (max 2 retries) → if still fails → set Status=Failed, show error
2. User edits card → card stays in `New` state
3. User publishes deck with 0 cards → error: "Add at least one card"
4. During study: card marked `Known` 3 consecutive times → promote to `Mastered`
5. Card marked `Forgotten` after `Mastered` → demote to `Learning`
6. Deck with 0 `New` + `Learning` cards → show "All cards mastered!"

## New Entities

### FlashcardDeck
| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| UserId | Guid | FK → users |
| DocumentId | Guid? | FK → documents (nullable for manual cards) |
| FolderId | Guid? | FK → folders (scope) |
| Title | string | Auto-generated or user-defined |
| Status | FlashcardDeckStatus | Generating, Draft, Active, Archived, Failed |
| TotalCards | int | Denormalized count |
| CardsMastered | int | Denormalized |
| CreatedAt | DateTimeOffset | |
| UpdatedAt | DateTimeOffset | |

### FlashcardDeckStatus enum
```
Generating = 0, Draft = 1, Active = 2, Archived = 3, Failed = 4
```

### Flashcard
| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| DeckId | Guid | FK → flashcard_decks (cascade) |
| Question | string | |
| Answer | string | |
| SourceChunkIndex | int? | For RAG traceability |
| SourceDocumentId | Guid? | |
| SourcePageNumber | int? | |
| Status | FlashcardStatus | New, Learning, Known, Mastered |
| TimesCorrect | int | Consecutive correct answers |
| TimesIncorrect | int | Total incorrect |
| LastStudiedAt | DateTimeOffset? | |
| SortOrder | int | For manual reordering |
| CreatedAt | DateTimeOffset | |

### FlashcardStatus enum
```
New = 0, Learning = 1, Known = 2, Mastered = 3
```

### FlashcardStudySession (tracks each study attempt)
| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| DeckId | Guid | FK → flashcard_decks |
| UserId | Guid | FK → users |
| StartedAt | DateTimeOffset | |
| CompletedAt | DateTimeOffset? | |
| CardsReviewed | int | |
| CardsCorrect | int | |

### FlashcardReviewLog (per-card attempt)
| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| SessionId | Guid | FK → flashcard_study_sessions |
| CardId | Guid | FK → flashcards |
| WasCorrect | bool | |
| ReviewedAt | DateTimeOffset | |

## API Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/flashcards/decks/generate` | Trigger AI generation for selected document(s). Body: `{ DocumentIds, FolderId?, Count?, Prompt? }`. Returns deck in Generating state. |
| GET | `/api/flashcards/decks` | List user's decks. Query: `?status=Active&folderId=...`. |
| GET | `/api/flashcards/decks/{id}` | Get deck with cards |
| PUT | `/api/flashcards/decks/{id}` | Update deck title, status (publish/archive) |
| DELETE | `/api/flashcards/decks/{id}` | Delete deck + cascade cards |
| PUT | `/api/flashcards/cards/{id}` | Edit a card's question/answer |
| POST | `/api/flashcards/sessions` | Start a study session. Body: `{ DeckId }`. Returns session with shuffled due cards. |
| POST | `/api/flashcards/sessions/{id}/review` | Submit one card review. Body: `{ CardId, WasCorrect }`. Updates card status + returns next card (or session summary if done). |
| GET | `/api/flashcards/sessions/{id}` | Get session progress |

## Implementation Order

### Phase 1 — Backend (3-4 days)
1. Create new migration: `FlashcardDeck`, `Flashcard`, `FlashcardStudySession`, `FlashcardReviewLog` tables + enums
2. Create DTOs: `FlashcardDeckDto`, `FlashcardDto`, `FlashcardSessionDto`, `FlashcardReviewRequest`, `GenerateDeckRequest`
3. Create `IFlashcardService` + `FlashcardService`:
   - `GenerateAsync`: call existing chunking service + Groq with structured prompt, parse JSON response, create deck in Draft
   - `GenerateAsync` branching: retry on malformed JSON (max 2), handle empty output
   - `PublishAsync`: validate at least 1 card, set Active
   - `StartSessionAsync`: shuffle cards where Status != Mastered, create session
   - `ReviewCardAsync`: update card status, advance session, track log
4. Create `FlashcardsController` with the endpoints above
5. Update `Program.cs` DI registration

### Phase 2 — Frontend (3 days)
1. **Flashcard list page** (`/flashcards`): MudTable of decks with status badges, search, folder filter
2. **Deck detail page** (`/flashcards/{id}`): card review/edit area
   - Generate button → loading spinner → deck appears in Draft
   - Draft mode: cards displayed as editable rows (question + answer text fields, delete button)
   - Publish button → transitions to Active
3. **Study mode**: Full-screen card-flip UI
   - Show question → click to reveal answer → "Correct" / "Incorrect" buttons
   - Progress bar (cards reviewed / total)
   - Session summary (score, time, mastered/unmastered breakdown)
4. **Smart Actions panel** in AiChat.razor: update "Generate Quiz" button to POST to flashcard generation endpoint instead of filling prompt text

### Phase 3 — Polish (1 day)
- Spaced repetition algorithm (Leitner or SM-2 simplified)
- Review history per card
- Re-study failed cards only
- Export flashcards as CSV/PDF

---

# Workflow B: Collaborative Study Session

## Target Workflow

```
Host opens a shared folder → clicks "Start Study Session"
  → System creates StudySession (Status=Open)
  → System generates invite code (6-char alphanumeric)
  → Guests enter code or click invite link → join session
  → All participants share synchronized AI chat workspace
     (same document scope, same Q&A history, same RAG context)
  → Host clicks "End Session"
  → System saves session transcript, updates Status=Closed
  → Session summary page available to all participants
```

## State Machine

```
StudySession: Open → Active → Closed
                    ↘ Expired (invite code TTL)
```

## Actors
- **Host**: user who owns the folder, creates/ends session
- **Guest**: any authenticated user who joins via code
- **System**: sync chat state across participants, handle concurrent messages

## Branching Conditions
1. Invite code expires (TTL = 24 hours) → session moves to `Expired`, no new joins allowed
2. Host leaves → session continues (guests can still chat). If host + all guests leave → auto-close after 5 minutes
3. Guest tries to join with invalid code → error: "Session not found"
4. Guest tries to join expired session → "This study session has ended"
5. Host reopens a closed session → new invite code generated, guests need to rejoin
6. Document scope changes mid-session (host adds/removes documents) → system broadcasts scope change to all participants

## New Entities

### StudySession
| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| HostUserId | Guid | FK → users |
| FolderId | Guid | FK → folders |
| Title | string | Auto-generated or host-set |
| Status | StudySessionStatus | Open, Active, Closed, Expired |
| InviteCode | string | 6-char alphanumeric (unique) |
| InviteCodeExpiresAt | DateTimeOffset | 24h after creation |
| MaxParticipants | int | Default 10 |
| ParticipantCount | int | Denormalized |
| StartedAt | DateTimeOffset | |
| EndedAt | DateTimeOffset? | |
| CreatedAt | DateTimeOffset | |

### StudySessionParticipant
| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| SessionId | Guid | FK → study_sessions |
| UserId | Guid | FK → users |
| Role | string | "Host" or "Guest" |
| JoinedAt | DateTimeOffset | |
| LeftAt | DateTimeOffset? | |

### StudySessionMessage
| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| SessionId | Guid | FK → study_sessions |
| UserId | Guid | FK → users |
| Question | string | |
| Answer | string | |
| AiModelUsed | string | Which model answered |
| Sources | jsonb | Snapshot of RAG sources at time of answer |
| DurationMs | int | |
| CreatedAt | DateTimeOffset | |

### StudySessionScopeSnapshot
| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| SessionId | Guid | FK → study_sessions |
| DocumentIds | Guid[] | Array of document IDs in scope |
| FolderId | Guid? | |
| ChangedAt | DateTimeOffset | |

### StudySessionStatus enum
```
Open = 0, Active = 1, Closed = 2, Expired = 3
```

## API Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/study-sessions` | Create session from folder. Body: `{ FolderId, Title? }`. Returns session + invite code. |
| POST | `/api/study-sessions/join` | Join by code. Body: `{ InviteCode }`. Returns session info + current scope + history. |
| GET | `/api/study-sessions/{id}` | Get session details + participants |
| POST | `/api/study-sessions/{id}/ask` | Ask a question within the session (like /ai/chat/ask but scoped to session, recorded in messages). Body: `{ Question, DocumentIds? }`. |
| GET | `/api/study-sessions/{id}/messages` | Get session Q&A history |
| POST | `/api/study-sessions/{id}/scope` | (Host only) Update document scope. Body: `{ DocumentIds, FolderId? }`. Creates scope snapshot, broadcasts to participants. |
| POST | `/api/study-sessions/{id}/end` | (Host only) End session. Sets Closed, generates summary. |
| GET | `/api/study-sessions/{id}/summary` | Get end-of-session summary (questions asked, top sources used, participant stats) |

## Real-Time Sync Strategy
Since the project uses Blazor Server (not WASM), real-time sync can leverage SignalR (built into Blazor Server):

1. `StudySessionHub` — SignalR hub with methods:
   - `JoinSessionGroup(sessionId)` → adds connection to SignalR group
   - `LeaveSessionGroup(sessionId)`
   - `ReceiveMessage(message)` — triggered when any participant asks a question; broadcasts answer to all group members
   - `ScopeChanged(scopeSnapshot)` — triggered when host updates document scope
   - `SessionEnded(summary)` — broadcast to all participants

2. The existing `AiChatSessionState` needs per-session isolation: when part of a session, chat history comes from `StudySessionMessage` table, not from local in-memory state.

## Implementation Order

### Phase 1 — Backend Core (2-3 days)
1. Migration: `StudySession`, `StudySessionParticipant`, `StudySessionMessage`, `StudySessionScopeSnapshot` tables + enums
2. DTOs: all request/response objects
3. `IStudySessionService` + `StudySessionService`:
   - `CreateAsync`: validate folder ownership + sharing status, generate unique invite code, create session in Open
   - `JoinAsync`: validate invite code (exists, not expired, not full), create participant record
   - `AskAsync`: reuses existing `RagSearchService` + `GroqChatCompletionClient` but stores result in `StudySessionMessage`
   - `EndAsync`: set Closed timestamp, generate summary from messages
4. `StudySessionsController` + SignalR `StudySessionHub`
5. DI registration in `Program.cs`

### Phase 2 — Frontend (2-3 days)
1. **Create session**: button on folder card `/documents` or folder detail → dialog with optional title → shows invite code + copy button
2. **Join session**: `/study/join` page → text field for invite code → redirects to session workspace
3. **Session workspace** (`/study/sessions/{id}`): modified version of AiChat.razor with:
   - Participant list sidebar (host badge, online dots)
   - Invite code banner (copy button, "share this code" tooltip)
   - Chat history from session (shared across participants)
   - Document scope indicator (which docs are active)
   - End Session button (host only)
4. **Session summary** (`/study/sessions/{id}/summary`): post-session page with question log, most-cited sources, participant activity

### Phase 3 — Real-Time Sync (1-2 days)
1. SignalR hub implementation (receive question → ask AI → broadcast answer to group)
2. Blazor Server circuit management: ensure each participant's chat state stays in sync
3. Handle disconnects: participant leaves → update `LeftAt`; if all gone → start 5-min auto-close timer

### Phase 4 — Polish (1 day)
- "Session ended" notification for remaining participants
- Resume from closed session (read-only archive)
- Limit concurrent sessions per user (e.g., max 2)

---

# Workflow C: Structured Quiz Generation & Tracking
*(Fixes the current plain-prompt Quiz into a full workflow)*

## Current State
`AiChat.razor:748` sends a text prompt to the AI:
```
"Generate 8 multiple-choice quiz questions from my selected context..."
```
Problems:
1. AI output is unstructured text — no guaranteed JSON
2. Quiz is not saved — disappear on page refresh
3. No score tracking or retake capability
4. No way to review answers later
5. Citations are unreliable in generated output

## Target Workflow

```
User selects document(s) → clicks "Generate Quiz"
  → System sends chunks + strict JSON schema prompt
  → AI returns quiz as JSON array
  → System validates JSON against schema
  → System creates Quiz (Status=Draft)
  → User reviews questions, edits, regenerates specific questions
  → User publishes quiz (Status=Published)
  → User takes quiz: answer each question, submit
  → System scores automatically, shows results with explanations
  → User can retake (shuffled) or review history
```

## State Machine

```
Quiz:    Generating → Draft → Published → Archived
                    ↘ Failed
Attempt: InProgress → Completed
```

## Actors
- **User**: generate, review, take quiz
- **AI (Groq LLM)**: generate questions from document chunks
- **System**: validate schema, auto-score, track attempts

## Branching Conditions
1. AI returns malformed JSON → retry with stricter prompt (max 2) → if fails → set Status=Failed
2. User publishes quiz with 0 questions → error: "Add at least one question"
3. User regenerates a specific question → replaces that question, keeps rest
4. Retake: shuffle question order, reset attempt, keep same questions
5. No documents selected → error: "Select at least one document first"
6. If `UseLocalDemoFallback=true` and Groq fails → demo quiz (2 sample questions based on first source)

## New Entities

### Quiz
| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| UserId | Guid | FK → users |
| DocumentId | Guid? | FK → documents |
| FolderId | Guid? | |
| Title | string | Auto-generated |
| Status | QuizStatus | Generating, Draft, Published, Archived, Failed |
| TotalQuestions | int | |
| CreatedAt | DateTimeOffset | |
| UpdatedAt | DateTimeOffset | |

### QuizQuestion
| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| QuizId | Guid | FK → quizzes |
| QuestionText | string | |
| OptionA | string | |
| OptionB | string | |
| OptionC | string | |
| OptionD | string | |
| CorrectOption | string | "A", "B", "C", or "D" |
| Explanation | string | Why the answer is correct |
| SourceChunkIndex | int? | For citation traceability |
| SourcePageNumber | int? | |
| SortOrder | int | |

### QuizAttempt
| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| QuizId | Guid | FK → quizzes |
| UserId | Guid | FK → users |
| StartedAt | DateTimeOffset | |
| CompletedAt | DateTimeOffset? | |
| TotalQuestions | int | |
| CorrectAnswers | int | Auto-calculated on completion |
| ScorePercent | decimal | |

### QuizAnswer
| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| AttemptId | Guid | FK → quiz_attempts |
| QuestionId | Guid | FK → quiz_questions |
| SelectedOption | string | "A", "B", "C", "D", or null (skipped) |
| IsCorrect | bool | Auto-calculated |
| AnsweredAt | DateTimeOffset | |

### QuizStatus enum
```
Generating = 0, Draft = 1, Published = 2, Archived = 3, Failed = 4
```

## API Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/quizzes/generate` | Trigger AI generation. Body: `{ DocumentIds, FolderId?, QuestionCount? }`. Returns quiz in Generating. |
| GET | `/api/quizzes` | List user's quizzes |
| GET | `/api/quizzes/{id}` | Get quiz with questions (correct answers hidden unless an attempt is completed) |
| PUT | `/api/quizzes/{id}` | Update title, status (publish/archive) |
| PUT | `/api/quizzes/{id}/questions/{qid}` | Edit a specific question's text/options |
| POST | `/api/quizzes/{id}/regenerate-question/{qid}` | Replace a single question via AI |
| DELETE | `/api/quizzes/{id}` | Delete quiz + cascade |
| POST | `/api/quizzes/{id}/attempts` | Start a new attempt. Returns shuffled questions WITHOUT correct answers. |
| POST | `/api/quizzes/{id}/attempts/{aid}/submit` | Submit all answers. Body: `{ Answers: [{ QuestionId, SelectedOption }] }`. Returns score + correct answers + explanations. |
| GET | `/api/quizzes/{id}/attempts` | List attempt history for a quiz |
| GET | `/api/quizzes/{id}/attempts/{aid}` | Get completed attempt with answers + explanations |

## AI Prompt Design

The structured prompt sent to Groq for generation:

```
You are a quiz generator. Based on the following document excerpts,
generate {count} multiple-choice quiz questions.

Respond with ONLY a valid JSON array. No markdown, no code fences, no explanation:
[
  {
    "question": "...",
    "options": { "A": "...", "B": "...", "C": "...", "D": "..." },
    "correctOption": "A",
    "explanation": "...",
    "sourceChunkIndex": 3,
    "sourcePageNumber": 2
  }
]

Rules:
- All 4 options (A-D) must be plausible
- correctOption must be exactly "A", "B", "C", or "D"
- explanation must cite the reason from the source
- sourceChunkIndex and sourcePageNumber from the excerpt headers if available
- Questions must be answerable from the excerpts provided below
```

JSON validation before saving:
```csharp
private bool TryValidateQuizJson(JsonElement root, out List<QuizQuestionDraft>? questions)
{
    // Must be JSON array
    // Each item must have: question (string), options (object with A,B,C,D), correctOption (string), explanation (string)
    // correctOption must be one of "A","B","C","D"
    // No null or empty question/options/explanation
}
```

## Implementation Order

### Phase 1 — Backend (2-3 days)
1. Migration: `quizzes`, `quiz_questions`, `quiz_attempts`, `quiz_answers` tables + enums
2. DTOs: all request/response objects
3. `IQuizService` + `QuizService`:
   - `GenerateAsync`: get chunks from RagSearchService → build structured prompt → call Groq → validate JSON → create Quiz (Draft) + questions
   - `PublishAsync`: validate ≥ 1 question → set Published
   - `StartAttemptAsync`: create attempt, return shuffled questions WITHOUT correct options
   - `SubmitAttemptAsync`: compare answers, auto-score, record each answer, update attempt stats, return results with explanations
4. `QuizzesController` with all endpoints
5. DI registration

### Phase 2 — Frontend (2 days)
1. **Quiz list page** (`/quizzes`): MudTable with status, question count, best score, retake button
2. **Quiz editor** (`/quizzes/{id}`):
   - Draft mode: each question shown in editable card (question text, 4 options, correct answer dropdown, explanation)
   - Regenerate single question button
   - Publish button → transitions to Published
3. **Quiz taker** (`/quizzes/{id}/take`):
   - One question at a time or scrollable list
   - Radio buttons for A-D
   - Submit button → shows score + per-question result (correct/wrong + explanation)
   - "Retake" button → new attempt
4. **Attempt history** (`/quizzes/{id}/history`): table of attempts with date, score, time taken
5. **AiChat.razor update**: Replace current "Generate Quiz" prompt with API call to `POST /api/quizzes/generate`, then redirect to quiz editor

### Phase 3 — Polish (1 day)
- Timer per attempt (optional, configurable)
- Question randomization for retakes
- Export quiz as printable PDF
- Quiz sharing between users (via study session or direct link)

---

# Dependency Graph & Sequencing

```
Flashcards (Workflow A) ───────────────────────┐
                                                ├── Reuses: PdfTextExtractionService
Quiz (Workflow C) ─────────────────────────────┤            ChunkingService
                                                │            GroqChatCompletionClient
                                                │            RagSearchService
Collaborative Study (Workflow B) ──────────────┘
         │
         └── Depends on:  Folder sharing (existing)
                          Reuses: RagSearchService, GroqChatCompletionClient
                          NEW: SignalR hub
```

## Recommended Order
1. **Quiz (Workflow C) first** — most incremental (fixes existing feature), reuses the most code, delivers value fastest
2. **Flashcards (Workflow A) second** — similar pattern to Quiz but adds spaced repetition
3. **Collaborative Study (Workflow B) third** — most complex (requires SignalR + real-time sync), depends on folder sharing which exists

## Existing Code to Reuse
| Component | Used By |
|-----------|---------|
| `RagSearchService.SearchAsync()` | A (Flashcard generation), C (Quiz generation) |
| `GroqChatCompletionClient.CompleteAsync()` | A, C |
| `PdfTextExtractionService` (via chunks) | A, C |
| `ChunkingService` | A, C |
| `DocumentApiClient.GetContentAsync()` | A, C (for chunk retrieval in UI) |
| Folder sharing (`IsShared`, `ListSharedAsync`) | B |
| `AiChat.razor` layout/components | B (session workspace) |
| `DocumentStatus` enum pattern | A, C (FlashcardDeckStatus, QuizStatus follow same pattern) |
| `DocumentApiClient` pattern | A, B, C (new *ApiClients follow same pattern) |
| `SemanticKernelRagChatService` prompt-building | C (structured prompt adapted for quiz generation) |

## New NuGet Dependencies
- **SignalR**: already available in ASP.NET Core (no new package needed for Workflow B)
- **System.Text.Json**: already available (used for quiz JSON validation in Workflow C)
