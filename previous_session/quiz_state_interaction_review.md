# Quiz State & Interaction Review
## NotebookLM-Style Quiz Mode — Pre-Implementation Analysis

Date: 2026-06-21
Status: Design Review — Do Not Implement Until Approved

---

## 1. Quiz Lifecycle

### States

```
NOT_STARTED ──> IN_PROGRESS ──> COMPLETED ──> REVIEW_MODE
                    │                              │
                    │  (close mid-quiz)             │  (retake)
                    └──> (resume later) ──────────────┘
```

### 1a. NOT_STARTED
- User clicks "Generate Quiz" button in the toolbar sidebar
- Preview card shown in chat area: skeleton/card with title "Generating Quiz..." and spinner
- Backend: `POST /api/quiz/generate` fires with selected document context
- Transition to IN_PROGRESS when AI returns structured quiz data

### 1b. IN_PROGRESS
- Quiz dialog open, question N displayed (N = 1 initially, or resumed N)
- User navigates questions, selects answers, submits per-question
- Progress indicator updates (circular SVG arc + "Question X/Y")
- Unanswered questions are navigable (user can skip and come back)
- Close (X) button → confirm dialog "You have X unanswered questions. Resume later?"

### 1c. COMPLETED
- All questions answered
- Score overlay displayed: "You scored X/Y" with percentage
- Options: [Review Answers] [Retake Quiz] [Close]
- Quiz result saved to DB (`quiz_attempts` as JSONB on Quiz row)

### 1d. REVIEW_MODE
- Same dialog layout, all answers locked (no interaction)
- Each question shows:
  - User's answer (with correct/incorrect badge)
  - Correct answer highlighted
  - Explanation text
  - Source references
- Footer: [Back to Results] [Retake] [Close]

### State machine summary

```
NOT_STARTED:
  events: generate() → IN_PROGRESS
  entry: call POST /api/quiz/generate

IN_PROGRESS:
  events: answer(q) → IN_PROGRESS (same question persists)
  events: navigate(n) → IN_PROGRESS
  events: close() → IN_PROGRESS (with resume persistence)
  events: complete() → COMPLETED
  entry: open quiz dialog, restore progress from DB if resuming

COMPLETED:
  events: review() → REVIEW_MODE
  events: retake() → IN_PROGRESS (reset answers, same questions)
  events: close() → CHAT (dismiss dialog)
  entry: show score overlay

REVIEW_MODE:
  events: back_to_results() → COMPLETED
  events: retake() → IN_PROGRESS
  events: close() → CHAT
  entry: show all answers with correct/incorrect markers
```

---

## 2. Question Lifecycle

### States

```
UNANSWERED ──> ANSWERED_CORRECT ──> REVIEWED
    │                                       │
    └──> ANSWERED_INCORRECT ────────────────┘
```

### 2a. UNANSWERED
- Default state for all questions at start
- No option selected, no feedback shown
- Option circles show letter (A/B/C/D), grey border
- Navigation allowed to any unanswered question
- Can submit only when an option is selected

### 2b. ANSWERED_CORRECT
- User selected the correct answer and submitted
- Green border `#2E7D32`, green bg `#E8F5E9`
- Circle shows checkmark icon instead of letter
- Badge: "CÂU TRẢ LỜI CHÍNH XÁC" (Correct Answer)
- User can still navigate away and back — state persists
- Cannot change answer after submission

### 2c. ANSWERED_INCORRECT
- User selected wrong answer and submitted
- Red border `#BA1A1A`, light red bg `rgba(255,218,214,0.1)`
- Circle shows "X" icon instead of letter
- Badge: "CHƯA ĐÚNG LẮM!" (Not Quite Right!)
- Correct answer also highlighted (Green) for learning
- User can see both their wrong answer and the correct one

### 2d. REVIEWED (in review mode)
- All answers locked, same visual display as ANSWERED states
- Explanation text shown below each question
- Source citations displayed
- No further interaction allowed

### Transitions

