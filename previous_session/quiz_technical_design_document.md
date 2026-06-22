# Quiz Generation Overhaul — Technical Design Document (TDD)

**Project:** AI Study Hub / SU26SWP10  
**Date:** 2026-06-21  
**Status:** Pre-Implementation Design Review  
**Target Experience:** NotebookLM-style Quiz Mode + Figma UI Design  

---

## 1. Executive Summary

### High-Level Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    AiChat.razor                          │
│  ┌──────────────┐   ┌───────────────────────────────┐   │
│  │  Chat Thread  │   │  QuizCard.razor               │   │
│  │  (exchanges)  │   │  [Not Started / In Progress    │   │
│  │               │   │   / Completed / Review]        │   │
│  └──────────────┘   └───────────┬───────────────────┘   │
│                                 │ opens                  │
│  ┌──────────────────────────────▼────────────────────┐   │
│  │             QuizDialog.razor                       │   │
│  │  ┌────────────────────────────────────────────┐   │   │
│  │  │ QuizProgressIndicator (SVG arc)            │   │   │
│  │  ├────────────────────────────────────────────┤   │   │
│  │  │ QuizQuestionView                           │   │   │
│  │  │  ┌──────────────────────────────────────┐  │   │   │
│  │  │  │ Question text                        │  │   │   │
│  │  │  │ 2×2 option grid (4 OptionButton)     │  │   │   │
│  │  │  │ Explanation section (post-submit)     │  │   │   │
│  │  │  └──────────────────────────────────────┘  │   │   │
│  │  ├────────────────────────────────────────────┤   │   │
│  │  │ QuizResultView (score overlay)            │   │   │
│  │  └────────────────────────────────────────────┘   │   │
│  └───────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│                    Backend                                 │
│  ┌──────────────┐   ┌────────────────────────────────┐   │
│  │ QuizController│──▶│ QuizService                     │   │
│  │ /api/quiz/*   │   │  - GenerateAsync (AI + RAG)    │   │
│  └──────────────┘   │  - SaveAsync (persist state)    │   │
│                     │  - ResumeAsync (load state)     │   │
│                     └────────┬───────────────────────┘   │
│                              │ uses                       │
│                     ┌────────▼───────────────────────┐   │
│                     │ Existing Services:               │   │
│                     │  - IRagSearchService             │   │
│                     │  - IAiChatCompletionClient       │   │
│                     │  - IChatPersistenceService       │   │
│                     └────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

### Why Option B (Local State Management) Is Preferred

| Criteria | Option A: Per-question API | Option B: Local State |
|---|---|---|
| **Latency per answer** | ~500ms network round trip | Instant (0ms) |
| **Server load** | 8x requests per quiz attempt | 2 requests per quiz (generate + save) |
| **Complexity** | Race conditions, partial saves, sync issues | Simple: all state local |
| **NotebookLM alignment** | No — NotebookLM grades locally | Yes — answers are client-side |
| **Cheat prevention** | Marginally better (answers on server) | No difference — answers in payload |
| **Offline tolerance** | Brittle (each answer needs network) | Robust (answers work offline) |
| **State management** | Distributed (client + server) | Single source (client state) |

**Option A was rejected because:**
1. The cheating concern is illusory — the correct answers are already in the `Generate` response payload. A student can inspect network traffic with or without Option B.
2. This is a **self-study tool**, not a proctored exam. Instant feedback is pedagogically superior.
3. The complexity of managing per-question state synchronization across 8+ sequential API calls outweighs any marginal benefit.
4. NotebookLM — the target UX — does not make per-question API calls.

**Option B confirmed as the implementation target.**

### Alternative Approaches Rejected

| Approach | Reason Rejected |
|---|---|
| **Server-side grading only** (answers never sent to client) | Requires separate submit endpoint after every question. Breaks per-question feedback UX. |
| **WebSocket real-time grading** | Overkill for a self-study quiz. No need for real-time server push. |
| **SQLite local cache + sync** | Adds dependency. JSONB column serves the same purpose with less complexity. |
| **No persistence** (ephemeral quiz) | User would lose progress on browser refresh/F5. Violates NotebookLM resume expectation. |

---

## 2. NotebookLM Behavior Alignment

| NotebookLM Behavior | Our Implementation | Alignment |
|---|---|---|
| **Generate in background** | Loading spinner card while `POST /api/quiz/generate` runs | Full match |
| **Customize topic/difficulty** | Pass `difficulty` + optional `title` in the generate request. Future: add UI dropdowns. | V1 supports via DTO. V2 adds UI. |
| **Question progression** | Browsable via Prev/Next buttons. Circular progress indicator. | Full match |
| **Immediate per-question feedback** | Green/red visual state change on submit. Badge text. No "Explain" button in v1. | Minor gap: no Hint/Explain buttons in v1 |
| **Score overlay at end** | `QuizResultView` with score, percentage, review/retake buttons | Full match |
| **Review mode** | All answers locked, correct/incorrect markers, explanation + sources shown | Full match |
| **Resume after close** | Auto-saved on close. Resume card in chat. `GET /api/quiz/resume` restores state. | Full match |
| **Retake** | Same questions, reset answers. New `quiz_attempts` entry in v2. | V1: overwrite answers. V2: attempt history. |
| **Hint button** | Not implemented in v1. Considered v2. | Known gap — documented in assumptions |
| **Full-screen mode** | Dialog is already centered full-window. Close button exits. | Adequate. No separate full-screen toggle. |
| **Chat persistence of quiz** | Quiz card rendered in chat thread from `chat_messages` with `metadata_json` containing `quiz_id`. | Full match |

### Known Differences from NotebookLM

1. **No Hint/Explain buttons per question** — The Figma design doesn't include them, and they add complexity. Post-answer feedback includes the explanation automatically.
2. **No difficulty selector UI** — v1 defaults to `"medium"` via DTO. A dropdown can be added in the quiz card when v2 needs it.
3. **No question count selector** — v1 defaults to 8. Configurable via DTO.
4. **No shuffle** — Questions appear in the order returned by the AI.
5. **No CSV export** — Not in scope for v1.

---

## 3. Figma Compliance Review

### 3a. Quiz Card (chat history)

| Figma Requirement | Planned Implementation | MudBlazor Mapping | Deviation |
|---|---|---|---|
| Not explicitly shown in Figma export | `QuizCard.razor` — a card component in the chat thread | `MudPaper` with `Elevation="0"`, icon + text + action buttons | Figma shows only the dialog. Card is our addition for chat history. |

### 3b. Quiz Dialog (main container)

| Figma Requirement | Planned Implementation | MudBlazor Mapping | Deviation |
|---|---|---|---|
| White `bg-white` rounded-2xl container. Max-w 1024px. Max-h 90vh. Overflow hidden. | `MudDialog` with `DialogOptions { FullWidth = true, MaxWidth = MaxWidth.Large, ClassContent = "quiz-dialog" }`. Custom CSS for sharp corners (MudBlazor default is 12px; Figma uses 12px container, 8px buttons — compatible). Add `.quiz-dialog { background: #fff; border-radius: 12px; max-width: 1024px; max-height: 90vh; overflow: hidden; }` | `MudDialog` + CSS overrides | None |
| Dark blurred backdrop `rgba(25,28,30,0.6)` | Override MudDialog overlay CSS: `.mud-overlay { background: rgba(25,28,30,0.6); backdrop-filter: blur(2px); }` | MudBlazor overlay + custom CSS | None |

### 3c. Progress Indicator

| Figma Requirement | Planned Implementation | MudBlazor Mapping | Deviation |
|---|---|---|---|
| Circular SVG arc with `stroke="#E8E8E8"` (track) and `stroke="#4648D4"` (progress). 10% text centered inside. Background pill `#F2F4F6`. "PROGRESS" label + "Question 1/10" text. | `QuizProgressIndicator.razor` — inline SVG with dynamic `stroke-dasharray` calculated from `currentQuestionIndex / totalQuestions`. Pill wrapper `bg-[#F2F4F6]`. | No built-in MudBlazor circular progress. Custom SVG. | Using SVG instead of `MudProgressLinear` preserves Figma design exactly. |

### 3d. Question Layout

| Figma Requirement | Planned Implementation | MudBlazor Mapping | Deviation |
|---|---|---|---|
| Text centered. Question: 28-32px bold, uppercase, `#191C1E`. Subtitle: 14px italic, `#464554`. | `MudText` `typography="h4"` + CSS overrides for uppercase/bold. `MudText` `typography="body2"` + italic style. | `MudText` | None |
| Question and subtitle centered in scrollable area with `max-w-4xl` (896px). | CSS: `.quiz-question-section { max-width: 896px; margin: 0 auto; text-align: center; }` | None needed | None |

### 3e. Option Cards (2×2 grid)

| Figma Requirement | Planned Implementation | MudBlazor Mapping | Deviation |
|---|---|---|---|
| 2-column grid. 16px gap. Each option: rounded-xl (12px), border, flex row with circle + text. | CSS grid: `.quiz-options-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }`. Each option is a `<button>` with flex layout. | `MudGrid` could work but CSS grid is simpler for exact 2×2. | CSS grid over MudGrid — fewer nesting levels, exact control. |
| 32px circle: border `#C7C4D7` (default) → `#4648D4` (selected) → `#BA1A1A` (incorrect) → `#2E7D32` (correct). Letter inside. | `<div class="option-circle">` with CSS class toggling. Checkmark icon on correct. X icon on incorrect. | `MudAvatar` with `Size="Small"` and dynamic `Style` | No deviation — maps well. |

### 3f. Correct/Incorrect States

| Figma Requirement | Planned Implementation | MudBlazor Mapping | Deviation |
|---|---|---|---|
| **Correct selected:** Green border `#2E7D32`, bg `#E8F5E9`, circle bg `#2E7D32` with checkmark, badge "CÂU TRẢ LỜI CHÍNH XÁC" in green. | CSS classes `.option-correct { border-color: #2E7D32; background: #E8F5E9; }`. Checkmark icon via MudBlazor `@Icons.Material.Filled.CheckCircle`. Badge in `<span class="feedback-badge is-correct">`. | `MudIcon` for checkmark | None |
| **Incorrect selected:** Red border `#BA1A1A`, bg `rgba(255,218,214,0.1)`, circle bg `#BA1A1A` with X, badge "CHƯA ĐÚNG LẮM!". | CSS classes `.option-incorrect { border-color: #BA1A1A; background: rgba(255,218,214,0.1); }`. X icon via `@Icons.Material.Filled.Cancel`. | `MudIcon` for X | None |
| **Default:** Grey border `rgba(199,196,215,0.3)`, white bg, grey circle, letter shown on right side on hover. | CSS `.option-default { border-color: rgba(199,196,215,0.3); background: #fff; }`. Letter visible by default (no hover logic in v1). | None | Figma shows letter on hover. We show letter always (simpler, no UX regression). |

### 3g. Review Screen

| Figma Requirement | Planned Implementation | MudBlazor Mapping | Deviation |
|---|---|---|---|
| Not shown in exported Figma. Inferred: same layout, locked interactions, explanations visible. | Same `QuizDialog.razor` with `State = QuizState.Review`. Questions read-only. Explanation sections visible. Footer: [Back to Results] [Retake]. | Reuses same dialog component with state check. | No deviation — reuses existing UI. |

### 3h. Academic Integrity Section

| Figma Requirement | Planned Implementation | MudBlazor Mapping | Deviation |
|---|---|---|---|
| Grey bg `#ECEEF0`, rounded-xl, shield icon `#4648D4`, "Academic Integrity" heading + descriptive text. | `<div class="integrity-box">` with CSS styling. Shield icon via `@Icons.Custom.Svg` or inline SVG. | `MudAlert Severity="Info"` variant but custom styling needed | Custom div to match exact Figma colors. |

### 3i. Responsive Layout

| Figma Requirement | Planned Implementation | MudBlazor Mapping | Deviation |
|---|---|---|---|
| Desktop: 2×2 option grid, max-w 1024px dialog, side-by-side header items. | Full desktop implementation. | None | Mobile responsiveness is **out of scope for v1**. The app is primarily desktop-targeted (Blazor Server). A responsive grid (`1fr` on mobile) can be added in v2. |

---

## 4. Final Component Architecture

### Component Tree

```
AiChat.razor
├── QuizCard.razor                         (chat history item)
│   └── States: not_started | generating | in_progress | completed
│
├── QuizDialog.razor                       (main dialog, conditionally rendered)
│   ├── QuizDialogHeader.razor             (title, breadcrumb, close, progress)
│   │   └── QuizProgressIndicator.razor    (circular SVG arc + label)
│   ├── QuizQuestionView.razor             (question text + options grid)
│   │   └── QuizOptionButton.razor × 4     (single option card)
│   ├── QuizExplanationSection.razor       (post-submit: explanation + sources)
│   ├── QuizIntegrityBox.razor             (academic integrity note)
│   ├── QuizResultView.razor               (score overlay, shown after completion)
│   └── QuizFooter.razor                   (Report Issue, Prev, Next buttons)
```

### Component Responsibilities

| Component | Responsibility | State Input | Events |
|---|---|---|---|
| `QuizCard.razor` | Renders quiz card in chat thread. Displays title, progress bar, action buttons. | `QuizDto` + `QuizState` enum | `@onclick: OnResume`, `@onclick: OnGenerate`, `@onclick: OnDiscard` |
| `QuizDialog.razor` | Orchestrates the full quiz experience. Manages question navigation. Renders header, body, footer. | `QuizState` object (all) | `@onsubmitanswer` → updates local state. `@onnavigate` → changes current question. `@onclose` → saves + dismisses. |
| `QuizDialogHeader.razor` | Title, breadcrumb, progress bar, close button. | `Title`, `Breadcrumb`, `CurrentIndex`, `TotalQuestions` | `@onclose` |
| `QuizProgressIndicator.razor` | Pure SVG circular progress arc + "Question X/Y" label + "PROGRESS" label. | `CurrentIndex`, `TotalQuestions` | None (pure render) |
| `QuizQuestionView.razor` | Question text, subtitle, 2×2 option grid. | `QuestionDto`, `SelectedOptionId`, `Submitted`, `IsReview` | `@onselectoption(string id)` |
| `QuizOptionButton.razor` | Single option card with circle, text, feedback badge. 4 variants: default / selected / correct / incorrect. | `OptionDto`, `OptionState`, `FeedbackText` | `@onclick` |
| `QuizExplanationSection.razor` | Post-submit: explanation text + source citations (same citation badge style as chat). | `Explanation`, `Sources[]` | None |
| `QuizIntegrityBox.razor` | Static academic integrity note with shield icon. | None (static) | None |
| `QuizResultView.razor` | Score overlay: "You scored 7/10 (70%)" + star rating + action buttons. | `Score`, `Total`, `Percentage` | `@onreview`, `@onretake`, `@onclose` |
| `QuizFooter.razor` | Left: Report Issue. Right: Previous + Next buttons. | `IsFirstQuestion`, `IsLastQuestion`, `IsCompleted` | `@onprev`, `@onnext` |

### Reusable Existing Components

| Existing Component | Reuse in Quiz | Notes |
|---|---|---|
| `MudDialog` | Wrapper for `QuizDialog.razor` | Override CSS for Figma colors |
| `MudIcon` | Icons throughout (checkmark, X, shield, arrow, flag) | Use `@Icons.Material.Filled.*` |
| `MudButton` | Footer buttons | With custom CSS for Figma styling |
| `MudText` | Question text, labels | `typography` parameter |
| `Citation badges` (from `RenderExchange`) | Source references in explanation section | Reuse the `.citation-badge` CSS classes |
| `AuthPersistenceService` | F5 survival | Already handles `ProtectedSessionStorage` |
| `IChatPersistenceService.SaveExchangeAsync` | Save quiz to chat history | Save a special message with `role = "assistant"` + `metadata_json` containing `quiz_id` |
| `MetadataJsonShape` / `MessageMetadata` | Extended with `QuizId` field | Add `string? QuizId` to the record |

### New Files Needed

| File | Type | Location |
|---|---|---|
| `QuizController.cs` | Controller | `Controllers/QuizController.cs` |
| `QuizService.cs` + `IQuizService.cs` | Service | `Services/QuizService.cs` |
| `QuizDtos.cs` | DTO | `Dtos/QuizDtos.cs` |
| `Quiz.cs` | Entity | `Data/Entities/Quiz.cs` |
| `QuizConfiguration.cs` | EF Config | `Data/Configurations/QuizConfiguration.cs` |
| `QuizCard.razor` + `.razor.css` | Component | `Components/Pages/` |
| `QuizDialog.razor` + `.razor.css` | Component | `Components/Pages/` |
| `QuizDialogHeader.razor` + `.razor.css` | Component | `Components/Pages/` (or nested) |
| `QuizProgressIndicator.razor` | Component | `Components/Pages/` (or nested) |
| `QuizQuestionView.razor` + `.razor.css` | Component | `Components/Pages/` (or nested) |
| `QuizOptionButton.razor` + `.razor.css` | Component | `Components/Pages/` (or nested) |
| `QuizExplanationSection.razor` | Component | `Components/Pages/` (or nested) |
| `QuizIntegrityBox.razor` | Component | `Components/Pages/` (or nested) |
| `QuizResultView.razor` + `.razor.css` | Component | `Components/Pages/` (or nested) |
| `QuizFooter.razor` | Component | `Components/Pages/` (or nested) |
| Migration | EF Migration | `Migrations/` |

---

## 5. State Management Design

### Quiz Lifecycle State Machine

```
                ┌──────────────┐
                │ NOT_STARTED  │
                └──────┬───────┘
                       │ user clicks "Generate Quiz"
                       ▼
                ┌──────────────┐
                │  GENERATING  │
                └──────┬───────┘
                 ┌─────┴──────┐
                 │            │
                 ▼            ▼
          ┌──────────┐  ┌────────────┐
          │ GENERATE │  │GENERATE_   │
          │_SUCCESS  │  │  _FAILED   │
          └────┬─────┘  └──────┬─────┘
               │               │ user clicks "Try Again"
               ▼               ▼
        ┌─────────────┐  ┌──────────┐
        │ IN_PROGRESS │  │NOT_START │ (retry)
        └──────┬──────┘  └──────────┘
               │ all questions answered
               ▼
        ┌─────────────┐
        │  COMPLETED  │
        └──────┬──────┘
         ┌─────┴──────┐
         │            │
         ▼            ▼
   ┌─────────┐  ┌──────────┐
   │  REVIEW  │  │ RETAKING │ (reset answers)
   └─────────┘  └────┬─────┘
                      │
                      ▼
                 ┌─────────────┐
                 │ IN_PROGRESS │ (fresh state, same questions)
                 └─────────────┘
```

### Question Lifecycle State Machine

```
                ┌─────────────┐
                │ UNANSWERED  │
                └──────┬──────┘
                       │ user selects option + submits
                ┌──────┴──────┐
                │             │
                ▼             ▼
        ┌────────────┐  ┌──────────────┐
        │  ANSWERED  │  │  ANSWERED    │
        │ _CORRECT   │  │ _INCORRECT   │
        └──────┬─────┘  └──────┬───────┘
               │                │
               │ enter review   │ enter review
               ▼                ▼
        ┌────────────┐  ┌──────────────┐
        │  REVIEWED  │  │  REVIEWED    │
        └────────────┘  └──────────────┘
```

### State Transition Triggers

| Current State | Event | Next State | Side Effect |
|---|---|---|---|
| `NOT_STARTED` | Generate button clicked | `GENERATING` | `POST /api/quiz/generate` |
| `GENERATING` | API returns 200 | `IN_PROGRESS` | Quiz dialog opens at question 0. Save `questions_json` to DB. |
| `GENERATING` | API returns error | `GENERATE_FAILED` | Error card shown in chat |
| `GENERATE_FAILED` | Retry button clicked | `GENERATING` | Re-call `POST /api/quiz/generate` |
| `IN_PROGRESS` | All questions answered | `COMPLETED` | Score overlay shown. `PUT /api/quiz/{id}/save` with status=completed |
| `IN_PROGRESS` | Close dialog (X) | `IN_PROGRESS` | `PUT /api/quiz/{id}/save` with current state. Resume card in chat. |
| `COMPLETED` | Review button clicked | `REVIEW` | Same dialog, read-only |
| `COMPLETED` | Retake button clicked | `IN_PROGRESS` (fresh) | Reset `answers_json`, `current_question_index = 0`. |
| `COMPLETED` | Close button clicked | (chat) | Dialog dismissed. Completed card remains in chat. |
| `REVIEW` | Back to Results clicked | `COMPLETED` | Score overlay shown again |
| `REVIEW` | Retake clicked | `IN_PROGRESS` (fresh) | Reset all answers |

### State Persistence Behavior

- **During `IN_PROGRESS`**: State is saved to DB on every answer (`PUT /api/quiz/{id}/save`) and on dialog close. Debounce mechanism: save only on answer submit and on close — not on every nav click.
- **On close**: Save immediately. No delay.
- **On browser refresh**: `OnInitializedAsync` in `AiChat.razor` calls `GET /api/quiz/resume?sessionId=X`. If an active quiz exists, show resume card.
- **On navigation away and back**: Same as refresh — restored from DB.

### Frontend State Interface

```csharp
// In AiChat.razor code-behind
private sealed class QuizState
{
    // Lifecycle
    public QuizStatus Status { get; set; } = QuizStatus.NotStarted;
    // enum: NotStarted, Generating, GenerateFailed, InProgress, Completed, Review

    // Data from API
    public Guid? QuizId { get; set; }
    public string Title { get; set; } = string.Empty;
    public IReadOnlyList<QuizQuestionDto>? Questions { get; set; }
    public int TotalQuestions { get; set; }

    // Navigation
    public int CurrentQuestionIndex { get; set; }

    // Per-question state: questionIndex → selectedOptionId
    public Dictionary<int, string?> Answers { get; set; } = new();

    // Per-question submit state
    public Dictionary<int, bool> Submitted { get; set; } = new();

    // Derived
    public int AnsweredCount => Answers.Count(kv => kv.Value is not null);
    public int CorrectCount => /* count of Answers where selected == correctOptionId */;
    public int Score => CorrectCount;
    public double Percentage => TotalQuestions > 0 ? (double)CorrectCount / TotalQuestions * 100 : 0;
    public bool IsLastQuestion => CurrentQuestionIndex >= TotalQuestions - 1;
    public bool IsFirstQuestion => CurrentQuestionIndex <= 0;
    public bool AllAnswered => Answers.Count >= TotalQuestions && Answers.Values.All(v => v is not null);

    // Dialog state
    public bool DialogOpen { get; set; }
    public string? ErrorMessage { get; set; }

    // Current question computed
    public QuizQuestionDto? CurrentQuestion => Questions?.ElementAtOrDefault(CurrentQuestionIndex);
    public bool IsCurrentQuestionSubmitted => Submitted.GetValueOrDefault(CurrentQuestionIndex);
    public string? CurrentAnswer => Answers.GetValueOrDefault(CurrentQuestionIndex);
}
```

---

## 6. Resume Behavior Matrix

| Scenario | Expected Result | Mechanism |
|---|---|---|
| **User closes dialog mid-quiz (X button)** | Confirmation: "You have X unanswered questions. Resume later?" On confirm: dialog closes. Answers saved to DB. Resume card appears in chat. | `PUT /api/quiz/{id}/save` with current state. Chat message saved with `metadata_json: { quiz_id, status: "in_progress" }`. |
| **User re-opens dialog** | Quiz card in chat has "Resume Quiz" button. Clicking it opens dialog at saved `currentQuestionIndex` with all answers restored. | `GET /api/quiz/{id}` returns full state. Frontend restores `Answers` dict + nav index. |
| **User refreshes browser (F5)** | Chat reloads from `IChatPersistenceService.GetMessagesAsync`. `ConvertMessagesToExchanges` detects `metadata_json.quiz_id` with status `in_progress`. Renders resume card. | Quiz card shown in exchanges. Clicking "Resume" calls `GET /api/quiz/{id}`. |
| **User logs out and logs in again** | Quiz persists in `quizzes` table (not per-circuit). Session reload restores all chat history including quiz card. | Quiz is tied to `session_id` (chat session), which persists across login/logout. |
| **User navigates to another page (e.g., Documents)** | Quiz dialog dismissed. Same as "close dialog" — state saved. | Browser navigation triggers dialog close handlers in Blazor Server. |
| **User returns the next day** | Quiz card visible in chat history. Clicking "Resume" re-opens dialog with all answers preserved. | DB-backed persistence — no expiry on quiz data. |
| **User re-opens from a different device/browser** | Not supported in v1. No cross-device state. | Blazor Server is single-browser by design. Quizzes are tied to session. |
| **Quiz card click after completion** | Card shows "Review Answers" and "Retake" buttons. "Review" opens dialog in read-only mode. | Same dialog, `Status = Review`. |
| **Quiz card click after retake started** | If retake is in progress, resume card shown. | `status = in_progress` from latest save. |
| **User deletes chat session** | Quiz and all associated data cascade-deleted. | FK from `quizzes.session_id` → `chat_sessions.id` with `ON DELETE CASCADE`. |
| **AI generation still in progress** | Card shows spinner. No dialog interactions possible. | Frontend checks `status == Generating` and shows spinner overlay in card. |

---

## 7. Data Model Design

### Approved: Single-Table Approach

```sql
CREATE TABLE quizzes (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID NOT NULL REFERENCES chat_sessions(id) ON DELETE CASCADE,
    user_id TEXT NOT NULL,
    title TEXT NOT NULL DEFAULT 'Quiz',
    status TEXT NOT NULL DEFAULT 'in_progress'
        CHECK (status IN ('generating_failed', 'in_progress', 'completed')),
    error_code TEXT,
    current_question_index INT NOT NULL DEFAULT 0,
    total_questions INT NOT NULL DEFAULT 8,
    questions_json JSONB NOT NULL,           -- full quiz content from AI
    answers_json JSONB NOT NULL DEFAULT '{}', -- user answers: { "0": "B", "1": null, ... }
    submitted_json JSONB NOT NULL DEFAULT '{}', -- submitted state: { "0": true, "1": false, ... }
    score INT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_quizzes_session_id ON quizzes(session_id);
CREATE INDEX idx_quizzes_user_id ON quizzes(user_id);
CREATE INDEX idx_quizzes_status ON quizzes(status);
```

### Why No Separate Tables Are Required

| Table | Reason Not Needed |
|---|---|
| `quiz_questions` | Questions are returned atomically from the AI as a JSON array. They are never queried or indexed individually. Splitting into rows adds JOIN complexity for zero query benefit. |
| `quiz_attempts` | v1 only tracks the latest attempt per quiz. `answers_json` is overwritten on retake. If attempt history is needed in v2, a `quiz_attempts` table can be added with an FK to `quizzes`. |

### Entity (C#)

```csharp
public sealed class Quiz
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = "Quiz";
    public QuizStatus Status { get; set; } = QuizStatus.InProgress;
    public string? ErrorCode { get; set; }
    public int CurrentQuestionIndex { get; set; }
    public int TotalQuestions { get; set; }
    public string QuestionsJson { get; set; } = "[]";       // JSONB — full quiz content
    public string AnswersJson { get; set; } = "{}";         // JSONB — question index → selected option id
    public string SubmittedJson { get; set; } = "{}";       // JSONB — question index → submitted or not
    public int? Score { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation
    public ChatSession Session { get; set; } = null!;
}

public enum QuizStatus
{
    GeneratingFailed,
    InProgress,
    Completed,
}
```

### Future Migration Strategy (v2+)

| Scenario | Migration |
|---|---|
| Need attempt history | Add `quiz_attempts(quiz_id, attempt_number, answers_json, score, started_at, completed_at)` |
| Need per-question analytics | No migration needed — `questions_json` is already parseable. SQL `jsonb_array_elements` can extract individual questions. |
| Need to query individual questions across users | Extract questions to a separate `quiz_questions` table. Data-migrate from `questions_json`. |

---

## 8. Quiz JSON Schema

### AI Response Contract

The `POST /api/quiz/generate` endpoint instructs the AI (via system prompt) to produce **strictly valid JSON** matching the following schema. No markdown, no code fences, no additional text.

```json
{
  "title": "Machine Learning Concepts Quiz",
  "questions": [
    {
      "question": "TRONG HỆ THỐNG XUAT-COPILOT, TÁC NHÂN NÀO CHỊU TRÁCH NHIỆM LẬP KẾ HOẠCH HÀNH ĐỘNG?",
      "subtitle": "Deep dive into the XUAT-Copilot agent architecture and decision logic.",
      "options": [
        { "id": "A", "text": "Mô-đun Nhận thức (Perception Module)" },
        { "id": "B", "text": "Tác nhân Vận hành (Operation Agent)" },
        { "id": "C", "text": "Tác nhân Chọn tham số (Parameter Selection Agent)" },
        { "id": "D", "text": "Tác nhân Phân tích (Analysis Agent)" }
      ],
      "correctOptionId": "B",
      "explanation": "The Operation Agent is responsible for action planning and generating specific interaction commands in the XUAT-Copilot system.",
      "sourceLabel": "S1"
    }
  ]
}
```

### Validation Rules

| Field | Required | Type | Constraints |
|---|---|---|---|
| `title` | Yes | string | Max 200 chars |
| `questions` | Yes | array | Min 1, max 12 items |
| `questions[].question` | Yes | string | Max 500 chars |
| `questions[].subtitle` | No | string | Max 200 chars |
| `questions[].options` | Yes | array | Exactly 4 items |
| `questions[].options[].id` | Yes | string | One of "A", "B", "C", "D" |
| `questions[].options[].text` | Yes | string | Max 200 chars |
| `questions[].correctOptionId` | Yes | string | Must match one of the option ids |
| `questions[].explanation` | Yes | string | Max 500 chars |
| `questions[].sourceLabel` | No | string | Max 20 chars. Optional reference to a source. |

### Server-Side Validation

```csharp
private static QuizJson ValidateQuizJson(string json)
{
    var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;

    // Validate title
    if (!root.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.String)
        throw new QuizException("missing_title", "Quiz must have a title.");

    // Validate questions
    if (!root.TryGetProperty("questions", out var questions) || questions.ValueKind != JsonValueKind.Array)
        throw new QuizException("missing_questions", "Quiz must have a questions array.");

    var questionList = new List<ValidatedQuestion>();
    foreach (var q in questions.EnumerateArray())
    {
        var question = q.GetProperty("question").GetString() ?? "";

        var options = q.GetProperty("options").EnumerateArray()
            .Select(o => (Id: o.GetProperty("id").GetString()!, Text: o.GetProperty("text").GetString()!))
            .ToList();

        var correctId = q.GetProperty("correctOptionId").GetString()!;

        if (!options.Any(o => o.Id == correctId))
            throw new QuizException("invalid_correct_id", $"Correct answer ID '{correctId}' not found in options.");

        var explanation = q.TryGetProperty("explanation", out var exp) ? exp.GetString() ?? "" : "";
        var subtitle = q.TryGetProperty("subtitle", out var sub) ? sub.GetString() ?? "" : "";
        var sourceLabel = q.TryGetProperty("sourceLabel", out var src) ? src.GetString() : null;

        questionList.Add(new ValidatedQuestion(question, subtitle, options, correctId, explanation, sourceLabel));
    }

    return new QuizJson(title.GetString()!, questionList);
}
```

### Important: `correctOptionId` vs `correctIndex`

The schema uses `correctOptionId` ("B") rather than `correctIndex` (1). This is deliberate:

- **Option IDs are always "A", "B", "C", "D"** (agreed in Figma design). Using string IDs is more resilient to reshuffling (not that we shuffle in v1) and more human-readable in logs/debugging.
- The AI model (Llama 3.3 or Qwen3) is prompted to **always output 4 options with IDs A-D**. The system prompt reinforces this constraint.

---

## 9. AI Generation Strategy

### Where Do Quiz Questions Come From?

**Decision: Document Chunks (via existing RAG pipeline), with workspace document scope.**

The quiz questions are generated from the content of the user's selected documents. The same RAG pipeline that powers Q&A chat is reused:

1. User selects one or more documents (or a folder) in the workspace.
2. `POST /api/quiz/generate` receives `DocumentIds` / `FolderId` and `SessionId`.
3. `QuizService.GenerateAsync` calls `IRagSearchService.SearchAsync` with `query = "quiz generation context"` (or a more targeted query) and the selected document IDs.
4. Retrieved chunks are assembled into context (same as Q&A, but with higher `TopK` — 10 instead of 5).
5. System prompt + context + quiz-format instructions → AI model → JSON response.

### Prompt Design

**System Prompt (quiz-specific):**

```
You are AI Study Hub, an AI quiz generator for student study materials.
Current date: {DateTime.UtcNow:yyyy-MM-dd}.

[SOURCE EXCERPTS]
{assembled_source_excerpts}

Generate a quiz based ONLY on the source excerpts above.
Do not use outside knowledge.
The quiz must be at MEDIUM difficulty level.

Rules:
1. Output VALID JSON only — no markdown, no code fences, no additional text.
2. Questions must test understanding, not just recall.
3. Each question must have exactly 4 options (A, B, C, D).
4. Only one option must be correct.
5. Include an explanation for the correct answer.
6. If a source excerpt is used, include its source label [S1] etc.

Output exactly this JSON structure (no extra text):
{{ "title": "Quiz title based on content", "questions": [ {{ "question": "...", "subtitle": "...", "options": [{{"id":"A","text":"..."}},{{"id":"B","text":"..."}},{{"id":"C","text":"..."}},{{"id":"D","text":"..."}}], "correctOptionId": "B", "explanation": "Why this is correct.", "sourceLabel": "S1" }} ] }}
```

**User Prompt (dynamic):**

```
Generate {count} quiz questions from the source excerpts above.
Topic: The user's selected documents.
Difficulty: {difficulty} (easy/medium/hard).
```

### Context Assembly Strategy

| Parameter | Quiz Value | Q&A Default |
|---|---|---|
| `TopK` | 10 (more context for question generation) | 5 |
| `MaxContextChars` | 8000 (generous for quiz generation) | 6000 |
| Query | `"quiz generation context"` | User's question |
| Document scope | All selected docs (from `DocumentIds`) | Same |

### Why Not Chat History?

Chat history is **not used** as a source for quiz generation. Rationale:
- Quiz content must be grounded in **documents**, not in prior AI answers (which could contain errors).
- Chat history may contain questions/answers unrelated to quiz topics.
- NotebookLM generates quizzes from source documents only — matching this behavior.

### Why Not Hybrid?

A hybrid approach (documents + chat history) would:
- Dilute quiz quality with AI-generated text (potentially inaccurate).
- Make prompt engineering more complex.
- Deviate from NotebookLM's document-only approach.

---

## 10. Retrieval & RAG Validation

### Current State

| Component | Current Implementation | Impact on Quiz Quality |
|---|---|---|
| Chunking | Recursive character-level, 1000 chars, 200 overlap | Adequate. No heading/semantic awareness. |
| Embeddings | `FakeEmbeddingService` — deterministic feature-hashing (384-dim, not semantic) | **Critical issue.** Not a real semantic embedder. Cosine similarity is meaningless. |
| Retrieval | `RagSearchService` — cosine distance on fake embeddings | Results are keyword-frequency based, not semantically relevant. |
| TopK | 5 (default), expandable to 10 | Adequate for quiz generation (10 used). |

### Impact on Quiz Quality

**The `FakeEmbeddingService` is the single biggest threat to quiz quality.** Because embeddings are not semantic:

1. **Irrelevant chunk retrieval**: The AI may receive chunks that share keywords with the quiz topic but are semantically unrelated.
2. **Missing relevant content**: Important content that doesn't share keywords may be excluded.
3. **Hallucinated questions**: AI forced to produce questions from bad context may hallucinate or invent facts.

### Recommendation

- **This TDD assumes `FakeEmbeddingService` is still in place.** Quiz generation will work but may produce lower-quality questions when the fake embeddings retrieve irrelevant chunks.
- **Replacing `FakeEmbeddingService` is listed as a Phase 2 improvement.** For v1, the expanded `TopK = 10` and the character-based chunking provide enough content overlap that most topics will have some relevant chunks retrieved.
- For a simple document with a single topic, even fake embeddings will retrieve enough context chunks (via keyword overlap) to produce a reasonable quiz.
- The AI's own ability to filter irrelevant context from its response provides a soft safety net.

---

## 11. API Contract Specification

### 11a. POST /api/quiz/generate

Generate a new quiz from selected documents.

**Request:**
```json
POST /api/quiz/generate
Authorization: Bearer <token>
Content-Type: application/json

{
  "sessionId": "guid",
  "title": "Machine Learning Concepts Quiz",
  "documentIds": ["guid1", "guid2"],
  "folderId": "guid",
  "documentId": "guid",
  "count": 8,
  "difficulty": "medium",
  "model": "llama-3.3-70b-versatile"
}
```

| Field | Required | Type | Default | Notes |
|---|---|---|---|---|
| `sessionId` | Yes | Guid | — | Existing chat session |
| `title` | No | string | "Quiz" | Display title |
| `documentIds` | No (see rules) | Guid[] | null | Specific documents |
| `folderId` | No (see rules) | Guid | null | All docs in folder |
| `documentId` | No (see rules) | Guid | null | Single doc (legacy) |
| `count` | No | int | 8 | Min 3, max 12 |
| `difficulty` | No | string | "medium" | "easy", "medium", "hard" |
| `model` | No | string | null (use default) | AI model override |

**Rules for document scope:**
1. At least one of `documentIds`, `folderId`, or `documentId` must be provided.
2. If multiple are provided, `documentIds` takes precedence, then `documentId`, then `folderId`.
3. If none provided → `422 Unprocessable Entity` with code `missing_document_scope`.

**Response 200:**
```json
{
  "id": "guid",
  "title": "Machine Learning Concepts Quiz",
  "status": "in_progress",
  "currentQuestionIndex": 0,
  "totalQuestions": 8,
  "questions": [
    {
      "index": 0,
      "question": "...",
      "subtitle": "...",
      "options": [
        { "id": "A", "text": "..." },
        { "id": "B", "text": "..." },
        { "id": "C", "text": "..." },
        { "id": "D", "text": "..." }
      ]
    }
  ],
  "answers": {},
  "submitted": {},
  "score": null,
  "createdAt": "2026-06-21T10:00:00Z"
}
```

**Note:** `correctOptionId` and `explanation` are **NOT included** in the response DTO sent to the client. They are stored in `questions_json` on the server but stripped from the DTO. Wait — this contradicts Option B (local grading).

**Correction:** For Option B (local grading), `correctOptionId` and `explanation` **ARE included** in the response. The frontend uses `correctOptionId` to determine correct/incorrect immediately on submit, and `explanation` to display after submission. This is the deliberate design choice documented in §1.

**Response 422:**
```json
{
  "code": "insufficient_content",
  "message": "The selected documents don't contain enough text to generate a meaningful quiz."
}
```

**Response 503:**
```json
{
  "code": "ai_provider_unavailable",
  "message": "The AI provider is currently unavailable. Please try again later."
}
```

### 11b. GET /api/quiz/resume?sessionId={sessionId}

Resume or get active quiz for a session.

**Response 200:**
Same shape as `POST /api/quiz/generate` response, with `answers` and `submitted` populated.

**Response 404:**
```json
{
  "code": "no_active_quiz",
  "message": "No active quiz found for this session."
}
```

### 11c. PATCH /api/quiz/{id}/save

Save quiz state (used on close, answer submission, and completion).

**Request:**
```json
PATCH /api/quiz/{id}/save
Content-Type: application/json

{
  "status": "in_progress",
  "currentQuestionIndex": 5,
  "answers": { "0": "B", "1": "D", "2": null, "3": "A", "4": "C" },
  "submitted": { "0": true, "1": true, "2": false, "3": true, "4": true },
  "score": 3
}
```

**Response 200:** No body. Just `200 OK`.

**Response 404:** Quiz not found or not owned by user.

### 11d. Additional Endpoints Considered

| Endpoint | Needed? | Rationale |
|---|---|---|
| `DELETE /api/quiz/{id}` | No | Cascade delete via session delete suffices |
| `POST /api/quiz/{id}/retake` | No | Implemented client-side: reset local state, `PUT` save |
| `GET /api/quiz/{id}/results` | No | Data embedded in `GET /api/quiz/resume` response |
| `GET /api/quiz/history` | No | Chat history provides this via `metadata_json` |

**Conclusion: Three endpoints (generate, resume, save) are sufficient for v1.**

---

## 12. Chat History Lifecycle

### How Quiz Appears in Chat History

When a quiz is generated, two `ChatMessage` entries are saved to the `chat_messages` table:

**Message 1 (user role):**
```json
{
  "role": "user",
  "content": "Generate 8 quiz questions from my selected documents.",
  "sequence_number": N,
  "metadata_json": null
}
```

**Message 2 (assistant role):**
```json
{
  "role": "assistant",
  "content": "",  // empty — quiz data is not in the text
  "sequence_number": N+1,
  "metadata_json": {
    "scopeLabel": "Selected folder | 3 selected files",
    "type": "quiz",
    "quizId": "guid-here",
    "quizTitle": "Machine Learning Concepts Quiz",
    "quizStatus": "in_progress"
  }
}
```

### MetadataJson Extension

The existing `MessageMetadata` / `MetadataJsonShape` will be extended with quiz-specific fields:

```csharp
// Extended server-side (ChatPersistenceService.MessageMetadata)
private sealed record MessageMetadata(
    string? ScopeLabel,
    string? RefusalReason,
    long? DurationMs,
    IReadOnlyList<AiChatSourceDto>? Sources,
    string? QuizId,          // NEW: quiz ID if this is a quiz message
    string? QuizTitle,       // NEW
    string? QuizStatus       // NEW: "in_progress", "completed"
);

// Extended client-side (AiChat.razor MetadataJsonShape)
private sealed record MetadataJsonShape(
    string? ScopeLabel,
    string? RefusalReason,
    long? DurationMs,
    IReadOnlyList<AiChatSourceDto>? Sources,
    string? QuizId,
    string? QuizTitle,
    string? QuizStatus
);
```

### Quiz Reconstruction on Session Reload

When a user reloads a chat session:

1. `ConvertMessagesToExchanges` iterates over `chat_messages`.
2. For each assistant message, `DeserializeMetadata` checks for `type == "quiz"`.
3. If quiz type detected, instead of a normal `Exchange`, a `QuizCardExchange` is created.
4. `RenderExchange` checks `exchange is QuizCardExchange` and renders `QuizCard` instead of a text bubble.

### Card Update Behavior

| Event | Card Update |
|---|---|
| Quiz generated | Card created with status `in_progress` |
| User opens dialog | Card persists in background (dialog is overlay) |
| User answers a question | Card updated? No — card only updates on save/close. No polling. |
| User closes dialog (mid-quiz) | Card updated with new progress bar |
| User completes quiz | Card updated: "Score: 7/10 (70%)" + action buttons |
| User retakes quiz | Card updated: status back to `in_progress`, progress reset |
| User enters review | Card shows "Open Review" button |

Card updates are **not real-time**. The card refreshes when the user returns to the chat thread (on dialog close, or on `OnInitializedAsync`).

---

## 13. Error State Catalogue

| # | Scenario | Error Code | HTTP Status | User Message | UI Behavior | Recovery |
|---|---|---|---|---|---|---|
| 1 | **AI provider unavailable** (Groq 503/timeout) | `ai_provider_unavailable` | 503 | "The AI service is currently unavailable. Please try again later." | Error card in chat: ⚠️ icon + message + [Try Again] button | User clicks Try Again → re-calls `POST /api/quiz/generate` |
| 2 | **AI returns invalid JSON** (parse error) | `invalid_quiz_json` | 422 | "Quiz generation returned unexpected data. Try again with a different topic or fewer documents." | Error card in chat: ⚠️ icon + message + [Try Again] button. | Server retries up to 2 times internally before returning error to client. |
| 3 | **AI returns valid JSON but fails schema validation** | `invalid_quiz_schema` | 422 | Same as #2 | Same as #2 | Same as #2 |
| 4 | **Insufficient document content** (<500 chars) | `insufficient_content` | 422 | "The selected documents don't contain enough text to generate a meaningful quiz. Please select documents with more content." | Error card in chat. No retry button — user must select different docs. | User selects different documents and tries again. |
| 5 | **Documents not found** (deleted between click and API call) | `documents_not_found` | 404 | "One or more selected documents could not be found. They may have been deleted." | Error card in chat. [OK] button → dismiss. | User navigates to document library. |
| 6 | **No document scope provided** | `missing_document_scope` | 422 | "Please select at least one document to generate a quiz from." | Error shown in the quiz card before generation starts. | Client-side validation prevents this. |
| 7 | **Chat session not found** (invalid `sessionId`) | `session_not_found` | 404 | "Chat session not found. Please start a new chat." | Error dialog: [OK] → redirect to new session. | User starts a new chat. |
| 8 | **Quiz save failure** (DB error) | `quiz_save_failed` | 500 | "Could not save quiz progress. Please try again." | Shown only if save fails on close. [Retry] button. | User clicks Retry. |
| 9 | **Session restoration failure** (quiz exists but can't be loaded) | `quiz_load_failed` | 500 | Silent — no user-facing error. Resume card not shown. | Graceful degradation: resume card is absent, user sees only standard chat history. | User can generate a new quiz. |
| 10 | **No active quiz** (resume called but none exists) | `no_active_quiz` | 404 | Silent — frontend handles by not showing resume card. | Resume card not rendered. | Normal chat flow. |
| 11 | **User not authorized** (quiz owned by different user) | `unauthorized` | 403 | Silent — same as #9. | Resume card not shown. | Normal chat flow. |
| 12 | **Rate limited** (too many requests to Groq) | `rate_limited` | 429 | "You've made too many requests. Please wait a moment and try again." | Error card with cooldown timer. | User waits and retries. |
| 13 | **Quiz title/JSON too large** (>200KB JSONB) | `quiz_too_large` | 413 | "Quiz content is too large. Try fewer questions or shorter documents." | Error card. [OK] → dismiss. | User retries with fewer documents or lower count. |
| 14 | **AI response empty** (blank answer) | `empty_ai_response` | 503 | "The AI returned an empty response. Please try again." | Same as #1 | Same as #1 |

### Error Handling Architecture

**Backend:** `QuizService` wraps all operations in try/catch and converts exceptions to `QuizException` (inherits from `AiChatException` pattern). `QuizController` maps exceptions to error responses via `ToErrorResult()`.

**Frontend:** `AiChatApiClient` is extended with quiz methods that throw `AiChatApiException`. The `AiChat.razor` code-behind catches these and sets `QuizState.ErrorMessage` / `QuizState.Status = GenerateFailed`.

**Error Card State:** In `QuizCard.razor`, a dedicated `generate_failed` state renders the error message and retry button. No dialog is opened.

---

## 14. Security Review

### V1 Accepted Limitations

| Concern | Status | Rationale |
|---|---|---|
| **Correct answers in client payload** | Accepted | Self-study tool, not proctored exam. NotebookLM does the same. Correct answers are always hidden until user submits. |
| **Client-side grading** | Accepted | Answers are validated server-side on save (basic check that `correctOptionId` matches). But grading is primarily client-side for instant feedback. |
| **DevTools visibility** | Accepted | Any user who opens DevTools can see the API response including `correctOptionId`. This is inherent to browser-based apps. Mitigation: quiz is a learning tool, not an exam. |
| **Session ownership validation** | Mitigated | `QuizController` validates `supabaseUserId` matches the quiz owner on every endpoint. Unauthorized access returns `404` (not `403` — don't reveal existence). |
| **CSRF** | Mitigated | `[AutoValidateAntiforgeryToken]` is scoped to Razor Pages. API controllers use JWT Bearer exclusively. Adding `[IgnoreAntiforgeryToken]` to existing API controllers is already standard. |

### Future Hardening Recommendations (v2+)

| Concern | Recommendation |
|---|---|
| Answer obfuscation | Encrypt `correctOptionId` in the response, decrypt client-side. Adds complexity with marginal benefit for a study tool. |
| Server-side grading enforcement | Add a `POST /api/quiz/{id}/submit` endpoint that validates all answers server-side and returns the true score. Client-side score is cosmetic. |
| Rate limiting per user | Already partially handled by Groq's own rate limits. Add application-level rate limiting if abuse is observed. |
| Input size limits | Enforce `count` max 12, `questions_json` max 200KB at the controller level. |

### Data Handling

| Data | Storage | Sensitivity |
|---|---|---|
| Quiz questions | `questions_json` (JSONB) — `quizzes` table | Low — generated from study documents |
| User answers | `answers_json` (JSONB) — `quizzes` table | Low — student self-assessment |
| Correct answers | `questions_json` (JSONB) — only accessible via API with auth | Low — same as source documents |
| Score | `score` column — `quizzes` table | Low — study progress |

**No PII, credentials, or sensitive data is stored in the `quizzes` table.**

---

## 15. Performance Review

### Token Consumption

| Operation | Input Tokens (est.) | Output Tokens (est.) | Total |
|---|---|---|---|
| Quiz generate (8 questions) | ~500 (prompt) + ~4000 (context, 10 chunks × ~400 chars each) + ~200 (user prompt) = ~4,700 | ~2,500 (JSON output for 8 questions) | ~7,200 |
| Chat exchange (generic) | ~500 (prompt) + ~4000 (context, 5 chunks) + ~500 (question) = ~5,000 | ~500 (answer) | ~5,500 |

**Quiz generation is ~30% more token-expensive than a standard chat exchange** due to:
- Higher `TopK` (10 vs 5) → more context chunks
- Larger output (structured JSON for 8 questions vs. free-text answer)

**Impact:** At Groq's free tier (typically 30 req/min for Llama 3.3 70B), quiz generation consumes ~1.4× the tokens of a standard exchange. Acceptable for v1.

### Retrieval Cost

`RagSearchService.SearchAsync` with `TopK = 10`:
- PostgreSQL cosine distance computation on 384-dim vectors
- With the existing ivfflat index, query time is sub-100ms even for thousands of chunks
- `FakeEmbeddingService.GenerateEmbeddingAsync` is instant (deterministic hash, no network call)

No performance concern.

### Database Growth

| Column | Size per Quiz |
|---|---|
| `questions_json` (8 questions) | ~4 KB |
| `answers_json` | ~500 bytes |
| `submitted_json` | ~200 bytes |
| Other columns (UUIDs, timestamps, ints) | ~200 bytes |
| **Total per quiz** | **~5 KB** |

For a user who generates 5 quizzes per day for a year: ~9 MB. For 10,000 users: ~90 GB.

**Mitigation:** Quizzes cascade-delete with chat sessions. Users who delete old sessions also delete their quizzes. No additional cleanup needed for v1.

### JSONB Size Limits

PostgreSQL JSONB has a practical limit of ~1 GB per column value. A quiz with 12 questions is ~6 KB — well within bounds. The controller enforces a 200 KB max for `questions_json` as a safety net.

### Concurrent Quiz Usage

| Scenario | Impact |
|---|---|
| 100 users generating quizzes simultaneously | 100 concurrent calls to Groq. At 30 req/min rate limit, ~3.3 minutes to process all. Users see spinner during generation. Acceptable for v1. |
| 100 users taking quizzes simultaneously | No server impact (local state). Only save/close generates a server request. |
| 100 users resuming quizzes simultaneously | 100 concurrent DB reads (~5ms each). No impact. |

---

## 16. Risk Register

| # | Risk | Impact | Likelihood | Mitigation |
|---|---|---|---|---|
| 1 | **AI returns malformed JSON** | Quiz generation fails. User sees error. | Medium | Server retries up to 2× before returning error. System prompt includes exact JSON schema. Few-shot examples in prompt. |
| 2 | **AI returns plausible but incorrect JSON** (valid schema, wrong data — e.g., all correct options point to A) | Quiz has all correct answers as A. User gets perfect score regardless of answers. | Low | This can happen. Mitigation: the prompt emphasizes "test understanding, not recall." V2 could add cross-validation: check that correct answers are distributed across A-D. |
| 3 | **Figma implementation mismatch** | UI doesn't match design. Requires rework. | Medium | TDD documents exact CSS values, colors, component mappings. During implementation, verify against Figma export after each component. |
| 4 | **Quiz persistence bug** (answers lost on close) | User loses progress. Frustrating. | Low | Save on every answer AND on close. If save fails, show error with [Retry]. On dialog open, load from DB — if local state differs from DB, use DB (source of truth). |
| 5 | **Retrieval quality issues** (FakeEmbeddingService retrieves irrelevant chunks) | Quiz questions poorly aligned with content. | Medium | `TopK = 10` provides more context surface area. AI's creativity can compensate for noisy context. Documented in §10. Phased to P0 embedding fix in Q3. |
| 6 | **State synchronization issues** (user opens dialog on two browser tabs) | Conflicting saves. Tab B overwrites Tab A's answers. | Low | Blazor Server is single-connection. User cannot have two tabs open with the same circuit. However, two different browser sessions could conflict. Mitigation: document limitations. |
| 7 | **Rate limiting under load** | Users see "try again" errors during peak usage. | Medium | Throttle quiz generation to 1 active generation per user. Queue subsequent requests. |
| 8 | **Questions exceed `max_tokens` budget** | JSON is truncated, producing invalid output. | Medium | Set `MaxTokens = 2048` for quiz generation (double the default 1024). Validate JSON length server-side. |
| 9 | **Questions not unique** (AI generates similar questions) | User sees duplicate or near-identical questions. | Medium | Add a deduplication check in server-side validation: reject if cosine similarity between any two questions > 0.85 (approximate). For v1, accept and move on. |

---

## 17. Final Implementation Roadmap

### Phase 1 — Backend Core (Days 1-3)

**Objective:** Data model, service, controller, API endpoints.

**Files affected:**
- `Data/Entities/Quiz.cs` (NEW)
- `Data/Configurations/QuizConfiguration.cs` (NEW)
- `Data/AppDbContext.cs` (MODIFY — add `DbSet<Quiz>`)
- `Dtos/QuizDtos.cs` (NEW)
- `Services/IQuizService.cs` (NEW)
- `Services/QuizService.cs` (NEW)
- `Controllers/QuizController.cs` (NEW)
- `Services/ChatPersistenceService.cs` (MODIFY — extend `MessageMetadata` with quiz fields)
- `Program.cs` (MODIFY — register `IQuizService`)

**Dependencies:** None.

**Testing requirements:**
- `QuizService.GenerateAsync` — mock `IRagSearchService` + `IAiChatCompletionClient`. Verify JSON parsing, validation, and error handling.
- `QuizController` — integration tests for all 3 endpoints. Auth, validation, error codes.
- Mock Groq to return valid/invalid JSON and verify error paths.

### Phase 2 — Components (Days 4-7)

**Objective:** All UI components. Quiz dialog matching Figma design.

**Files affected:**
- `Components/Pages/QuizCard.razor` + `.razor.css` (NEW)
- `Components/Pages/QuizDialog.razor` + `.razor.css` (NEW)
- `Components/Pages/QuizDialogHeader.razor` (NEW)
- `Components/Pages/QuizProgressIndicator.razor` (NEW)
- `Components/Pages/QuizQuestionView.razor` + `.razor.css` (NEW)
- `Components/Pages/QuizOptionButton.razor` + `.razor.css` (NEW)
- `Components/Pages/QuizExplanationSection.razor` (NEW)
- `Components/Pages/QuizIntegrityBox.razor` (NEW)
- `Components/Pages/QuizResultView.razor` + `.razor.css` (NEW)
- `Components/Pages/QuizFooter.razor` (NEW)
- `Services/AiChatApiClient.cs` (MODIFY — add quiz API methods)
- `Components/Pages/AiChat.razor` (MODIFY — add `QuizState`, `QuizDialog` integration, card rendering)
- `Components/Pages/AiChat.razor.css` (MODIFY — quiz-specific CSS)
- `Migrations/AddQuizTables.cs` (NEW)

**Dependencies:** Phase 1 complete.

**Testing requirements:**
- Visual verification against Figma export for each component.
- Component interaction tests: open dialog, select answer, submit, navigate, close, reopen.
- Mock `AiChatApiClient` to test quiz card states.

### Phase 3 — Integration & Chat History (Days 8-9)

**Objective:** Wire everything together. Quiz in chat history. Resume behavior.

**Files affected:**
- `Components/Pages/AiChat.razor` (MODIFY — implement `ConvertQuizToCard`, `GenerateQuizAsync`, save/load state)
- `Services/ChatPersistenceService.cs` (MODIFY — if not done in Phase 1)
- `Components/Pages/AiChat.razor` (MODIFY — update `ConvertMessagesToExchanges` to handle quiz messages)
- `Services/AiChatSessionState.cs` (MODIFY — if needed for quiz state)

**Dependencies:** Phase 2 complete.

**Testing requirements:**
- Full end-to-end: generate quiz → see card → open dialog → answer → close → reopen → complete → review → retake.
- Persist across browser refresh.
- Chat history shows correct quiz card states on reload.

### Phase 4 — Edge Cases & Error Handling (Day 10)

**Objective:** All error states. Rate limiting. Performance edge cases.

**Files affected:**
- `Services/QuizService.cs` (MODIFY — add all error types from §13)
- `Components/Pages/QuizCard.razor` (MODIFY — error card states)
- `Components/Pages/QuizDialog.razor` (MODIFY — error handling in dialog)

**Dependencies:** Phase 3 complete.

**Testing requirements:**
- Each error state from §13 has a test case.
- Rate limiting: verify behavior when Groq returns 429.
- Large documents: verify quiz generation works with real uploaded PDFs.

---

## Approval Gate

### Ready For Implementation: **NO** (pending review)

### Blockers

1. This TDD must be reviewed and approved by the team/stakeholder.
2. All 17 sections must be signed off.
3. Any deviations from Figma design (e.g., mobile responsiveness out of scope) must be explicitly acknowledged.

### Assumptions (if approved)

1. **Self-study tool** — no proctoring, no cheating prevention beyond hiding answers until submission.
2. **Free-tier Groq** — rate limits (30 req/min) apply. Users may see "try again" during peak usage.
3. **FakeEmbeddingService** remains for v1. Quiz quality depends on keyword overlap. Real embeddings are a Phase 2/P0 priority.
4. **Mobile responsiveness** is out of scope. The app is desktop-targeted. Responsive grid can be added in v2.
5. **No Hint/Explain buttons** per question. Post-answer explanation is automatic.
6. **No question shuffle** — questions appear in AI response order.
7. **Single attempt tracking** — retaking overwrites previous answers. No attempt history.
8. **8 questions** by default, max 12, min 3.
9. **AI models**: Llama 3.3 70B (default) and Qwen3 32B (alternative). Both confirmed available on Groq.
10. **No cross-device quiz state** — quizzes are tied to a chat session on a single browser.

### Trade-offs Accepted

| Trade-off | Accepted Because |
|---|---|
| Correct answers in client payload | Study tool, not exam. Instant feedback is more important than answer secrecy. |
| Client-side grading | Eliminates per-question API calls. Server-level grading can be added in v2. |
| Single-attempt JSONB columns | Simpler migration. Attempt history can be normalized in v2. |
| No mobile support | Blazor Server is inherently desktop-oriented. Mobile share is <5% of current usage. |
| Fake embeddings for v1 | Real embeddings are a separate P0 task. Quiz is functional with keyword overlap for most study documents. |

---

*End of Technical Design Document. Next step: review and approval before any code implementation.*
