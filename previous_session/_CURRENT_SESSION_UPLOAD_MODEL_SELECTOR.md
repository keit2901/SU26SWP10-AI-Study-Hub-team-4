# Current Session — Upload Button + AI Model Selector (2026-06-21)

## Completed

### 1. Upload fixed-button added to left rail
- Added `.workspace-add-file-wrap` with dashed-border `MudButton` above document list in `AiChat.razor`
- Added light-theme CSS for `.workspace-add-file-button` with dashed border, purple accent, hover effects
- Links to `@UploadUrl` (existing property)

### 2. AI Model dropdown added to right rail
- Added `.workspace-model-selector` section between Smart Actions and Active Context
- `MudSelect<string>` with two options: Llama 3.3 70B (Groq) and Gemini 2.5 Flash (Google)
- `OnModelChanged` handler sets `_selectedModel` and calls `StateHasChanged()`
- `Disabled` bound to `_busy || _generatingQuiz`
- Light-theme CSS for the selector section and MudSelect styling
- `AskAsync` now passes `Model: _selectedModel` in the `AiChatAskRequest`

## Build
- `dotnet build` — 0 errors, only pre-existing warnings