```
UNANSWERED:
  events: select_option(id) → UNANSWERED (highlighted, not submitted)
  events: submit() → ANSWERED_CORRECT | ANSWERED_INCORRECT (depending on correctness)
  events: navigate_away() → UNANSWERED (no change)

ANSWERED_CORRECT:
  events: navigate_away() → ANSWERED_CORRECT (persists)
  events: enter_review() → REVIEWED

ANSWERED_INCORRECT:
  events: navigate_away() → ANSWERED_INCORRECT (persists)
  events: enter_review() → REVIEWED
```

---

## 3. Resume Behavior

### What happens if the user closes the dialog mid-quiz?

The dialog close is a non-destructive action. The design follows NotebookLM's pattern: **progress is automatically saved and resumable**.

**Close behavior:**
1. User clicks X button or presses Escape
2. Confirmation dialog appears: "You have X unanswered question(s). Your progress has been saved. Resume later?"
3. Options: [Resume Later] [Cancel] (Cancel = stay in quiz)
4. On "Resume Later": dialog closes, quiz state persists. A quiz card appears in the chat thread showing: "Quiz in progress (4/10 completed)"

### How should progress be resumed?

1. User sees the "Quiz in progress" card in the chat thread
2. Clicking the card re-opens the quiz dialog at the exact same question
3. All previous answers are restored

### How is progress persisted?

**Frontend:** `protected override async Task OnInitializedAsync()` calls `GET /api/quiz/resume?sessionId={sessionId}` which returns the active quiz state (question index, all answers, correct/incorrect status).

**Backend DB schema:**
```sql
quizzes (
    id UUID PK,
    session_id UUID FK → chat_sessions,
    title VARCHAR(255),
    status VARCHAR(20) -- 'in_progress', 'completed',
    current_question_index INT DEFAULT 0,
    total_questions INT DEFAULT 8,
    answers_json JSONB, -- { "0": "B", "1": "D", "2": null, ... }
    score INT,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
)
```

The `answers_json` column stores a simple JSON object mapping question index → selected option ID (or `null` for unanswered). This is lightweight and avoids a separate `quiz_attempts` table for v1.

**Competing for v2:** if we need detailed per-attempt history, use a separate `quiz_attempts` table with `(quiz_id, attempt_number, question_index, selected_option_id, is_correct, answered_at)`.

**On dialog open (resume):**
```
GET /api/quiz/resume?sessionId={id}
  → returns QuizDto { id, title, status, currentQuestionIndex, totalQuestions, answers: {0: "B", 1: null, ...}, questions: [...] }
```

**On dialog close (mid-quiz):**
```
PATCH /api/quiz/{id}
  { status: "in_progress", currentQuestionIndex: 5, answers: { ... } }
```

**On submit answer:**
```
PATCH /api/quiz/{id}/answer
  { questionIndex: 3, selectedOptionId: "B" }
  → returns { isCorrect: true, correctOptionId: "B", explanation: "..." }
```

---

## 4. Chat History Behavior

This section defines how quiz cards appear in the main chat thread (right side of AiChat page), mimicking NotebookLM's Studio panel output model.

### 4a. Quiz card before starting (NOT_STARTED)

**Visual:** A card component in the chat exchange thread showing:
```
┌─────────────────────────────────┐
│ [ShieldCheck icon]   Generate Quiz  │
│ Click to generate 8 MCQs from     │
│ your selected documents.          │
│ ┌─────────────────────────────┐   │
│ │ [Generate Quiz]  [Cancel]   │   │
│ └─────────────────────────────┘   │
└─────────────────────────────────┘
```

This replaces the current behavior where clicking "Generate Quiz" just fills the text input.

**Behavior:** User must click "Generate Quiz" on this card to initiate generation. This is the NotebookLM pattern — quiz generation is explicit and separate from chat.

### 4b. Quiz card during generation (LOADING)

