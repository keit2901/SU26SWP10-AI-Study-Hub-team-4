# Live Log: Chat History Persistence (DB-backed sessions)

**Date:** 2026-06-21  
**Branch:** sprint2/integration  

## Problem
Chat history disappeared after `dotnet run` (server restart).  
**Root cause:** History was stored in `ProtectedSessionStorage` (browser `sessionStorage` encrypted with server-side data protection keys). On server restart, the data protection key ring changed → `GetAsync()` threw → the catch block silently reset to empty history.

## Solution: Database-backed chat sessions

### New entities
- `Data/Entities/ChatSession.cs` — session owned by a user, with optional FolderId, Model, TopK, Title, timestamps
- `Data/Entities/ChatMessage.cs` — individual message (role=user/assistant), content, metadata_json (JSONB), sequence_number, FK → ChatSession

### New configurations
- `Data/Configurations/ChatSessionConfiguration.cs` — table `chat_sessions`, cascade delete user, set-null folder
- `Data/Configurations/ChatMessageConfiguration.cs` — table `chat_messages`, cascade delete session, unique (session, seq)

### DbContext changes
- Added `DbSet<ChatSession>`, `DbSet<ChatMessage>`  
- Auto-timestamp `UpdatedAt` for ChatSession in `SaveChanges`/`SaveChangesAsync`

### New service
- `Services/IChatPersistenceService.cs` — interface with `ListSessionsAsync`, `CreateSessionAsync`, `GetMessagesAsync`, `DeleteSessionAsync`, `SaveExchangeAsync`
- `Services/ChatPersistenceService.cs` — implementation using `AppDbContext`. `SaveExchangeAsync` stores both user and assistant messages as a pair, auto-generates session title from first question

### DTO changes
- `AiChatAskRequest` — added `Guid? SessionId = null`
- `AiChatAnswerResponse` — added `Guid? SessionId = null` (populated after save)
- `Dtos/ChatDtos.cs` — new: `ChatSessionDto`, `ChatMessageDto`, `CreateChatSessionRequest`

### Controller changes (`AiChatController.cs`)
- Constructor now takes `IChatPersistenceService`
- `POST /api/ai/chat/ask` — if no `SessionId`, auto-creates a new session; saves Q&A exchange after LLM answer
- `GET /api/ai/chat/sessions` — list user's sessions (newest first)
- `POST /api/ai/chat/sessions` — create a new session manually
- `GET /api/ai/chat/sessions/{sessionId}` — get messages for a session
- `DELETE /api/ai/chat/sessions/{sessionId}` — delete a session

### Client changes (`AiChatApiClient.cs`)
- Added `ListSessionsAsync`, `CreateSessionAsync`, `GetSessionMessagesAsync`, `DeleteSessionAsync`

### AiChat.razor changes
- **Removed** `ProtectedSessionStorage` and `HistoryStorageKey`  
- **Removed** `SaveHistoryAsync`, `LoadHistoryAsync` (replaced by API calls)  
- New: `LoadSessionsAsync()` — calls API to list sessions, loads latest or creates empty state  
- New: `LoadSessionMessagesAsync()` — loads messages for a session, converts `ChatMessageDto` → `AiChatHistoryExchange` (paired user+assistant messages)  
- New: `CreateNewSessionAsync()` — creates session via API, clears local state  
- New: `SwitchSessionAsync()` — switch between sessions  
- New: `ConvertMessagesToExchanges()` — helper that pairs (user, assistant) messages from flat list, deserializes JSON metadata  
- Updated `AskAsync` — passes `_currentSessionId` as `SessionId` in request; tracks `response.SessionId` from server  
- Updated `ClearHistory` — deletes session via API instead of `SessionStorage.DeleteAsync`  
- Updated `OnWorkspaceFolderChanged` — calls `LoadSessionsAsync`  
- UI: "New Chat" button (`CreateNewSessionAsync`) added above composer box  

### DI registration (Program.cs)
- `builder.Services.AddScoped<IChatPersistenceService, ChatPersistenceService>()`

### Migration
- `20260621035144_AddChatSessionTables` — creates `chat_sessions` and `chat_messages` tables with proper indexes, FKs, and defaults

### Test changes
- `AiChatControllerTests.cs` — `BuildSut` now accepts optional `IChatPersistenceService` or creates a mock with `CreateSessionAsync` returning a valid session; HappyPath assertion uses value comparison instead of reference equality

## Result
- **Build:** 0 errors, 149/150 tests pass (1 pre-existing skip)  
- Chat history now persists across server restarts because it's stored in PostgreSQL via EF Core  
- Users can create multiple chat sessions ("New Chat" button) and they're persisted forever until deleted  
- Backward compatible: old UI still works, existing sessions will appear after migration