```
┌─────────────────────────────────┐
│ [Spinner icon]   Generating Quiz...│
│ Creating questions based on your  │
│ selected documents...              │
│ [             ════════░░░░ 60%  ]  │
│ *This may take 10-15 seconds*      │
└─────────────────────────────────┘
```

### 4c. Quiz card during progress (IN_PROGRESS)

```
┌─────────────────────────────────┐
│ 📝 Quiz: Machine Learning Concepts  │
│ Progress: ▰▰▰▰▰▰▰▱▱▱ 4/10       │
│ Status: In progress               │
│                                    │
│ [Resume Quiz] [Discard]           │
└─────────────────────────────────┘
```

**Behavior:** Clicking "Resume Quiz" re-opens the dialog at the saved question index. The card remains visible even while the dialog is open (chat history is behind the dialog overlay).

### 4d. Quiz card after completion (COMPLETED)

```
┌─────────────────────────────────┐
│ 📝 Quiz: Machine Learning Concepts  │
│ Score: 7/10 (70%) ★★★☆☆       │
│ Completed: 10:32 AM today         │
│                                    │
│ [Review Answers] [Retake] [Close] │
└─────────────────────────────────┘
```

### 4e. Quiz card in review mode

```
┌─────────────────────────────────┐
│ 📝 Quiz: Machine Learning Concepts  │
│ Score: 7/10 (70%) — Review Mode  │
│                                    │
│ [Open Review] [Back to Results]   │
└─────────────────────────────────┘
```

### Implementation notes for chat history:

The quiz card is a **non-exchange item** in the chat. It's inserted as a `QuizCardViewModel` in the `List<Exchange> _exchanges` collection alongside regular Q&A exchanges. This means `ConvertMessagesToExchanges` (or a new `ConvertQuizToCard`) needs to handle the `metadata_json` field.

When a quiz is generated, a special chat message with `role = "quiz"` and `metadata_json = { quizId: "..." }` is saved to the `chat_messages` table. The frontend detects this role and renders a `QuizCard` instead of a text bubble.

---

## 5. Review Mode

In review mode (post-completion), the dialog shows all questions in a scrollable list, each with:

### Per-question layout:

```
┌──────────────────────────────────────────┐
│ Question 1/10                            │
│ "TRONG HỆ THỐNG XUAT-COPILOT, TÁC NHÂN  │
│ NÀO CHỊU TRÁCH NHIỆM LẬP KẾ HOẠCH...?" │
│                                          │
│ ┌─── Option A ──────────────────────┐    │
│ │ 🔴 A. Mô-đun Nhận thức           │ ← user's wrong answer
│ │    CHƯA ĐÚNG LẮM!                 │    │
│ └───────────────────────────────────┘    │
│ ┌─── Option B ──────────────────────┐    │
│ │ ✅ B. Tác nhân Vận hành           │ ← correct answer highlighted
│ │    CÂU TRẢ LỜI CHÍNH XÁC          │    │
│ └───────────────────────────────────┘    │
│ ┌─── Option C ──────────────────────┐    │
│ │    C. Tác nhân Chọn tham số       │    │
│ └───────────────────────────────────┘    │
│ ┌─── Option D ──────────────────────┐    │
│ │    D. Tác nhân Phân tích          │    │
│ └───────────────────────────────────┘    │
│                                          │
│ ┌── Explanation ──────────────────┐      │
│ │ 📖 The Operation Agent is...   │      │
│ │ Source: [S1] Lecture Notes p.42 │      │
│ └──────────────────────────────────┘      │
└──────────────────────────────────────────┘
```

### Data requirements for review mode:

Each quiz question response from the AI must include:

```json
{
  "id": 1,
  "question": "...",
  "subtitle": "...",
  "options": [
    { "id": "A", "text": "...", "isCorrect": false },
    { "id": "B", "text": "...", "isCorrect": true },
    { "id": "C", "text": "...", "isCorrect": false },
    { "id": "D", "text": "...", "isCorrect": false }
  ],
  "explanation": "The Operation Agent is responsible for...",
  "sources": [
    { "label": "S1", "text": "Lecture Notes p.42" }
  ],
  "userAnswer": "A",
  "isCorrect": false
}
```

The `explanation` and `sources` fields are only displayed in review mode or post-submission. They are NOT shown before the user answers.

---

## 6. Error States

### 6a. Quiz generation failure

**Scenario:** AI provider returns an error (429 rate limit, 500 server error, timeout, Groq down)

**UX:**
```
┌─────────────────────────────────┐
│ ⚠️ Quiz Generation Failed       │
│                                    │
│ "The AI service is currently      │
│ unavailable. Please try again."   │
│                                    │
│ [Try Again] [Cancel]             │
└─────────────────────────────────┘
```

**State:** The quiz card in chat shows error state. User can retry (re-calls the generation endpoint) or cancel (removes the card).

**Backend:** `POST /api/quiz/generate` catches `AiChatException`, `HttpRequestException`, `TimeoutException`, returns `503 Service Unavailable` or `502 Bad Gateway` with error detail.

### 6b. Invalid AI JSON

**Scenario:** AI returns malformed JSON that doesn't match the expected schema (missing fields, wrong types, unparseable)

**UX:** Same error dialog as 6a but with a different message:
```
"Quiz generation returned unexpected data. Try again with a different topic."
```

**Backend:** JSON deserialization failure caught in `QuizService`. If AI repeatedly returns invalid JSON (configurable retry count, e.g. 2 retries), return `422 Unprocessable Entity` with error detail.

**Mitigation:**
1. Use structured prompt engineering: system prompt explicitly requests JSON with schema definition
2. Use few-shot examples in the system prompt
3. Validate JSON against a `QuizJsonSchema` (using `System.Text.Json` with `JsonSerializerOptions` or `JsonDocument` validation)
4. On parse failure, retry with stronger instruction: "You MUST output valid JSON only, no markdown, no code fences, no additional text."

### 6c. Insufficient document content

**Scenario:** User selected documents but they contain very little text (empty PDFs, image-only files, minimal content)

**UX:**
```
┌─────────────────────────────────┐
| ⚠️ Not Enough Content            │
│                                    │
│ "The selected documents don't     │
│ contain enough text to generate    │
│ a meaningful quiz. Please select   │
│ documents with more content."      │
│                                    │
│ [Select Different Documents] [Cancel] │
└─────────────────────────────────┘
```

**Backend:** `QuizService` counts total characters across retrieved chunks. If `< 500` characters (configurable threshold), return `422 Insufficient Content`.

### 6d. Empty retrieval result

**Scenario:** RAG search returns zero chunks (documents exist but no semantic match with the quiz topic? Actually this shouldn't happen for quiz generation since we use all selected documents)

**More precise scenario:** `DocumentIds` or `FolderId` provided but documents are deleted between clicking "Generate" and the API call.

**UX:**
```
┌─────────────────────────────────┐
| ⚠️ Documents Not Found           │
│                                    │
│ "The selected documents are no     │
│ longer available. They may have    │
│ been deleted."                     │
│                                    │
│ [OK]                               │
└─────────────────────────────────┘
```

**Backend:** `DocumentService.GetByIdAsync` returns null for one or more requested docs → `404 Not Found`.

### 6e. Session restoration failures

**Scenario:** `GET /api/quiz/resume` fails because:
- Quiz ID doesn't exist (404)
- Quiz belongs to a different user (403)
- DB connection error (500)
- `SessionId` is null (no active quiz for this session)

**UX:** If session can't be restored, the resume card is replaced with a "Start New Quiz" card. No error is shown to the user — graceful degradation.

**Frontend:** `OnInitializedAsync` in `AiChat.razor` calls `GetActiveQuizAsync(sessionId)` and silently handles null/error by hiding the resume prompt.

### Error handling summary matrix

| Error | HTTP Code | Frontend UX | Recovery |
|---|---|---|---|
| AI provider down | 502/503 | Error card: "AI service unavailable" | Retry button |
| Invalid JSON | 422 | Error card: "Data error" | Retry button |
| Insufficient content | 422 | Error card: "Not enough content" | Change docs |
| Docs not found | 404 | Error card: "Documents not found" | OK → dismiss |
| Session not found | 404 | Silent: hide resume card | Auto-hide |
| Auth failure | 403 | Silent: hide resume card | Auto-hide |
| Rate limited | 429 | Error card: "Too many requests, try later" | Retry after delay |

---

## 7. API Interaction Review

### Option A: Per-question API calls

```
User clicks "Generate Quiz"
  → POST /api/quiz/generate  (generates ALL 8 questions at once)
  → Returns QuizDto { id, questions: [...] }

User clicks answer option on question N
  → PATCH /api/quiz/{id}/answer  { questionIndex: N, selectedOptionId: "B" }
  → Returns { isCorrect, correctOptionId, explanation, sources }

User navigates to next question
  → No API call (local state)

User completes quiz
  → POST /api/quiz/{id}/complete
  → Returns { score, total, percentage }

User closes mid-quiz
  → PUT /api/quiz/{id}/save  { status: "in_progress", currentQuestionIndex: N, answers: {...} }
```

### Option B: All-local state management

```
User clicks "Generate Quiz"
  → POST /api/quiz/generate  (generates ALL 8 questions at once)
  → Returns QuizDto { id, questions: [...] }

User selects answer on question N
  → Local state update only
  → Correct/incorrect determined client-side (questions have isCorrect flag)

User completes quiz
  → POST /api/quiz/{id}/complete  { answers: {"0": "B", "1": "A", ...} }
  → Server validates and returns score

User closes mid-quiz
  → PUT /api/quiz/{id}/save  { status: "in_progress", currentQuestionIndex: N, answers: {...} }
```

### Recommendation: Option B (All-local state management)

**Why Option B matches NotebookLM behavior better:**

1. **Instant feedback** — No network latency between answer and feedback. NotebookLM gives immediate feedback when you select an answer.
2. **Offline-capable** — Answers work even during temporary network disconnects.
3. **Lower server cost** — No per-question API calls means 8x fewer requests per quiz.
4. **Simpler state management** — All quiz state lives in one place (frontend `QuizState` object). No race conditions from parallel PATCH calls.
5. **No cheating concern** — The answers are embedded in the quiz response from the AI. A student who inspects network traffic can already see the correct answers. Embedding `isCorrect` in the frontend doesn't change this.
6. **NotebookLM pattern** — NotebookLM sends the entire quiz at once and grades locally (the correct answers are present in the response, they're just hidden visually).

**The only reason to use Option A would be to prevent cheating** (e.g., a proctored exam scenario). But this is a self-study tool — preventing cheating is not a goal. The user should be able to learn interactively without artificial constraints.

### API interaction flow (Option B):

```
POST /api/quiz/generate
  Request: { sessionId, title?, documentIds[], folderId?, count?: 8, difficulty?: "medium" }
  Response: {
    id: "quiz-uuid",
    title: "Machine Learning Concepts Quiz",
    questions: [
      {
        index: 0,
        question: "...",
        subtitle: "...",
        options: [
          { id: "A", text: "..." },
          { id: "B", text: "..." },
          { id: "C", text: "..." },
          { id: "D", text: "..." }
        ],
        correctOptionId: "B",       // used client-side for grading
        explanation: "...",          // shown after answer
        sources: [{ label: "S1", text: "..." }]
      },
      // ... 7 more questions
    ],
    totalQuestions: 8
  }

PUT /api/quiz/{id}/save
  Request: { status: "in_progress" | "completed", currentQuestionIndex: int, answers: dict<int, string|null>, score?: int }
  Response: 200 OK

GET /api/quiz/resume?sessionId={sessionId}
  Response: QuizDto (same as generate response, with answers included)
```

---

## 8. State Management Review

### Frontend State

```typescript
// Global chat state (AiChat.razor)
interface ChatState {
  sessions: ChatSessionDto[];
  activeSessionId: Guid?;
  exchanges: Exchange[];           // Q&A pairs + quiz cards
  busy: boolean;
  quizState: QuizState?;           // null when no quiz is active
}

// Quiz-specific state
interface QuizState {
  status: 'not_started' | 'generating' | 'in_progress' | 'completed' | 'review';
  quizData: QuizDataDto?;          // from POST /generate response
  currentQuestionIndex: number;
  answers: Record<number, string | null>;  // questionIndex → selectedOptionId
  questionStatus: Record<number, 'unanswered' | 'correct' | 'incorrect'>;
  dialogOpen: boolean;

  // Derived (computed)
  get unansweredCount(): number;
  get answeredCount(): number;
  get score(): { correct: number, total: number, percentage: number };
  get isLastQuestion(): boolean;
  get isFirstQuestion(): boolean;
  get currentQuestion(): QuestionDto;
}

// UI-only state (re-rendered per question)
interface QuestionUIState {
  selectedOptionId: string?;       // currently highlighted option
  submitted: boolean;
  showExplanation: boolean;        // true after answer submission
  feedbackBadge: 'correct' | 'incorrect' | null;
}
```

### Backend State

```csharp
// DB Entity
public sealed record Quiz
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string UserId { get; set; }
    public string Title { get; set; }
    public QuizStatus Status { get; set; }  // enum: GeneratingFailed, InProgress, Completed
    public string? ErrorCode { get; set; }  // null if ok, "insufficient_content", "ai_error", "invalid_json"
    public int CurrentQuestionIndex { get; set; }
    public int TotalQuestions { get; set; }
    public string? AnswersJson { get; set; }  // "{\"0\":\"B\",\"1\":null,...}"
    public int? Score { get; set; }
    public string? QuestionsJson { get; set; }  // full quiz data (JSONB)
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public ChatSession Session { get; set; }
}

// DTOs
public sealed record GenerateQuizRequest(
    Guid? SessionId,
    string? Title,
    IReadOnlyList<Guid>? DocumentIds,
    Guid? FolderId,
    int Count = 8,
    string Difficulty = "medium"
);

public sealed record QuizDto(
    Guid Id,
    string Title,
    QuizStatus Status,
    int CurrentQuestionIndex,
    int TotalQuestions,
    IReadOnlyList<QuizQuestionDto> Questions,
    IReadOnlyDictionary<int, string?> Answers,
    int? Score
);

public sealed record QuizQuestionDto(
    int Index,
    string Question,
    string Subtitle,
    IReadOnlyList<QuizOptionDto> Options,
    string CorrectOptionId,
    string Explanation,
    IReadOnlyList<QuizSourceDto> Sources
);

public sealed record QuizOptionDto(
    string Id,
    string Text
);

public sealed record QuizSourceDto(
    string Label,
    string Text
);

public sealed record SaveQuizRequest(
    QuizStatus Status,
    int CurrentQuestionIndex,
    IReadOnlyDictionary<int, string?> Answers,
    int? Score
);
```

### Frontend State Machine (React state equivalent)

```
quizState.status:
  'not_started':  Show quiz card with [Generate] button
  'generating':   Show spinner card
  'in_progress':  Dialog open (if dialogOpen = true), or resume card in chat
  'completed':    Score overlay + completed card in chat
  'review':       Review mode dialog

Transitions:
  not_started → generating       (user clicks Generate)
  generating → in_progress       (API response received)
  generating → not_started       (API error, user cancels)
  in_progress → completed        (last question answered)
  in_progress → in_progress      (navigate, answer, close/resume)
  completed → review              (user clicks Review)
  completed → in_progress        (user clicks Retake)
  review → completed             (user clicks Back to Results)
  review → in_progress           (user clicks Retake)
```

### Component tree (proposed)

```
AiChat.razor
├── ChatLayout (left sidebar + main area + right sidebar)
│   ├── Sidebar (left)
│   │   ├── SessionList
│   │   └── ...
│   ├── ChatThread (center)
│   │   ├── Exchange (text Q&A)
│   │   ├── QuizCard (for quiz items in chat)
│   │   │   └── States: not_started | generating | in_progress | completed
│   │   └── Composer (input area)
│   └── Toolbar (right)
│       ├── ToolsList
│       └── GenerateQuizButton → sets quizState.status = 'not_started'

QuizDialog.razor (rendered conditionally when quizState.dialogOpen)
├── MudDialog
│   ├── QuizHeader
│   │   ├── Title + Breadcrumb
│   │   └── ProgressIndicator (SVG arc + label)
│   ├── QuizContent (scrollable)
│   │   ├── QuestionDisplay
│   │   │   ├── QuestionText
│   │   │   ├── Subtitle
│   │   │   └── OptionsGrid (2×2)
│   │   │       └── OptionButton × 4
│   │   │           States: default | selected | correct | incorrect
│   │   └── ExplanationSection (shown after submit)
│   │       ├── ExplanationText
│   │       └── SourceReferences
│   └── QuizFooter
│       ├── ReportIssueButton
│       └── NavigationButtons (Prev | Next)
```

### DB Migration plan

```sql
CREATE TABLE IF NOT EXISTS quizzes (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID NOT NULL REFERENCES chat_sessions(id) ON DELETE CASCADE,
    user_id TEXT NOT NULL,
    title TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'in_progress'
        CHECK (status IN ('generating_failed', 'in_progress', 'completed')),
    error_code TEXT,
    current_question_index INT NOT NULL DEFAULT 0,
    total_questions INT NOT NULL DEFAULT 8,
    answers_json JSONB NOT NULL DEFAULT '{}',
    score INT,
    questions_json JSONB NOT NULL,      -- full quiz content, parsed once
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_quizzes_session_id ON quizzes(session_id);
CREATE INDEX idx_quizzes_user_id ON quizzes(user_id);
CREATE INDEX idx_quizzes_status ON quizzes(status);
```

---

## Summary of Key Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | **Quiz lifecycle**: NOT_STARTED → IN_PROGRESS → COMPLETED → REVIEW | Matches NotebookLM flow. Clean state machine, easy to implement |
| 2 | **Question lifecycle**: UNANSWERED → ANSWERED_CORRECT/INCORRECT | Immediate per-question feedback is essential for learning |
| 3 | **Resume**: Auto-save on close, restore via `PUT /api/quiz/{id}/save` + `GET /api/quiz/resume` | NotebookLM remembers progress — so should we |
| 4 | **Chat history cards**: Separate QuizCard component in exchanges list | Keeps quiz visible in chat context. NotebookLM uses Studio panel — cards are our equivalent |
| 5 | **Review mode**: Same dialog, read-only, with explanations + sources | Maximizes code reuse. Only difference is locked interaction and visible explanations |
| 6 | **API approach**: Option B (local state, batch generation) | Faster UX (no network per question), simpler state management, matches NotebookLM pattern |
| 7 | **One DB table**: `quizzes` with `questions_json` (JSONB) | Keeps migration simple. V2 can add `quiz_attempts` if needed |
| 8 | **Error states**: Graceful degradation, retry buttons | Self-study tool — no need for harsh errors. Guide user to fix the issue |

---

## Next Steps (After This Review Is Approved)

1. Implement DB migration (`quizzes` table)
2. Implement `QuizService` + `POST /api/quiz/generate`
3. Implement `QuizController` + `PUT /api/quiz/{id}/save` + `GET /api/quiz/resume`
4. Implement `QuizDialog.razor` (matching Figma design from gap analysis)
5. Implement `QuizCard` component for chat history
6. Integrate into `AiChat.razor` (replace `UseQuizPrompt`)
7. Add unit/integration tests
8. Add quiz card rendering to `ConvertMessagesToExchanges`

**Do not proceed beyond step 0 until this review is approved.**
